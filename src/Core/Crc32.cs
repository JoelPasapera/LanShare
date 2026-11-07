namespace LanShare.Core
{
    /// <summary>
    /// CRC-32 de IEEE 802.3, el de ZIP, PNG y Ethernet: polinomio reflejado
    /// 0xEDB88320, valor inicial y final 0xFFFFFFFF.
    ///
    /// Implementado por "slicing-by-8": ocho tablas de 256 entradas permiten
    /// consumir ocho bytes por vuelta con ocho consultas independientes, en
    /// lugar de un byte por vuelta con una cadena de dependencias. Calcular el
    /// CRC de lo que se envia no debe ser nunca lo que frene una descarga.
    /// </summary>
    public sealed class Crc32
    {
        private const uint Polynomial = 0xEDB88320;

        /// <summary>Tabla k en las posiciones [k*256, k*256+255].</summary>
        private static readonly uint[] Tables = BuildTables();

        private uint _state = 0xFFFFFFFF;

        public uint Value
        {
            get { return ~_state; }
        }

        public void Reset()
        {
            _state = 0xFFFFFFFF;
        }

        public void Update(byte[] data, int offset, int count)
        {
            uint crc = _state;
            uint[] t = Tables;
            int i = offset;
            int end = offset + count;

            while (end - i >= 8)
            {
                uint low = crc ^ (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
                uint high = (uint)(data[i + 4] | data[i + 5] << 8 | data[i + 6] << 16 | data[i + 7] << 24);

                crc = t[1792 + (int)(low & 0xFF)] ^ t[1536 + (int)((low >> 8) & 0xFF)] ^
                      t[1280 + (int)((low >> 16) & 0xFF)] ^ t[1024 + (int)(low >> 24)] ^
                      t[768 + (int)(high & 0xFF)] ^ t[512 + (int)((high >> 8) & 0xFF)] ^
                      t[256 + (int)((high >> 16) & 0xFF)] ^ t[(int)(high >> 24)];
                i += 8;
            }

            while (i < end)
            {
                crc = t[(int)((crc ^ data[i]) & 0xFF)] ^ (crc >> 8);
                i++;
            }

            _state = crc;
        }

        /// <summary>CRC de un bloque completo.</summary>
        public static uint Compute(byte[] data, int offset, int count)
        {
            Crc32 crc = new Crc32();
            crc.Update(data, offset, count);
            return crc.Value;
        }

        private static uint[] BuildTables()
        {
            uint[] t = new uint[8 * 256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
                t[n] = c;
            }

            // La tabla k da el efecto de un byte seguido de k bytes a cero.
            for (int k = 1; k < 8; k++)
            {
                for (int n = 0; n < 256; n++)
                {
                    uint previous = t[(k - 1) * 256 + n];
                    t[k * 256 + n] = (previous >> 8) ^ t[(int)(previous & 0xFF)];
                }
            }
            return t;
        }
    }
}
