using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LanShare.Https
{
    /// <summary>
    /// Construye los dos certificados de LanShare a partir de sus claves, sin
    /// tocar ningun almacen: la autoridad local y el certificado del servidor.
    /// Al no guardar nada, las pruebas pueden emitirlos con claves efimeras y
    /// comprobar la cadena con el verificador de Windows.
    ///
    /// El perfil (extensiones, tamanos, vigencias) se valido contra OpenSSL y
    /// contra el verificador de Chromium, que es el mismo de Chrome en Android.
    /// </summary>
    public static class CertificateFactory
    {
        /// <summary>RSA 3072 para la autoridad, que vive diez anos.</summary>
        public const int AuthorityKeySize = 3072;

        /// <summary>RSA 2048 para el servidor: el estandar, y se renueva cada ano.</summary>
        public const int ServerKeySize = 2048;

        public const int AuthorityValidityYears = 10;

        private const int SerialNumberLength = 16;

        /// <summary>Lo que se admite en el nombre comun de la autoridad; lo demas se cambia por un guion.</summary>
        private const string AllowedNameSymbols = " -_.()";

        /// <summary>
        /// Autoridad local autofirmada. Restricciones basicas de autoridad sin
        /// subordinadas (pathLen 0), firma de certificados y listas, uso
        /// limitado a servidores TLS y restricciones de nombre a la red local.
        /// Las restricciones de nombre van como no criticas, como permite el
        /// CA/Browser Forum: los verificadores que las entienden (Chrome,
        /// Firefox, Windows, OpenSSL) las aplican y uno antiguo que no las
        /// entendiera no rechaza el certificado entero.
        /// </summary>
        public static X509Certificate2 CreateAuthority(RSA key, string commonName, DateTimeOffset now)
        {
            if (key == null) throw new ArgumentNullException("key");

            X500DistinguishedName subject = BuildName(commonName);
            CertificateRequest request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256,
                                                                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(ServerAuthenticationOnly());
            request.CertificateExtensions.Add(new X509Extension(LocalNameConstraints.Oid,
                                                                LocalNameConstraints.Encode(), false));

            X509SignatureGenerator signer = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);
            return request.Create(subject, signer, now.AddDays(-LeafPolicy.BackdateDays),
                                  now.AddYears(AuthorityValidityYears), NewSerialNumber());
        }

        /// <summary>
        /// Certificado del servidor firmado por la autoridad. Sujeto
        /// CN=localhost, que cae dentro de las restricciones de nombre: algunos
        /// verificadores tambien las aplican al nombre comun. Los navegadores
        /// solo miran los nombres alternativos, que son los de
        /// <paramref name="names"/>.
        /// </summary>
        public static X509Certificate2 CreateServer(X509Certificate2 authority, RSA authorityKey, RSA serverKey,
                                                    LeafNames names, DateTimeOffset now)
        {
            if (authority == null) throw new ArgumentNullException("authority");
            if (authorityKey == null) throw new ArgumentNullException("authorityKey");
            if (serverKey == null) throw new ArgumentNullException("serverKey");
            if (names == null) throw new ArgumentNullException("names");

            byte[] authorityKeyId = CertificateExtensionData.ReadSubjectKeyIdentifier(
                CertificateExtensionData.Find(authority, CertificateExtensionData.SubjectKeyIdentifierOid));
            if (authorityKeyId == null) throw new ArgumentException("La autoridad no tiene identificador de clave.", "authority");

            X500DistinguishedName subject = new X500DistinguishedName("CN=localhost, O=LanShare");
            CertificateRequest request = new CertificateRequest(subject, serverKey, HashAlgorithmName.SHA256,
                                                                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(ServerAuthenticationOnly());
            request.CertificateExtensions.Add(SubjectAltNames(names));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(new X509Extension(
                CertificateExtensionData.AuthorityKeyIdentifierOid,
                CertificateExtensionData.EncodeAuthorityKeyIdentifier(authorityKeyId), false));

            // Un certificado no puede durar mas que la autoridad que lo avala.
            DateTimeOffset notAfter = now.AddDays(LeafPolicy.ValidityDays);
            DateTimeOffset authorityEnd = new DateTimeOffset(authority.NotAfter);
            if (notAfter > authorityEnd) notAfter = authorityEnd;

            X509SignatureGenerator signer = X509SignatureGenerator.CreateForRSA(authorityKey, RSASignaturePadding.Pkcs1);
            return request.Create(authority.SubjectName, signer, now.AddDays(-LeafPolicy.BackdateDays),
                                  notAfter, NewSerialNumber());
        }

        private static X509EnhancedKeyUsageExtension ServerAuthenticationOnly()
        {
            OidCollection usages = new OidCollection();
            usages.Add(new Oid(CertificateExtensionData.ServerAuthenticationOid));
            return new X509EnhancedKeyUsageExtension(usages, false);
        }

        private static X509Extension SubjectAltNames(LeafNames names)
        {
            SubjectAlternativeNameBuilder builder = new SubjectAlternativeNameBuilder();
            foreach (string name in names.DnsNames) builder.AddDnsName(name);
            foreach (string address in names.Addresses) builder.AddIpAddress(IPAddress.Parse(address));
            return builder.Build(false);
        }

        /// <summary>
        /// 128 bits aleatorios, como piden las reglas de los navegadores para
        /// que dos certificados nunca compartan numero. El primer byte va de 1
        /// a 127: positivo y sin ceros a la izquierda que DER tendria que quitar.
        /// </summary>
        private static byte[] NewSerialNumber()
        {
            byte[] serial = new byte[SerialNumberLength];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(serial);
            }
            serial[0] = (byte)(serial[0] % 0x7F + 1);
            return serial;
        }

        /// <summary>
        /// Nombre distinguido con un nombre comun saneado: una coma o unas
        /// comillas en el nombre del equipo romperian la sintaxis.
        /// </summary>
        private static X500DistinguishedName BuildName(string commonName)
        {
            StringBuilder clean = new StringBuilder();
            foreach (char c in commonName ?? "")
            {
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                            AllowedNameSymbols.IndexOf(c) >= 0;
                clean.Append(safe ? c : '-');
            }

            string name = clean.ToString().Trim();
            if (name.Length == 0) name = "LanShare CA";
            return new X500DistinguishedName("CN=" + name + ", O=LanShare");
        }
    }
}
