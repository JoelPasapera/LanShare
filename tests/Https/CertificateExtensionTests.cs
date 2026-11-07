using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Las extensiones que LanShare escribe o lee a mano y la huella que se
    /// compara a ojo con la del movil.
    /// </summary>
    public static class CertificateExtensionTests
    {
        /// <summary>AuthorityKeyIdentifier del identificador 00..13, codificado por la biblioteca cryptography de Python.</summary>
        private const string ReferenceAuthorityKeyId = "30168014000102030405060708090a0b0c0d0e0f10111213";

        private static byte[] Sequential(int count)
        {
            byte[] data = new byte[count];
            for (int i = 0; i < count; i++) data[i] = (byte)i;
            return data;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / extensiones del certificado");

            runner.Test("CLAVE: el AKI coincide con el de otra implementacion", delegate
            {
                Check.Equal(ReferenceAuthorityKeyId,
                            Hex.Format(CertificateExtensionData.EncodeAuthorityKeyIdentifier(Sequential(20))), "AKI");
            });

            runner.Test("el AKI se lee de vuelta, aunque lleve mas campos", delegate
            {
                byte[] keyId = Sequential(20);
                Check.BytesEqual(keyId, CertificateExtensionData.ReadAuthorityKeyIdentifier(
                    CertificateExtensionData.EncodeAuthorityKeyIdentifier(keyId)), "solo el identificador");

                // Forma completa de RFC 5280: [0] identificador, [1] emisor, [2] numero de serie.
                byte[] full = DerWriter.Sequence(
                    DerWriter.ContextPrimitive(0, keyId),
                    DerWriter.ContextConstructed(1, new byte[0]),
                    DerWriter.ContextPrimitive(2, new byte[] { 0x05 }));
                Check.BytesEqual(keyId, CertificateExtensionData.ReadAuthorityKeyIdentifier(full), "forma completa");

                byte[] withoutKeyId = DerWriter.Sequence(DerWriter.ContextPrimitive(2, new byte[] { 0x05 }));
                Check.Null(CertificateExtensionData.ReadAuthorityKeyIdentifier(withoutKeyId), "sin identificador");
                Check.Null(CertificateExtensionData.ReadAuthorityKeyIdentifier(null), "sin extension");
            });

            runner.Test("un AKI sin identificador no se puede escribir", delegate
            {
                Check.Throws(delegate { CertificateExtensionData.EncodeAuthorityKeyIdentifier(null); }, "nulo");
                Check.Throws(delegate { CertificateExtensionData.EncodeAuthorityKeyIdentifier(new byte[0]); }, "vacio");
            });

            runner.Test("el SKI se lee de un OCTET STRING", delegate
            {
                byte[] keyId = Sequential(20);
                Check.BytesEqual(keyId, CertificateExtensionData.ReadSubjectKeyIdentifier(DerWriter.OctetString(keyId)),
                                 "identificador");
                Check.Null(CertificateExtensionData.ReadSubjectKeyIdentifier(null), "sin extension");
            });

            runner.Test("nombres alternativos: dominios y direcciones, el resto se salta", delegate
            {
                byte[] san = DerWriter.Sequence(
                    DerWriter.ContextPrimitive(2, Encoding.ASCII.GetBytes("localhost")),
                    DerWriter.ContextPrimitive(1, Encoding.ASCII.GetBytes("yo@example.com")),
                    DerWriter.ContextPrimitive(7, new byte[] { 192, 168, 1, 5 }),
                    DerWriter.ContextPrimitive(7, IPAddress.IPv6Loopback.GetAddressBytes()),
                    DerWriter.ContextPrimitive(7, new byte[] { 1, 2, 3 }),
                    DerWriter.ContextPrimitive(6, Encoding.ASCII.GetBytes("http://example.com/")));

                LeafNames names = CertificateExtensionData.ReadSubjectAltNames(san);
                Check.Equal(1, names.DnsNames.Count, "un dominio");
                Check.Equal("localhost", names.DnsNames[0], "dominio");
                Check.Equal(2, names.Addresses.Count, "dos direcciones validas");
                Check.Equal("192.168.1.5", names.Addresses[0], "IPv4");
                Check.Equal("::1", names.Addresses[1], "IPv6");
            });

            runner.Test("lee los nombres que escribe el propio .NET", delegate
            {
                SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
                builder.AddDnsName("localhost");
                builder.AddDnsName("joel-pc.local");
                builder.AddIpAddress(IPAddress.Parse("192.168.1.5"));
                builder.AddIpAddress(IPAddress.IPv6Loopback);

                LeafNames names = CertificateExtensionData.ReadSubjectAltNames(builder.Build(false).RawData);
                LeafNames expected = new LeafNames(
                    new string[] { "localhost", "joel-pc.local" },
                    new IPAddress[] { IPAddress.Parse("192.168.1.5"), IPAddress.IPv6Loopback });

                Check.True(names.Covers(expected) && expected.Covers(names), "mismos nombres: " + names.Describe());
            });

            runner.Test("sin extension de nombres no hay nombres", delegate
            {
                LeafNames names = CertificateExtensionData.ReadSubjectAltNames(null);
                Check.Equal(0, names.DnsNames.Count + names.Addresses.Count, "vacio");
            });

            runner.Test("comparar bytes exige misma longitud y mismo contenido", delegate
            {
                Check.True(CertificateExtensionData.BytesEqual(new byte[] { 1, 2 }, new byte[] { 1, 2 }), "iguales");
                Check.True(CertificateExtensionData.BytesEqual(new byte[0], new byte[0]), "vacios");
                Check.False(CertificateExtensionData.BytesEqual(new byte[] { 1, 2 }, new byte[] { 1, 3 }), "distintos");
                Check.False(CertificateExtensionData.BytesEqual(new byte[] { 1 }, new byte[] { 1, 0 }), "otra longitud");
                Check.False(CertificateExtensionData.BytesEqual(null, new byte[0]), "uno nulo");
                Check.False(CertificateExtensionData.BytesEqual(null, null), "los dos nulos");
            });

            runner.Suite("Https / huella del certificado");

            runner.Test("pares hexadecimales en mayusculas separados por dos puntos", delegate
            {
                Check.Equal("00:AB:0F", CertificateFingerprint.Format(new byte[] { 0x00, 0xAB, 0x0F }), "tres bytes");
                Check.Equal("", CertificateFingerprint.Format(new byte[0]), "vacia");
                Check.Throws(delegate { CertificateFingerprint.Format(null); }, "nula");
            });

            runner.Test("en renglones sin partir ningun par", delegate
            {
                Check.Equal("AA:BB:\r\nCC:DD:\r\nEE", CertificateFingerprint.InLines("AA:BB:CC:DD:EE", 2), "de dos en dos");
                Check.Equal("AA:BB:CC", CertificateFingerprint.InLines("AA:BB:CC", 16), "cabe en uno");
                Check.Equal("", CertificateFingerprint.InLines(null, 16), "nula");

                string full = CertificateFingerprint.Sha256(Encoding.ASCII.GetBytes("abc"));
                string[] lines = CertificateFingerprint.InLines(full, 16).Split(new string[] { "\r\n" },
                                                                               StringSplitOptions.None);
                Check.Equal(2, lines.Length, "SHA-256 en dos renglones");
                Check.Equal(48, lines[0].Length, "16 pares con sus dos puntos");
                Check.Equal(full, lines[0] + lines[1], "unidos dan la huella de una linea");
                Check.Throws(delegate { CertificateFingerprint.InLines(full, 0); }, "cero pares por renglon");
            });

            runner.Test("SHA-256 de un vector conocido", delegate
            {
                // FIPS 180-2, ejemplo "abc".
                string fingerprint = CertificateFingerprint.Sha256(Encoding.ASCII.GetBytes("abc"));
                Check.Equal("BA:78:16:BF:8F:01:CF:EA:41:41:40:DE:5D:AE:22:23:B0:03:61:A3:96:17:7A:9C:B4:10:FF:61:F2:00:15:AD",
                            fingerprint, "huella");
                Check.Equal(95, fingerprint.Length, "32 pares y 31 separadores");
                Check.Throws(delegate { CertificateFingerprint.Sha256(null); }, "nula");
            });
        }
    }
}
