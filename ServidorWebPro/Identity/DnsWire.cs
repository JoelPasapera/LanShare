using System;
using System.Collections.Generic;
using System.Text;

namespace ServidorWebPro.Identity
{
    /// <summary>
    /// Serializacion y lectura de nombres en formato DNS. La comparten mDNS
    /// (puerto 5353) y NetBIOS (137), que usan el mismo encabezado de 12 bytes.
    /// </summary>
    internal static class DnsWire
    {
        public static void WriteName(List<byte> buffer, string name)
        {
            foreach (string label in name.Split('.'))
            {
                if (label.Length == 0) continue;
                buffer.Add((byte)label.Length);
                foreach (char c in label) buffer.Add((byte)c);
            }
            buffer.Add(0);
        }

        /// <summary>
        /// Lee un nombre con soporte de compresion por punteros (0xC0). El
        /// parametro next queda apuntando al byte siguiente en el flujo real,
        /// no en el destino del puntero.
        /// </summary>
        public static string ReadName(byte[] data, int offset, out int next)
        {
            StringBuilder sb = new StringBuilder();
            int pos = offset;
            int jumps = 0;
            next = -1;

            while (pos >= 0 && pos < data.Length)
            {
                int len = data[pos];
                if (len == 0)
                {
                    pos++;
                    if (next < 0) next = pos;
                    break;
                }

                if ((len & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= data.Length) break;
                    int pointer = ((len & 0x3F) << 8) | data[pos + 1];
                    if (next < 0) next = pos + 2;
                    pos = pointer;
                    // Un paquete malicioso puede encadenar punteros circulares.
                    if (++jumps > 16) break;
                    continue;
                }

                pos++;
                if (pos + len > data.Length) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(data, pos, len));
                pos += len;
            }

            if (next < 0) next = pos;
            return sb.ToString();
        }

        public static int SkipName(byte[] data, int offset)
        {
            int next;
            ReadName(data, offset, out next);
            return next;
        }

        public static string StripLocalSuffix(string name)
        {
            if (name != null && name.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 6);
            return name;
        }
    }
}
