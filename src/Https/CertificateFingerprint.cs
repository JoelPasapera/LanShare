using System;
using System.Security.Cryptography;
using System.Text;

namespace LanShare.Https
{
    /// <summary>
    /// Huella SHA-256 de un certificado, escrita como la muestran Windows,
    /// Android e iOS: pares hexadecimales en mayusculas separados por dos
    /// puntos. Sirve para comprobar a ojo que el certificado que se instala
    /// en el movil es el mismo que tiene el PC.
    /// </summary>
    public static class CertificateFingerprint
    {
        public static string Sha256(byte[] certificateDer)
        {
            if (certificateDer == null) throw new ArgumentNullException("certificateDer");

            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(certificateDer);
            }
            return Format(hash);
        }

        /// <summary>
        /// La huella en renglones de <paramref name="pairsPerLine"/> pares, cada
        /// uno salvo el ultimo acabado en dos puntos: asi no se parte un par
        /// por la mitad y se compara a ojo sin perder la cuenta.
        /// </summary>
        public static string InLines(string fingerprint, int pairsPerLine)
        {
            if (pairsPerLine < 1) throw new ArgumentOutOfRangeException("pairsPerLine");
            if (string.IsNullOrEmpty(fingerprint)) return "";

            string[] pairs = fingerprint.Split(':');
            StringBuilder text = new StringBuilder(fingerprint.Length + pairs.Length / pairsPerLine * 2);
            for (int i = 0; i < pairs.Length; i++)
            {
                if (i > 0) text.Append(i % pairsPerLine == 0 ? ":\r\n" : ":");
                text.Append(pairs[i]);
            }
            return text.ToString();
        }

        /// <summary>"AB:CD:EF..." a partir de los bytes de una huella.</summary>
        public static string Format(byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException("hash");

            StringBuilder text = new StringBuilder(hash.Length * 3);
            for (int i = 0; i < hash.Length; i++)
            {
                if (i > 0) text.Append(':');
                text.Append(hash[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }
    }
}
