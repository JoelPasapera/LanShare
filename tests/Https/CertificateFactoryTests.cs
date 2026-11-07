using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LanShare.Https;

namespace LanShare.Tests
{
    /// <summary>
    /// Emision de los dos certificados con claves efimeras y verificacion de
    /// la cadena con el verificador de Windows. No toca ningun almacen: ni la
    /// autoridad se instala ni el certificado del servidor se guarda.
    /// </summary>
    public static class CertificateFactoryTests
    {
        private const string BasicConstraintsOid = "2.5.29.19";
        private const string KeyUsageOid = "2.5.29.15";
        private const string EnhancedKeyUsageOid = "2.5.29.37";
        private const string Sha256WithRsaOid = "1.2.840.113549.1.1.11";

        /// <summary>Se generan una vez: una clave RSA tarda en crearse y aqui no importa compartirla.</summary>
        private static RSA _authorityKey;
        private static RSA _serverKey;
        private static X509Certificate2 _authority;
        private static DateTimeOffset _now;

        private static void EnsureAuthority()
        {
            if (_authority != null) return;
            _now = DateTimeOffset.UtcNow;
            _authorityKey = new RSACng(2048);
            _serverKey = new RSACng(2048);
            _authority = CertificateFactory.CreateAuthority(_authorityKey, "LanShare CA (PRUEBAS)", _now);
        }

        private static X509Certificate2 Leaf(LeafNames names)
        {
            EnsureAuthority();
            return CertificateFactory.CreateServer(_authority, _authorityKey, _serverKey, names, _now);
        }

        private static LeafNames Names(string dnsName, string address)
        {
            return new LeafNames(new string[] { dnsName }, new IPAddress[] { IPAddress.Parse(address) });
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Https / emision de la autoridad");

            runner.Test("es una autoridad sin subordinadas y solo firma certificados", delegate
            {
                EnsureAuthority();
                X509BasicConstraintsExtension basic = (X509BasicConstraintsExtension)_authority.Extensions[BasicConstraintsOid];
                Check.NotNull(basic, "restricciones basicas");
                Check.True(basic.CertificateAuthority, "es autoridad");
                Check.True(basic.HasPathLengthConstraint, "limita la profundidad");
                Check.Equal(0, basic.PathLengthConstraint, "sin subordinadas");
                Check.True(basic.Critical, "critica");

                X509KeyUsageExtension usage = (X509KeyUsageExtension)_authority.Extensions[KeyUsageOid];
                Check.NotNull(usage, "uso de clave");
                Check.Equal(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, usage.KeyUsages, "usos");
                Check.True(usage.Critical, "uso critico");
            });

            runner.Test("CLAVE: lleva las restricciones de nombre a la red local", delegate
            {
                EnsureAuthority();
                X509Extension constraints = _authority.Extensions[LocalNameConstraints.Oid];
                Check.NotNull(constraints, "restricciones de nombre");
                Check.False(constraints.Critical, "no critica, como permite el CA/Browser Forum");
                Check.BytesEqual(LocalNameConstraints.Encode(), constraints.RawData, "contenido");
            });

            runner.Test("solo vale para servidores TLS", delegate
            {
                EnsureAuthority();
                Check.Equal(CertificateExtensionData.ServerAuthenticationOid, OnlyUsage(_authority), "uso extendido");
            });

            runner.Test("autofirmada en SHA-256, diez anos y numero de serie positivo de 128 bits", delegate
            {
                EnsureAuthority();
                Check.BytesEqual(_authority.SubjectName.RawData, _authority.IssuerName.RawData, "autofirmada");
                Check.Equal(Sha256WithRsaOid, _authority.SignatureAlgorithm.Value, "algoritmo de firma");
                Check.NotNull(_authority.Extensions[CertificateExtensionData.SubjectKeyIdentifierOid], "SKI");

                double years = (_authority.NotAfter - _authority.NotBefore).TotalDays / 365.25;
                Check.InRange((long)Math.Round(years), CertificateFactory.AuthorityValidityYears,
                              CertificateFactory.AuthorityValidityYears, "anos de validez");
                CheckSerial(_authority);
            });

            runner.Test("el nombre comun se sanea", delegate
            {
                EnsureAuthority();
                Check.Contains(_authority.Subject, "CN=LanShare CA (PRUEBAS)", "nombre limpio intacto");
                Check.Contains(_authority.Subject, "O=LanShare", "organizacion");

                X509Certificate2 odd = CertificateFactory.CreateAuthority(_authorityKey, "Equipo, \"raro\"=1", _now);
                Check.Contains(odd.Subject, "CN=Equipo- -raro--1", "comas, comillas e igual cambiados por guiones");

                X509Certificate2 empty = CertificateFactory.CreateAuthority(_authorityKey, "  ", _now);
                Check.Contains(empty.Subject, "CN=LanShare CA", "sin nombre usa uno por defecto");
            });

            runner.Suite("Https / emision del certificado del servidor");

            runner.Test("perfil de servidor: no es autoridad, firma y cifra, solo TLS", delegate
            {
                X509Certificate2 leaf = Leaf(LeafPolicy.Desired("PRUEBAS-PC", new string[] { "192.168.1.5" }));

                X509BasicConstraintsExtension basic = (X509BasicConstraintsExtension)leaf.Extensions[BasicConstraintsOid];
                Check.NotNull(basic, "restricciones basicas");
                Check.False(basic.CertificateAuthority, "no es autoridad");
                Check.True(basic.Critical, "critica");

                X509KeyUsageExtension usage = (X509KeyUsageExtension)leaf.Extensions[KeyUsageOid];
                Check.Equal(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, usage.KeyUsages, "usos");
                Check.True(usage.Critical, "uso critico");

                Check.Equal(CertificateExtensionData.ServerAuthenticationOid, OnlyUsage(leaf), "uso extendido");
                Check.Equal(Sha256WithRsaOid, leaf.SignatureAlgorithm.Value, "algoritmo de firma");
                Check.Contains(leaf.Subject, "CN=localhost", "sujeto");
                CheckSerial(leaf);
            });

            runner.Test("CLAVE: cubre exactamente los nombres pedidos", delegate
            {
                LeafNames desired = LeafPolicy.Desired("PRUEBAS-PC", new string[] { "192.168.1.5", "10.0.0.7" });
                X509Certificate2 leaf = Leaf(desired);

                LeafNames covered = CertificateExtensionData.ReadSubjectAltNames(
                    CertificateExtensionData.Find(leaf, CertificateExtensionData.SubjectAltNameOid));
                Check.True(covered.Covers(desired) && desired.Covers(covered), "nombres: " + covered.Describe());
            });

            runner.Test("lo firma la autoridad y apunta a su clave", delegate
            {
                X509Certificate2 leaf = Leaf(LeafPolicy.Desired("PRUEBAS-PC", null));
                Check.BytesEqual(_authority.SubjectName.RawData, leaf.IssuerName.RawData, "emisor");

                byte[] authorityKeyId = CertificateExtensionData.ReadSubjectKeyIdentifier(
                    CertificateExtensionData.Find(_authority, CertificateExtensionData.SubjectKeyIdentifierOid));
                byte[] pointsTo = CertificateExtensionData.ReadAuthorityKeyIdentifier(
                    CertificateExtensionData.Find(leaf, CertificateExtensionData.AuthorityKeyIdentifierOid));
                Check.BytesEqual(authorityKeyId, pointsTo, "AKI del servidor = SKI de la autoridad");
                Check.NotNull(leaf.Extensions[CertificateExtensionData.SubjectKeyIdentifierOid], "SKI propio");
            });

            runner.Test("vigencia de " + LeafPolicy.ValidityDays + " dias con un dia de margen hacia atras", delegate
            {
                X509Certificate2 leaf = Leaf(LeafPolicy.Desired("PRUEBAS-PC", null));
                DateTime notBefore = leaf.NotBefore.ToUniversalTime();
                DateTime notAfter = leaf.NotAfter.ToUniversalTime();

                Check.InRange((long)Math.Round((_now.UtcDateTime - notBefore).TotalHours), 24, 24, "margen hacia atras");
                Check.InRange((long)Math.Round((notAfter - _now.UtcDateTime).TotalDays), LeafPolicy.ValidityDays,
                              LeafPolicy.ValidityDays, "dias hacia delante");
                Check.True((notAfter - notBefore).TotalDays <= 398, "dentro del limite de Apple");
            });

            runner.Test("nunca dura mas que la autoridad", delegate
            {
                EnsureAuthority();
                DateTimeOffset late = new DateTimeOffset(_authority.NotAfter.ToUniversalTime().AddDays(-100), TimeSpan.Zero);
                X509Certificate2 leaf = CertificateFactory.CreateServer(_authority, _authorityKey, _serverKey,
                                                                        LeafPolicy.Desired("PRUEBAS-PC", null), late);
                Check.True(leaf.NotAfter <= _authority.NotAfter, "recortado al final de la autoridad");
            });

            runner.Test("una autoridad sin identificador de clave no puede firmar", delegate
            {
                EnsureAuthority();
                CertificateRequest request = new CertificateRequest("CN=sin SKI", _authorityKey,
                                                                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                X509Certificate2 bare = request.CreateSelfSigned(_now.AddDays(-1), _now.AddDays(10));
                Check.Throws(delegate
                {
                    CertificateFactory.CreateServer(bare, _authorityKey, _serverKey, LeafPolicy.Desired("PC", null), _now);
                }, "sin SKI");
            });

            runner.Suite("Https / cadena con el verificador de Windows");

            runner.Test("CLAVE: un servidor de la red local se valida contra la autoridad", delegate
            {
                X509Certificate2 leaf = Leaf(LeafPolicy.Desired("PRUEBAS-PC", new string[] { "192.168.1.5" }));
                string problems;
                bool built = BuildChain(leaf, out problems);

                Check.True(built, "cadena valida (problemas: " + problems + ")");
                Check.Equal("", problems, "solo falta que la raiz sea de confianza");
            });

            runner.Test("CLAVE: una direccion de Internet viola las restricciones", delegate
            {
                X509Certificate2 leaf = Leaf(Names("localhost", "8.8.8.8"));
                string problems;
                bool built = BuildChain(leaf, out problems);

                Check.False(built, "cadena rechazada");
                Check.Contains(problems, X509ChainStatusFlags.HasNotPermittedNameConstraint.ToString(), "motivo");
            });

            runner.Test("CLAVE: un dominio de Internet viola las restricciones", delegate
            {
                X509Certificate2 leaf = Leaf(Names("www.example.com", "192.168.1.5"));
                string problems;
                bool built = BuildChain(leaf, out problems);

                Check.False(built, "cadena rechazada");
                Check.Contains(problems, X509ChainStatusFlags.HasNotPermittedNameConstraint.ToString(), "motivo");
            });

            runner.Suite("Https / clave del servidor");

            runner.Test("CLAVE: la clave CAPI viaja en un PFX y firma al volver", delegate
            {
                // El mismo camino que la instalacion real, sin el almacen del
                // equipo: clave CAPI, copia con clave, PFX y vuelta a cargar.
                EnsureAuthority();
                using (RSACryptoServiceProvider serverKey = new RSACryptoServiceProvider(CertificateFactory.ServerKeySize))
                using (X509Certificate2 issued = CertificateFactory.CreateServer(_authority, _authorityKey, serverKey,
                                                                                 LeafPolicy.Desired("PC", null), _now))
                using (X509Certificate2 withKey = issued.CopyWithPrivateKey(serverKey))
                {
                    Check.True(withKey.HasPrivateKey, "copia con clave");

                    byte[] pfx = withKey.Export(X509ContentType.Pkcs12, "prueba");
                    using (X509Certificate2 loaded = new X509Certificate2(pfx, "prueba", X509KeyStorageFlags.DefaultKeySet))
                    {
                        Check.True(loaded.HasPrivateKey, "el PFX lleva la clave");
                        Check.Equal(issued.Thumbprint, loaded.Thumbprint, "mismo certificado");

                        byte[] data = Encoding.ASCII.GetBytes("LanShare");
                        byte[] signature;
                        using (RSA privateKey = loaded.GetRSAPrivateKey())
                        {
                            signature = privateKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                        }
                        using (RSA publicKey = loaded.GetRSAPublicKey())
                        {
                            Check.True(publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256,
                                                            RSASignaturePadding.Pkcs1), "firma valida");
                        }
                    }
                }
            });
        }

        /// <summary>El unico uso extendido del certificado, o la lista si hay varios.</summary>
        private static string OnlyUsage(X509Certificate2 certificate)
        {
            X509EnhancedKeyUsageExtension usage = (X509EnhancedKeyUsageExtension)certificate.Extensions[EnhancedKeyUsageOid];
            if (usage == null) return "(sin uso extendido)";

            List<string> values = new List<string>();
            foreach (Oid oid in usage.EnhancedKeyUsages) values.Add(oid.Value);
            return string.Join(",", values.ToArray());
        }

        /// <summary>16 bytes con el primero entre 1 y 127: positivo y sin ceros sobrantes.</summary>
        private static void CheckSerial(X509Certificate2 certificate)
        {
            string serial = certificate.SerialNumber;
            Check.Equal(32, serial.Length, "numero de serie de 16 bytes");
            int first = Convert.ToInt32(serial.Substring(0, 2), 16);
            Check.InRange(first, 1, 127, "primer byte del numero de serie");
        }

        /// <summary>
        /// Construye la cadena con la autoridad como almacen extra, sin
        /// instalarla: el unico problema admitido es que la raiz no sea de
        /// confianza. Sin revocacion, que LanShare no publica.
        /// </summary>
        /// <param name="problems">Los demas problemas de la cadena, separados por comas; vacio si no hay.</param>
        private static bool BuildChain(X509Certificate2 leaf, out string problems)
        {
            EnsureAuthority();
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.ExtraStore.Add(_authority);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CertificateExtensionData.ServerAuthenticationOid));

                bool built = chain.Build(leaf);

                X509ChainStatusFlags flags = X509ChainStatusFlags.NoError;
                foreach (X509ChainStatus status in chain.ChainStatus) flags |= status.Status;
                foreach (X509ChainElement element in chain.ChainElements)
                {
                    foreach (X509ChainStatus status in element.ChainElementStatus) flags |= status.Status;
                }

                flags &= ~X509ChainStatusFlags.UntrustedRoot;
                problems = flags == X509ChainStatusFlags.NoError ? "" : flags.ToString();

                if (built && chain.ChainElements.Count != 2)
                {
                    problems += " (cadena de " + chain.ChainElements.Count + " certificados)";
                }
                return built;
            }
        }
    }
}
