using System;
using System.Collections.Generic;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Decodificador de codigos QR escrito para las pruebas.
    ///
    /// No reutiliza nada del codificador salvo las tablas de la norma: construye
    /// su propio campo finito, reconstruye por su cuenta que modulos son de
    /// servicio y evalua los sindromes Reed-Solomon con matematica distinta
    /// (Horner sobre potencias de alfa, frente al resto de una division).
    ///
    /// Leccion aprendida: la primera version leia los bits de formato con la
    /// misma idea equivocada que el codificador (fila por columna), asi que las
    /// pruebas pasaban y ningun movil leia los codigos. Ahora el formato se lee
    /// exactamente como lo hace un lector real (ZXing) y, ademas, QrCodeTests
    /// compara con matrices que si leyo un lector independiente.
    /// </summary>
    internal static class QrDecoder
    {
        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        private static readonly int[] TotalCodewords = { 0, 26, 44, 70, 100, 134, 172 };
        private static readonly int[] EcPerBlock = { 0, 10, 16, 26, 18, 24, 16 };
        private static readonly int[] BlockCount = { 0, 1, 1, 1, 2, 2, 4 };
        private static readonly int[] AlignmentCenter = { 0, 0, 18, 22, 26, 30, 34 };

        /// <summary>Bits de relleno que no forman codeword, por version.</summary>
        private static readonly int[] RemainderBits = { 0, 0, 7, 7, 7, 7, 7 };

        private static readonly Dictionary<int, int> FormatTable = new Dictionary<int, int>();

        static QrDecoder()
        {
            int value = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)value;
                Log[value] = (byte)i;
                value <<= 1;
                if ((value & 0x100) != 0) value ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];

            for (int data = 0; data < 32; data++) FormatTable[EncodeFormat(data)] = data;
        }

        private static int EncodeFormat(int data5)
        {
            int remainder = data5;
            for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            return ((data5 << 10) | remainder) ^ 0x5412;
        }

        private static byte Multiply(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        public sealed class Result
        {
            public int Version;
            public int Mask;
            public int FormatDistance;
            public int ErrorCorrectionLevel;
            public bool SyndromesZero;
            public int Mode;
            public int DeclaredLength;
            public string Text;
        }

        public static Result Decode(QrCode code)
        {
            if (code == null) throw new ArgumentNullException("code");

            bool[,] modules = new bool[code.Size, code.Size];
            for (int row = 0; row < code.Size; row++)
                for (int column = 0; column < code.Size; column++) modules[row, column] = code[row, column];
            return Decode(modules);
        }

        /// <summary>
        /// Decodifica una matriz cualquiera, [fila, columna] y true para oscuro:
        /// tambien las de referencia que ya leyo un lector independiente, que es
        /// lo que ata este decodificador a la realidad.
        /// </summary>
        public static Result Decode(bool[,] modules)
        {
            if (modules == null) throw new ArgumentNullException("modules");

            int size = modules.GetLength(0);
            int version = (size - 17) / 4;
            if (modules.GetLength(1) != size || version < 1 || version > 6 || version * 4 + 17 != size)
            {
                throw new ArgumentException("Tamano de matriz QR no soportado: " + size, "modules");
            }

            Result result = new Result();
            result.Version = version;

            int format = ReadFormat(modules, out result.FormatDistance);
            result.ErrorCorrectionLevel = format >> 3;
            result.Mask = format & 7;

            byte[] codewords = ReadCodewords(modules, version, result.Mask);

            int blocks = BlockCount[version];
            int ecCount = EcPerBlock[version];
            int perBlock = (TotalCodewords[version] - ecCount * blocks) / blocks;

            byte[][] data = new byte[blocks][];
            byte[][] ec = new byte[blocks][];
            for (int b = 0; b < blocks; b++)
            {
                data[b] = new byte[perBlock];
                ec[b] = new byte[ecCount];
            }

            int index = 0;
            for (int i = 0; i < perBlock; i++)
                for (int b = 0; b < blocks; b++) data[b][i] = codewords[index++];
            for (int i = 0; i < ecCount; i++)
                for (int b = 0; b < blocks; b++) ec[b][i] = codewords[index++];

            result.SyndromesZero = true;
            for (int b = 0; b < blocks; b++)
            {
                if (!SyndromesAreZero(data[b], ec[b], ecCount)) result.SyndromesZero = false;
            }

            ReadPayload(data, blocks, perBlock, result);
            return result;
        }

        /// <summary>
        /// Lee las dos copias de los quince bits de formato en el orden en que
        /// lo hace ZXing (BitMatrixParser.readFormatInformation): el primer
        /// modulo leido es el bit mas significativo. En coordenadas (fila,
        /// columna), la primera copia recorre la fila 8 de la columna 0 a la 8
        /// y luego sube por la columna 8; la segunda sube por la columna 8 desde
        /// la ultima fila y sigue por la fila 8 hasta el borde derecho.
        ///
        /// La distancia devuelta es la peor de las dos copias, que deberia ser
        /// cero. Si las copias no dicen lo mismo se devuelve 99.
        /// </summary>
        private static int ReadFormat(bool[,] code, out int distance)
        {
            int size = code.GetLength(0);

            int first = 0;
            for (int column = 0; column <= 5; column++) first = Push(first, code[8, column]);
            first = Push(first, code[8, 7]);
            first = Push(first, code[8, 8]);
            first = Push(first, code[7, 8]);
            for (int row = 5; row >= 0; row--) first = Push(first, code[row, 8]);

            int second = 0;
            for (int row = size - 1; row >= size - 7; row--) second = Push(second, code[row, 8]);
            for (int column = size - 8; column < size; column++) second = Push(second, code[8, column]);

            int firstDistance, secondDistance;
            int format = Nearest(first, out firstDistance);
            int other = Nearest(second, out secondDistance);

            distance = format == other ? Math.Max(firstDistance, secondDistance) : 99;
            return format;
        }

        private static int Push(int value, bool dark)
        {
            return (value << 1) | (dark ? 1 : 0);
        }

        /// <summary>El dato de formato legal mas cercano a los bits leidos.</summary>
        private static int Nearest(int bits, out int distance)
        {
            int best = 0;
            distance = 99;
            foreach (KeyValuePair<int, int> entry in FormatTable)
            {
                int d = CountBits(entry.Key ^ bits);
                if (d < distance) { distance = d; best = entry.Value; }
            }
            return best;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0) { count += value & 1; value >>= 1; }
            return count;
        }

        /// <summary>
        /// Reconstruye el mapa de modulos de servicio sin mirar el codificador:
        /// las tres esquinas con separador, la sincronizacion, el area de
        /// formato y el patron de alineacion.
        /// </summary>
        private static bool[,] BuildFunctionMap(int size, int version)
        {
            bool[,] map = new bool[size, size];

            Fill(map, size, 0, 0, 8, 8);
            Fill(map, size, 0, size - 8, 8, size - 1);
            Fill(map, size, size - 8, 0, size - 1, 8);

            for (int i = 0; i < size; i++) { map[6, i] = true; map[i, 6] = true; }

            if (version >= 2)
            {
                int center = AlignmentCenter[version];
                int[] centers = { 6, center };
                foreach (int row in centers)
                {
                    foreach (int column in centers)
                    {
                        if ((row == 6 && column == 6) ||
                            (row == 6 && column == center) ||
                            (row == center && column == 6)) continue;
                        Fill(map, size, row - 2, column - 2, row + 2, column + 2);
                    }
                }
            }

            return map;
        }

        private static void Fill(bool[,] map, int size, int r0, int c0, int r1, int c1)
        {
            for (int r = Math.Max(0, r0); r <= Math.Min(size - 1, r1); r++)
                for (int c = Math.Max(0, c0); c <= Math.Min(size - 1, c1); c++) map[r, c] = true;
        }

        public static int CountFunctionModules(int size, int version)
        {
            bool[,] map = BuildFunctionMap(size, version);
            int count = 0;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++) if (map[r, c]) count++;
            return count;
        }

        public static int ExpectedFunctionModules(int version)
        {
            int size = version * 4 + 17;
            return size * size - (TotalCodewords[version] * 8 + RemainderBits[version]);
        }

        private static bool MaskCondition(int mask, int row, int column)
        {
            switch (mask)
            {
                case 0: return (column + row) % 2 == 0;
                case 1: return row % 2 == 0;
                case 2: return column % 3 == 0;
                case 3: return (column + row) % 3 == 0;
                case 4: return (column / 3 + row / 2) % 2 == 0;
                case 5: return column * row % 2 + column * row % 3 == 0;
                case 6: return (column * row % 2 + column * row % 3) % 2 == 0;
                case 7: return ((column + row) % 2 + column * row % 3) % 2 == 0;
                default: return false;
            }
        }

        private static byte[] ReadCodewords(bool[,] code, int version, int mask)
        {
            int size = code.GetLength(0);
            bool[,] functions = BuildFunctionMap(size, version);

            List<int> bits = new List<int>(size * size);

            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;

                for (int vertical = 0; vertical < size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int column = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int row = upward ? size - 1 - vertical : vertical;

                        if (functions[row, column]) continue;

                        bool dark = code[row, column];
                        if (MaskCondition(mask, row, column)) dark = !dark;
                        bits.Add(dark ? 1 : 0);
                    }
                }
            }

            int total = TotalCodewords[version];
            byte[] codewords = new byte[total];
            for (int i = 0; i < total; i++)
            {
                int value = 0;
                for (int b = 0; b < 8; b++) value = (value << 1) | bits[i * 8 + b];
                codewords[i] = (byte)value;
            }
            return codewords;
        }

        /// <summary>
        /// Evalua el polinomio del bloque en las primeras potencias de alfa. Si
        /// la correccion es correcta, todos los sindromes valen cero.
        /// </summary>
        private static bool SyndromesAreZero(byte[] data, byte[] ec, int ecCount)
        {
            byte[] full = new byte[data.Length + ec.Length];
            Array.Copy(data, 0, full, 0, data.Length);
            Array.Copy(ec, 0, full, data.Length, ec.Length);

            for (int i = 0; i < ecCount; i++)
            {
                byte syndrome = 0;
                for (int j = 0; j < full.Length; j++) syndrome = (byte)(Multiply(syndrome, Exp[i]) ^ full[j]);
                if (syndrome != 0) return false;
            }
            return true;
        }

        private static void ReadPayload(byte[][] data, int blocks, int perBlock, Result result)
        {
            List<int> stream = new List<int>(blocks * perBlock * 8);
            for (int b = 0; b < blocks; b++)
                for (int i = 0; i < perBlock; i++)
                    for (int bit = 7; bit >= 0; bit--) stream.Add((data[b][i] >> bit) & 1);

            result.Mode = ReadNumber(stream, 0, 4);
            result.DeclaredLength = ReadNumber(stream, 4, 8);

            int available = (stream.Count - 12) / 8;
            int length = Math.Min(result.DeclaredLength, Math.Max(0, available));

            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++) payload[i] = (byte)ReadNumber(stream, 12 + i * 8, 8);

            result.Text = Encoding.UTF8.GetString(payload);
        }

        private static int ReadNumber(List<int> stream, int offset, int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                if (offset + i >= stream.Count) break;
                value = (value << 1) | stream[offset + i];
            }
            return value;
        }
    }
}
