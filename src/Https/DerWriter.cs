using System;
using System.IO;

namespace LanShare.Https
{
    /// <summary>
    /// Codificador DER minimo, escrito a mano.
    ///
    /// .NET Framework 4.8 sabe construir casi todas las extensiones de un
    /// certificado, pero no las dos que hacen falta aqui: las restricciones de
    /// nombre y el identificador de la clave de la autoridad. Ambas son
    /// estructuras ASN.1 pequenas, y esto es lo justo para escribirlas.
    ///
    /// DER es TLV (etiqueta, longitud, valor) con una sola forma valida de
    /// escribir cada cosa: dos codificadores correctos producen los mismos
    /// bytes, y las pruebas comparan estos con los de uno independiente.
    /// </summary>
    public static class DerWriter
    {
        public const byte TagOctetString = 0x04;
        public const byte TagSequence = 0x30;

        /// <summary>Clase "especifica de contexto", primitiva: [n] IMPLICIT sobre un valor simple.</summary>
        private const byte ContextPrimitiveBase = 0x80;

        /// <summary>Clase "especifica de contexto", construida: [n] sobre una estructura.</summary>
        private const byte ContextConstructedBase = 0xA0;

        /// <summary>Etiquetas de una sola posicion: 0 a 30. Las de aqui son 0, 2 y 7.</summary>
        private const int MaxShortTagNumber = 30;

        /// <summary>Etiqueta, longitud y valor.</summary>
        public static byte[] Encode(byte tag, byte[] content)
        {
            if (content == null) throw new ArgumentNullException("content");

            byte[] length = EncodeLength(content.Length);
            byte[] result = new byte[1 + length.Length + content.Length];
            result[0] = tag;
            Buffer.BlockCopy(length, 0, result, 1, length.Length);
            Buffer.BlockCopy(content, 0, result, 1 + length.Length, content.Length);
            return result;
        }

        /// <summary>
        /// Longitud en DER: un byte hasta 127; por encima, 0x80 mas el numero
        /// de bytes que siguen, y la longitud en big endian sin ceros sobrantes.
        /// </summary>
        public static byte[] EncodeLength(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            if (length < 0x80) return new byte[] { (byte)length };

            int byteCount = 0;
            for (int remaining = length; remaining > 0; remaining >>= 8) byteCount++;

            byte[] result = new byte[1 + byteCount];
            result[0] = (byte)(0x80 | byteCount);
            for (int i = byteCount; i >= 1; i--)
            {
                result[i] = (byte)(length & 0xFF);
                length >>= 8;
            }
            return result;
        }

        public static byte[] Sequence(params byte[][] items)
        {
            return Encode(TagSequence, Concat(items));
        }

        public static byte[] OctetString(byte[] content)
        {
            return Encode(TagOctetString, content);
        }

        /// <summary>[number] IMPLICIT sobre un valor primitivo (una cadena, unos bytes).</summary>
        public static byte[] ContextPrimitive(int number, byte[] content)
        {
            return Encode(ContextTag(ContextPrimitiveBase, number), content);
        }

        /// <summary>[number] sobre una estructura: en IMPLICIT sustituye a la etiqueta de la SEQUENCE.</summary>
        public static byte[] ContextConstructed(int number, byte[] content)
        {
            return Encode(ContextTag(ContextConstructedBase, number), content);
        }

        public static byte[] Concat(params byte[][] parts)
        {
            if (parts == null) throw new ArgumentNullException("parts");

            using (MemoryStream buffer = new MemoryStream())
            {
                foreach (byte[] part in parts)
                {
                    if (part == null) throw new ArgumentException("Una de las partes es nula.", "parts");
                    buffer.Write(part, 0, part.Length);
                }
                return buffer.ToArray();
            }
        }

        private static byte ContextTag(byte classBits, int number)
        {
            if (number < 0 || number > MaxShortTagNumber) throw new ArgumentOutOfRangeException("number");
            return (byte)(classBits | number);
        }
    }
}
