using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using LanShare.Core;
using LanShare.Net;
using LanShare.Platform;

namespace LanShare.Ui
{
    /// <summary>
    /// La red como publica en Windows: la causa mas comun de que el movil no
    /// conecte. La regla de firewall de LanShare solo abre las redes privadas
    /// y las de dominio, a proposito: en una red publica de verdad (una
    /// cafeteria) no conviene abrir puertos. Pero Windows clasifica asi muchas
    /// redes de casa, y entonces parece que el codigo QR no funciona. Aqui se
    /// detecta y se ofrece pasarla a privada.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Redes que Windows tenia como publicas en la ultima comprobacion.</summary>
        private List<PublicNetwork> _publicNetworks = new List<PublicNetwork>();

        /// <summary>Las que el usuario prefirio dejar asi en esta sesion: no se le vuelve a preguntar.</summary>
        private readonly HashSet<Guid> _keptPublicNetworks = new HashSet<Guid>();

        /// <summary>Las ya avisadas en la bandeja, para no repetirlo en cada reinicio desatendido.</summary>
        private readonly HashSet<Guid> _notifiedPublicNetworks = new HashSet<Guid>();

        /// <summary>Si la lista de redes de Windows ya fallo una vez: el aviso va al registro solo esa vez.</summary>
        private bool _networkListFailed;

        /// <summary>
        /// La consulta va por COM llamando a mano a la tabla de metodos: si
        /// alguna vez tumbara el proceso, la marca impide que se repita en
        /// cada arranque y LanShare sigue funcionando sin este aviso.
        /// </summary>
        private static readonly CrashGuard NetworkListGuard =
            new CrashGuard(Path.GetDirectoryName(AppSettings.FilePath), "lista-de-redes");

        /// <summary>Vuelve a preguntar a Windows que redes tiene como publicas.</summary>
        private void RefreshPublicNetworks()
        {
            _publicNetworks = new List<PublicNetwork>();

            if (NetworkListGuard.Tripped)
            {
                ReportNetworkListProblem("la consulta cerro LanShare la ultima vez y queda desactivada. Para " +
                                         "volver a probarla, borra " + NetworkListGuard.MarkerPath);
                return;
            }

            List<NetworkConnectionInfo> connections = null;
            try
            {
                NetworkListGuard.Run(delegate { connections = NetworkListManager.GetConnections(); });
            }
            catch (Exception ex)
            {
                ReportNetworkListProblem(ex.Message);
                return;
            }

            if (connections != null)
            {
                _publicNetworks = PublicNetworks.Find(LanAddresses.GetActiveDetailed(), connections);
            }
        }

        /// <summary>Lo que impide saber si la red es publica, una sola vez por sesion en el registro.</summary>
        private void ReportNetworkListProblem(string reason)
        {
            if (_networkListFailed) return;
            _networkListFailed = true;
            _log.Write("WARN", "No se pudo consultar si la red es publica o privada: " + reason);
        }

        /// <summary>Si algun sitio se sirve a la red local: solo entonces importa la categoria de la red.</summary>
        private static bool ServesLan(IList<SiteRuntime> runtimes)
        {
            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.LanMode) return true;
            }
            return false;
        }

        /// <summary>
        /// Tras arrancar con sitios en red local: por cada red publica, un aviso
        /// en el registro y la pregunta de si pasarla a privada. Sin nadie
        /// delante, solo un aviso en la bandeja.
        /// </summary>
        /// <param name="canAsk">Si se puede mostrar un dialogo (hay alguien delante).</param>
        private void OfferPrivateNetworks(bool canAsk)
        {
            foreach (PublicNetwork network in _publicNetworks.ToArray())
            {
                _log.Write("WARN", "Windows tiene la red " + network.Describe() + " como publica: el firewall no " +
                                   "deja entrar a los moviles.");

                if (!canAsk)
                {
                    NotifyPublicNetworkOnce(network);
                    continue;
                }
                if (_keptPublicNetworks.Contains(network.NetworkId)) continue;

                DialogResult answer = MessageBox.Show(this, PublicNetworkQuestion(network), "Red publica",
                                                      MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    _keptPublicNetworks.Add(network.NetworkId);
                    continue;
                }

                string error = MakeNetworkPrivate(network);
                if (error != null)
                {
                    Warn("No se pudo pasar la red a privada: " + error + "\r\n\r\n" + ManualPrivateSteps, "Red publica");
                }
            }
        }

        /// <summary>
        /// Pasa una red a privada y, si se pudo, lo refleja en el estado.
        /// </summary>
        /// <returns>null si se pudo; si no, el motivo.</returns>
        private string MakeNetworkPrivate(PublicNetwork network)
        {
            string error = null;
            if (NetworkListGuard.Tripped)
            {
                error = "la lista de redes de Windows esta desactivada en LanShare.";
            }
            else
            {
                NetworkListGuard.Run(delegate { error = NetworkListManager.MakePrivate(network.NetworkId); });
            }

            if (error != null)
            {
                _log.Write("WARN", "No se pudo pasar a privada la red " + network.Describe() + ": " + error);
                return error;
            }

            _log.Write("INFO", "La red " + network.Describe() + " ahora es privada: los moviles ya pueden conectar.");
            RefreshPublicNetworks();
            if (_server != null) BuildStatusText(_server.Options.Sites);
            return null;
        }

        /// <summary>El aviso del codigo QR si alguna red esta como publica; null si ninguna.</summary>
        private NetworkNotice PublicNetworkNotice()
        {
            if (_publicNetworks.Count == 0) return null;

            List<PublicNetwork> networks = new List<PublicNetwork>(_publicNetworks);
            string text = "Windows tiene " + DescribeAll(networks) + " como publica" + (networks.Count > 1 ? "s" : "") +
                          " y el firewall no deja entrar al movil: este codigo no abrira nada.";

            return new NetworkNotice(text, "Pasar a privada", delegate
            {
                foreach (PublicNetwork network in networks)
                {
                    string error = MakeNetworkPrivate(network);
                    if (error != null) return error;
                }
                return null;
            });
        }

        /// <summary>La linea de aviso del cuadro de estado; vacia si no hay redes publicas.</summary>
        private string PublicNetworkStatusLine()
        {
            if (_publicNetworks.Count == 0) return "";
            return "Aviso: Windows tiene " + DescribeAll(_publicNetworks) + " como publica" +
                   (_publicNetworks.Count > 1 ? "s" : "") + "; el firewall no deja entrar a los moviles " +
                   "(Codigo QR: Pasar a privada).";
        }

        private void NotifyPublicNetworkOnce(PublicNetwork network)
        {
            if (_tray == null || !_notifiedPublicNetworks.Add(network.NetworkId)) return;

            string name = network.Name.Length > 0 ? "\"" + network.Name + "\"" : "de este equipo";
            _tray.Notify(AppInfo.Name, "La red " + name + " esta como publica en Windows: los moviles no podran " +
                                       "conectar. Abre LanShare para pasarla a privada.", ToolTipIcon.Warning);
        }

        private static string PublicNetworkQuestion(PublicNetwork network)
        {
            string name = network.Name.Length > 0 ? "\"" + network.Name + "\"" : "la red";
            return "Windows tiene la red " + network.Describe() + " clasificada como publica, y la regla de " +
                   "firewall de LanShare solo abre las redes privadas: el movil no podra conectar y parecera " +
                   "que el codigo QR no funciona.\r\n\r\n" +
                   "Si es la red de tu casa o de tu trabajo, pasala a privada. Si es una red publica de verdad " +
                   "(cafeteria, aeropuerto, hotel), mejor dejala asi.\r\n\r\n" +
                   "Pasar " + name + " a privada ahora?";
        }

        /// <summary>"la red "Casa"" o "las redes "Casa", "Oficina"".</summary>
        private static string DescribeAll(IList<PublicNetwork> networks)
        {
            StringBuilder text = new StringBuilder(networks.Count == 1 ? "la red " : "las redes ");
            for (int i = 0; i < networks.Count; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(networks[i].Describe());
            }
            return text.ToString();
        }

        private const string ManualPrivateSteps =
            "Puedes hacerlo a mano: Configuracion de Windows, Red e Internet, propiedades de tu Wi-Fi (o de tu " +
            "Ethernet), Tipo de perfil de red: Red privada.";
    }
}
