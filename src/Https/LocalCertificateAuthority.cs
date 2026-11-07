using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanShare.Https
{
    /// <summary>
    /// La autoridad de certificacion propia de este equipo: la que se instala
    /// una vez en cada movil para que confie en LanShare.
    ///
    /// Su clave privada es una clave CNG con nombre en el almacen del usuario
    /// de Windows (protegida por DPAPI con su sesion) y NO exportable: ni
    /// LanShare puede sacarla, solo pedirle a Windows que firme con ella. El
    /// certificado publico se guarda aparte, en %APPDATA%\LanShare\https.
    ///
    /// Ademas de eso, el propio certificado limita lo que puede avalar a la
    /// red local (LocalNameConstraints) y a servidores TLS: aun robada, la
    /// clave no serviria para suplantar webs de Internet.
    /// </summary>
    public sealed class LocalCertificateAuthority
    {
        /// <summary>Nombre fijo de la clave: con uno por usuario basta, y asi no hay que recordarlo.</summary>
        public const string KeyName = "LanShare-CA-v1";

        private const string CertificateFileName = "LanShare-CA.cer";

        private readonly X509Certificate2 _certificate;
        private readonly byte[] _keyIdentifier;

        private LocalCertificateAuthority(X509Certificate2 certificate)
        {
            _certificate = certificate;
            _keyIdentifier = CertificateExtensionData.ReadSubjectKeyIdentifier(
                CertificateExtensionData.Find(certificate, CertificateExtensionData.SubjectKeyIdentifierOid));
        }

        /// <summary>Certificado publico de la autoridad, sin clave.</summary>
        public X509Certificate2 Certificate
        {
            get { return _certificate; }
        }

        /// <summary>El certificado en DER: lo que se descarga en el movil.</summary>
        public byte[] RawData
        {
            get { return _certificate.RawData; }
        }

        public string Fingerprint
        {
            get { return CertificateFingerprint.Sha256(_certificate.RawData); }
        }

        /// <summary>Fin de la validez de la autoridad, en UTC.</summary>
        public DateTime NotAfterUtc
        {
            get { return _certificate.NotAfter.ToUniversalTime(); }
        }

        /// <summary>Identificador de su clave, el que llevan los certificados que firma.</summary>
        public byte[] KeyIdentifier
        {
            get { return (byte[])_keyIdentifier.Clone(); }
        }

        public static string Folder
        {
            get
            {
                return Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanShare"), "https");
            }
        }

        private static string CertificatePath
        {
            get { return Path.Combine(Folder, CertificateFileName); }
        }

        /// <summary>
        /// La autoridad de este usuario, o null si falta el certificado, falta
        /// la clave, no casan entre si o ya caduco. En cualquiera de esos casos
        /// hay que crear otra (y reinstalarla en los moviles).
        ///
        /// Una clave que existe pero que este proceso no puede abrir (la creo
        /// LanShare con administrador y ahora corre sin el) no invalida la
        /// autoridad: para ofrecer el certificado y reconocer los que firmo
        /// basta el certificado, y firmar solo se firma con administrador.
        /// </summary>
        public static LocalCertificateAuthority Load()
        {
            X509Certificate2 certificate = ReadStoredCertificate();
            if (certificate == null) return null;
            if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) return null;

            KeyCheck check = CheckKey(certificate);
            if (check == KeyCheck.Missing || check == KeyCheck.Mismatch) return null;

            LocalCertificateAuthority authority = new LocalCertificateAuthority(certificate);
            return authority._keyIdentifier != null ? authority : null;
        }

        /// <summary>
        /// Crea la clave (sustituyendo una anterior con el mismo nombre) y el
        /// certificado autofirmado, y guarda este. Tarda un segundo o dos: es
        /// la generacion de una clave RSA de 3072 bits.
        /// </summary>
        public static LocalCertificateAuthority Create(string machineName, DateTimeOffset now)
        {
            CngKeyCreationParameters parameters = new CngKeyCreationParameters();
            parameters.Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider;
            parameters.ExportPolicy = CngExportPolicies.None;
            parameters.KeyUsage = CngKeyUsages.Signing;
            parameters.KeyCreationOptions = CngKeyCreationOptions.OverwriteExistingKey;
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(CertificateFactory.AuthorityKeySize),
                                                      CngPropertyOptions.None));

            X509Certificate2 certificate;
            using (CngKey key = CngKey.Create(CngAlgorithm.Rsa, KeyName, parameters))
            using (RSACng rsa = new RSACng(key))
            {
                certificate = CertificateFactory.CreateAuthority(rsa, CommonName(machineName, now), now);
            }

            Directory.CreateDirectory(Folder);
            WriteReplacing(CertificatePath, certificate.RawData);
            return new LocalCertificateAuthority(certificate);
        }

        /// <summary>
        /// Firma un certificado de servidor con la clave de la autoridad. Antes
        /// comprueba que la clave sea la del certificado: firmar con otra
        /// daria certificados que ningun movil aceptaria, sin pista del motivo.
        /// </summary>
        public X509Certificate2 IssueServerCertificate(RSA serverKey, LeafNames names, DateTimeOffset now)
        {
            using (CngKey key = CngKey.Open(KeyName))
            using (RSACng rsa = new RSACng(key))
            {
                if (!SamePublicKey(_certificate, rsa))
                {
                    throw new CryptographicException("La clave de la autoridad local no corresponde a su certificado. " +
                                                     "Borra los certificados desde Configuracion, HTTPS, y vuelve a iniciar.");
                }
                return CertificateFactory.CreateServer(_certificate, rsa, serverKey, names, now);
            }
        }

        // ------------------------------------------------------------------
        // Confianza en este PC
        // ------------------------------------------------------------------

        /// <summary>
        /// Si los navegadores de este PC (Chrome, Edge) confian en la autoridad:
        /// esta en las raices de confianza del usuario o del equipo.
        /// </summary>
        public bool IsTrustedHere()
        {
            using (X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                return store.Certificates.Find(X509FindType.FindByThumbprint, _certificate.Thumbprint, false).Count > 0;
            }
        }

        /// <summary>
        /// Anade la autoridad a las raices de confianza del usuario. Windows
        /// pide confirmacion con su propio aviso de seguridad: es la garantia de
        /// que nadie lo hace a escondidas. Devuelve false si el usuario dice que no.
        /// </summary>
        public bool TrustHere()
        {
            try
            {
                using (X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
                {
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(new X509Certificate2(_certificate.RawData));
                }
                return IsTrustedHere();
            }
            catch (CryptographicException)
            {
                // Lo que lanza Windows cuando se pulsa "No" en su aviso.
                return false;
            }
        }

        /// <summary>Quita la autoridad de las raices del usuario. Windows tambien pide confirmacion.</summary>
        public bool UntrustHere()
        {
            return RemoveFromRoot(_certificate.Thumbprint);
        }

        // ------------------------------------------------------------------
        // Borrado
        // ------------------------------------------------------------------

        /// <summary>
        /// Borra la autoridad de este usuario: la quita de las raices de
        /// confianza (si estaba), borra su clave y su certificado. Los moviles
        /// que la tuvieran instalada dejaran de confiar en el siguiente
        /// certificado: habra que instalar la nueva. Lo que no se puede borrar
        /// no detiene lo demas: se anota en <paramref name="pending"/>.
        /// </summary>
        public static void DeleteForThisUser(IList<string> pending)
        {
            if (pending == null) throw new ArgumentNullException("pending");

            // Se quita de las raices aunque la clave ya no exista: lo que
            // confia el PC es el certificado, no la clave.
            X509Certificate2 stored = ReadStoredCertificate();
            if (stored != null && !RemoveFromRoot(stored.Thumbprint))
            {
                pending.Add("La autoridad sigue entre las raices de confianza de este PC porque se cancelo " +
                            "el aviso de Windows.");
            }

            try
            {
                if (CngKey.Exists(KeyName))
                {
                    using (CngKey key = CngKey.Open(KeyName))
                    {
                        key.Delete();
                    }
                }
            }
            catch (CryptographicException ex)
            {
                pending.Add("La clave de la autoridad no se pudo borrar (" + ex.Message + "). Si se creo con " +
                            "administrador, repite el borrado tras pulsar Escalar Privilegios.");
                return;
            }

            if (File.Exists(CertificatePath)) File.Delete(CertificatePath);
        }

        /// <summary>Lo que se sabe de la clave de la autoridad desde este proceso.</summary>
        private enum KeyCheck
        {
            Matches,
            Missing,
            Mismatch,
            Inaccessible
        }

        /// <summary>
        /// Si la clave guardada en Windows es la del certificado. Si existe pero
        /// este proceso no la puede abrir, se dice tal cual: no es lo mismo
        /// que no tenerla.
        /// </summary>
        private static KeyCheck CheckKey(X509Certificate2 certificate)
        {
            try
            {
                if (!CngKey.Exists(KeyName)) return KeyCheck.Missing;
                using (CngKey key = CngKey.Open(KeyName))
                using (RSACng rsa = new RSACng(key))
                {
                    return SamePublicKey(certificate, rsa) ? KeyCheck.Matches : KeyCheck.Mismatch;
                }
            }
            catch (CryptographicException)
            {
                return KeyCheck.Inaccessible;
            }
        }

        /// <summary>
        /// El certificado guardado, sea valido o no. null si no hay archivo o
        /// esta danado: en ese caso se crea una autoridad nueva en vez de
        /// dejar HTTPS roto hasta que alguien borre el archivo a mano.
        /// </summary>
        private static X509Certificate2 ReadStoredCertificate()
        {
            if (!File.Exists(CertificatePath)) return null;
            try
            {
                return new X509Certificate2(File.ReadAllBytes(CertificatePath));
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private static bool RemoveFromRoot(string thumbprint)
        {
            try
            {
                using (X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
                {
                    store.Open(OpenFlags.ReadWrite);
                    foreach (X509Certificate2 found in store.Certificates.Find(X509FindType.FindByThumbprint,
                                                                              thumbprint, false))
                    {
                        store.Remove(found);
                    }
                }
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>
        /// "LanShare CA (JOEL-PC 2026-11-06)": en la lista de certificados del
        /// movil se distingue de quien la creo y cuando, por si algun dia se
        /// regenera y conviene borrar la vieja.
        /// </summary>
        private static string CommonName(string machineName, DateTimeOffset now)
        {
            string date = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(machineName)
                ? "LanShare CA (" + date + ")"
                : "LanShare CA (" + machineName + " " + date + ")";
        }

        /// <summary>
        /// El certificado guardado y la clave del almacen casan si comparten
        /// modulo: una clave regenerada a mano con el mismo nombre no lo haria.
        /// La parte publica se puede leer aunque la clave no sea exportable.
        /// </summary>
        private static bool SamePublicKey(X509Certificate2 certificate, RSA key)
        {
            using (RSA published = certificate.GetRSAPublicKey())
            {
                if (published == null) return false;
                byte[] left = published.ExportParameters(false).Modulus;
                byte[] right = key.ExportParameters(false).Modulus;
                return CertificateExtensionData.BytesEqual(left, right);
            }
        }

        /// <summary>Escribe en un temporal y lo pone en su sitio: un corte a mitad no deja un archivo roto.</summary>
        private static void WriteReplacing(string path, byte[] content)
        {
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }
    }
}
