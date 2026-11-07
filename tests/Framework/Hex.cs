using System;
using System.Globalization;
using System.Text;

namespace LanShare.Tests
{
    /// <summary>
    /// Bytes escritos en hexadecimal, para comparar con vectores de referencia
    /// generados fuera de .NET (OpenSSL, la biblioteca cryptography de Python).
    /// </summary>
    public static class Hex
    {
        /// <summary>"3016a0" a {0x30, 0x16, 0xA0}. Admite espacios y mayusculas.</summary>
        public static byte[] Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");

            string compact = text.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            if (compact.Length % 2 != 0) throw new FormatException("Hexadecimal con un numero impar de cifras.");

            byte[] result = new byte[compact.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = byte.Parse(compact.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return result;
        }

        /// <summary>{0x30, 0x16} a "3016", en minusculas como los vectores de referencia.</summary>
        public static string Format(byte[] data)
        {
            if (data == null) return "";

            StringBuilder text = new StringBuilder(data.Length * 2);
            foreach (byte value in data) text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
