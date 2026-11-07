using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanShare.Https
{
    /// <summary>Lo que se pudo preparar para servir por HTTPS en este arranque.</summary>
    public sealed class HttpsPlan
    {
        private readonly HashSet<int> _readyPorts = new HashSet<int>();
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _notes = new List<string>();

        /// <summary>La autoridad local; null si no se pudo cargar ni crear.</summary>
        public LocalCertificateAuthority Authority { get; internal set; }

        /// <summary>Puertos HTTPS que tienen certificado asociado y se pueden servir.</summary>
        public ICollection<int> ReadyPorts
        {
            get { return _readyPorts; }
        }

        /// <summary>Lo que impidio servir algun puerto por HTTPS, para avisar al usuario.</summary>
        public IList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>
        /// Lo que no impide el HTTPS pero pide atencion: un certificado que
        /// hay que renovar y que, sin administrador, se sigue usando.
        /// </summary>
        public IList<string> Warnings
        {
            get { return _warnings; }
        }

        /// <summary>Lo que se hizo (crear la autoridad, renovar el certificado), para el registro.</summary>
        public IList<string> Notes
        {
            get { return _notes; }
        }

        internal void AddReady(int port)
        {
            _readyPorts.Add(port);
        }
    }

    /// <summary>
    /// Prepara todo lo que necesita HTTPS antes de arrancar el servidor:
    /// la autoridad local (se crea la primera vez), el certificado del
    /// servidor (se renueva si caduca o el equipo tiene direcciones nuevas) y
    /// su asociacion en http.sys a cada puerto.
    ///
    /// Lo que no se puede hacer no detiene nada: ese puerto se queda sin
    /// HTTPS, el sitio sigue sirviendose por HTTP y el motivo vuelve en el
    /// plan. Crear la autoridad no necesita administrador; instalar el
    /// certificado del servidor y asociarlo, si.
    /// </summary>
    public static class HttpsProvisioner
    {
        /// <param name="machineName">Nombre del equipo, para el nombre comun de una autoridad nueva.</param>
        /// <param name="desired">Nombres que debe cubrir el certificado del servidor (ver LeafPolicy).</param>
        public static HttpsPlan Prepare(ICollection<int> httpsPorts, bool isAdmin, string machineName,
                                        LeafNames desired, DateTimeOffset now)
        {
            HttpsPlan plan = new HttpsPlan();
            if (httpsPorts == null || httpsPorts.Count == 0) return plan;
            if (desired == null) throw new ArgumentNullException("desired");

            LocalCertificateAuthority authority;
            LocalCertificateAuthority replaced = null;
            try
            {
                authority = LocalCertificateAuthority.Load();
                bool expiring = authority != null &&
                                LeafPolicy.AuthorityNeedsReplacement(authority.NotAfterUtc, now.UtcDateTime);

                if (expiring && !isAdmin)
                {
                    // Sin administrador no se podria emitir enseguida el
                    // certificado del servidor con la nueva: se sigue con la
                    // actual, que aun vale, y se avisa.
                    plan.Warnings.Add("La autoridad local de HTTPS caduca en menos de un ano: pulsa Escalar " +
                                      "Privilegios para crear otra (despues habra que instalarla en cada movil).");
                }
                else if (authority == null || expiring)
                {
                    if (expiring) replaced = authority;
                    authority = LocalCertificateAuthority.Create(machineName, now);
                    plan.Notes.Add("Creada la autoridad local de HTTPS. Huella SHA-256: " + authority.Fingerprint);
                    plan.Notes.Add("Instala la autoridad una vez en cada movil: clic derecho en el sitio, codigo QR " +
                                   "del certificado.");
                    if (expiring)
                    {
                        plan.Warnings.Add("La autoridad local de HTTPS caducaba en menos de un ano y se creo otra: " +
                                          "instalala de nuevo en cada movil (clic derecho en el sitio, codigo QR " +
                                          "del certificado) y borra alli la anterior.");
                    }
                }
            }
            catch (Exception ex)
            {
                plan.Problems.Add("No se pudo preparar la autoridad local de HTTPS: " + ex.Message);
                return plan;
            }
            plan.Authority = authority;

            X509Certificate2 leaf;
            try
            {
                leaf = PrepareLeaf(authority, isAdmin, desired, now, plan);
            }
            catch (Exception ex)
            {
                plan.Problems.Add("No se pudo preparar el certificado HTTPS del servidor: " + ex.Message);
                return plan;
            }
            if (leaf == null) return plan;

            // Los certificados que firmo la autoridad sustituida ya no sirven:
            // los moviles confiaran en la nueva.
            if (replaced != null) RemoveSignedBy(replaced, leaf, plan);

            byte[] hash = leaf.GetCertHash();
            foreach (int port in httpsPorts)
            {
                try
                {
                    BindPort(port, hash, isAdmin, plan);
                }
                catch (Exception ex)
                {
                    plan.Problems.Add("Puerto " + port + ": no se pudo asociar el certificado (" + ex.Message + ").");
                }
            }
            return plan;
        }

        /// <summary>
        /// Quita las asociaciones de LanShare de los puertos que ya no son el
        /// puerto HTTPS de ningun sitio. Solo con administrador. Las que usan
        /// un certificado vigente de otra autoridad son de otro usuario de este
        /// equipo y no se tocan.
        /// </summary>
        /// <returns>Los puertos liberados.</returns>
        public static List<int> ReleaseUnused(ICollection<int> stillConfigured)
        {
            byte[] authorityKeyId = CurrentAuthorityKeyId();
            List<int> released = new List<int>();
            List<int> examined = new List<int>();

            foreach (SslBinding binding in HttpSysCertBinding.ListAll())
            {
                if (binding.AppId != HttpSysCertBinding.AppId || binding.Port <= 0) continue;
                if (examined.Contains(binding.Port)) continue;
                examined.Add(binding.Port);

                if (stillConfigured != null && stillConfigured.Contains(binding.Port)) continue;
                if (!ServerCertificateStore.CanRelease(binding.CertificateHash, authorityKeyId)) continue;
                if (HttpSysCertBinding.Remove(binding.Port) == null) released.Add(binding.Port);
            }
            return released;
        }

        /// <summary>Quita del almacen los certificados de servidor de una autoridad sustituida.</summary>
        private static void RemoveSignedBy(LocalCertificateAuthority replaced, X509Certificate2 keep, HttpsPlan plan)
        {
            try
            {
                ServerCertificateStore.RemoveStale(replaced, keep);
            }
            catch (Exception ex)
            {
                plan.Notes.Add("No se pudieron retirar los certificados de la autoridad anterior: " + ex.Message);
            }
        }

        /// <summary>Identificador de la autoridad de este usuario, o null si no tiene o no se puede leer.</summary>
        private static byte[] CurrentAuthorityKeyId()
        {
            try
            {
                LocalCertificateAuthority authority = LocalCertificateAuthority.Load();
                return authority != null ? authority.KeyIdentifier : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Borra de Windows todo lo de HTTPS de LanShare: las asociaciones de
        /// http.sys y los certificados de servidor (con administrador) y la
        /// autoridad local de este usuario.
        /// </summary>
        /// <returns>Avisos sobre lo que no se pudo borrar; vacia si se borro todo.</returns>
        public static List<string> RemoveEverything(bool isAdmin)
        {
            List<string> pending = new List<string>();

            if (isAdmin)
            {
                foreach (int port in HttpSysCertBinding.FindOurPorts())
                {
                    string error = HttpSysCertBinding.Remove(port);
                    if (error != null) pending.Add("Puerto " + port + ": " + error);
                }
                ServerCertificateStore.RemoveAll();
            }
            else if (HttpSysCertBinding.FindOurPorts().Count > 0)
            {
                pending.Add("Las asociaciones de http.sys y el certificado del servidor solo se pueden borrar " +
                            "como administrador: vuelve a hacerlo tras pulsar Escalar Privilegios.");
            }

            LocalCertificateAuthority.DeleteForThisUser(pending);
            return pending;
        }

        /// <summary>
        /// El certificado del servidor que hay que usar. Si hace falta uno
        /// nuevo y hay administrador, se emite, se instala y se retiran los
        /// anteriores. Sin administrador se sigue con el que haya, si lo hay.
        /// </summary>
        private static X509Certificate2 PrepareLeaf(LocalCertificateAuthority authority, bool isAdmin,
                                                    LeafNames desired, DateTimeOffset now, HttpsPlan plan)
        {
            X509Certificate2 current = ServerCertificateStore.FindCurrent(authority);

            LeafNames covered = current != null
                ? CertificateExtensionData.ReadSubjectAltNames(
                      CertificateExtensionData.Find(current, CertificateExtensionData.SubjectAltNameOid))
                : null;
            DateTime notAfter = current != null ? current.NotAfter.ToUniversalTime() : DateTime.MinValue;

            LeafRenewal renewal = LeafPolicy.Evaluate(current != null, true, notAfter, now.UtcDateTime,
                                                      covered, desired);
            if (renewal == LeafRenewal.NotNeeded) return current;

            if (!isAdmin)
            {
                if (current == null)
                {
                    plan.Problems.Add("Instalar el certificado HTTPS del servidor necesita administrador (la primera " +
                                      "vez, o si el anterior caduco): pulsa Escalar Privilegios.");
                    return null;
                }

                plan.Warnings.Add("Hay que renovar el certificado HTTPS del servidor (" + LeafPolicy.Describe(renewal) +
                                  "): pulsa Escalar Privilegios. Mientras tanto se sigue usando el actual.");
                return current;
            }

            X509Certificate2 renewed = IssueAndInstall(authority, desired, now);
            ServerCertificateStore.RemoveStale(authority, renewed);
            plan.Notes.Add("Certificado HTTPS del servidor emitido (" + LeafPolicy.Describe(renewal) +
                           "). Cubre: " + desired.Describe() + ".");
            return renewed;
        }

        /// <summary>
        /// La clave del servidor nace como clave efimera en memoria y se guarda
        /// en el almacen del equipo a traves de un PFX, que es el camino probado
        /// en .NET Framework para dejar una clave permanente y asociada a su
        /// certificado. Al importarla queda como no exportable, y el PFX se
        /// borra de la memoria en cuanto se usa (ServerCertificateStore.Install).
        /// </summary>
        private static X509Certificate2 IssueAndInstall(LocalCertificateAuthority authority, LeafNames names,
                                                        DateTimeOffset now)
        {
            using (RSACryptoServiceProvider serverKey = new RSACryptoServiceProvider(CertificateFactory.ServerKeySize))
            using (X509Certificate2 issued = authority.IssueServerCertificate(serverKey, names, now))
            using (X509Certificate2 withKey = issued.CopyWithPrivateKey(serverKey))
            {
                return ServerCertificateStore.Install(withKey);
            }
        }

        private static void BindPort(int port, byte[] hash, bool isAdmin, HttpsPlan plan)
        {
            if (!isAdmin)
            {
                if (HttpSysCertBinding.IsBound(port, hash)) plan.AddReady(port);
                else plan.Problems.Add("Puerto " + port + ": asociar el certificado en http.sys necesita " +
                                       "administrador. Pulsa Escalar Privilegios.");
                return;
            }

            string warning;
            string error = HttpSysCertBinding.Ensure(port, hash, out warning);
            if (error != null)
            {
                plan.Problems.Add("Puerto " + port + ": " + error);
                return;
            }

            plan.AddReady(port);
            if (warning != null) plan.Notes.Add(warning);
        }
    }
}
