using System.Net;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Lo unico que puede avalar la autoridad local: la red de casa. Si esta
    /// tabla se equivoca, o el certificado del servidor se rechaza entero, o
    /// la autoridad vale para mas de lo que deberia.
    /// </summary>
    public static class NameConstraintsTests
    {
        /// <summary>
        /// La misma extension codificada por la biblioteca cryptography de
        /// Python (otra implementacion de DER, sin codigo en comun) y aceptada
        /// por OpenSSL y por el verificador de Chromium.
        /// </summary>
        private const string ReferenceEncoding =
            "3081c1a081be300b82096c6f63616c686f7374300782056c6f63616c300a87080a000000ff000000" +
            "300a8708ac100000fff00000300a8708c0a80000ffff0000300a87087f000000ff000000" +
            "300a8708a9fe0000ffff0000" +
            "3022872000000000000000000000000000000001ffffffffffffffffffffffffffffffff" +
            "30228720fe800000000000000000000000000000ffc00000000000000000000000000000" +
            "30228720fc000000000000000000000000000000fe000000000000000000000000000000";

        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / restricciones de nombre");

            runner.Test("CLAVE: la extension coincide byte a byte con la de otra implementacion", delegate
            {
                byte[] expected = Hex.Parse(ReferenceEncoding);
                Check.Equal(196, expected.Length, "longitud del vector");
                Check.Equal(ReferenceEncoding, Hex.Format(LocalNameConstraints.Encode()), "codificacion");
            });

            runner.Test("cada llamada devuelve una copia nueva", delegate
            {
                byte[] first = LocalNameConstraints.Encode();
                first[0] = 0;
                Check.Equal(ReferenceEncoding, Hex.Format(LocalNameConstraints.Encode()), "sin efectos entre llamadas");
            });

            runner.Test("admite las redes privadas, el bucle local y el enlace local", delegate
            {
                string[] permitted =
                {
                    "10.0.0.1", "10.255.255.254", "172.16.0.1", "172.31.255.255", "192.168.0.1", "192.168.1.5",
                    "127.0.0.1", "169.254.10.20", "::1", "fe80::1", "fd12:3456:789a::1", "fc00::5"
                };
                foreach (string text in permitted)
                {
                    Check.True(LocalNameConstraints.PermitsAddress(IPAddress.Parse(text)), text);
                }
            });

            runner.Test("rechaza Internet y el espacio compartido de CGNAT", delegate
            {
                string[] rejected =
                {
                    "8.8.8.8", "1.1.1.1", "11.0.0.1", "172.15.255.255", "172.32.0.1", "192.169.0.1",
                    "100.64.0.1", "100.100.100.100", "0.0.0.0", "255.255.255.255", "2001:4860:4860::8888", "::"
                };
                foreach (string text in rejected)
                {
                    Check.False(LocalNameConstraints.PermitsAddress(IPAddress.Parse(text)), text);
                }
                Check.False(LocalNameConstraints.PermitsAddress(null), "nula");
            });

            runner.Test("nombres: localhost, .local y nada mas", delegate
            {
                Check.True(LocalNameConstraints.PermitsDnsName("localhost"), "localhost");
                Check.True(LocalNameConstraints.PermitsDnsName("LOCALHOST"), "mayusculas");
                Check.True(LocalNameConstraints.PermitsDnsName("app.localhost"), "subdominio de localhost");
                Check.True(LocalNameConstraints.PermitsDnsName("joel-pc.local"), "nombre mDNS");
                Check.True(LocalNameConstraints.PermitsDnsName("JOEL-PC.LOCAL."), "con punto final");

                Check.False(LocalNameConstraints.PermitsDnsName("example.com"), "Internet");
                Check.False(LocalNameConstraints.PermitsDnsName("localhost.example.com"), "localhost como prefijo");
                Check.False(LocalNameConstraints.PermitsDnsName("notlocal"), "termina en local sin punto");
                Check.False(LocalNameConstraints.PermitsDnsName("mylocalhost"), "contiene localhost");
                Check.False(LocalNameConstraints.PermitsDnsName(""), "vacio");
                Check.False(LocalNameConstraints.PermitsDnsName(null), "nulo");
            });
        }
    }
}
