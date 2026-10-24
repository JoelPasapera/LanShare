using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LanShare.Core
{
    /// <summary>
    /// Escribe un archivo ZIP en flujo, sin volver nunca atras: se puede
    /// enviar por HTTP a la vez que se lee del disco, con memoria constante.
    ///
    /// Cada archivo lleva descriptor de datos (bit 3 de los indicadores): el
    /// CRC y el tamano van detras del contenido, cuando ya se conocen, y el
    /// directorio central los repite al final. Las entradas se guardan sin
    /// comprimir (metodo 0): fotos y videos ya vienen comprimidos y en red
    /// local comprimir costaria mas CPU de lo que ahorra en transferencia.
    ///
    /// Los nombres van en UTF-8 (bit 11) y la fecha, ademas de en el formato
    /// de MS-DOS (hora local, resolucion de 2 s), en el campo extra 0x5455 en
    /// UTC. ZIP64 solo aparece cuando hace falta: un archivo de 4 GB o mas, un
    /// desplazamiento por encima de 4 GB o mas de 65535 entradas.
    ///
    /// Formato: PKWARE APPNOTE.TXT 6.3.
    /// </summary>
    public sealed class ZipWriter
    {
        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint DescriptorSignature = 0x08074b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint Zip64EndSignature = 0x06064b50;
        private const uint Zip64LocatorSignature = 0x07064b50;
        private const uint EndSignature = 0x06054b50;

        private const ushort FlagDescriptor = 0x0008;
        private const ushort FlagUtf8 = 0x0800;
        private const ushort VersionDefault = 20;
        private const ushort VersionZip64 = 45;

        private const ushort ExtraZip64 = 0x0001;
        private const ushort ExtraTimestamp = 0x5455;

        private const uint DosAttributeDirectory = 0x10;
        private const uint DosAttributeArchive = 0x20;

        private const long Max32 = 0xFFFFFFFFL;
        private const int Max16 = 0xFFFF;
        private const int BufferSize = 64 * 1024;

        private sealed class Entry
        {
            public byte[] Name;
            public ushort DosTime;
            public ushort DosDate;
            public long UnixTime;
            public bool IsDirectory;
            public bool Zip64Local;
            public uint Crc;
            public long Size;
            public long Offset;
        }

        private readonly Stream _output;
        private readonly bool _forceZip64;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly byte[] _buffer = new byte[BufferSize];
        private readonly Crc32 _crc = new Crc32();
        private long _position;
        private bool _finished;

        public ZipWriter(Stream output)
            : this(output, false)
        {
        }

        /// <param name="forceZip64">Solo para pruebas: ejercita ZIP64 sin generar 4 GB.</param>
        internal ZipWriter(Stream output, bool forceZip64)
        {
            if (output == null) throw new ArgumentNullException("output");
            _output = output;
            _forceZip64 = forceZip64;
        }

        /// <summary>Bytes escritos hasta ahora.</summary>
        public long Position
        {
            get { return _position; }
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        /// <param name="name">Ruta dentro del ZIP con '/', sin barra inicial; se le anade la final.</param>
        public Task AddDirectoryAsync(string name, DateTime modifiedUtc)
        {
            string clean = CleanName(name);
            if (!clean.EndsWith("/", StringComparison.Ordinal)) clean += "/";

            Entry entry = NewEntry(clean, modifiedUtc, true, false);
            return WriteAsync(LocalHeader(entry));
        }

        /// <summary>
        /// Copia como mucho <paramref name="length"/> bytes del contenido. Si
        /// el archivo encoge mientras se lee, o deja de poder leerse, se guarda
        /// lo que habia; si crece, lo que sobra se ignora: el ZIP siempre queda
        /// coherente.
        /// </summary>
        /// <returns>Bytes de contenido escritos; menos que length si la lectura se corto.</returns>
        public async Task<long> AddFileAsync(string name, Stream content, long length, DateTime modifiedUtc)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            Entry entry = NewEntry(CleanName(name), modifiedUtc, false, _forceZip64 || length >= Max32);
            await WriteAsync(LocalHeader(entry)).ConfigureAwait(false);

            _crc.Reset();
            long remaining = length;
            while (remaining > 0)
            {
                int wanted = remaining < _buffer.Length ? (int)remaining : _buffer.Length;

                // Un error de lectura a mitad (un rango bloqueado por quien tiene
                // el archivo abierto, una memoria USB que falla) corta la entrada
                // ahi, con su tamano y CRC reales: el resto del ZIP sigue siendo
                // valido. Lanzar aqui dejaria un ZIP sin directorio central.
                int read;
                try { read = await content.ReadAsync(_buffer, 0, wanted).ConfigureAwait(false); }
                catch (IOException) { read = 0; }
                catch (UnauthorizedAccessException) { read = 0; }
                if (read <= 0) break;

                _crc.Update(_buffer, 0, read);
                await _output.WriteAsync(_buffer, 0, read).ConfigureAwait(false);
                _position += read;
                remaining -= read;
            }

            entry.Size = length - remaining;
            entry.Crc = _crc.Value;
            await WriteAsync(Descriptor(entry)).ConfigureAwait(false);
            return entry.Size;
        }

        /// <summary>Escribe el directorio central y el cierre. Despues no se admite nada mas.</summary>
        public async Task FinishAsync()
        {
            if (_finished) throw new InvalidOperationException("El ZIP ya se cerro.");
            _finished = true;

            long centralOffset = _position;
            foreach (Entry entry in _entries)
            {
                await WriteAsync(CentralHeader(entry)).ConfigureAwait(false);
            }
            long centralSize = _position - centralOffset;

            bool zip64 = _forceZip64 || _entries.Count >= Max16 || centralOffset >= Max32 || centralSize >= Max32;
            if (zip64)
            {
                long zip64EndOffset = _position;
                await WriteAsync(Zip64End(centralOffset, centralSize)).ConfigureAwait(false);
                await WriteAsync(Zip64Locator(zip64EndOffset)).ConfigureAwait(false);
            }

            await WriteAsync(End(centralOffset, centralSize)).ConfigureAwait(false);
            await _output.FlushAsync().ConfigureAwait(false);
        }

        // ------------------------------------------------------------------

        private Entry NewEntry(string name, DateTime modifiedUtc, bool isDirectory, bool zip64Local)
        {
            if (_finished) throw new InvalidOperationException("El ZIP ya se cerro.");

            byte[] encoded = Encoding.UTF8.GetBytes(name);
            if (encoded.Length == 0 || encoded.Length > Max16) throw new ArgumentException("Nombre de entrada no valido.", "name");

            DateTime utc = modifiedUtc.Kind == DateTimeKind.Local ? modifiedUtc.ToUniversalTime() : modifiedUtc;
            ushort time, date;
            ToDos(DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime(), out time, out date);

            Entry entry = new Entry();
            entry.Name = encoded;
            entry.DosTime = time;
            entry.DosDate = date;
            entry.UnixTime = UnixSeconds(utc);
            entry.IsDirectory = isDirectory;
            entry.Zip64Local = zip64Local;
            entry.Offset = _position;
            _entries.Add(entry);
            return entry;
        }

        private static byte[] LocalHeader(Entry entry)
        {
            int extra = TimestampExtraLength(entry) + (entry.Zip64Local ? 20 : 0);
            Writer w = new Writer(30 + entry.Name.Length + extra);

            w.U32(LocalHeaderSignature);
            w.U16(entry.Zip64Local ? VersionZip64 : VersionDefault);
            w.U16((ushort)(FlagUtf8 | (entry.IsDirectory ? 0 : FlagDescriptor)));
            w.U16(0);                                   // metodo: sin comprimir
            w.U16(entry.DosTime);
            w.U16(entry.DosDate);
            w.U32(0);                                   // CRC: en el descriptor
            w.U32(entry.Zip64Local ? 0xFFFFFFFF : 0);   // tamano comprimido
            w.U32(entry.Zip64Local ? 0xFFFFFFFF : 0);   // tamano original
            w.U16((ushort)entry.Name.Length);
            w.U16((ushort)extra);
            w.Bytes(entry.Name);

            // Con descriptor, la norma pide igualmente ambos tamanos en el
            // extra ZIP64 de la cabecera local; van a cero y el descriptor
            // lleva los reales en 8 bytes.
            if (entry.Zip64Local)
            {
                w.U16(ExtraZip64);
                w.U16(16);
                w.U64(0);
                w.U64(0);
            }
            WriteTimestampExtra(w, entry);
            return w.ToArray();
        }

        private static byte[] Descriptor(Entry entry)
        {
            Writer w = new Writer(entry.Zip64Local ? 24 : 16);
            w.U32(DescriptorSignature);
            w.U32(entry.Crc);
            if (entry.Zip64Local)
            {
                w.U64((ulong)entry.Size);
                w.U64((ulong)entry.Size);
            }
            else
            {
                w.U32((uint)entry.Size);
                w.U32((uint)entry.Size);
            }
            return w.ToArray();
        }

        private static byte[] CentralHeader(Entry entry)
        {
            bool bigSize = entry.Size >= Max32;
            bool bigOffset = entry.Offset >= Max32;
            int zip64Length = (bigSize ? 16 : 0) + (bigOffset ? 8 : 0);
            bool zip64 = zip64Length > 0 || entry.Zip64Local;

            int extra = TimestampExtraLength(entry) + (zip64Length > 0 ? 4 + zip64Length : 0);
            Writer w = new Writer(46 + entry.Name.Length + extra);

            w.U32(CentralHeaderSignature);
            w.U16(VersionZip64);                        // hecho por: MS-DOS, version 4.5
            w.U16(zip64 ? VersionZip64 : VersionDefault);
            w.U16((ushort)(FlagUtf8 | (entry.IsDirectory ? 0 : FlagDescriptor)));
            w.U16(0);
            w.U16(entry.DosTime);
            w.U16(entry.DosDate);
            w.U32(entry.Crc);
            w.U32(bigSize ? 0xFFFFFFFF : (uint)entry.Size);
            w.U32(bigSize ? 0xFFFFFFFF : (uint)entry.Size);
            w.U16((ushort)entry.Name.Length);
            w.U16((ushort)extra);
            w.U16(0);                                   // comentario
            w.U16(0);                                   // disco
            w.U16(0);                                   // atributos internos
            w.U32(entry.IsDirectory ? DosAttributeDirectory : DosAttributeArchive);
            w.U32(bigOffset ? 0xFFFFFFFF : (uint)entry.Offset);
            w.Bytes(entry.Name);

            // En el directorio central solo van los campos desbordados, en
            // este orden fijo: original, comprimido, desplazamiento.
            if (zip64Length > 0)
            {
                w.U16(ExtraZip64);
                w.U16((ushort)zip64Length);
                if (bigSize)
                {
                    w.U64((ulong)entry.Size);
                    w.U64((ulong)entry.Size);
                }
                if (bigOffset) w.U64((ulong)entry.Offset);
            }
            WriteTimestampExtra(w, entry);
            return w.ToArray();
        }

        private byte[] Zip64End(long centralOffset, long centralSize)
        {
            Writer w = new Writer(56);
            w.U32(Zip64EndSignature);
            w.U64(44);                                  // tamano del registro sin estos 12 bytes
            w.U16(VersionZip64);
            w.U16(VersionZip64);
            w.U32(0);
            w.U32(0);
            w.U64((ulong)_entries.Count);
            w.U64((ulong)_entries.Count);
            w.U64((ulong)centralSize);
            w.U64((ulong)centralOffset);
            return w.ToArray();
        }

        private static byte[] Zip64Locator(long zip64EndOffset)
        {
            Writer w = new Writer(20);
            w.U32(Zip64LocatorSignature);
            w.U32(0);
            w.U64((ulong)zip64EndOffset);
            w.U32(1);
            return w.ToArray();
        }

        private byte[] End(long centralOffset, long centralSize)
        {
            ushort count = (ushort)Math.Min(_entries.Count, Max16);
            Writer w = new Writer(22);
            w.U32(EndSignature);
            w.U16(0);
            w.U16(0);
            w.U16(count);
            w.U16(count);
            w.U32(centralSize >= Max32 ? 0xFFFFFFFF : (uint)centralSize);
            w.U32(centralOffset >= Max32 ? 0xFFFFFFFF : (uint)centralOffset);
            w.U16(0);
            return w.ToArray();
        }

        /// <summary>
        /// Campo 0x5455 con la fecha de modificacion en segundos Unix (UTC).
        /// Es un entero con signo de 32 bits: fuera de su rango se omite y
        /// queda solo la fecha de MS-DOS.
        /// </summary>
        private static int TimestampExtraLength(Entry entry)
        {
            return entry.UnixTime >= 0 && entry.UnixTime <= int.MaxValue ? 9 : 0;
        }

        private static void WriteTimestampExtra(Writer w, Entry entry)
        {
            if (TimestampExtraLength(entry) == 0) return;
            w.U16(ExtraTimestamp);
            w.U16(5);
            w.U8(1);                                    // solo fecha de modificacion
            w.U32((uint)entry.UnixTime);
        }

        private async Task WriteAsync(byte[] data)
        {
            await _output.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            _position += data.Length;
        }

        /// <summary>
        /// Barras normales, sin barra inicial ni tramos "." o "..": un ZIP con
        /// rutas que salen de la carpeta de destino es un ataque conocido
        /// (zip slip) contra quien lo descomprime.
        /// </summary>
        internal static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Nombre vacio.", "name");

            string[] parts = name.Replace('\\', '/').Split('/');
            StringBuilder clean = new StringBuilder(name.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == "." || part == "..")
                {
                    // Una barra final se conserva: marca una carpeta.
                    if (part.Length == 0 && i == parts.Length - 1 && clean.Length > 0) clean.Append('/');
                    continue;
                }
                if (clean.Length > 0 && clean[clean.Length - 1] != '/') clean.Append('/');
                clean.Append(part);
            }

            if (clean.Length == 0 || clean.ToString() == "/") throw new ArgumentException("Nombre vacio.", "name");
            return clean.ToString();
        }

        /// <summary>Fecha y hora de MS-DOS: de 1980 a 2107, con resolucion de 2 segundos.</summary>
        internal static void ToDos(DateTime local, out ushort time, out ushort date)
        {
            if (local.Year < 1980) local = new DateTime(1980, 1, 1, 0, 0, 0);
            else if (local.Year > 2107) local = new DateTime(2107, 12, 31, 23, 59, 58);

            time = (ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2));
            date = (ushort)(((local.Year - 1980) << 9) | (local.Month << 5) | local.Day);
        }

        private static long UnixSeconds(DateTime utc)
        {
            return (long)Math.Floor((utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
        }

        /// <summary>Escritura en little-endian sobre un bloque de tamano conocido.</summary>
        private sealed class Writer
        {
            private readonly byte[] _data;
            private int _at;

            public Writer(int size)
            {
                _data = new byte[size];
            }

            public void U8(byte value)
            {
                _data[_at++] = value;
            }

            public void U16(ushort value)
            {
                _data[_at++] = (byte)value;
                _data[_at++] = (byte)(value >> 8);
            }

            public void U32(uint value)
            {
                for (int i = 0; i < 4; i++) _data[_at++] = (byte)(value >> (8 * i));
            }

            public void U64(ulong value)
            {
                for (int i = 0; i < 8; i++) _data[_at++] = (byte)(value >> (8 * i));
            }

            public void Bytes(byte[] value)
            {
                Buffer.BlockCopy(value, 0, _data, _at, value.Length);
                _at += value.Length;
            }

            public byte[] ToArray()
            {
                if (_at != _data.Length) throw new InvalidOperationException("Cabecera ZIP de tamano inesperado.");
                return _data;
            }
        }
    }
}
