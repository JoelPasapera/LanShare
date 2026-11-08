using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using LanShare.Core;
using LanShare.Identity;
using LanShare.Net;
using LanShare.Platform;

namespace LanShare.Ui
{
    public partial class MainForm : Form
    {
        private const int MaxLogChars = 400000;
        private const int MaxLogDrainPerTick = 500;

        /// <summary>
        /// Entradas que se conservan en memoria para poder filtrar y guardar.
        /// El cuadro de texto muestra solo las que pasan el filtro; esta es la
        /// fuente completa.
        /// </summary>
        private const int MaxLogEntries = 4000;

        private readonly LogBus _log = new LogBus();
        private readonly Dictionary<string, ListViewItem> _rows =
            new Dictionary<string, ListViewItem>(StringComparer.Ordinal);

        private readonly Timer _logTimer = new Timer();
        private readonly Timer _clientsTimer = new Timer();

        private readonly AppSettings _settings = AppSettings.Load();
        private HttpFileServer _server;
        private bool _closing;

        /// <summary>Parada en curso, para que una segunda peticion espere a la misma.</summary>
        private System.Threading.Tasks.Task _stopping;
        private List<UrlChoice> _lanUrls = new List<UrlChoice>();
        private readonly List<int> _firewallPorts = new List<int>();
        private bool _suspendSiteEvents;
        private DateTime _lastSiteInput = DateTime.MinValue;

        private readonly Queue<LogEntry> _logEntries = new Queue<LogEntry>(MaxLogEntries);
        private int _logFilterPort;   // 0 = todos los sitios
        private bool _isAdmin;
        private readonly StartupOptions _startup;

        private string _lastDetailIp = "";
        private string _lastDetailText = "";

        public MainForm()
            : this(StartupOptions.Empty)
        {
        }

        public MainForm(StartupOptions startup)
        {
            _startup = startup ?? StartupOptions.Empty;
            BuildUi();

            _isAdmin = Elevation.IsAdministrator();
            _permissionLabel.Text = _isAdmin
                ? "Permisos: Administrador (Elevado)"
                : "Permisos: Usuario Estandar (Sin Administrador)";
            _permissionLabel.ForeColor = _isAdmin ? Color.DarkGreen : Color.DarkRed;
            _elevateButton.Enabled = !_isAdmin;

            ApplySettings();
            ClientRegistry.EnablePortScan = _settings.PortScan;
            ClientRegistry.EnableDeepName = _settings.DeepName;
            LoadOuiTable();
            InitializeTray();
            InitializeFileDrop();
            InitializeShortcuts();
            InitializeRestart();

            _logTimer.Interval = 100;
            _logTimer.Tick += OnLogTick;
            _logTimer.Start();

            _clientsTimer.Interval = 1500;
            _clientsTimer.Tick += OnClientsTick;
            _clientsTimer.Start();

            // El mantenimiento no abre sockets por si mismo, asi que puede
            // arrancar desde el principio. La escucha mDNS no: se une a un grupo
            // multicast con un socket a la escucha y eso dispara el aviso del
            // firewall, asi que espera al modo LAN.
            ClientRegistry.StartMaintenance();

            Shown += HandleShown;
            FormClosing += HandleFormClosing;
        }

        /// <summary>Vuelca la configuracion guardada sobre los controles.</summary>
        private void ApplySettings()
        {
            // Se guarda ClientSize, asi que hay que comparar contra el area de
            // cliente minima, no contra MinimumSize, que incluye los bordes.
            Size minimumClient = new Size(
                MinimumSize.Width - (Size.Width - ClientSize.Width),
                MinimumSize.Height - (Size.Height - ClientSize.Height));

            if (_settings.WindowWidth >= minimumClient.Width &&
                _settings.WindowHeight >= minimumClient.Height)
            {
                ClientSize = new Size(_settings.WindowWidth, _settings.WindowHeight);
            }

            // Despues del tamano: las alturas guardadas se encajan en el que haya.
            RestorePaneHeights();

            ClientRegistry.LoadAliases(_settings.Aliases);
            RefreshSiteList();
        }

        /// <summary>Recoge el estado actual de los controles antes de guardar.</summary>
        private void CaptureSettings()
        {
            CapturePaneHeights();
            // Minimizada u oculta en la bandeja, ClientSize no es el tamano real
            // y guardarlo haria que la siguiente sesion abriera diminuta.
            if (Visible && WindowState == FormWindowState.Normal)
            {
                _settings.WindowWidth = ClientSize.Width;
                _settings.WindowHeight = ClientSize.Height;
            }
            _settings.Aliases = ClientRegistry.ExportAliases();
        }

        // ------------------------------------------------------------------
        // Sitios
        // ------------------------------------------------------------------

        private void OnSiteListInput(object sender, EventArgs e)
        {
            _lastSiteInput = DateTime.UtcNow;
        }

        private void RefreshSiteList()
        {
            // Repoblar pierde la seleccion; se anota para restaurarla, porque
            // esto ahora corre tambien al arrancar y parar el servidor.
            int selectedPort = -1;
            if (_sitesList.SelectedItems.Count > 0)
            {
                SiteDefinition previo = _sitesList.SelectedItems[0].Tag as SiteDefinition;
                if (previo != null) selectedPort = previo.Port;
            }

            // Rellenar la tabla dispara ItemChecked por cada fila; sin esta
            // guarda, repoblarla reescribiria la configuracion con su propio eco.
            _suspendSiteEvents = true;
            _sitesList.BeginUpdate();
            try
            {
                _sitesList.Items.Clear();
                foreach (SiteDefinition site in _settings.Sites)
                {
                    ListViewItem item = new ListViewItem(
                        site.Port.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(site.RootPath);
                    item.SubItems.Add(site.StartFileDisplay);
                    item.SubItems.Add(site.LanMode ? "Red local" : "Solo este equipo");
                    item.SubItems.Add(site.HttpsPort > 0 ? site.HttpsPort.ToString(CultureInfo.InvariantCulture) : "");
                    item.SubItems.Add(site.CrossOriginIsolation ? IsolatedMark : "");
                    item.SubItems.Add(site.Label);
                    item.SubItems.Add("http://localhost:" + site.Port + "/");
                    item.Tag = site;

                    // Checked se asigna DESPUES de insertar: en un elemento aun
                    // suelto el estado no siempre sobrevive a la insercion.
                    _sitesList.Items.Add(item);
                    item.Checked = site.Enabled;
                }
            }
            finally
            {
                _sitesList.EndUpdate();
                _suspendSiteEvents = false;
            }

            if (selectedPort < 0) return;
            foreach (ListViewItem item in _sitesList.Items)
            {
                SiteDefinition site = item.Tag as SiteDefinition;
                if (site != null && site.Port == selectedPort) { item.Selected = true; break; }
            }
        }

        private SiteDefinition SelectedSite
        {
            get
            {
                return _sitesList.SelectedItems.Count > 0
                    ? (SiteDefinition)_sitesList.SelectedItems[0].Tag
                    : null;
            }
        }

        private void OnSiteCheckedChanged(object sender, ItemCheckedEventArgs e)
        {
            // ListView emite este evento tambien al recrear su handle interno,
            // no solo cuando el usuario hace clic. Sin filtrarlo, arrancar el
            // servidor podia desmarcar un sitio en la configuracion guardada.
            //
            // El criterio es haber recibido raton o teclado sobre la tabla hace
            // un instante. Mirar si tiene el foco seria mas simple pero puede
            // fallar al reves: si el foco aun no ha llegado cuando se emite el
            // evento, se descartaria un clic legitimo y el modelo quedaria
            // desincronizado de lo que ve el usuario.
            if (_suspendSiteEvents) return;
            if ((DateTime.UtcNow - _lastSiteInput).TotalSeconds > 1.0) return;

            SiteDefinition site = e.Item.Tag as SiteDefinition;
            if (site == null) return;
            if (site.Enabled == e.Item.Checked) return;

            site.Enabled = e.Item.Checked;
            _settings.Save();
            ScheduleRestart();
        }

        private void OnAddSiteClick(object sender, EventArgs e)
        {
            SiteDefinition added = AddSite(null, null);
            if (added != null) OnSiteAddedWhileRunning(added);
        }

        /// <summary>
        /// Dialogo de sitio nuevo. Con <paramref name="root"/>, ya relleno con
        /// esa carpeta, como al arrastrarla sobre la ventana.
        /// </summary>
        /// <param name="startFile">Pagina de inicio propuesta; "" para listar la carpeta.</param>
        /// <returns>El sitio anadido, o null si se cancelo.</returns>
        private SiteDefinition AddSite(string root, string startFile)
        {
            using (SiteDialog dialog = new SiteDialog(null, _settings.RecentRoots, OtherSitePorts(null)))
            {
                if (root != null) dialog.Prefill(root, startFile);
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                if (!PortsAreFree(dialog.Result, null)) return null;

                _settings.Sites.Add(dialog.Result);
                _settings.RememberRoot(dialog.Result.RootPath);
                _settings.Save();
                RefreshSiteList();
                return dialog.Result;
            }
        }

        private void OnEditSiteClick(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null) return;

            using (SiteDialog dialog = new SiteDialog(site.Clone(), _settings.RecentRoots, OtherSitePorts(site)))
            {
                // Sus puertos los tiene ocupados el propio servidor: no es un conflicto.
                if (IsSiteRunning(site))
                {
                    dialog.CurrentPort = site.Port;
                    dialog.CurrentHttpsPort = RunningHttpsPort(site);
                }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!PortsAreFree(dialog.Result, site)) return;

                int index = _settings.Sites.IndexOf(site);
                if (index < 0) return;

                _settings.Sites[index] = dialog.Result;
                _settings.RememberRoot(dialog.Result.RootPath);
                _settings.Save();
                RefreshSiteList();
                if (AffectsServing(site, dialog.Result)) ScheduleRestart();
            }
        }

        private void OnRemoveSiteClick(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null) return;

            DialogResult confirm = MessageBox.Show(this,
                "Quitar el sitio del puerto " + site.Port + "?\r\n\r\n" + site.RootPath,
                "Quitar sitio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            _settings.Sites.Remove(site);
            _settings.Save();
            RefreshSiteList();
            if (site.Enabled) ScheduleRestart();
        }

        /// <summary>Puertos de los demas sitios (HTTP y HTTPS), para no proponer uno ya tomado.</summary>
        private List<int> OtherSitePorts(SiteDefinition exclude)
        {
            List<int> ports = new List<int>();
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (ReferenceEquals(site, exclude)) continue;
                ports.Add(site.Port);
                if (site.HttpsPort > 0) ports.Add(site.HttpsPort);
            }
            return ports;
        }

        /// <summary>
        /// Dos sitios en el mismo puerto harian fallar el listener entero; y
        /// el puerto HTTPS de uno cuenta igual que el HTTP de otro.
        /// </summary>
        private bool PortsAreFree(SiteDefinition candidate, SiteDefinition ignore)
        {
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (ReferenceEquals(site, ignore)) continue;

                foreach (int port in new int[] { candidate.Port, candidate.HttpsPort })
                {
                    if (port <= 0) continue;
                    if (site.Port != port && site.HttpsPort != port) continue;

                    Warn("El puerto " + port + " ya lo usa otro sitio:\r\n\r\n" + site.RootPath,
                         "Puerto ocupado");
                    return false;
                }
            }
            return true;
        }

        private void LoadOuiTable()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "oui.txt");
            OuiTable.TryLoadFile(path);
            UpdateOuiLabel();
        }

        private void HandleShown(object sender, EventArgs e)
        {
            CompleteStartup();
        }

        // ------------------------------------------------------------------
        // Arranque y parada
        // ------------------------------------------------------------------

        private async void OnStartClick(object sender, EventArgs e)
        {
            if (_server != null)
            {
                await StopServerAsync();
                return;
            }

            // Se consume al principio: si el arranque se aborta mas abajo, la
            // marca no puede quedarse puesta y silenciar el siguiente arranque
            // manual, que entonces no abriria las pestanas.
            bool quiet = ConsumeQuietStart();

            if (!_restarting)
            {
                _sessionEnding = false;
                CancelPendingRestart();
            }

            List<SiteDefinition> activos = new List<SiteDefinition>();
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (site.Enabled) activos.Add(site);
            }

            if (activos.Count == 0)
            {
                // Desmarcar el ultimo sitio con el servidor en marcha es pedir
                // que pare: no merece un aviso.
                if (_restarting) _log.Write("INFO", "No queda ningun sitio marcado: el servidor se queda detenido.");
                else ReportStartProblem(quiet, "Marca al menos un sitio para servir.", "Sin sitios activos");
                return;
            }

            // Desatendido no se puede preguntar: lo que no puede arrancar se
            // omite con su motivo en el registro y el resto se sirve igual.
            List<string> omitidos = new List<string>();

            bool necesitaAdmin = false;
            foreach (SiteDefinition site in activos)
            {
                if (site.LanMode) necesitaAdmin = true;
            }
            if (necesitaAdmin && !_isAdmin)
            {
                if (!quiet)
                {
                    Warn("Hay sitios en modo Red local y eso exige ejecutar la aplicacion como Administrador.",
                         "Permisos Insuficientes");
                    return;
                }

                List<SiteDefinition> locales = new List<SiteDefinition>();
                foreach (SiteDefinition site in activos)
                {
                    if (!site.LanMode) locales.Add(site);
                    else omitidos.Add("Puerto " + site.Port + ": el modo Red local necesita administrador.");
                }
                activos = locales;
            }

            List<SiteRuntime> runtimes = new List<SiteRuntime>();
            foreach (SiteDefinition site in activos)
            {
                SiteRuntime runtime = ResolveSite(site, quiet, omitidos);
                if (runtime != null) runtimes.Add(runtime);
                else if (!quiet) return;   // ResolveSite ya explico el motivo
            }

            foreach (string motivo in omitidos) _log.Write("WARN", "Sitio omitido. " + motivo);

            if (runtimes.Count == 0)
            {
                ReportStartProblem(quiet, "Ningun sitio pudo arrancar. El motivo de cada uno esta en el registro.",
                                   "Servidor sin iniciar");
                return;
            }

            // HTTPS se prepara antes de abrir los puertos. Un sitio que no lo
            // consiga se sirve igual, solo por HTTP, y se explica el motivo.
            List<string> httpsProblems = new List<string>();
            List<string> httpsWarnings = new List<string>();
            runtimes = PrepareHttps(runtimes, httpsProblems, httpsWarnings);

            ServerOptions options;
            try
            {
                options = BuildServerOptions(runtimes);
            }
            catch (Exception ex)
            {
                // Un settings.json editado a mano puede traer puertos repetidos.
                ReportStartProblem(quiet, ex.Message, "Configuracion invalida");
                return;
            }

            Exception failure;
            HttpFileServer server = TryStartServer(options, out failure);

            // Un HTTPS que otro programa ya tiene reservado en http.sys no impide
            // servir el sitio por HTTP, ni con la ventana delante ni desatendido:
            // se le quita el HTTPS, se explica y se intenta de nuevo.
            if (server == null)
            {
                List<SiteRuntime> soloHttp = DropBlockedHttps(runtimes, httpsProblems);
                if (soloHttp != null)
                {
                    runtimes = soloHttp;
                    server = TryStartServer(BuildServerOptions(runtimes), out failure);
                }
            }

            // HttpListener arranca todos sus prefijos o ninguno. Desatendido, un
            // puerto que ocupa otro programa (otro servidor de desarrollo en el
            // 3000, por ejemplo) no debe dejar sin servir a los demas sitios:
            // se apartan los que no se pueden reservar y se intenta de nuevo.
            if (server == null && quiet)
            {
                List<SiteRuntime> libres = new List<SiteRuntime>();
                bool cambiado = false;
                foreach (SiteRuntime runtime in runtimes)
                {
                    if (!PortAvailability.CanReserveHttp(runtime.Port, runtime.LanMode))
                    {
                        string motivo = "Puerto " + runtime.Port + ": ocupado por otro programa o sin permiso para reservarlo.";
                        omitidos.Add(motivo);
                        _log.Write("WARN", "Sitio omitido. " + motivo);
                        cambiado = true;
                        continue;
                    }

                    libres.Add(runtime);
                }

                if (libres.Count > 0 && cambiado)
                {
                    runtimes = libres;
                    server = TryStartServer(BuildServerOptions(runtimes), out failure);
                }
            }

            if (server == null)
            {
                // HttpListener falla en bloque y no dice cual de los prefijos
                // fue: se averigua probando cada sitio por separado, con el
                // mismo mecanismo, y se nombra al proceso que ocupa el puerto.
                ReportStartProblem(quiet, "No se pudo iniciar el servidor.\r\n\r\n" + DiagnoseStartFailure(runtimes) +
                                          "\r\n\r\nDetalle tecnico: " + (failure != null ? failure.Message : ""),
                                   "Error al iniciar");
                return;
            }

            _server = server;

            // Tras un reinicio por cambios de sitios no se repiten los avisos
            // de las funciones que ya estaban activas.
            if (!_restarting)
            {
                _uploadAnnounced = false;
                _consoleAnnounced = false;
            }
            CompressionCache.ResetStatistics();
            ApplyDevFeatures();

            _copyLanButton.Enabled = false;
            _qrButton.Enabled = false;

            CaptureSettings();
            _settings.Save();

            // Un reinicio por cambios en los sitios conserva los dispositivos ya
            // identificados: siguen siendo los mismos.
            if (!_restarting)
            {
                ClientRegistry.Clear();
                _rows.Clear();
                _clientsList.Items.Clear();
                _lastDetailIp = "";
                _lastDetailText = "";
                _detailTextBox.Text = "Selecciona un cliente para ver su detalle completo.";
            }

            // La categoria de la red se mira antes de pintar el estado, que la
            // avisa; solo importa si algun sitio se sirve a la red local.
            if (ServesLan(runtimes)) RefreshPublicNetworks();
            else _publicNetworks = new List<PublicNetwork>();

            BuildStatusText(runtimes);
            RefreshLogFilter(runtimes);
            ApplyFirewallRules(runtimes);

            SetInputsEnabled(false);
            _startButton.Text = "Detener Servidor";
            _startButton.BackColor = Color.FromArgb(220, 53, 69);

            foreach (SiteRuntime runtime in runtimes)
            {
                string https = runtime.HttpsPrefix != null ? " y " + runtime.HttpsPrefix : "";
                string isolated = runtime.CrossOriginIsolation ? ", aislado de otros origenes (COOP/COEP)" : "";
                _log.Write("INFO", "Sitio en " + runtime.Prefix + https + isolated +
                                   " [Raiz real: " + runtime.RealRootPath + "]");
            }

            NotifyServerState(true, runtimes.Count);
            ReportHttpsProblems(quiet, httpsProblems, httpsWarnings);
            OfferPrivateNetworks(!quiet || (_restarting && Visible));

            // Arranque automatico u oculto: un aviso en la bandeja en vez de
            // abrir una pestana por proyecto en cada inicio de sesion.
            if (quiet && _restarting)
            {
                FinishRestart(runtimes, omitidos);
                return;
            }

            if (quiet)
            {
                if (_tray != null)
                {
                    string aviso = "Sirviendo " + runtimes.Count + (runtimes.Count == 1 ? " sitio." : " sitios.");
                    if (omitidos.Count > 0)
                    {
                        aviso += " " + omitidos.Count + (omitidos.Count == 1 ? " omitido" : " omitidos") +
                                 ": abre LanShare para ver el motivo.";
                    }
                    _tray.Notify(AppInfo.Name, aviso, omitidos.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
                }
                return;
            }

            // Se abren todos los sitios marcados, no solo el primero: si el
            // usuario activo cuatro proyectos, espera ver los cuatro.
            foreach (SiteRuntime runtime in runtimes)
            {
                try { System.Diagnostics.Process.Start(runtime.LocalUrl); }
                catch (Exception ex)
                {
                    _log.Write("WARN", "No se pudo abrir " + runtime.LocalUrl + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Las opciones salen completas antes de arrancar: si la recarga, la
        /// consola o la subida se activasen despues, una pestana que reconecta
        /// justo en ese instante recibiria "apagada" y dejaria de intentarlo.
        /// </summary>
        private ServerOptions BuildServerOptions(List<SiteRuntime> runtimes)
        {
            ServerOptions options = new ServerOptions(runtimes, _settings.Cors);
            options.EnableDirectoryListing = _settings.DirectoryListing;
            options.EnableCompression = _settings.Compression;
            options.DistributionMode = _settings.DistributionMode;
            options.EnableLiveReload = _settings.LiveReload;
            options.EnableRemoteConsole = _settings.RemoteConsole;
            options.UploadFolder = _settings.GetUploadFolder();
            options.UploadMaxBytes = (long)_settings.UploadMaxMegabytes * 1024 * 1024;
            options.EnableUpload = _settings.UploadEnabled;
            ApplyHttpsToOptions(options);
            return options;
        }

        /// <summary>Crea y arranca el servidor. null, con el motivo, si no pudo.</summary>
        private HttpFileServer TryStartServer(ServerOptions options, out Exception failure)
        {
            failure = null;
            HttpFileServer server = new HttpFileServer(options, _log);
            server.InjectProbe = _settings.InjectProbe;
            server.Faulted += OnServerFaulted;

            try
            {
                server.Start();
                return server;
            }
            catch (Exception ex)
            {
                failure = ex;
                server.Faulted -= OnServerFaulted;
                server.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Un problema al arrancar: con la ventana delante, un dialogo; en un
        /// arranque desatendido, el registro y un aviso en la bandeja. Un
        /// dialogo modal al iniciar sesion bloquearia sin que nadie lo vea.
        /// </summary>
        private void ReportStartProblem(bool quiet, string message, string title)
        {
            // Un reinicio por cambios de sitios es silencioso para no abrir
            // pestanas, pero quien acaba de editar esta delante: si falla, se le
            // dice con un dialogo.
            if (!quiet || (_restarting && Visible))
            {
                Warn(message, title);
                return;
            }

            _log.Write("ERROR", title + ". " + message.Replace("\r\n\r\n", " ").Replace("\r\n", " "));
            if (_tray != null)
            {
                _tray.Notify(AppInfo.Name, title + ". Abre LanShare para ver el registro.", ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// Explica que sitio impidio arrancar y por que. Se prueba cada prefijo
        /// en solitario porque HttpListener, con varios, falla sin decir cual.
        /// </summary>
        private static string DiagnoseStartFailure(IList<SiteRuntime> runtimes)
        {
            Dictionary<int, int> owners = PortAvailability.GetListenerOwners();
            StringBuilder sb = new StringBuilder();

            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.HttpsPort > 0 && !PortAvailability.CanReserveHttp(runtime.HttpsPort, runtime.LanMode, true))
                {
                    string sitio = runtime.Label.Length > 0 ? runtime.Label : CarpetaCorta(runtime.RootPath);
                    sb.Append("Puerto HTTPS ").Append(runtime.HttpsPort.ToString(CultureInfo.InvariantCulture))
                      .Append(" (").Append(sitio).Append("): otro programa ya lo reservo en http.sys.\r\n");
                }

                if (PortAvailability.CanReserveHttp(runtime.Port, runtime.LanMode)) continue;

                string nombre = runtime.Label.Length > 0 ? runtime.Label : CarpetaCorta(runtime.RootPath);
                sb.Append("Puerto ").Append(runtime.Port.ToString(CultureInfo.InvariantCulture))
                  .Append(" (").Append(nombre).Append("): ");

                int pid;
                if (owners.TryGetValue(runtime.Port, out pid))
                {
                    sb.Append("lo ocupa ").Append(PortAvailability.DescribeOwner(pid)).Append('.');
                }
                else
                {
                    string exclusion = PortAvailability.DescribeExclusion(runtime.Port);
                    sb.Append(exclusion.Length > 0
                        ? "Windows lo tiene en un " + exclusion + "."
                        : runtime.LanMode
                            ? "no se puede reservar en red local (hace falta administrador)."
                            : "http.sys no permite reservarlo.");
                }
                sb.Append("\r\n");
            }

            if (sb.Length == 0)
            {
                return "Ningun puerto por separado da problemas: puede que otro programa los " +
                       "tomara y soltara justo en ese momento. Prueba a iniciar de nuevo.";
            }

            sb.Append("\r\nCambia el puerto del sitio (Editar, boton Auto) o cierra ese programa.");
            return sb.ToString();
        }

        /// <summary>Valida y canoniza un sitio. Devuelve null si no se puede servir.</summary>
        /// <param name="quiet">
        /// Arranque desatendido: nada de dialogos. El motivo se anota en
        /// <paramref name="omitted"/> y el sitio simplemente no arranca.
        /// </param>
        private SiteRuntime ResolveSite(SiteDefinition site, bool quiet, List<string> omitted)
        {
            if (string.IsNullOrEmpty(site.RootPath) || !Directory.Exists(site.RootPath))
            {
                // Al iniciar sesion es tipico: una unidad externa aun sin montar.
                if (quiet) omitted.Add("Puerto " + site.Port + ": la carpeta no existe (" + site.RootPath + ").");
                else Warn("La carpeta del sitio en el puerto " + site.Port + " no existe:\r\n\r\n" +
                          site.RootPath, "Carpeta no encontrada");
                return null;
            }

            string basePath = Path.GetFullPath(site.RootPath);

            // Servir la raiz de una unidad entera es casi siempre un accidente,
            // y sin nadie delante para confirmarlo no se sirve.
            if (string.Equals(Path.GetPathRoot(basePath), basePath, StringComparison.OrdinalIgnoreCase))
            {
                bool alreadyConfirmed = _restarting &&
                                        (_confirmedRoots.Contains(basePath) ||
                                         _confirmedRoots.Contains(basePath + Path.DirectorySeparatorChar));
                if (quiet && !alreadyConfirmed)
                {
                    omitted.Add("Puerto " + site.Port + ": apunta a la raiz completa de " + basePath +
                                " y eso solo se sirve confirmandolo al iniciar a mano.");
                    return null;
                }

                if (!alreadyConfirmed)
                {
                    DialogResult confirm = MessageBox.Show(this,
                        "El sitio del puerto " + site.Port + " apunta a la RAIZ COMPLETA de la unidad (" +
                        basePath + ").\r\n\r\nTodo el contenido de ese disco quedara accesible. Continuar?",
                        "Advertencia de exposicion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (confirm != DialogResult.Yes) return null;
                }
            }

            string realRoot = PathGuard.CanonicalizeRoot(basePath);
            if (string.IsNullOrEmpty(realRoot))
            {
                if (quiet) omitted.Add("Puerto " + site.Port + ": no se pudo canonizar la carpeta.");
                else Warn("No se pudo canonizar la carpeta del puerto " + site.Port + ". Sin esa " +
                          "resolucion el control anti-symlink no puede aplicarse y el sitio no arrancara.",
                          "Error de resolucion de ruta");
                return null;
            }

            // Un settings.json editado a mano puede traer un puerto HTTPS
            // imposible o igual al HTTP: el sitio se sirve igual, sin HTTPS.
            int httpsPort = site.HttpsPort;
            if (httpsPort < 1 || httpsPort > 65535 || httpsPort == site.Port) httpsPort = 0;

            return new SiteRuntime(site.Port, site.LanMode, site.Label, basePath, realRoot, site.StartFile, httpsPort,
                                   site.CrossOriginIsolation);
        }

        private void BuildStatusText(IList<SiteRuntime> runtimes)
        {
            List<LanAddress> direcciones = LanAddresses.GetActiveDetailed();
            _lanUrls = new List<UrlChoice>();

            StringBuilder estado = new StringBuilder();
            estado.Append("Corriendo: ").Append(runtimes.Count)
                  .AppendLine(runtimes.Count == 1 ? " sitio" : " sitios");

            foreach (SiteRuntime runtime in runtimes)
            {
                string nombre = runtime.Label.Length > 0 ? runtime.Label : CarpetaCorta(runtime.RootPath);

                // Una linea por sitio: con cuatro proyectos, el formato anterior
                // obligaba a desplazarse para ver cualquier cosa.
                estado.Append(":").Append(runtime.Port.ToString(CultureInfo.InvariantCulture).PadRight(6))
                      .Append(Recortar(nombre, 22).PadRight(24))
                      .Append("localhost:").Append(runtime.Port);

                if (runtime.LanMode)
                {
                    // Ya vienen ordenadas: la primera es la que suele alcanzar el
                    // movil, y es la que el QR muestra de entrada. Con HTTPS, su
                    // direccion va delante: es la que se activo para usarla.
                    foreach (LanAddress direccion in direcciones)
                    {
                        string url = "http://" + direccion.Address + ":" + runtime.Port + "/";
                        estado.Append("   ").Append(direccion.Address).Append(":").Append(runtime.Port);
                        if (runtime.HttpsPort > 0)
                        {
                            string segura = "https://" + direccion.Address + ":" + runtime.HttpsPort + "/";
                            _lanUrls.Add(UrlChoice.ForSite(nombre, segura, direccion.AdapterName));
                        }
                        _lanUrls.Add(UrlChoice.ForSite(nombre, url, direccion.AdapterName));
                    }
                    if (direcciones.Count == 0) estado.Append("   (sin adaptadores de red)");
                }

                if (runtime.HttpsPort > 0) estado.Append("   https:").Append(runtime.HttpsPort);
                if (runtime.CrossOriginIsolation) estado.Append("   aislado");

                estado.AppendLine();
            }

            string networkWarning = PublicNetworkStatusLine();
            if (networkWarning.Length > 0) estado.AppendLine().Append(networkWarning);

            _statusTextBox.Text = estado.ToString();
            _copyLanButton.Enabled = _lanUrls.Count > 0;
            _qrButton.Enabled = _lanUrls.Count > 0;
        }

        private static string CarpetaCorta(string rootPath)
        {
            try
            {
                string trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar);
                string name = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? trimmed : name;
            }
            catch (Exception) { return rootPath; }
        }

        private static string Recortar(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max - 1) + "~";
        }

        /// <summary>
        /// Una regla de firewall por cada sitio en red local y la escucha mDNS
        /// si hay alguno. Tras un reinicio por cambios de sitios, las reglas que
        /// siguen valiendo se conservan y solo se crea o quita lo que cambio:
        /// cada regla cuesta un netsh.
        /// </summary>
        private void ApplyFirewallRules(IList<SiteRuntime> runtimes)
        {
            HashSet<int> lanPorts = new HashSet<int>();
            foreach (SiteRuntime runtime in runtimes)
            {
                if (!runtime.LanMode) continue;

                // El puerto HTTPS de un sitio necesita su propia regla.
                foreach (int port in new int[] { runtime.Port, runtime.HttpsPort })
                {
                    if (port <= 0) continue;
                    lanPorts.Add(port);
                    if (_firewallPorts.Contains(port)) continue;

                    string error;
                    if (FirewallManager.TryAdd(port, out error)) _firewallPorts.Add(port);
                    else _log.Write("WARN", "No se pudo crear la regla de firewall para el puerto " +
                                            port + ": " + error);
                }
            }

            for (int i = _firewallPorts.Count - 1; i >= 0; i--)
            {
                if (lanPorts.Contains(_firewallPorts[i])) continue;
                FirewallManager.TryRemove(_firewallPorts[i]);
                _firewallPorts.RemoveAt(i);
            }

            RemoveLeftoverFirewallRules(lanPorts);

            // Se arranca por haber sitios en red local, no por que la regla de
            // firewall se creara: si la regla falla igual queremos los nombres.
            if (lanPorts.Count > 0)
            {
                bool wasActive = MdnsListener.IsActive;
                MdnsListener.Start();
                if (!wasActive) _log.Write("INFO", "Escucha mDNS pasiva iniciada en 224.0.0.251:5353.");
            }
            else
            {
                MdnsListener.Stop();
            }
        }

        /// <summary>
        /// Reglas que dejo una sesion cerrada por Windows con el servidor en
        /// marcha y que ya no corresponden a ningun sitio en red local.
        /// </summary>
        private void RemoveLeftoverFirewallRules(ICollection<int> keep)
        {
            // Sin administrador netsh no puede borrarlas: si se olvidaran aqui,
            // se quedarian en el firewall sin que nadie volviera a intentarlo.
            if (!_isAdmin) return;
            if (_settings.FirewallPorts == null || _settings.FirewallPorts.Count == 0) return;

            foreach (int port in _settings.FirewallPorts)
            {
                if (keep != null && keep.Contains(port)) continue;
                FirewallManager.TryRemove(port);
            }
            _settings.FirewallPorts.Clear();
            _settings.Save();
        }

        /// <summary>
        /// Detiene el servidor. Se puede pedir a la vez desde el boton, la
        /// bandeja y la X de la ventana: la primera peticion detiene y las
        /// demas esperan a esa misma parada en lugar de repetirla. Antes, la
        /// segunda terminaba primero, anulaba el servidor y la primera fallaba
        /// al reanudarse con una excepcion no controlada.
        /// </summary>
        private System.Threading.Tasks.Task StopServerAsync()
        {
            if (_stopping != null) return _stopping;

            System.Threading.Tasks.Task stop = StopServerCoreAsync();

            // Si termino sin esperar nada, no queda parada en curso que compartir.
            _stopping = stop.IsCompleted ? null : stop;
            return stop;
        }

        private async System.Threading.Tasks.Task StopServerCoreAsync()
        {
            try
            {
                _startButton.Enabled = false;
                try
                {
                    HttpFileServer server = _server;
                    if (server != null)
                    {
                        await server.StopAsync();
                        server.Dispose();
                        if (ReferenceEquals(_server, server)) _server = null;
                    }

                    // En un reinicio por cambios de sitios, las reglas de firewall
                    // y la escucha mDNS se conservan: el arranque ajusta solo lo
                    // que cambio. Rehacerlas costaba dos netsh por puerto. Si el
                    // arranque falla, el reinicio las cierra igualmente.
                    if (!_restarting) ReleaseNetworkExposure();
                }
                finally
                {
                    _startButton.Enabled = true;
                }

                ShowServerStopped();
            }
            finally
            {
                _stopping = null;
            }
        }

        /// <summary>Cierra lo que expone el equipo a la red: reglas de firewall y escucha mDNS.</summary>
        private void ReleaseNetworkExposure()
        {
            MdnsListener.Stop();
            foreach (int port in _firewallPorts) FirewallManager.TryRemove(port);
            _firewallPorts.Clear();
        }

        /// <summary>La interfaz con el servidor parado, sea cual sea la forma en que se paro.</summary>
        private void ShowServerStopped()
        {
            SetInputsEnabled(true);
            _copyLanButton.Enabled = false;
            _qrButton.Enabled = false;
            _lanUrls.Clear();

            _statusTextBox.Text = "Estado: Detenido";
            _startButton.Text = "Iniciar Servidor";
            _startButton.BackColor = Color.FromArgb(40, 167, 69);

            _log.Write("INFO", "Servidor detenido.");
            NotifyServerState(false, 0);
        }

        /// <summary>
        /// Cierre de sesion o apagado de Windows: no hay tiempo para la parada
        /// asincrona y cancelar el cierre haria que Windows mostrase "LanShare
        /// impide cerrar la sesion". Se para en el acto, con un tope de dos
        /// segundos; StopAsync no necesita el hilo de la interfaz para acabar.
        /// </summary>
        private void StopForSessionEnd()
        {
            // Nada de reinicios pendientes: entre la consulta de Windows y el
            // final de la sesion el bucle de mensajes sigue vivo.
            _sessionEnding = true;
            _restartTimer.Stop();

            HttpFileServer server = _server;
            _server = null;
            if (server != null)
            {
                try { server.StopAsync().Wait(2000); }
                catch (Exception) { }
                try { server.Dispose(); }
                catch (Exception) { }
            }

            // Las reglas de firewall se quedan: borrarlas lanza un netsh por
            // puerto y Windows esta esperando para cerrar la sesion. No exponen
            // nada mientras no escuche nadie. Se apuntan para borrar en la
            // siguiente sesion las que entonces ya no hagan falta.
            MdnsListener.Stop();
            try
            {
                // Se suman a las que ya estuvieran pendientes: un cierre de sesion
                // anterior que alguien cancelo pudo dejar otras sin borrar.
                List<int> pending = _settings.FirewallPorts ?? new List<int>();
                foreach (int port in _firewallPorts)
                {
                    if (!pending.Contains(port)) pending.Add(port);
                }
                _settings.FirewallPorts = pending;
                _settings.Save();
            }
            catch (Exception) { }
            _firewallPorts.Clear();

            // Si otra aplicacion cancela el cierre de sesion, la ventana sigue
            // abierta y debe reflejar que el servidor ya no esta en marcha.
            if (server != null) ShowServerStopped();
        }

        /// <summary>
        /// Llega desde un hilo del pool: hay que marshalear a la interfaz antes
        /// de tocar controles.
        /// </summary>
        private void OnServerFaulted(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed) return;
                    _statusTextBox.Text = "Estado: DETENIDO POR ERROR\r\n\r\n" +
                                          "El bucle de aceptacion cayo. Revisa la pestana de " +
                                          "telemetria y reinicia el servidor.";
                    _startButton.BackColor = Color.FromArgb(255, 193, 7);
                    _startButton.Text = "Detener Servidor (caido)";
                    if (_tray != null && !Visible)
                    {
                        _tray.Notify(AppInfo.Name, "El servidor se detuvo por un error. Abre la ventana " +
                                     "para ver el registro.", ToolTipIcon.Error);
                    }
                });
            }
            catch (Exception) { }
        }

        private void SetInputsEnabled(bool enabled)
        {
            // Cambiar Enabled puede hacer que el ListView recree su handle y
            // reemita ItemChecked por cada fila. Se invalida ademas la marca de
            // interaccion: si el usuario marco una casilla y pulso Iniciar en
            // menos de un segundo, un evento tardio pasaria el filtro temporal.
            _lastSiteInput = DateTime.MinValue;
            _suspendSiteEvents = true;
            try { SetInputsEnabledCore(enabled); }
            finally { _suspendSiteEvents = false; }

            // Tras la transicion, la tabla se repinta desde el modelo, que es
            // la unica fuente de verdad del estado de cada sitio.
            RefreshSiteList();
        }

        private void SetInputsEnabledCore(bool enabled)
        {
            // Con el servidor en marcha se puede seguir editando: cada cambio se
            // aplica reiniciandolo (MainForm.Restart). Solo cambia la ayuda.
            _addSiteButton.Enabled = true;
            _editSiteButton.Enabled = true;
            _removeSiteButton.Enabled = true;
            _sitesHint.Text = enabled ? SitesHintIdle : SitesHintRunning;
        }

        // ------------------------------------------------------------------
        // Acciones de la interfaz
        // ------------------------------------------------------------------

        private void OnSettingsClick(object sender, EventArgs e)
        {
            bool running = _server != null;
            bool autoStart = AutoStart.Current() != AutoStartMode.None;

            using (SettingsDialog dialog = new SettingsDialog(_settings, autoStart, _isAdmin, running))
            {
                DialogResult result = dialog.ShowDialog(this);

                // Borrar los certificados ocurre al momento, se acepte o no.
                if (dialog.HttpsRemoved)
                {
                    _settings.HttpsPrepared = false;
                    _settings.Save();
                    _log.Write("INFO", "HTTPS: borrados de Windows los certificados de LanShare.");
                }

                if (result != DialogResult.OK) return;

                dialog.ApplyTo(_settings);
                _settings.Save();
                ApplyRuntimeSettings();

                if (dialog.AutoStartChanged) ApplyAutoStart(dialog.AutoStartRequested);
            }
        }

        /// <summary>
        /// Lleva la configuracion a las piezas que ya estan funcionando. Todo se
        /// aplica sin reiniciar: los hilos de peticion leen estas opciones en
        /// cada llamada, no solo al arrancar.
        /// </summary>
        private void ApplyRuntimeSettings()
        {
            ClientRegistry.EnablePortScan = _settings.PortScan;
            ClientRegistry.EnableDeepName = _settings.DeepName;

            if (_server == null) return;

            _server.Options.EnableDirectoryListing = _settings.DirectoryListing;
            _server.Options.EnableCompression = _settings.Compression;
            _server.Options.DistributionMode = _settings.DistributionMode;
            _server.Options.CorsEnabled = _settings.Cors;
            _server.InjectProbe = _settings.InjectProbe;
            ApplyDevFeatures();

            _log.Write("INFO", "Configuracion aplicada sin reiniciar el servidor.");
        }

        private void ApplyAutoStart(bool enable)
        {
            string message;
            bool ok = enable
                ? AutoStart.Enable(Application.ExecutablePath, _isAdmin, out message)
                : AutoStart.Disable(out message);

            _log.Write(ok ? "INFO" : "WARN", enable
                ? (ok ? "Inicio con Windows activado." : "No se pudo activar el inicio con Windows.")
                : (ok ? "Inicio con Windows desactivado." : "No se pudo desactivar el inicio con Windows."));

            if (message.Length > 0)
            {
                MessageBox.Show(this, message, "Iniciar con Windows", MessageBoxButtons.OK,
                                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        private void OnElevateClick(object sender, EventArgs e)
        {
            // Si el servidor corria, la nueva instancia lo vuelve a levantar:
            // escalar suele hacerse precisamente para poder servir en red local.
            string arguments = StartupOptions.Build(_server != null, false, true);

            string error;
            if (Elevation.Relaunch(arguments, out error)) Close();
            else if (!string.IsNullOrEmpty(error)) Warn("No se pudo elevar: " + error, "Error");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SingleInstance.AllowShowMessage(Handle);

            // Se repite si la ventana se recrea: el permiso va por ventana.
            if (_dropViaMessages) FileDropTarget.Accept(Handle);
        }

        /// <summary>
        /// Mensajes propios de la ventana: una segunda instancia que pide que se
        /// muestre, archivos soltados cuando corre como administrador y la
        /// franja inferior que hace de borde de la ventana.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (SingleInstance.ShowMessage != 0 && m.Msg == SingleInstance.ShowMessage)
            {
                BringToFrontFromAnywhere();
                return;
            }

            // Archivos soltados sobre la ventana cuando corre como administrador.
            if (m.Msg == FileDropTarget.WM_DROPFILES)
            {
                string[] paths = FileDropTarget.ReadDrop(m.WParam);
                if (paths.Length > 0) OnFilesDropped(paths);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);

            // La franja bajo el registro hace de borde inferior (MainForm.Panes).
            if (m.Msg == WM_NCHITTEST) AdjustBottomHitTest(ref m);
        }

        /// <summary>Restaura la ventana aunque este minimizada u oculta en la bandeja.</summary>
        private void BringToFrontFromAnywhere()
        {
            if (!Visible) Show();

            // Como estaba antes de minimizarla: forzar Normal desmaximizaba la
            // ventana cada vez que se volvia desde la bandeja.
            if (WindowState == FormWindowState.Minimized) WindowState = _restoreState;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
        }

        private void OnCopyLanClick(object sender, EventArgs e)
        {
            if (_lanUrls.Count == 0) return;

            // Al portapapeles va la URL desnuda, no la etiqueta del desplegable.
            List<string> soloUrls = new List<string>();
            foreach (UrlChoice choice in _lanUrls) soloUrls.Add(choice.Url);
            string text = string.Join("\r\n", soloUrls.ToArray());
            try
            {
                Clipboard.SetText(text);
                MessageBox.Show("URL(s) LAN copiada(s) al portapapeles:\r\n\r\n" + text,
                                "Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Warn("No se pudo acceder al portapapeles: " + ex.Message, "Error");
            }
        }

        private void OnQrClick(object sender, EventArgs e)
        {
            if (_lanUrls.Count == 0) return;
            ShowQr(_lanUrls);
        }

        private void OnReprobeClick(object sender, EventArgs e)
        {
            ClientRegistry.ResetResolution();
            _log.Write("INFO", "Re-sondeando rDNS, ARP, TTL, mDNS y NetBIOS de todos los clientes.");
        }

        private void OnClearClientsClick(object sender, EventArgs e)
        {
            ClientRegistry.Clear();
            _clientsList.Items.Clear();
            _rows.Clear();
            _lastDetailIp = "";
            _lastDetailText = "";
            _detailTextBox.Text = "";
        }

        private void OnClientDoubleClick(object sender, EventArgs e)
        {
            AssignAlias();
        }

        private void OnAliasClick(object sender, EventArgs e)
        {
            AssignAlias();
        }

        private void AssignAlias()
        {
            if (_clientsList.SelectedItems.Count == 0)
            {
                MessageBox.Show("Selecciona primero un cliente de la lista.", "Poner alias",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string ip = (string)_clientsList.SelectedItems[0].Tag;
            ClientInfo client = ClientRegistry.Find(ip);
            if (client == null) return;

            string prompt = string.Format(
                "Nombre para este dispositivo ({0}).\r\nSe recordara asociado a {1}. Dejalo vacio para quitarlo.",
                ip, client.AliasKey);

            string alias = PromptDialog.Ask(this, "Poner alias", prompt, client.Alias);
            if (alias == null) return;   // cancelado

            ClientRegistry.SetAlias(client.AliasKey, alias);
            _settings.Aliases = ClientRegistry.ExportAliases();
            _settings.Save();

            _lastDetailText = "";   // forzar repintado del detalle
            RefreshClientList();
        }

        /// <summary>Cero significa todos los sitios.</summary>
        private bool Matches(LogEntry entry)
        {
            // Los mensajes sin puerto (arranque, avisos) se ven con cualquier
            // filtro: afectan al servidor entero, no a un sitio.
            return _logFilterPort == 0 || entry.Port == 0 || entry.Port == _logFilterPort;
        }

        /// <summary>Rellena el desplegable con los puertos que se estan sirviendo.</summary>
        private void RefreshLogFilter(IList<SiteRuntime> runtimes)
        {
            _logFilter.BeginUpdate();
            try
            {
                _logFilter.Items.Clear();
                _logFilter.Items.Add(new PortChoice(0, "Todos"));

                if (runtimes != null)
                {
                    foreach (SiteRuntime runtime in runtimes)
                    {
                        string nombre = runtime.Label.Length > 0
                            ? runtime.Label
                            : CarpetaCorta(runtime.RootPath);
                        _logFilter.Items.Add(new PortChoice(
                            runtime.Port,
                            ":" + runtime.Port.ToString(CultureInfo.InvariantCulture) + "  " + nombre));
                    }
                }

                // Se conserva el sitio que se estaba mirando si sigue en marcha:
                // un reinicio por cambios no debe devolver el filtro a "Todos".
                int index = 0;
                for (int i = 1; i < _logFilter.Items.Count; i++)
                {
                    PortChoice choice = _logFilter.Items[i] as PortChoice;
                    if (choice != null && choice.Port == _logFilterPort) index = i;
                }
                _logFilter.SelectedIndex = index;
            }
            finally { _logFilter.EndUpdate(); }
        }

        private void OnLogFilterChanged(object sender, EventArgs e)
        {
            PortChoice choice = _logFilter.SelectedItem as PortChoice;
            int port = choice != null ? choice.Port : 0;
            if (port == _logFilterPort) return;

            _logFilterPort = port;
            RenderLog();
        }

        /// <summary>Repinta el cuadro entero desde el anillo, aplicando el filtro.</summary>
        private void RenderLog()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LogEntry entry in _logEntries)
            {
                if (Matches(entry)) sb.Append(entry.Text);
            }

            string texto = sb.ToString();
            if (texto.Length > MaxLogChars)
            {
                texto = "[... registro truncado ...]\r\n" +
                        texto.Substring(texto.Length - (int)(MaxLogChars * 0.7));
            }

            IntPtr handle = _logTextBox.Handle;
            NativeUi.SetRedraw(handle, false);
            try
            {
                _logTextBox.Text = texto;
                _logTextBox.SelectionStart = _logTextBox.TextLength;
                _logTextBox.ScrollToCaret();
            }
            finally
            {
                NativeUi.SetRedraw(handle, true);
                _logTextBox.Invalidate();
            }
        }

        private void OnSaveLogClick(object sender, EventArgs e)
        {
            if (_logEntries.Count == 0)
            {
                Warn("No hay nada que guardar todavia.", "Registro vacio");
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Archivo de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*";
                dialog.FileName = "lanshare-registro-" +
                                  DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) +
                                  (_logFilterPort > 0
                                      ? "-puerto" + _logFilterPort.ToString(CultureInfo.InvariantCulture)
                                      : "") + ".txt";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // Se guarda desde el anillo, no desde el cuadro de texto:
                    // asi no se pierde lo que el recorte de pantalla descarto.
                    StringBuilder sb = new StringBuilder();
                    int contadas = 0;
                    foreach (LogEntry entry in _logEntries)
                    {
                        if (!Matches(entry)) continue;
                        sb.Append(entry.Text);
                        contadas++;
                    }

                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                    _log.Write("INFO", "Registro guardado: " + contadas + " entradas en " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    Warn("No se pudo guardar el registro.\r\n\r\n" + ex.Message, "Error al guardar");
                }
            }
        }

        private void OnClearLogClick(object sender, EventArgs e)
        {
            _logEntries.Clear();
            _logTextBox.Clear();
        }

        private void OnExportClick(object sender, EventArgs e)
        {
            ClientInfo[] clients = ClientRegistry.Snapshot();
            if (clients.Length == 0)
            {
                MessageBox.Show("No hay clientes registrados.", "Exportar",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV (*.csv)|*.csv";
                dialog.FileName = "clientes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try { ClientCsvWriter.Write(dialog.FileName, clients); }
                catch (Exception ex) { Warn("No se pudo exportar: " + ex.Message, "Error"); }
            }
        }

        // ------------------------------------------------------------------
        // Refresco
        // ------------------------------------------------------------------

        private void OnLogTick(object sender, EventArgs e)
        {
            List<LogEntry> batch = _log.Drain(MaxLogDrainPerTick);
            if (batch.Count == 0) return;

            StringBuilder visible = new StringBuilder();
            foreach (LogEntry entry in batch)
            {
                _logEntries.Enqueue(entry);
                if (Matches(entry)) visible.Append(entry.Text);
            }
            while (_logEntries.Count > MaxLogEntries) _logEntries.Dequeue();

            // Puede no haber nada que pintar aunque hayan entrado peticiones:
            // basta con que todas fueran de otro sitio.
            if (visible.Length == 0) return;

            IntPtr handle = _logTextBox.Handle;

            // Si el usuario subio a leer una peticion antigua, no se le arrastra
            // de vuelta al final. AppendText siempre hace scroll al fondo, asi
            // que hay que anotar la posicion antes y restaurarla despues.
            bool wasAtBottom = IsLogAtBottom(handle);
            int firstVisible = NativeUi.GetFirstVisibleLine(handle);

            NativeUi.SetRedraw(handle, false);
            try
            {
                _logTextBox.AppendText(visible.ToString());

                if (_logTextBox.TextLength > MaxLogChars)
                {
                    int keep = (int)(MaxLogChars * 0.7);
                    string trimmed = _logTextBox.Text.Substring(_logTextBox.TextLength - keep);
                    int newline = trimmed.IndexOf('\n');
                    if (newline >= 0) trimmed = trimmed.Substring(newline + 1);
                    _logTextBox.Text = "[... registro truncado ...]\r\n" + trimmed;
                    wasAtBottom = true;   // tras recortar no hay posicion previa util
                }

                if (wasAtBottom)
                {
                    _logTextBox.SelectionStart = _logTextBox.TextLength;
                    _logTextBox.ScrollToCaret();
                }
                else
                {
                    NativeUi.ScrollToLine(handle, firstVisible);
                }
            }
            finally
            {
                NativeUi.SetRedraw(handle, true);
                _logTextBox.Invalidate();
            }
        }

        private bool IsLogAtBottom(IntPtr handle)
        {
            int total = NativeUi.GetLineCount(handle);
            if (total <= 0) return true;

            int first = NativeUi.GetFirstVisibleLine(handle);
            int lineHeight = _logTextBox.Font.Height;
            if (lineHeight <= 0) return true;

            int visible = _logTextBox.ClientSize.Height / lineHeight;
            return first + visible >= total - 1;
        }

        private void OnClientsTick(object sender, EventArgs e)
        {
            UpdateStatsLabel();

            if (_tabs.SelectedTab != _clientsTab)
            {
                UpdateTabCaption(ClientRegistry.Count);
                return;
            }

            RefreshClientList();
            UpdateOuiLabel();
        }

        private void RefreshClientList()
        {
            ClientInfo[] clients = ClientRegistry.Snapshot();
            UpdateTabCaption(clients.Length);
            if (clients.Length == 0) return;

            _clientsList.BeginUpdate();
            try
            {
                foreach (ClientInfo client in clients)
                {
                    ListViewItem item;
                    if (!_rows.TryGetValue(client.Ip, out item))
                    {
                        item = new ListViewItem(client.Ip);
                        for (int i = 0; i < 9; i++) item.SubItems.Add("");
                        item.Tag = client.Ip;
                        _clientsList.Items.Add(item);
                        _rows[client.Ip] = item;
                    }

                    string[] values = new string[]
                    {
                        ClientDetailFormatter.ShortName(client.BestName),
                        client.Mac.Length > 0 ? client.Mac : "...",
                        client.MacRandomized ? "MAC aleatoria" : (client.Vendor.Length > 0 ? client.Vendor : "..."),
                        client.DeviceLabel,
                        client.BrowserLabel,
                        client.Verdict.Length > 0 ? client.Verdict : "sondeando...",
                        client.Gpu.Length > 0 ? client.Gpu : "-",
                        client.RequestCount.ToString(CultureInfo.InvariantCulture),
                        ClientDetailFormatter.FormatElapsed(client.LastSeenTicks)
                    };

                    // Escribir una celda invalida su fila aunque el valor sea el
                    // mismo, y eso es la mitad del parpadeo. Solo se toca lo que
                    // cambio de verdad.
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (item.SubItems[i + 1].Text != values[i])
                            item.SubItems[i + 1].Text = values[i];
                    }

                    Color color = client.IdleTime.TotalSeconds > 60 ? Color.Gray : Color.Black;
                    if (item.ForeColor != color) item.ForeColor = color;
                }
            }
            finally
            {
                _clientsList.EndUpdate();
            }

            if (_clientsList.SelectedItems.Count > 0) ShowClientDetail();
        }

        private void OnClientSelectionChanged(object sender, EventArgs e)
        {
            ShowClientDetail();
        }

        private void ShowClientDetail()
        {
            if (_clientsList.SelectedItems.Count == 0) return;

            string ip = (string)_clientsList.SelectedItems[0].Tag;
            ClientInfo client = ClientRegistry.Find(ip);
            if (client == null) return;

            string text = ClientDetailFormatter.Build(client);

            // Si el texto no cambio no se toca el control. Como los tiempos del
            // panel son absolutos y no relativos, un cliente en reposo no genera
            // ni un solo repintado.
            bool sameClient = _lastDetailIp == ip;
            if (sameClient && text == _lastDetailText) return;

            _lastDetailIp = ip;
            _lastDetailText = text;

            IntPtr handle = _detailTextBox.Handle;

            // Al cambiar de cliente el contenido es otro y se empieza arriba. En
            // el mismo cliente se conserva la LINEA SUPERIOR VISIBLE, que es la
            // posicion real de la barra.
            int firstLine = sameClient ? NativeUi.GetFirstVisibleLine(handle) : 0;
            int selectionStart = _detailTextBox.SelectionStart;
            int selectionLength = _detailTextBox.SelectionLength;

            NativeUi.SetRedraw(handle, false);
            try
            {
                _detailTextBox.Text = text;
                if (sameClient && selectionStart <= _detailTextBox.TextLength)
                {
                    _detailTextBox.SelectionStart = selectionStart;
                    _detailTextBox.SelectionLength =
                        Math.Min(selectionLength, _detailTextBox.TextLength - selectionStart);
                }
                NativeUi.ScrollToLine(handle, firstLine);
            }
            finally
            {
                NativeUi.SetRedraw(handle, true);
                _detailTextBox.Invalidate();
            }
        }

        private void UpdateTabCaption(int count)
        {
            string caption = count > 0
                ? "Clientes conectados (" + count + ")"
                : "Clientes conectados";
            if (_clientsTab.Text != caption) _clientsTab.Text = caption;
        }

        private void UpdateOuiLabel()
        {
            string mdns = MdnsListener.IsActive
                ? "mDNS pasivo: " + MdnsListener.Count + " nombres"
                : "mDNS pasivo: inactivo (solo en modo LAN)";

            string caption = "Fabricante: " + OuiTable.Source + ".  |  " + mdns;
            if (_ouiLabel.Text != caption) _ouiLabel.Text = caption;
        }

        // ------------------------------------------------------------------

        private async void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            // Un segundo clic en la X mientras se apaga volvia a entrar aqui.
            if (_closing && (_server != null || _stopping != null)) { e.Cancel = true; return; }

            try { CaptureSettings(); _settings.Save(); } catch (Exception) { }

            // Los temporizadores siguen vivos al cerrar sesion: si otra aplicacion
            // la cancela, LanShare sigue abierto y el registro no puede quedarse
            // congelado el resto de la sesion.
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                StopForSessionEnd();
                return;
            }

            _logTimer.Stop();
            _clientsTimer.Stop();

            if (_server != null || _stopping != null)
            {
                // Se cancela el cierre para poder esperar al apagado ordenado y
                // se vuelve a cerrar despues; si no, el proceso muere antes de
                // liberar el puerto y de borrar la regla de firewall.
                e.Cancel = true;
                _closing = true;
                await StopServerAsync();
                Close();
                return;
            }

            MdnsListener.Stop();
            foreach (int port in _firewallPorts) FirewallManager.TryRemove(port);
            RemoveLeftoverFirewallRules(null);
        }

        private static void Warn(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
