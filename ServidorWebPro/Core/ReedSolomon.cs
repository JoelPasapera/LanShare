using System;

namespace ServidorWebPro.Core
{
    /// <summary>
    /// Aritmetica en el cuerpo finito GF(256) y generacion de codewords de
    /// correccion Reed-Solomon, tal y como los define la norma QR.
    ///
    /// La multiplicacion se resuelve con tablas de logaritmos: convierte cada
    /// producto en una suma de indices, que es lo que permite calcular la
    /// correccion de un codigo completo en microsegundos.
    /// </summary>
    internal static class ReedSolomon
    {
        /// <summary>Polinomio primitivo de QR: x^8 + x^4 + x^3 + x^2 + 1.</summary>
        private const int Primitive = 0x11D;

        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        static ReedSolomon()
        {
            int value = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)value;
                Log[value] = (byte)i;

                value <<= 1;
                if ((value & 0x100) != 0) value ^= Primitive;
            }

            // Duplicar la tabla evita tener que reducir modulo 255 en cada producto.
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        private static byte Multiply(byte a, byte b)
        {
            if (a == 0 || b == 0) return 0;
            return Exp[Log[a] + Log[b]];
        }

        /// <summary>
        /// Polinomio generador de grado count: el producto de (x - alfa^i) para
        /// i de 0 a count-1. El coeficiente de mayor grado va primero.
        /// </summary>
        private static byte[] BuildGenerator(int count)
        {
            byte[] polynomial = new byte[] { 1 };

            for (int i = 0; i < count; i++)
            {
                byte[] next = new byte[polynomial.Length + 1];
                for (int j = 0; j < polynomial.Length; j++)
                {
                    next[j] ^= polynomial[j];                            // termino por x
                    next[j + 1] ^= Multiply(polynomial[j], Exp[i]);      // termino por alfa^i
                }
                polynomial = next;
            }

            return polynomial;
        }

        /// <summary>
        /// Resto de dividir el mensaje (desplazado) entre el generador. Ese
        /// resto es, literalmente, el bloque de correccion de errores.
        /// </summary>
        public static byte[] Encode(byte[] data, int errorCorrectionCount)
        {
            byte[] generator = BuildGenerator(errorCorrectionCount);
            byte[] remainder = new byte[errorCorrectionCount];

            for (int d = 0; d < data.Length; d++)
            {
                byte factor = (byte)(data[d] ^ remainder[0]);

                Array.Copy(remainder, 1, remainder, 0, errorCorrectionCount - 1);
                remainder[errorCorrectionCount - 1] = 0;

                for (int i = 0; i < errorCorrectionCount; i++)
                {
                    remainder[i] ^= Multiply(generator[i + 1], factor);
                }
            }

            return remainder;
        }
    }
}
