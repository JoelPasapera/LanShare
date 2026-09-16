using System;
using System.Collections.Generic;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Generador de codigos QR, implementado desde cero segun ISO/IEC 18004.
    ///
    /// Alcance deliberado: modo byte, nivel de correccion M y versiones 1 a 6.
    /// Eso cubre 106 caracteres, muy por encima de cualquier URL de red local,
    /// y evita el bloque de informacion de version que solo exigen las
    /// versiones 7 en adelante. Menos casos, menos donde equivocarse.
    /// </summary>
    public sealed class QrCode
    {
        private const int MinVersion = 1;
        private const int MaxVersion = 6;

        /// <summary>Nivel M: recupera hasta el 15% de modulos danados.</summary>
        private const int FormatEcBits = 0;

        // Tablas por version (indice = numero de version).
        private static readonly int[] TotalCodewords  = { 0, 26, 44, 70, 100, 134, 172 };
        private static readonly int[] EcPerBlock      = { 0, 10, 16, 26,  18,  24,  16 };
        private static readonly int[] BlockCount      = { 0,  1,  1,  1,   2,   2,   4 };

        /// <summary>Segundo centro de los patrones de alineacion; 0 en la version 1.</summary>
        private static readonly int[] AlignmentCenter = { 0,  0, 18, 22,  26,  30,  34 };

        private static readonly int[] PenaltyWeights = { 3, 3, 40, 10 };

        private readonly bool[,] _modules;
        private readonly bool[,] _isFunction;

        public int Version { get; private set; }
        public int Size { get; private set; }

        public bool this[int row, int column]
        {
            get { return _modules[row, column]; }
        }

        private QrCode(int version)
        {
            Version = version;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _isFunction = new bool[Size, Size];
        }

        // ------------------------------------------------------------------
        // Construccion
        // ------------------------------------------------------------------

        public static QrCode Encode(string text)
        {
            if (text == null) throw new ArgumentNullException("text");

            byte[] payload = Encoding.UTF8.GetBytes(text);
            int version = ChooseVersion(payload.Length);
            if (version < 0)
            {
                throw new ArgumentException(
                    "El texto no cabe en un codigo QR de version 6 o inferior.");
            }

            QrCode code = new QrCode(version);
            byte[] codewords = code.BuildCodewords(payload);

            code.DrawFunctionPatterns();
            code.DrawCodewords(codewords);
            code.ApplyBestMask();

            return code;
        }

        private static int DataCodewords(int version)
        {
            return TotalCodewords[version] - EcPerBlock[version] * BlockCount[version];
        }

        private static int ChooseVersion(int payloadLength)
        {
            for (int version = MinVersion; version <= MaxVersion; version++)
            {
                // Cabecera: 4 bits de modo mas 8 de contador (versiones 1 a 9).
                int capacity = DataCodewords(version) - 2;
                if (payloadLength <= capacity) return version;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Datos y correccion de errores
        // ------------------------------------------------------------------

        private byte[] BuildCodewords(byte[] payload)
        {
            int dataCodewords = DataCodewords(Version);

            BitBuffer bits = new BitBuffer();
            bits.Append(0x4, 4);                 // indicador de modo byte
            bits.Append(payload.Length, 8);      // contador de caracteres
            for (int i = 0; i < payload.Length; i++) bits.Append(payload[i], 8);

            int capacityBits = dataCodewords * 8;

            // Terminador, relleno hasta byte completo y bytes de relleno
            // alternos, en ese orden: lo exige la norma.
            bits.Append(0, Math.Min(4, capacityBits - bits.Length));
            bits.Append(0, (8 - bits.Length % 8) % 8);

            byte[] padding = { 0xEC, 0x11 };
            for (int i = 0; bits.Length < capacityBits; i++) bits.Append(padding[i % 2], 8);

            byte[] data = bits.ToBytes();

            // En las versiones 1 a 6 con nivel M todos los bloques miden igual,
            // asi que el entrelazado es una simple transposicion.
            int blocks = BlockCount[Version];
            int perBlock = dataCodewords / blocks;
            int ecCount = EcPerBlock[Version];

            byte[][] dataBlocks = new byte[blocks][];
            byte[][] ecBlocks = new byte[blocks][];

            for (int b = 0; b < blocks; b++)
            {
                dataBlocks[b] = new byte[perBlock];
                Array.Copy(data, b * perBlock, dataBlocks[b], 0, perBlock);
                ecBlocks[b] = ReedSolomon.Encode(dataBlocks[b], ecCount);
            }

            List<byte> result = new List<byte>(TotalCodewords[Version]);
            for (int i = 0; i < perBlock; i++)
                for (int b = 0; b < blocks; b++) result.Add(dataBlocks[b][i]);
            for (int i = 0; i < ecCount; i++)
                for (int b = 0; b < blocks; b++) result.Add(ecBlocks[b][i]);

            return result.ToArray();
        }

        // ------------------------------------------------------------------
        // Patrones fijos
        // ------------------------------------------------------------------

        private void SetFunction(int row, int column, bool dark)
        {
            _modules[row, column] = dark;
            _isFunction[row, column] = true;
        }

        private void DrawFunctionPatterns()
        {
            // Patrones de sincronizacion: alternan claro y oscuro y le dan al
            // lector la referencia de cuanto mide un modulo.
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(3, Size - 4);
            DrawFinder(Size - 4, 3);

            DrawAlignmentPatterns();

            // Reservar el area de formato antes de colocar datos.
            DrawFormatBits(0);
        }

        private void DrawFinder(int centerRow, int centerColumn)
        {
            // Anillos concentricos: oscuro hasta 1, claro en 2, oscuro en 3, y
            // el 4 es el separador claro que lo aisla del resto.
            for (int dr = -4; dr <= 4; dr++)
            {
                for (int dc = -4; dc <= 4; dc++)
                {
                    int row = centerRow + dr;
                    int column = centerColumn + dc;
                    if (row < 0 || row >= Size || column < 0 || column >= Size) continue;

                    int ring = Math.Max(Math.Abs(dr), Math.Abs(dc));
                    SetFunction(row, column, ring != 2 && ring <= 3);
                }
            }
        }

        private void DrawAlignmentPatterns()
        {
            if (AlignmentCenter[Version] == 0) return;

            int[] centers = { 6, AlignmentCenter[Version] };

            foreach (int row in centers)
            {
                foreach (int column in centers)
                {
                    // Las tres esquinas las ocupan los patrones de busqueda.
                    bool collidesWithFinder =
                        (row == 6 && column == 6) ||
                        (row == 6 && column == centers[1]) ||
                        (row == centers[1] && column == 6);
                    if (collidesWithFinder) continue;

                    for (int dr = -2; dr <= 2; dr++)
                    {
                        for (int dc = -2; dc <= 2; dc++)
                        {
                            int r = row + dr;
                            int c = column + dc;
                            if (r < 0 || r >= Size || c < 0 || c >= Size) continue;
                            SetFunction(r, c, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Los 15 bits de formato llevan su propia correccion BCH y se repiten
        /// en dos sitios: si una esquina se estropea, el lector usa la otra.
        /// </summary>
        private void DrawFormatBits(int mask)
        {
            int data = FormatEcBits << 3 | mask;

            int remainder = data;
            for (int i = 0; i < 10; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            int bits = ((data << 10) | remainder) ^ 0x5412;

            for (int i = 0; i <= 5; i++) SetFunction(8, i, GetBit(bits, i));
            SetFunction(8, 7, GetBit(bits, 6));
            SetFunction(8, 8, GetBit(bits, 7));
            SetFunction(7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, GetBit(bits, i));

            // La segunda copia reparte 7 bits en la columna junto al buscador
            // inferior y 8 en la fila bajo el superior derecho. Repartir 8 y 7
            // dejaria el modulo (8, size-8) como dato y pisaria el bit 7 con el
            // modulo oscuro: el lector leeria todo el flujo desplazado.
            for (int i = 0; i < 7; i++) SetFunction(Size - 1 - i, 8, GetBit(bits, i));
            for (int i = 7; i < 15; i++) SetFunction(8, Size - 15 + i, GetBit(bits, i));

            SetFunction(Size - 8, 8, true);   // modulo oscuro, siempre presente
        }

        private static bool GetBit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }

        // ------------------------------------------------------------------
        // Colocacion de datos
        // ------------------------------------------------------------------

        /// <summary>
        /// Recorrido en zigzag desde la esquina inferior derecha, en columnas
        /// de dos en dos, saltando la columna 6 que ocupa la sincronizacion.
        /// </summary>
        private void DrawCodewords(byte[] codewords)
        {
            int bitIndex = 0;
            int totalBits = codewords.Length * 8;

            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;

                for (int vertical = 0; vertical < Size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int column = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int row = upward ? Size - 1 - vertical : vertical;

                        if (_isFunction[row, column] || bitIndex >= totalBits) continue;

                        _modules[row, column] = GetBit(codewords[bitIndex >> 3], 7 - (bitIndex & 7));
                        bitIndex++;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Enmascarado
        // ------------------------------------------------------------------

        /// <summary>
        /// Se prueban las ocho mascaras y gana la de menor penalizacion. Sin
        /// esto, ciertos datos producen zonas uniformes o dibujos parecidos a
        /// los patrones de busqueda, y el lector se pierde.
        /// </summary>
        private void ApplyBestMask()
        {
            int bestMask = 0;
            int bestPenalty = int.MaxValue;

            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);

                int penalty = ComputePenalty();
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                }

                ApplyMask(mask);   // la mascara es su propia inversa
            }

            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
        }

        private void ApplyMask(int mask)
        {
            for (int row = 0; row < Size; row++)
            {
                for (int column = 0; column < Size; column++)
                {
                    if (_isFunction[row, column]) continue;
                    if (MaskCondition(mask, row, column)) _modules[row, column] ^= true;
                }
            }
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

        // Secuencia 1:1:3:1:1 rodeada de zona clara, que es lo que imita a un
        // patron de busqueda y confunde al lector.
        private static readonly bool[] FinderLike =
            { true, false, true, true, true, false, true, false, false, false, false };

        private int ComputePenalty()
        {
            int penalty = 0;
            penalty += PenaltyRuns();
            penalty += PenaltyBlocks();
            penalty += PenaltyFinderLike();
            penalty += PenaltyBalance();
            return penalty;
        }

        private int PenaltyRuns()
        {
            int penalty = 0;

            for (int line = 0; line < Size; line++)
            {
                penalty += RunPenaltyForLine(line, true);
                penalty += RunPenaltyForLine(line, false);
            }

            return penalty;
        }

        private int RunPenaltyForLine(int line, bool horizontal)
        {
            int penalty = 0;
            int runLength = 1;
            bool runColor = horizontal ? _modules[line, 0] : _modules[0, line];

            for (int i = 1; i < Size; i++)
            {
                bool current = horizontal ? _modules[line, i] : _modules[i, line];
                if (current == runColor)
                {
                    runLength++;
                }
                else
                {
                    if (runLength >= 5) penalty += PenaltyWeights[0] + (runLength - 5);
                    runColor = current;
                    runLength = 1;
                }
            }

            if (runLength >= 5) penalty += PenaltyWeights[0] + (runLength - 5);
            return penalty;
        }

        private int PenaltyBlocks()
        {
            int penalty = 0;

            for (int row = 0; row < Size - 1; row++)
            {
                for (int column = 0; column < Size - 1; column++)
                {
                    bool color = _modules[row, column];
                    if (color == _modules[row, column + 1] &&
                        color == _modules[row + 1, column] &&
                        color == _modules[row + 1, column + 1])
                    {
                        penalty += PenaltyWeights[1];
                    }
                }
            }

            return penalty;
        }

        private int PenaltyFinderLike()
        {
            int penalty = 0;
            int length = FinderLike.Length;

            for (int line = 0; line < Size; line++)
            {
                for (int start = 0; start + length <= Size; start++)
                {
                    if (MatchesPattern(line, start, true, false)) penalty += PenaltyWeights[2];
                    if (MatchesPattern(line, start, true, true)) penalty += PenaltyWeights[2];
                    if (MatchesPattern(line, start, false, false)) penalty += PenaltyWeights[2];
                    if (MatchesPattern(line, start, false, true)) penalty += PenaltyWeights[2];
                }
            }

            return penalty;
        }

        private bool MatchesPattern(int line, int start, bool horizontal, bool reversed)
        {
            int length = FinderLike.Length;

            for (int i = 0; i < length; i++)
            {
                bool expected = FinderLike[reversed ? length - 1 - i : i];
                bool actual = horizontal ? _modules[line, start + i] : _modules[start + i, line];
                if (actual != expected) return false;
            }

            return true;
        }

        private int PenaltyBalance()
        {
            int dark = 0;
            for (int row = 0; row < Size; row++)
                for (int column = 0; column < Size; column++)
                    if (_modules[row, column]) dark++;

            int total = Size * Size;
            int deviation = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return Math.Max(0, deviation) * PenaltyWeights[3];
        }

        // ------------------------------------------------------------------

        /// <summary>Acumulador de bits en orden de mayor peso a menor.</summary>
        private sealed class BitBuffer
        {
            private readonly List<bool> _bits = new List<bool>();

            public int Length { get { return _bits.Count; } }

            public void Append(int value, int bitCount)
            {
                for (int i = bitCount - 1; i >= 0; i--) _bits.Add(((value >> i) & 1) != 0);
            }

            public byte[] ToBytes()
            {
                byte[] result = new byte[(_bits.Count + 7) / 8];
                for (int i = 0; i < _bits.Count; i++)
                {
                    if (_bits[i]) result[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                }
                return result;
            }
        }
    }
}
