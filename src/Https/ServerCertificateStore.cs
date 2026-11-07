using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanShare.Https
{
    /// <summary>
    /// El certificado del servidor en el almacen del equipo (LocalMachine\My).
    ///
    /// Tiene que estar ahi y no en el del usuario: quien hace el TLS es
    /// http.sys, desde el servicio del sistema, y solo ve el almacen del
    /// equipo. Por eso instalarlo y borrarlo exige administrador; buscarlo no.
    /// </summary>
    public static class ServerCertificateStore
    {
        /// <summary>Nombre descriptivo con el que se reconocen los certificados de LanShare en el almacen.</summary>
        public const string FriendlyName = "LanShare - servidor local";

        /// <summary>
        /// El certificado de servidor vigente que firmo esta autoridad (si hay
        /// varios, el que caduca mas tarde), con su clave privada. null si no hay.
        /// </summary>
        public static X509Certificate2 FindCurrent(LocalCertificateAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException("authority");
            byte[] authorityKeyId = authority.KeyIdentifier;
            DateTime now = DateTime.Now;

            X509Certificate2 best = null;
            foreach (X509Certificate2 candidate in Ours())
            {
                if (!candidate.HasPrivateKey) continue;
                if (candidate.NotBefore > now || candidate.NotAfter <= now) continue;

                byte[] signedBy = SafeAuthorityKeyId(candidate);
                if (!CertificateExtensionData.BytesEqual(signedBy, authorityKeyId)) continue;

                if (best == null || candidate.NotAfter > best.NotAfter) best = candidate;
            }
            return best;
        }

        /// <summary>
        /// Instala un certificado con su clave privada. La clave entra por un
        /// PFX en memoria, que es como Windows la guarda de forma permanente y
        /// asociada al certificado; se marca como no exportable y el PFX se
        /// borra de la memoria en cuanto se importa.
        /// </summary>
        public static X509Certificate2 Install(X509Certificate2 certificateWithKey)
        {
            if (certificateWithKey == null) throw new ArgumentNullException("certificateWithKey");
            if (!certificateWithKey.HasPrivateKey) throw new ArgumentException("Falta la clave privada.", "certificateWithKey");

            string password = Guid.NewGuid().ToString("N");
            byte[] pfx = certificateWithKey.Export(X509ContentType.Pkcs12, password);
            try
            {
                X509Certificate2 persisted = new X509Certificate2(pfx, password,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
                persisted.FriendlyName = FriendlyName;

                using (X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(persisted);
                }
                return persisted;
            }
            finally
            {
                Array.Clear(pfx, 0, pfx.Length);
            }
        }

        /// <summary>
        /// Tras una renovacion, quita del almacen, con sus claves, los
        /// certificados anteriores de esta autoridad y los caducados de
        /// cualquiera. Los vigentes de otra autoridad son de otro usuario de
        /// este equipo con su propio LanShare, y se dejan: borrarlos le
        /// romperia su HTTPS.
        /// </summary>
        /// <returns>Cuantos se quitaron.</returns>
        public static int RemoveStale(LocalCertificateAuthority authority, X509Certificate2 keep)
        {
            if (authority == null) throw new ArgumentNullException("authority");

            byte[] authorityKeyId = authority.KeyIdentifier;
            string keepThumbprint = keep != null ? keep.Thumbprint : null;
            DateTime now = DateTime.Now;

            return RemoveWhere(delegate(X509Certificate2 candidate)
            {
                if (keepThumbprint != null &&
                    string.Equals(candidate.Thumbprint, keepThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return IsStale(SafeAuthorityKeyId(candidate), candidate.NotAfter, now, authorityKeyId);
            });
        }

        /// <summary>
        /// Quita todos los certificados de servidor de LanShare, de quien sean:
        /// es el borrado completo que pide el usuario desde Configuracion.
        /// </summary>
        /// <returns>Cuantos se quitaron.</returns>
        public static int RemoveAll()
        {
            return RemoveWhere(delegate(X509Certificate2 candidate) { return true; });
        }

        /// <summary>
        /// Si una asociacion de http.sys de LanShare se puede soltar sin
        /// romper el HTTPS de otro usuario: si su certificado ya no esta en el
        /// almacen, caduco o lo firmo la autoridad de este usuario.
        /// </summary>
        /// <param name="authorityKeyId">Identificador de la autoridad de este usuario; null si no tiene.</param>
        public static bool CanRelease(byte[] certificateHash, byte[] authorityKeyId)
        {
            X509Certificate2 certificate = FindByHash(certificateHash);
            if (certificate == null) return true;
            return IsStale(SafeAuthorityKeyId(certificate), certificate.NotAfter, DateTime.Now, authorityKeyId);
        }

        /// <summary>
        /// Regla de limpieza, sin almacen: un certificado de LanShare sobra si
        /// ya caduco (sea de quien sea) o si lo firmo la autoridad de este
        /// usuario. Uno vigente de otra autoridad es de otro usuario.
        /// </summary>
        /// <param name="signedBy">Identificador de la autoridad que lo firmo, leido de su AKI.</param>
        /// <param name="notAfter">Fin de su validez, en la misma zona horaria que <paramref name="now"/>.</param>
        /// <param name="currentAuthorityKeyId">El de la autoridad de este usuario; null si no tiene.</param>
        public static bool IsStale(byte[] signedBy, DateTime notAfter, DateTime now, byte[] currentAuthorityKeyId)
        {
            if (notAfter <= now) return true;
            return CertificateExtensionData.BytesEqual(signedBy, currentAuthorityKeyId);
        }

        private delegate bool CertificateFilter(X509Certificate2 candidate);

        /// <summary>Quita, con su clave, cada certificado de LanShare que cumpla el filtro.</summary>
        private static int RemoveWhere(CertificateFilter shouldRemove)
        {
            int removed = 0;
            using (X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadWrite);
                foreach (X509Certificate2 candidate in store.Certificates)
                {
                    if (!string.Equals(candidate.FriendlyName, FriendlyName, StringComparison.Ordinal)) continue;
                    if (!shouldRemove(candidate)) continue;

                    DeletePrivateKey(candidate);
                    store.Remove(candidate);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>El certificado del almacen del equipo con esa huella SHA-1, o null.</summary>
        private static X509Certificate2 FindByHash(byte[] certificateHash)
        {
            if (certificateHash == null || certificateHash.Length == 0) return null;

            string thumbprint = BitConverter.ToString(certificateHash).Replace("-", "");
            using (X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint,
                                                                           thumbprint, false);
                return found.Count > 0 ? found[0] : null;
            }
        }

        /// <summary>Los certificados del almacen que llevan el nombre de LanShare.</summary>
        private static List<X509Certificate2> Ours()
        {
            List<X509Certificate2> found = new List<X509Certificate2>();
            using (X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (X509Certificate2 candidate in store.Certificates)
                {
                    if (string.Equals(candidate.FriendlyName, FriendlyName, StringComparison.Ordinal)) found.Add(candidate);
                }
            }
            return found;
        }

        private static byte[] SafeAuthorityKeyId(X509Certificate2 certificate)
        {
            try
            {
                return CertificateExtensionData.ReadAuthorityKeyIdentifier(
                    CertificateExtensionData.Find(certificate, CertificateExtensionData.AuthorityKeyIdentifierOid));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Borra el contenedor de la clave privada. Quitar el certificado del
        /// almacen no lo hace por si solo: la clave se quedaria huerfana en
        /// el disco. Si no se puede, se sigue: lo importante es el certificado.
        /// </summary>
        private static void DeletePrivateKey(X509Certificate2 certificate)
        {
            try
            {
                RSA key = certificate.GetRSAPrivateKey();

                RSACryptoServiceProvider legacy = key as RSACryptoServiceProvider;
                if (legacy != null)
                {
                    legacy.PersistKeyInCsp = false;
                    legacy.Clear();
                    return;
                }

                RSACng modern = key as RSACng;
                if (modern != null) modern.Key.Delete();
            }
            catch (Exception)
            {
            }
        }
    }
}
