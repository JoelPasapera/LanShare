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
        private List<UrlChoice> _lanUrls = new List<UrlChoice>();
        private readonly List<int> _firewallPorts = new List<int>();
        private bool _suspendSiteEvents;
        private DateTime _lastSiteInput = DateTime.MinValue;

        private readonly Queue<LogEntry> _logEntries = new Queue<LogEntry>(MaxLogEntries);
        private int _logFilterPort;   // 0 = todos los sitios
        private bool _isAdmin;

        private string _lastDetailIp = "";
        private string _lastDetailText = "";

        public MainForm()
        {
            BuildUi();

            _isAdmin = Elevation.IsAdministrator();
            _permissionLabel.Text = _isAdmin
                ? "Permisos: Administrador (Elevado)"
                : "Permisos: Usuario Estandar (Sin Administrador)";
            _permissionLabel.ForeColor = _isAdmin ? Color.DarkGreen : Color.DarkRed;
            _elevateButton.Enabled = !_isAdmin;

            ApplySettings();
            LoadOuiTable();

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

            ClientRegistry.LoadAliases(_settings.Aliases);
            RefreshSiteList();
        }

        /// <summary>Recoge el estado actual de los controles antes de guardar.</summary>
        private void CaptureSettings()
        {
            _settings.SplitterDistance = _split.SplitterDistance;
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
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
        }

        private void OnAddSiteClick(object sender, EventArgs e)
        {
            using (SiteDialog dialog = new SiteDialog(null, _settings.RecentRoots, OtherSitePorts(null)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!PortIsFree(dialog.Result.Port, null)) return;

                _settings.Sites.Add(dialog.Result);
                _settings.RememberRoot(dialog.Result.RootPath);
                _settings.Save();
                RefreshSiteList();
            }
        }

        private void OnEditSiteClick(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null) return;

            using (SiteDialog dialog = new SiteDialog(site.Clone(), _settings.RecentRoots, OtherSitePorts(site)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!PortIsFree(dialog.Result.Port, site)) return;

                int index = _settings.Sites.IndexOf(site);
                if (index < 0) return;

                _settings.Sites[index] = dialog.Result;
                _settings.RememberRoot(dialog.Result.RootPath);
                _settings.Save();
                RefreshSiteList();
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
        }

        /// <summary>Puertos de los demas sitios, para no proponer uno ya tomado.</summary>
        private List<int> OtherSitePorts(SiteDefinition exclude)
        {
            List<int> ports = new List<int>();
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (!ReferenceEquals(site, exclude)) ports.Add(site.Port);
            }
            return ports;
        }

        /// <summary>Dos sitios en el mismo puerto harian fallar el listener entero.</summary>
        private bool PortIsFree(int port, SiteDefinition ignore)
        {
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (!ReferenceEquals(site, ignore) && site.Port == port)
                {
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
            int distancia = _settings.SplitterDistance;
            if (distancia < _split.Panel1MinSize) distancia = 170;
            try { _split.SplitterDistance = distancia; } catch (Exception) { }
            AppIcon.Apply(this);
            NativeUi.EnableListViewDoubleBuffer(_clientsList.Handle);
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

            List<SiteDefinition> activos = new List<SiteDefinition>();
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (site.Enabled) activos.Add(site);
            }

            if (activos.Count == 0)
            {
                Warn("Marca al menos un sitio para servir.", "Sin sitios activos");
                return;
            }

            bool necesitaAdmin = false;
            foreach (SiteDefinition site in activos)
            {
                if (site.LanMode) necesitaAdmin = true;
            }
            if (necesitaAdmin && !_isAdmin)
            {
                Warn("Hay sitios en modo Red local y eso exige ejecutar la aplicacion como Administrador.",
                     "Permisos Insuficientes");
                return;
            }

            List<SiteRuntime> runtimes = new List<SiteRuntime>();
            foreach (SiteDefinition site in activos)
            {
                SiteRuntime runtime = ResolveSite(site);
                if (runtime == null) return;   // ResolveSite ya explico el motivo
                runtimes.Add(runtime);
            }

            ServerOptions options;
            try
            {
                options = new ServerOptions(runtimes, _settings.Cors);
            }
            catch (Exception ex)
            {
                // Un settings.json editado a mano puede traer puertos repetidos.
                Warn(ex.Message, "Configuracion invalida");
                return;
            }

            options.EnableDirectoryListing = _settings.DirectoryListing;
            options.EnableCompression = _settings.Compression;
            options.DistributionMode = _settings.DistributionMode;

            HttpFileServer server = new HttpFileServer(options, _log);
            server.InjectProbe = true;
            server.Faulted += OnServerFaulted;

            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                server.Dispose();
                List<string> puertos = new List<string>();
                foreach (SiteRuntime runtime in runtimes)
                {
                    puertos.Add(runtime.Port.ToString(CultureInfo.InvariantCulture));
                }

                // HttpListener no dice cual de los prefijos fallo, asi que al
                // menos se listan todos los que se intentaron.
                Warn("No se pudo iniciar el servidor.\r\n\r\n" + ex.Message +
                     "\r\n\r\nPuertos solicitados: " + string.Join(", ", puertos.ToArray()) +
                     "\r\nCausa habitual: otro programa ya ocupa alguno de ellos.",
                     "Error al iniciar");
                return;
            }

            _server = server;

            _copyLanButton.Enabled = false;
            _qrButton.Enabled = false;

            CaptureSettings();
            _settings.Save();

            ClientRegistry.Clear();
            _rows.Clear();
            _clientsList.Items.Clear();
            _lastDetailIp = "";
            _lastDetailText = "";
            _detailTextBox.Text = "Selecciona un cliente para ver su detalle completo.";

            BuildStatusText(runtimes);
            RefreshLogFilter(runtimes);
            ApplyFirewallRules(runtimes);

            SetInputsEnabled(false);
            _startButton.Text = "Detener Servidor";
            _startButton.BackColor = Color.FromArgb(220, 53, 69);

            foreach (SiteRuntime runtime in runtimes)
            {
                _log.Write("INFO", "Sitio en " + runtime.Prefix + " [Raiz real: " + runtime.RealRootPath + "]");
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

        /// <summary>Valida y canoniza un sitio. Devuelve null si no se puede servir.</summary>
        private SiteRuntime ResolveSite(SiteDefinition site)
        {
            if (string.IsNullOrEmpty(site.RootPath) || !Directory.Exists(site.RootPath))
            {
                Warn("La carpeta del sitio en el puerto " + site.Port + " no existe:\r\n\r\n" +
                     site.RootPath, "Carpeta no encontrada");
                return null;
            }

            string basePath = Path.GetFullPath(site.RootPath);

            // Servir la raiz de una unidad entera es casi siempre un accidente.
            if (string.Equals(Path.GetPathRoot(basePath), basePath, StringComparison.OrdinalIgnoreCase))
            {
                DialogResult confirm = MessageBox.Show(this,
                    "El sitio del puerto " + site.Port + " apunta a la RAIZ COMPLETA de la unidad (" +
                    basePath + ").\r\n\r\nTodo el contenido de ese disco quedara accesible. Continuar?",
                    "Advertencia de exposicion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return null;
            }

            string realRoot = PathGuard.CanonicalizeRoot(basePath);
            if (string.IsNullOrEmpty(realRoot))
            {
                Warn("No se pudo canonizar la carpeta del puerto " + site.Port + ". Sin esa " +
                     "resolucion el control anti-symlink no puede aplicarse y el sitio no arrancara.",
                     "Error de resolucion de ruta");
                return null;
            }

            return new SiteRuntime(site.Port, site.LanMode, site.Label, basePath, realRoot, site.StartFile);
        }

        private void BuildStatusText(IList<SiteRuntime> runtimes)
        {
            List<string> direcciones = LanAddresses.GetActive();
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
                    foreach (string ip in direcciones)
                    {
                        string url = "http://" + ip + ":" + runtime.Port + "/";
                        estado.Append("   ").Append(ip).Append(":").Append(runtime.Port);
                        _lanUrls.Add(new UrlChoice(url, nombre + "   -   " + url));
                    }
                    if (direcciones.Count == 0) estado.Append("   (sin adaptadores de red)");
                }

                estado.AppendLine();
            }

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

        /// <summary>Una regla por puerto expuesto; se retiran todas al detener.</summary>
        private void ApplyFirewallRules(IList<SiteRuntime> runtimes)
        {
            foreach (SiteRuntime runtime in runtimes)
            {
                if (!runtime.LanMode) continue;

                string error;
                if (FirewallManager.TryAdd(runtime.Port, out error)) _firewallPorts.Add(runtime.Port);
                else _log.Write("WARN", "No se pudo crear la regla de firewall para el puerto " +
                                        runtime.Port + ": " + error);
            }

            // Se arranca por haber sitios en red local, no por que la regla de
            // firewall se creara: si la regla falla igual queremos los nombres.
            bool hayLan = false;
            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.LanMode) hayLan = true;
            }
            if (hayLan)
            {
                MdnsListener.Start();
                _log.Write("INFO", "Escucha mDNS pasiva iniciada en 224.0.0.251:5353.");
            }
        }

        private async System.Threading.Tasks.Task StopServerAsync()
        {
            _startButton.Enabled = false;
            try
            {
                if (_server != null)
                {
                    await _server.StopAsync();
                    _server.Dispose();
                    _server = null;
                }

                MdnsListener.Stop();

                foreach (int port in _firewallPorts) FirewallManager.TryRemove(port);
                _firewallPorts.Clear();
            }
            finally
            {
                _startButton.Enabled = true;
            }

            SetInputsEnabled(true);
            _copyLanButton.Enabled = false;
            _qrButton.Enabled = false;
            _lanUrls.Clear();

            _statusTextBox.Text = "Estado: Detenido";
            _startButton.Text = "Iniciar Servidor";
            _startButton.BackColor = Color.FromArgb(40, 167, 69);

            _log.Write("INFO", "Servidor detenido.");
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
            // La tabla sigue visible pero no editable: cambiar un puerto con el
            // listener en marcha no tendria efecto y solo confundiria.
            _addSiteButton.Enabled = enabled;
            _editSiteButton.Enabled = enabled;
            _removeSiteButton.Enabled = enabled;
            _sitesList.Enabled = enabled;
        }

        // ------------------------------------------------------------------
        // Acciones de la interfaz
        // ------------------------------------------------------------------

        private void OnSettingsClick(object sender, EventArgs e)
        {
            bool running = _server != null;

            using (SettingsDialog dialog = new SettingsDialog(
                       _settings.DirectoryListing, _settings.Compression,
                       _settings.DistributionMode, _settings.Cors, running))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                _settings.DirectoryListing = dialog.DirectoryListing;
                _settings.Compression = dialog.Compression;
                _settings.DistributionMode = dialog.DistributionMode;
                _settings.Cors = dialog.Cors;
                _settings.Save();

                // Se aplican sin reiniciar: los hilos de peticion leen las
                // opciones en cada llamada, no solo al arrancar.
                if (_server != null)
                {
                    _server.Options.EnableDirectoryListing = _settings.DirectoryListing;
                    _server.Options.EnableCompression = _settings.Compression;
                    _server.Options.DistributionMode = _settings.DistributionMode;
                    _server.Options.CorsEnabled = _settings.Cors;
                    _log.Write("INFO", "Configuracion aplicada sin reiniciar el servidor.");
                }
            }
        }

        private void OnElevateClick(object sender, EventArgs e)
        {
            string error;
            if (Elevation.Relaunch(out error)) Close();
            else if (!string.IsNullOrEmpty(error)) Warn("No se pudo elevar: " + error, "Error");
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

            using (QrDialog dialog = new QrDialog(_lanUrls))
            {
                dialog.ShowDialog(this);
            }
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

                _logFilter.SelectedIndex = 0;
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
            if (_closing && _server != null) { e.Cancel = true; return; }

            _logTimer.Stop();
            _clientsTimer.Stop();

            try { CaptureSettings(); _settings.Save(); } catch (Exception) { }

            if (_server != null)
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
        }

        private static void Warn(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
