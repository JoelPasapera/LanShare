using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ServidorWebPro.Core;
using ServidorWebPro.Identity;
using ServidorWebPro.Net;

namespace ServidorWebPro.Ui
{
    public partial class MainForm : Form
    {
        private const int MaxLogChars = 400000;
        private const int MaxLogDrainPerTick = 500;

        private readonly LogBus _log = new LogBus();
        private readonly Dictionary<string, ListViewItem> _rows =
            new Dictionary<string, ListViewItem>(StringComparer.Ordinal);

        private readonly Timer _logTimer = new Timer();
        private readonly Timer _clientsTimer = new Timer();

        private readonly AppSettings _settings = AppSettings.Load();
        private HttpFileServer _server;
        private bool _closing;
        private List<string> _lanUrls = new List<string>();
        private int _firewallPort = -1;
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
            foreach (string recent in _settings.RecentRoots) _rootCombo.Items.Add(recent);

            _rootCombo.Text = !string.IsNullOrEmpty(_settings.RootPath)
                ? _settings.RootPath
                : AppDomain.CurrentDomain.BaseDirectory;

            _portTextBox.Text = _settings.Port.ToString(CultureInfo.InvariantCulture);
            _lanRadio.Checked = _settings.LanMode;
            _localRadio.Checked = !_settings.LanMode;

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
        }

        /// <summary>Recoge el estado actual de los controles antes de guardar.</summary>
        private void CaptureSettings()
        {
            _settings.RootPath = _rootCombo.Text.Trim();
            _settings.LanMode = _lanRadio.Checked;
            _settings.SplitterDistance = _split.SplitterDistance;
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
            _settings.Aliases = ClientRegistry.ExportAliases();

            int port;
            if (int.TryParse(_portTextBox.Text.Trim(), out port) && port >= 1 && port <= 65535)
                _settings.Port = port;
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

            int port;
            if (!int.TryParse(_portTextBox.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                Warn("Ingrese un puerto valido (1-65535).", "Error de Validacion");
                return;
            }

            string rootInput = _rootCombo.Text.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(rootInput) || !Directory.Exists(rootInput))
            {
                Warn("Selecciona una carpeta raiz valida.", "Error");
                return;
            }

            string basePath = Path.GetFullPath(rootInput);

            // Servir la raiz de una unidad entera es casi siempre un accidente.
            if (string.Equals(Path.GetPathRoot(basePath), basePath, StringComparison.OrdinalIgnoreCase))
            {
                DialogResult confirm = MessageBox.Show(
                    "Vas a servir la RAIZ COMPLETA de la unidad (" + basePath + ").\r\n\r\n" +
                    "Todo el contenido de ese disco quedara accesible. Continuar?",
                    "Advertencia de exposicion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            string realRoot = PathGuard.CanonicalizeRoot(basePath);
            if (string.IsNullOrEmpty(realRoot))
            {
                Warn("No se pudo canonizar la carpeta raiz. Sin esa resolucion el control " +
                     "anti-symlink no puede aplicarse y el servidor no arrancara.",
                     "Error de resolucion de ruta");
                return;
            }

            bool lanMode = _lanRadio.Checked;
            if (lanMode && !_isAdmin)
            {
                Warn("El modo Red Local requiere ejecutar la aplicacion como Administrador.",
                     "Permisos Insuficientes");
                return;
            }

            string prefix = lanMode
                ? "http://+:" + port + "/"
                : "http://localhost:" + port + "/";

            ServerOptions options = new ServerOptions(prefix, basePath, realRoot, _settings.Cors);
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
                Warn("No se pudo iniciar el listener en " + prefix + "\r\n\r\n" + ex.Message,
                     "Error al iniciar");
                return;
            }

            _server = server;

            // Estado explicito: heredarlo de la sesion anterior es como se
            // cuelan los botones habilitados sin URL detras.
            _copyLanButton.Enabled = false;
            _qrButton.Enabled = false;

            _settings.RememberRoot(basePath);
            CaptureSettings();
            _settings.Save();
            RefreshRecentRoots();

            ClientRegistry.Clear();
            _rows.Clear();
            _clientsList.Items.Clear();
            _lastDetailIp = "";
            _lastDetailText = "";
            _detailTextBox.Text = "Selecciona un cliente para ver su detalle completo.";

            string status = "Estado: Corriendo\r\n\r\nAcceso local:\r\nhttp://localhost:" + port + "/";
            _lanUrls = new List<string>();

            if (lanMode)
            {
                List<string> addresses = LanAddresses.GetActive();
                if (addresses.Count > 0)
                {
                    foreach (string ip in addresses) _lanUrls.Add("http://" + ip + ":" + port + "/");
                    status += "\r\n\r\nAcceso LAN:\r\n" + string.Join("\r\n", _lanUrls.ToArray());
                    _copyLanButton.Enabled = true;
                    _qrButton.Enabled = true;
                }
                else
                {
                    status += "\r\n\r\nAcceso LAN:\r\nNo se detectaron adaptadores de red activos.";
                }

                string firewallError;
                if (FirewallManager.TryAdd(port, out firewallError))
                {
                    _firewallPort = port;
                }
                else
                {
                    _log.Write("WARN", "No se pudo crear la regla de firewall: " + firewallError);
                }

                MdnsListener.Start();
                _log.Write("INFO", "Escucha mDNS pasiva iniciada en 224.0.0.251:5353.");
            }

            SetInputsEnabled(false);
            _statusTextBox.Text = status;
            _startButton.Text = "Detener Servidor";
            _startButton.BackColor = Color.FromArgb(220, 53, 69);

            _log.Write("INFO", "Servidor iniciado en " + prefix + " [Raiz real: " + realRoot + "]");

            try { System.Diagnostics.Process.Start("http://localhost:" + port + "/"); }
            catch (Exception ex) { _log.Write("WARN", "No se pudo abrir el navegador: " + ex.Message); }
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

                if (_firewallPort >= 0)
                {
                    FirewallManager.TryRemove(_firewallPort);
                    _firewallPort = -1;
                }
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
            _scopeGroup.Enabled = enabled;
            _rootCombo.Enabled = enabled;
            _browseButton.Enabled = enabled;
            _portTextBox.Enabled = enabled;
        }

        // ------------------------------------------------------------------
        // Acciones de la interfaz
        // ------------------------------------------------------------------

        private void OnBrowseClick(object sender, EventArgs e)
        {
            ModernFolderPicker picker = new ModernFolderPicker();
            picker.InitialFolder = _rootCombo.Text;
            if (picker.ShowDialog(Handle)) _rootCombo.Text = picker.SelectedPath;
        }

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

            string text = string.Join("\r\n", _lanUrls.ToArray());
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

        private void RefreshRecentRoots()
        {
            // Items.Clear() puede llevarse por delante el texto visible, y en
            // ese momento el control esta deshabilitado sirviendo esa carpeta.
            string current = _rootCombo.Text;

            _rootCombo.BeginUpdate();
            try
            {
                _rootCombo.Items.Clear();
                foreach (string recent in _settings.RecentRoots) _rootCombo.Items.Add(recent);
            }
            finally
            {
                _rootCombo.EndUpdate();
            }

            if (_rootCombo.Text != current) _rootCombo.Text = current;
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
            string batch = _log.Drain(MaxLogDrainPerTick);
            if (batch.Length == 0) return;

            IntPtr handle = _logTextBox.Handle;

            // Si el usuario subio a leer una peticion antigua, no se le arrastra
            // de vuelta al final. AppendText siempre hace scroll al fondo, asi
            // que hay que anotar la posicion antes y restaurarla despues.
            bool wasAtBottom = IsLogAtBottom(handle);
            int firstVisible = NativeUi.GetFirstVisibleLine(handle);

            NativeUi.SetRedraw(handle, false);
            try
            {
                _logTextBox.AppendText(batch);

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
            if (_firewallPort >= 0) FirewallManager.TryRemove(_firewallPort);
        }

        private static void Warn(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
