using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace LanShare.Https
{
    /// <summary>
    /// Lo unico para lo que vale la autoridad de LanShare: nombres y
    /// direcciones de la red local.
    ///
    /// Va escrito dentro del propio certificado raiz (extension de
    /// restricciones de nombre, RFC 5280 4.2.1.10) y lo hacen cumplir Chrome,
    /// Firefox, Windows y OpenSSL. Asi, aunque alguien robara la clave privada
    /// de la autoridad, no podria usarla para suplantar una web de Internet en
    /// los moviles que la tienen instalada: solo direcciones privadas,
    /// localhost y nombres .local.
    ///
    /// La misma tabla decide que direcciones entran en el certificado del
    /// servidor: nunca se emite uno que la propia autoridad no podria avalar.
    /// </summary>
    public static class LocalNameConstraints
    {
        public const string Oid = "2.5.29.30";

        /// <summary>
        /// Dominios permitidos. Una restriccion "local" admite "local" y
        /// cualquier nombre que termine en ".local" (los de mDNS).
        /// </summary>
        private static readonly string[] PermittedDnsNames = { "localhost", "local" };

        /// <summary>
        /// Redes permitidas. En IPv4: las privadas (RFC 1918), el bucle local y
        /// las de enlace local. En IPv6: ::1, enlace local y direcciones unicas
        /// locales. El espacio compartido 100.64/10 (CGNAT, Tailscale) queda
        /// fuera a proposito: no es la red de casa.
        /// </summary>
        private static readonly string[] PermittedNetworks =
        {
            "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "169.254.0.0/16",
            "::1/128", "fe80::/10", "fc00::/7"
        };

        /// <summary>
        /// La extension codificada: NameConstraints con solo permittedSubtrees.
        /// Cada GeneralSubtree lleva unicamente su base (minimum vale 0 por
        /// defecto y DER no escribe los valores por defecto).
        /// </summary>
        public static byte[] Encode()
        {
            byte[][] subtrees = new byte[PermittedDnsNames.Length + PermittedNetworks.Length][];
            int index = 0;

            foreach (string name in PermittedDnsNames)
            {
                // dNSName es [2] IMPLICIT IA5String.
                subtrees[index++] = DerWriter.Sequence(DerWriter.ContextPrimitive(2, Encoding.ASCII.GetBytes(name)));
            }

            foreach (string network in PermittedNetworks)
            {
                // iPAddress es [7] IMPLICIT OCTET STRING: direccion seguida de mascara.
                IpNetwork parsed = IpNetwork.Parse(network);
                subtrees[index++] = DerWriter.Sequence(
                    DerWriter.ContextPrimitive(7, DerWriter.Concat(parsed.Address, parsed.Mask)));
            }

            // permittedSubtrees es [0] IMPLICIT GeneralSubtrees.
            return DerWriter.Sequence(DerWriter.ContextConstructed(0, DerWriter.Concat(subtrees)));
        }

        /// <summary>Si una direccion cae en alguna de las redes permitidas.</summary>
        public static bool PermitsAddress(IPAddress address)
        {
            if (address == null) return false;
            byte[] bytes = address.GetAddressBytes();

            foreach (string network in PermittedNetworks)
            {
                if (IpNetwork.Parse(network).Contains(bytes)) return true;
            }
            return false;
        }

        /// <summary>Si un nombre de dominio cae bajo alguno de los permitidos, como lo comprueba un navegador.</summary>
        public static bool PermitsDnsName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.TrimEnd('.').ToLowerInvariant();

            foreach (string permitted in PermittedDnsNames)
            {
                if (lower == permitted || lower.EndsWith("." + permitted, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Una red escrita como "direccion/prefijo".</summary>
        private sealed class IpNetwork
        {
            public byte[] Address;
            public byte[] Mask;

            public static IpNetwork Parse(string text)
            {
                int slash = text.IndexOf('/');
                IPAddress address = IPAddress.Parse(text.Substring(0, slash));
                int prefix = int.Parse(text.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture);

                IpNetwork network = new IpNetwork();
                network.Address = address.GetAddressBytes();
                network.Mask = new byte[network.Address.Length];
                for (int bit = 0; bit < prefix; bit++) network.Mask[bit / 8] |= (byte)(0x80 >> (bit % 8));
                return network;
            }

            public bool Contains(byte[] candidate)
            {
                if (candidate == null || candidate.Length != Address.Length) return false;
                for (int i = 0; i < Address.Length; i++)
                {
                    if ((candidate[i] & Mask[i]) != (Address[i] & Mask[i])) return false;
                }
                return true;
            }
        }
    }
}
