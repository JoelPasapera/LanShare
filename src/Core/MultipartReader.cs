using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LanShare.Core
{
    public class MultipartException : Exception
    {
        public MultipartException(string message) : base(message) { }
    }

    /// <summary>Una parte supero el tamano permitido.</summary>
    public sealed class MultipartLimitException : MultipartException
    {
        public MultipartLimitException(string message) : base(message) { }
    }

    public sealed class MultipartPart
    {
        public string Name { get; internal set; }

        /// <summary>Nombre de archivo tal como lo envio el cliente; null si no es un archivo.</summary>
        public string FileName { get; internal set; }

        public string ContentType { get; internal set; }

        public bool IsFile
        {
            get { return FileName != null; }
        }
    }

    /// <summary>
    /// Lector en flujo de multipart/form-data (RFC 7578 sobre RFC 2046).
    ///
    /// Nunca carga una parte entera en memoria: trabaja sobre un bufer de
    /// 64 KB que se desliza por la entrada. El delimitador se busca con
    /// Boyer-Moore-Horspool, que salta varios bytes por comparacion: con un
    /// delimitador de 40 bytes, la mayoria de las posiciones del cuerpo ni se
    /// examinan.
    ///
    /// El delimitador puede quedar partido entre dos lecturas. Por eso, cuando
    /// no aparece, se entregan todos los bytes salvo los ultimos (longitud del
    /// delimitador - 1), que pueden ser su comienzo, y se rellena el bufer.
    ///
    /// Uso: ReadNextPartAsync hasta que devuelva null; para cada parte de
    /// archivo, CopyBodyAsync. Si una parte no se consume, se descarta sola.
    /// </summary>
    public sealed class MultipartReader
    {
        public const int MaxHeaderBytes = 16 * 1024;
        public const int MaxBoundaryLength = 200;
        private const int BufferSize = 64 * 1024;

        private static readonly byte[] HeaderTerminator = { 13, 10, 13, 10 };
        private static readonly int[] HeaderSkip = BuildSkipTable(HeaderTerminator);

        private readonly Stream _input;
        private readonly byte[] _delimiter;
        private readonly int[] _skip;
        private readonly byte[] _buffer;
        private int _start;
        private int _end;
        private bool _eof;
        private bool _started;
        private bool _inBody;
        private bool _finished;

        public MultipartReader(Stream input, string boundary)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (string.IsNullOrEmpty(boundary) || boundary.Length > MaxBoundaryLength)
                throw new MultipartException("Delimitador multipart ausente o invalido.");

            _input = input;
            _delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
            _skip = BuildSkipTable(_delimiter);
            _buffer = new byte[BufferSize];

            // Salto de linea virtual al principio: el primer delimitador puede
            // ir en la primera linea, sin CRLF delante. Asi todos los
            // delimitadores tienen la misma forma y una sola busqueda sirve.
            _buffer[0] = 13;
            _buffer[1] = 10;
            _end = 2;
        }

        /// <summary>
        /// Avanza a la siguiente parte y lee sus cabeceras. Devuelve null cuando
        /// llega el delimitador de cierre.
        /// </summary>
        public async Task<MultipartPart> ReadNextPartAsync()
        {
            if (_finished) return null;

            if (!_started)
            {
                // Preambulo: lo anterior al primer delimitador se ignora.
                _inBody = true;
                await CopyBodyAsync(null, MaxHeaderBytes).ConfigureAwait(false);
                _started = true;
            }
            else if (_inBody)
            {
                // El llamante no consumio la parte anterior: se descarta.
                await CopyBodyAsync(null, long.MaxValue).ConfigureAwait(false);
            }

            // CopyBodyAsync deja el cursor sobre el delimitador.
            _start += _delimiter.Length;

            await EnsureAsync(2).ConfigureAwait(false);
            if (_end - _start >= 2 && _buffer[_start] == (byte)'-' && _buffer[_start + 1] == (byte)'-')
            {
                _finished = true;
                return null;
            }

            // Relleno opcional de espacios antes del salto de linea (RFC 2046).
            while (true)
            {
                await EnsureAsync(1).ConfigureAwait(false);
                if (_end - _start < 1) throw new MultipartException("La entrada termino tras un delimitador.");
                byte b = _buffer[_start];
                if (b != (byte)' ' && b != (byte)'\t') break;
                _start++;
            }

            // El cursor esta en el CRLF que cierra la linea del delimitador. Las
            // cabeceras terminan en la primera linea vacia; si no hay ninguna,
            // la linea vacia es justo esta y el patron aparece en el desplazamiento 0.
            int headerEnd = await FindAsync(HeaderTerminator, HeaderSkip, MaxHeaderBytes + 4).ConfigureAwait(false);
            if (headerEnd < 0) throw new MultipartException("Cabeceras de parte ausentes o demasiado largas.");
            if (_buffer[_start] != 13 || _buffer[_start + 1] != 10)
                throw new MultipartException("Falta el salto de linea tras el delimitador.");

            int blockLength = headerEnd - 2;
            string headers = blockLength > 0 ? Encoding.UTF8.GetString(_buffer, _start + 2, blockLength) : "";
            _start += headerEnd + 4;

            MultipartPart part = ParseHeaders(headers);
            _inBody = true;
            return part;
        }

        /// <summary>
        /// Copia el cuerpo de la parte actual a destination (null para
        /// descartarlo). Lanza MultipartLimitException si pasa de maxBytes; en
        /// ese caso la entrada queda a medias y no debe seguir leyendose.
        /// </summary>
        public async Task<long> CopyBodyAsync(Stream destination, long maxBytes)
        {
            if (!_inBody) throw new InvalidOperationException("No hay ninguna parte abierta.");

            long total = 0;
            int keep = _delimiter.Length - 1;

            while (true)
            {
                int index = IndexOf(_buffer, _start, _end, _delimiter, _skip);
                int safeEnd = index >= 0 ? index : Math.Max(_start, _end - keep);
                int length = safeEnd - _start;

                if (length > 0)
                {
                    total += length;
                    if (total > maxBytes)
                    {
                        throw new MultipartLimitException("La parte supera el tamano maximo permitido.");
                    }
                    if (destination != null)
                    {
                        await destination.WriteAsync(_buffer, _start, length).ConfigureAwait(false);
                    }
                    _start = safeEnd;
                }

                if (index >= 0)
                {
                    _inBody = false;
                    return total;
                }

                if (!await FillAsync().ConfigureAwait(false))
                    throw new MultipartException("La entrada termino antes del delimitador.");
            }
        }

        // ------------------------------------------------------------------
        // Bufer
        // ------------------------------------------------------------------

        /// <summary>Compacta y lee mas datos. Falso si la entrada se acabo.</summary>
        private async Task<bool> FillAsync()
        {
            if (_eof) return false;

            if (_start > 0)
            {
                int pending = _end - _start;
                if (pending > 0) Buffer.BlockCopy(_buffer, _start, _buffer, 0, pending);
                _start = 0;
                _end = pending;
            }

            // Con el bufer lleno y sin poder avanzar no hay progreso posible:
            // solo pasa con un bloque de cabeceras desmesurado.
            if (_end == _buffer.Length) throw new MultipartException("Bloque multipart demasiado grande.");

            int read = await _input.ReadAsync(_buffer, _end, _buffer.Length - _end).ConfigureAwait(false);
            if (read <= 0)
            {
                _eof = true;
                return false;
            }

            _end += read;
            return true;
        }

        private async Task EnsureAsync(int count)
        {
            while (_end - _start < count)
            {
                if (!await FillAsync().ConfigureAwait(false)) return;
            }
        }

        /// <summary>
        /// Busca el patron desde el cursor, rellenando si hace falta. Devuelve
        /// el desplazamiento relativo al cursor, o -1 si la entrada se acaba o
        /// se recorren maxSpan bytes sin encontrarlo.
        /// </summary>
        private async Task<int> FindAsync(byte[] pattern, int[] skip, int maxSpan)
        {
            while (true)
            {
                int limit = Math.Min(_end, _start + maxSpan);
                int index = IndexOf(_buffer, _start, limit, pattern, skip);
                if (index >= 0) return index - _start;
                if (_end - _start >= maxSpan) return -1;
                if (!await FillAsync().ConfigureAwait(false)) return -1;
            }
        }

        // ------------------------------------------------------------------
        // Busqueda
        // ------------------------------------------------------------------

        /// <summary>
        /// Tabla de saltos de Horspool: para cada byte, cuanto se puede avanzar
        /// si es el que queda bajo el final del patron y no hay coincidencia.
        /// </summary>
        public static int[] BuildSkipTable(byte[] pattern)
        {
            int[] skip = new int[256];
            int m = pattern.Length;
            for (int i = 0; i < 256; i++) skip[i] = m;
            for (int i = 0; i < m - 1; i++) skip[pattern[i]] = m - 1 - i;
            return skip;
        }

        /// <summary>Primera aparicion del patron en data[from, to), o -1.</summary>
        public static int IndexOf(byte[] data, int from, int to, byte[] pattern, int[] skip)
        {
            int m = pattern.Length;
            int last = m - 1;
            int i = from;

            while (i <= to - m)
            {
                int j = last;
                while (j >= 0 && data[i + j] == pattern[j]) j--;
                if (j < 0) return i;
                i += skip[data[i + last]];
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Cabeceras
        // ------------------------------------------------------------------

        private static MultipartPart ParseHeaders(string block)
        {
            MultipartPart part = new MultipartPart();

            foreach (string line in block.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();

                if (string.Equals(name, "Content-Disposition", StringComparison.OrdinalIgnoreCase))
                {
                    string fieldName, fileName;
                    ParseContentDisposition(value, out fieldName, out fileName);
                    part.Name = fieldName;
                    part.FileName = fileName;
                }
                else if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    part.ContentType = value;
                }
            }

            return part;
        }

        /// <summary>
        /// form-data; name="x"; filename="a.jpg"; filename*=UTF-8''a%20b.jpg
        /// Si llegan las dos formas del nombre, manda la extendida (RFC 5987),
        /// que es la que conserva los caracteres no ASCII sin ambiguedad.
        /// </summary>
        public static void ParseContentDisposition(string value, out string name, out string fileName)
        {
            name = null;
            fileName = null;
            string extended = null;

            foreach (KeyValuePair<string, string> parameter in SplitParameters(value))
            {
                string key = parameter.Key.ToLowerInvariant();
                if (key == "name") name = parameter.Value;
                else if (key == "filename") fileName = parameter.Value;
                else if (key == "filename*") extended = DecodeExtendedValue(parameter.Value);
            }

            if (extended != null) fileName = extended;
        }

        /// <summary>Extrae el delimitador de "multipart/form-data; boundary=...".</summary>
        public static bool TryGetBoundary(string contentType, out string boundary)
        {
            boundary = null;
            if (string.IsNullOrEmpty(contentType)) return false;

            int semicolon = contentType.IndexOf(';');
            string mediaType = (semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType).Trim();
            if (!string.Equals(mediaType, "multipart/form-data", StringComparison.OrdinalIgnoreCase)) return false;

            foreach (KeyValuePair<string, string> parameter in SplitParameters(contentType))
            {
                if (!string.Equals(parameter.Key, "boundary", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(parameter.Value) || parameter.Value.Length > MaxBoundaryLength) return false;
                boundary = parameter.Value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Parametros separados por ';' con valores opcionalmente entre
        /// comillas. Dentro de las comillas el ';' no separa y la barra
        /// invertida escapa el caracter siguiente.
        /// </summary>
        private static List<KeyValuePair<string, string>> SplitParameters(string value)
        {
            List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(value)) return result;

            int i = 0;
            int n = value.Length;

            // Lo anterior al primer ';' es el tipo, no un parametro.
            while (i < n && value[i] != ';') i++;

            while (i < n)
            {
                i++;   // salta ';'
                while (i < n && (value[i] == ' ' || value[i] == '\t')) i++;

                int keyStart = i;
                while (i < n && value[i] != '=' && value[i] != ';') i++;
                string key = value.Substring(keyStart, i - keyStart).Trim();

                string parameterValue = "";
                if (i < n && value[i] == '=')
                {
                    i++;
                    while (i < n && (value[i] == ' ' || value[i] == '\t')) i++;

                    if (i < n && value[i] == '"')
                    {
                        i++;
                        StringBuilder sb = new StringBuilder();
                        while (i < n && value[i] != '"')
                        {
                            if (value[i] == '\\' && i + 1 < n) i++;
                            sb.Append(value[i]);
                            i++;
                        }
                        if (i < n) i++;   // comilla de cierre
                        parameterValue = sb.ToString();
                        while (i < n && value[i] != ';') i++;
                    }
                    else
                    {
                        int valueStart = i;
                        while (i < n && value[i] != ';') i++;
                        parameterValue = value.Substring(valueStart, i - valueStart).Trim();
                    }
                }

                if (key.Length > 0) result.Add(new KeyValuePair<string, string>(key, parameterValue));
            }

            return result;
        }

        /// <summary>RFC 5987: charset'idioma'valor-con-porcentajes.</summary>
        private static string DecodeExtendedValue(string value)
        {
            if (value == null) return null;

            int first = value.IndexOf('\'');
            int second = first >= 0 ? value.IndexOf('\'', first + 1) : -1;
            if (first < 0 || second < 0) return null;

            string charset = value.Substring(0, first);
            string encoded = value.Substring(second + 1);

            Encoding encoding;
            try { encoding = Encoding.GetEncoding(charset.Length > 0 ? charset : "utf-8"); }
            catch (ArgumentException) { return null; }

            List<byte> bytes = new List<byte>(encoded.Length);
            for (int i = 0; i < encoded.Length; i++)
            {
                char c = encoded[i];
                if (c == '%' && i + 2 < encoded.Length && IsHex(encoded[i + 1]) && IsHex(encoded[i + 2]))
                {
                    bytes.Add((byte)((HexValue(encoded[i + 1]) << 4) | HexValue(encoded[i + 2])));
                    i += 2;
                }
                else if (c < 128)
                {
                    bytes.Add((byte)c);
                }
                else
                {
                    return null;
                }
            }

            return encoding.GetString(bytes.ToArray());
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }
    }
}
