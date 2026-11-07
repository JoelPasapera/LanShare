using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using LanShare.Core;
using LanShare.Https;
using LanShare.Net;

namespace LanShare.Ui
{
    /// <summary>
    /// HTTPS en la ventana principal: prepararlo al arrancar, decir lo que no
    /// se pudo y ofrecer al movil el certificado de la autoridad local.
    ///
    /// Un sitio cuyo HTTPS no se puede preparar (falta administrador, otro
    /// programa tiene el puerto) no se pierde: se sirve igual por HTTP y se
    /// explica el motivo.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Lo preparado en el ultimo arranque; null si ningun sitio se sirve por HTTPS.</summary>
        private HttpsPlan _httpsPlan;

        /// <summary>
        /// Prepara HTTPS para los sitios que lo piden y devuelve los sitios
        /// tal como se van a servir: sin HTTPS los que no se pudo preparar.
        /// </summary>
        /// <param name="problems">Donde anotar, para el usuario, lo que impidio el HTTPS.</param>
        /// <param name="warnings">Donde anotar lo que no lo impide pero pide atencion.</param>
        private List<SiteRuntime> PrepareHttps(List<SiteRuntime> runtimes, List<string> problems, List<string> warnings)
        {
            List<int> ports = new List<int>();
            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.HttpsPort > 0) ports.Add(runtime.HttpsPort);
            }

            ReleaseUnusedHttpsBindings();

            if (ports.Count == 0)
            {
                _httpsPlan = null;
                return runtimes;
            }

            // El certificado solo cubre la red local si algun sitio con HTTPS se
            // sirve en ella: si no, basta el bucle local y un cambio de Wi-Fi
            // no obliga a renovarlo.
            string host = LocalHostName();
            LeafNames desired = ServesHttpsToLan(runtimes)
                ? LeafPolicy.Desired(host, ReachableLanAddresses())
                : LeafPolicy.LoopbackOnly();

            // La primera vez se genera una clave RSA de 3072 bits: un segundo o dos.
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            HttpsPlan plan;
            try
            {
                plan = HttpsProvisioner.Prepare(ports, _isAdmin, host, desired, DateTimeOffset.Now);
            }
            finally
            {
                Cursor.Current = previous;
            }

            foreach (string note in plan.Notes) _log.Write("INFO", "HTTPS: " + note);
            foreach (string problem in plan.Problems)
            {
                _log.Write("WARN", "HTTPS: " + problem);
                if (!problems.Contains(problem)) problems.Add(problem);
            }
            foreach (string warning in plan.Warnings)
            {
                _log.Write("WARN", "HTTPS: " + warning);
                if (!warnings.Contains(warning)) warnings.Add(warning);
            }

            if (plan.ReadyPorts.Count > 0 && !_settings.HttpsPrepared)
            {
                _settings.HttpsPrepared = true;
                _settings.Save();
            }

            List<SiteRuntime> result = new List<SiteRuntime>(runtimes.Count);
            foreach (SiteRuntime runtime in runtimes)
            {
                bool wanted = runtime.HttpsPort > 0;
                bool ready = wanted && plan.ReadyPorts.Contains(runtime.HttpsPort);
                result.Add(wanted && !ready ? runtime.WithoutHttps() : runtime);
            }

            _httpsPlan = plan.Authority != null && plan.ReadyPorts.Count > 0 ? plan : null;
            return result;
        }

        /// <summary>Si algun sitio se va a servir por HTTPS a toda la red local.</summary>
        private static bool ServesHttpsToLan(List<SiteRuntime> runtimes)
        {
            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.HttpsPort > 0 && runtime.LanMode) return true;
            }
            return false;
        }

        /// <summary>
        /// Las direcciones por las que puede llegar un movil. Las de WSL o del
        /// conmutador por defecto de Hyper-V cambian en cada arranque y ningun
        /// movil las alcanza: con ellas en el certificado habria que renovarlo
        /// a cada reinicio.
        /// </summary>
        private static List<string> ReachableLanAddresses()
        {
            List<string> addresses = new List<string>();
            foreach (LanAddress address in LanAddresses.GetActiveDetailed())
            {
                if (LanAddressRanking.IsReachableFromLan(address)) addresses.Add(address.Address);
            }
            return addresses;
        }

        /// <summary>
        /// Quita el HTTPS a los sitios cuyo prefijo https:// no se puede
        /// reservar porque otro programa lo tiene en http.sys. null si no habia
        /// ninguno: entonces el fallo es otro y reintentar no serviria.
        /// </summary>
        private List<SiteRuntime> DropBlockedHttps(List<SiteRuntime> runtimes, List<string> problems)
        {
            List<SiteRuntime> result = new List<SiteRuntime>(runtimes.Count);
            bool changed = false;

            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.HttpsPort > 0 && !PortAvailability.CanReserveHttp(runtime.HttpsPort, runtime.LanMode, true))
                {
                    string reason = "Puerto " + runtime.HttpsPort + ": otro programa ya reservo ese puerto HTTPS.";
                    if (!problems.Contains(reason)) problems.Add(reason);
                    _log.Write("WARN", "HTTPS: " + reason);
                    result.Add(runtime.WithoutHttps());
                    changed = true;
                }
                else
                {
                    result.Add(runtime);
                }
            }

            return changed ? result : null;
        }

        /// <summary>
        /// Asociaciones de http.sys que dejo un sitio al que se le quito el
        /// HTTPS o se le cambio el puerto. Solo con administrador, y solo si
        /// LanShare llego a preparar HTTPS alguna vez: quien no lo usa no
        /// necesita que se consulte nada de http.sys en cada arranque.
        /// </summary>
        private void ReleaseUnusedHttpsBindings()
        {
            if (!_isAdmin || !_settings.HttpsPrepared) return;

            HashSet<int> configured = new HashSet<int>();
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (site.HttpsPort > 0) configured.Add(site.HttpsPort);
            }

            try
            {
                foreach (int port in HttpsProvisioner.ReleaseUnused(configured))
                {
                    _log.Write("INFO", "HTTPS: liberado el puerto " + port + ", que ya no usa ningun sitio.");
                }
            }
            catch (Exception ex)
            {
                _log.Write("WARN", "HTTPS: no se pudieron revisar las asociaciones de http.sys: " + ex.Message);
            }
        }

        /// <summary>El certificado que se ofrece en /__certificado viaja con las opciones del servidor.</summary>
        private void ApplyHttpsToOptions(ServerOptions options)
        {
            if (_httpsPlan == null || _httpsPlan.Authority == null) return;
            options.HttpsAuthorityCertificate = _httpsPlan.Authority.RawData;
            options.HttpsAuthorityFingerprint = _httpsPlan.Authority.Fingerprint;
        }

        /// <summary>
        /// Lo que impidio el HTTPS o pide atencion, una vez arrancado el
        /// servidor: con la ventana delante, un dialogo; desatendido, la bandeja.
        /// </summary>
        private void ReportHttpsProblems(bool quiet, List<string> problems, List<string> warnings)
        {
            if (problems.Count == 0 && warnings.Count == 0) return;

            if (quiet && !(_restarting && Visible))
            {
                if (_tray != null)
                {
                    _tray.Notify(AppInfo.Name, problems.Count > 0
                                     ? "Algun sitio se sirve solo por HTTP: abre LanShare para ver el motivo."
                                     : "El certificado HTTPS necesita atencion: abre LanShare para ver el registro.",
                                 ToolTipIcon.Warning);
                }
                return;
            }

            StringBuilder text = new StringBuilder();
            if (problems.Count > 0)
            {
                text.Append("Estos sitios se sirven por HTTP, pero no por HTTPS:\r\n");
                foreach (string problem in problems) text.Append("\r\n- ").Append(problem);
            }
            if (warnings.Count > 0)
            {
                if (text.Length > 0) text.Append("\r\n\r\n");
                text.Append("HTTPS funciona, pero:\r\n");
                foreach (string warning in warnings) text.Append("\r\n- ").Append(warning);
            }
            Warn(text.ToString(), problems.Count > 0 ? "HTTPS no disponible" : "HTTPS");
        }

        /// <summary>
        /// Nombre del equipo tal como lo anuncia Windows por mDNS (nombre.local):
        /// el nombre DNS completo, no el de NetBIOS, que se corta en 15 letras.
        /// </summary>
        private static string LocalHostName()
        {
            try
            {
                string name = System.Net.Dns.GetHostName();
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch (Exception)
            {
            }
            return Environment.MachineName;
        }

        /// <summary>Si el sitio se esta sirviendo ahora mismo tambien por HTTPS.</summary>
        private bool IsSiteServedOverHttps(SiteDefinition site)
        {
            if (site == null || _server == null) return false;
            SiteRuntime running = _server.Options.FindByPort(site.Port);
            return running != null && running.HttpsPort > 0;
        }

        /// <summary>Puerto HTTPS con el que se sirve el sitio ahora, o 0.</summary>
        private int RunningHttpsPort(SiteDefinition site)
        {
            if (site == null || _server == null) return 0;
            SiteRuntime running = _server.Options.FindByPort(site.Port);
            return running != null ? running.HttpsPort : 0;
        }

        /// <summary>
        /// Direcciones de la pagina del certificado, una por adaptador: van por
        /// HTTP porque el movil aun no confia en el HTTPS.
        /// </summary>
        private static List<UrlChoice> CertificateUrlsFor(SiteDefinition site)
        {
            List<UrlChoice> urls = new List<UrlChoice>();
            if (site == null) return urls;

            foreach (LanAddress address in LanAddresses.GetActiveDetailed())
            {
                string url = "http://" + address.Address + ":" +
                             site.Port.ToString(CultureInfo.InvariantCulture) + CertificatePage.Endpoint;
                urls.Add(UrlChoice.ForSite("Certificado para el movil", url, address.AdapterName));
            }
            return urls;
        }
    }
}
