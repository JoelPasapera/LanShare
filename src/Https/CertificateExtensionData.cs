using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LanShare.Https
{
    /// <summary>
    /// Escritura y lectura de las extensiones de certificado que .NET
    /// Framework 4.8 no trae hechas: el identificador de la clave de la
    /// autoridad (escribirlo) y los nombres alternativos y los
    /// identificadores de clave (leerlos de un certificado ya emitido).
    /// </summary>
    public static class CertificateExtensionData
    {
        public const string SubjectAltNameOid = "2.5.29.17";
        public const string SubjectKeyIdentifierOid = "2.5.29.14";
        public const string AuthorityKeyIdentifierOid = "2.5.29.35";

        /// <summary>Uso extendido "autenticacion de servidor TLS".</summary>
        public const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

        /// <summary>dNSName en GeneralName: [2] IMPLICIT IA5String.</summary>
        private const byte TagDnsName = 0x82;

        /// <summary>iPAddress en GeneralName: [7] IMPLICIT OCTET STRING.</summary>
        private const byte TagIpAddress = 0x87;

        /// <summary>keyIdentifier dentro del AKI: [0] IMPLICIT OCTET STRING.</summary>
        private const byte TagKeyIdentifier = 0x80;

        /// <summary>
        /// AuthorityKeyIdentifier con solo el identificador de clave, que es lo
        /// que usan los navegadores para encontrar la autoridad de un
        /// certificado (RFC 5280 4.2.1.1).
        /// </summary>
        public static byte[] EncodeAuthorityKeyIdentifier(byte[] keyIdentifier)
        {
            if (keyIdentifier == null || keyIdentifier.Length == 0) throw new ArgumentException("keyIdentifier");
            return DerWriter.Sequence(DerWriter.ContextPrimitive(0, keyIdentifier));
        }

        /// <summary>El identificador de clave de un AKI, o null si no lo lleva.</summary>
        public static byte[] ReadAuthorityKeyIdentifier(byte[] extensionValue)
        {
            if (extensionValue == null) return null;

            DerReader fields = new DerReader(extensionValue).Read(DerWriter.TagSequence).Children();
            while (fields.HasMore)
            {
                DerElement field = fields.Read();
                if (field.Tag == TagKeyIdentifier) return field.Content();
            }
            return null;
        }

        /// <summary>El valor de un SubjectKeyIdentifier: un OCTET STRING con el identificador.</summary>
        public static byte[] ReadSubjectKeyIdentifier(byte[] extensionValue)
        {
            if (extensionValue == null) return null;
            return new DerReader(extensionValue).Read(DerWriter.TagOctetString).Content();
        }

        /// <summary>
        /// Dominios y direcciones de un SubjectAltName. Los demas tipos de
        /// nombre (correo, URI) se saltan: un servidor de LanShare no los usa.
        /// </summary>
        public static LeafNames ReadSubjectAltNames(byte[] extensionValue)
        {
            List<string> dnsNames = new List<string>();
            List<IPAddress> addresses = new List<IPAddress>();

            if (extensionValue != null)
            {
                DerReader names = new DerReader(extensionValue).Read(DerWriter.TagSequence).Children();
                while (names.HasMore)
                {
                    DerElement name = names.Read();
                    if (name.Tag == TagDnsName)
                    {
                        dnsNames.Add(Encoding.ASCII.GetString(name.Content()));
                    }
                    else if (name.Tag == TagIpAddress && (name.ContentLength == 4 || name.ContentLength == 16))
                    {
                        addresses.Add(new IPAddress(name.Content()));
                    }
                }
            }

            return new LeafNames(dnsNames, addresses);
        }

        /// <summary>El valor de una extension del certificado, o null si no la tiene.</summary>
        public static byte[] Find(X509Certificate2 certificate, string oid)
        {
            if (certificate == null) return null;
            X509Extension extension = certificate.Extensions[oid];
            return extension != null ? extension.RawData : null;
        }

        /// <summary>Compara dos bloques de bytes (identificadores de clave, modulos) entero.</summary>
        public static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
