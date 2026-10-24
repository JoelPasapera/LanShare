using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using LanShare.Core;
using LanShare.Net;

namespace LanShare.Ui
{
    /// <summary>Alta y edicion de un sitio: una carpeta servida en un puerto.</summary>
    public sealed class SiteDialog : Form
    {
        private readonly ComboBox _root;
        private readonly TextBox _port;
        private readonly TextBox _label;
        private readonly RadioButton _local;
        private readonly RadioButton _lan;
        private readonly CheckBox _enabled;
        private readonly Label _portStatus;
        private readonly ICollection<int> _otherSitePorts;
        private readonly Timer _statusDelay;
        private readonly ComboBox _startFile;

        /// <summary>Entrada que representa servir la carpeta sin indice.</summary>
        private const string ListFolderOption = "(listar la carpeta)";

        public SiteDefinition Result { get; private set; }

        private int _currentPort;

        /// <summary>
        /// Puerto en el que este mismo sitio se esta sirviendo ahora, o 0. Lo
        /// ocupa el propio LanShare, asi que no cuenta como conflicto: al
        /// aceptar, el servidor se reinicia con los cambios. Se asigna despues
        /// del constructor, que ya pinto el estado del puerto: hay que repintarlo.
        /// </summary>
        public int CurrentPort
        {
            get { return _currentPort; }
            set
            {
                _currentPort = value;
                UpdatePortStatus();
            }
        }

        public SiteDialog(SiteDefinition site, IList<string> recentRoots,
                          ICollection<int> otherSitePorts)
        {
            _otherSitePorts = otherSitePorts;
            bool isNew = site == null;
            if (site == null)
            {
                site = new SiteDefinition();

                // Un sitio nuevo nacia con el 8080 aunque ya lo usara otro de tus
                // proyectos, y habia que pulsar Auto. Ahora propone el primero
                // libre de los habituales, que es el 8080 si esta libre.
                site.Port = FirstFreePort(site.Port);
            }

            Text = isNew ? "Anadir sitio" : "Editar sitio";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            int width = 560;
            int y = 16;

            Controls.Add(MakeLabel("Carpeta que se va a servir:", 16, y, width - 32));
            y += 20;

            _root = new ComboBox();
            _root.Location = new Point(16, y);
            _root.Size = new Size(width - 32 - 96, 23);
            _root.DropDownStyle = ComboBoxStyle.DropDown;
            _root.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _root.AutoCompleteSource = AutoCompleteSource.FileSystemDirectories;
            if (recentRoots != null)
            {
                foreach (string recent in recentRoots) _root.Items.Add(recent);
            }
            _root.Text = site.RootPath;
            Controls.Add(_root);

            Button browse = new Button();
            browse.Location = new Point(width - 16 - 90, y - 1);
            browse.Size = new Size(90, 25);
            browse.Text = "Examinar...";
            browse.Click += OnBrowseClick;
            Controls.Add(browse);
            y += 34;

            Controls.Add(MakeLabel("Archivo de inicio:", 16, y, 250));
            Controls.Add(MakeLabel("Etiqueta (opcional):", 290, y, 200));
            y += 20;

            // Se ofrece lo que hay de verdad en la carpeta: elegir de una lista
            // evita escribir un nombre y descubrir el error al arrancar.
            _startFile = new ComboBox();
            _startFile.Location = new Point(16, y);
            _startFile.Size = new Size(250, 23);
            _startFile.DropDownStyle = ComboBoxStyle.DropDown;
            Controls.Add(_startFile);

            _label = new TextBox();
            _label.Location = new Point(290, y);
            _label.Size = new Size(width - 16 - 290, 23);
            _label.Text = site.Label;
            Controls.Add(_label);
            y += 32;

            Controls.Add(MakeLabel("Puerto TCP:", 16, y, 120));
            y += 20;

            _port = new TextBox();
            _port.Location = new Point(16, y);
            _port.Size = new Size(80, 23);
            _port.Text = site.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Antiperdida de tiempo: sin esto, cada tecla disparaba dos
            // consultas al sistema y un intento de reserva. Escribir "8080"
            // eran cuatro escaneos completos.
            _statusDelay = new Timer();
            _statusDelay.Interval = 350;
            _statusDelay.Tick += OnStatusDelayTick;

            _port.TextChanged += OnPortChanged;
            Controls.Add(_port);

            Button ports = new Button();
            ports.Location = new Point(102, y - 1);
            ports.Size = new Size(110, 25);
            ports.Text = "Ver libres...";
            ports.Click += OnBrowsePortsClick;
            Controls.Add(ports);

            Button suggest = new Button();
            suggest.Location = new Point(218, y - 1);
            suggest.Size = new Size(62, 25);
            suggest.Text = "Auto";
            suggest.Click += OnSuggestPortClick;
            Controls.Add(suggest);

            y += 26;

            // Estado en vivo: enterarse de que el puerto esta ocupado aqui es
            // mejor que enterarse al pulsar Iniciar con un error de HttpListener.
            _portStatus = new Label();
            _portStatus.Location = new Point(16, y);
            _portStatus.Size = new Size(width - 32, 18);
            _portStatus.Font = new Font("Segoe UI", 8f);
            Controls.Add(_portStatus);
            y += 24;

            GroupBox scope = new GroupBox();
            scope.Location = new Point(16, y);
            scope.Size = new Size(width - 32, 50);
            scope.Text = "Alcance";

            _local = new RadioButton();
            _local.Location = new Point(14, 20);
            _local.Size = new Size(200, 22);
            _local.Text = "Solo este equipo";
            _local.Checked = !site.LanMode;
            _local.CheckedChanged += OnScopeChanged;
            scope.Controls.Add(_local);

            _lan = new RadioButton();
            _lan.Location = new Point(224, 20);
            _lan.Size = new Size(280, 22);
            _lan.Text = "Red local (requiere administrador)";
            _lan.Checked = site.LanMode;
            scope.Controls.Add(_lan);

            Controls.Add(scope);
            y += 58;

            _enabled = new CheckBox();
            _enabled.Location = new Point(16, y);
            _enabled.Size = new Size(300, 22);
            _enabled.Text = "Servir este sitio al iniciar";
            _enabled.Checked = site.Enabled;
            Controls.Add(_enabled);
            y += 34;

            Button ok = new Button();
            ok.Location = new Point(width - 16 - 86 * 2 - 8, y);
            ok.Size = new Size(86, 28);
            ok.Text = "Aceptar";
            ok.Click += OnAcceptClick;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Location = new Point(width - 16 - 86, y);
            cancel.Size = new Size(86, 28);
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(width, y + 28 + 16);

            RefreshStartFiles(site.StartFile);
            UpdatePortStatus();
        }

        /// <summary>
        /// Rellena el desplegable con los .html de la carpeta. index.html
        /// aparece siempre aunque no exista: es el valor por defecto y ver que
        /// falta explica por que el sitio caia en el listado.
        /// </summary>
        private void RefreshStartFiles(string selected)
        {
            _startFile.BeginUpdate();
            try
            {
                _startFile.Items.Clear();
                _startFile.Items.Add(ListFolderOption);

                string root = _root.Text.Trim().Trim('"', '\'');
                bool hasDefault = false;

                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(root, "*.htm*"))
                        {
                            string name = Path.GetFileName(file);
                            _startFile.Items.Add(name);
                            if (string.Equals(name, StaticFileResolver.DefaultIndexFile,
                                              StringComparison.OrdinalIgnoreCase)) hasDefault = true;
                        }
                    }
                    catch (Exception) { }
                }

                if (!hasDefault) _startFile.Items.Add(StaticFileResolver.DefaultIndexFile);

                _startFile.Text = string.IsNullOrEmpty(selected)
                    ? ListFolderOption
                    : selected;
            }
            finally { _startFile.EndUpdate(); }
        }

        private static Label MakeLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Location = new Point(x, y);
            label.Size = new Size(width, 18);
            label.Text = text;
            return label;
        }

        private void OnPortChanged(object sender, EventArgs e)
        {
            _portStatus.Text = "Comprobando...";
            _portStatus.ForeColor = Color.DimGray;

            _statusDelay.Stop();
            _statusDelay.Start();
        }

        private void OnStatusDelayTick(object sender, EventArgs e)
        {
            _statusDelay.Stop();
            UpdatePortStatus();
        }

        private void OnScopeChanged(object sender, EventArgs e)
        {
            // Cambiar de alcance cambia el prefijo, y con el lo que se prueba.
            UpdatePortStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _statusDelay != null) _statusDelay.Dispose();
            base.Dispose(disposing);
        }

        private void UpdatePortStatus()
        {
            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                _portStatus.Text = "Puerto invalido (1-65535)";
                _portStatus.ForeColor = Color.Firebrick;
                return;
            }

            if (CurrentPort > 0 && port == CurrentPort)
            {
                _portStatus.Text = "Lo usa este mismo sitio ahora; se reiniciara con los cambios";
                _portStatus.ForeColor = Color.FromArgb(20, 120, 45);
                return;
            }

            PortInfo info = PortAvailability.Describe(port, "", _otherSitePorts,
                                                      PortAvailability.GetListeningPorts(),
                                                      PortAvailability.GetListenerOwners());

            // Decir quien lo ocupa ahorra el viaje al administrador de tareas.
            string texto = info.Owner.Length > 0
                ? info.StatusText + ": " + info.Owner
                : info.StatusText;
            bool libre = info.Status == PortStatus.Free;

            // Prueba definitiva con el prefijo exacto que usara este sitio:
            // es lo unico que detecta un conflicto en http.sys o la falta de
            // permisos para el comodin de red local.
            if (libre && !PortAvailability.CanReserveHttp(port, _lan.Checked))
            {
                libre = false;
                texto = _lan.Checked
                    ? "El socket esta libre, pero no se puede reservar en red local (hace falta administrador)"
                    : "El socket esta libre, pero http.sys no permite reservarlo";
            }

            _portStatus.Text = texto;
            _portStatus.ForeColor = libre ? Color.FromArgb(20, 120, 45) : Color.Firebrick;
        }

        /// <summary>
        /// Prepara el dialogo de un sitio nuevo para una carpeta concreta, como
        /// la que se arrastra sobre la ventana principal.
        /// </summary>
        /// <param name="startFile">Pagina de inicio; "" para listar la carpeta, null para la de siempre.</param>
        public void Prefill(string root, string startFile)
        {
            if (string.IsNullOrEmpty(root)) return;
            _root.Text = root;
            RefreshStartFiles(startFile ?? _startFile.Text);
        }

        private int FirstFreePort(int fallback)
        {
            try
            {
                int free = PortAvailability.FindFirstFree(_otherSitePorts ?? new int[0]);
                return free > 0 ? free : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private void OnBrowsePortsClick(object sender, EventArgs e)
        {
            using (PortPickerDialog dialog = new PortPickerDialog(_otherSitePorts))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _port.Text = dialog.SelectedPort.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private void OnSuggestPortClick(object sender, EventArgs e)
        {
            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;
            int free;
            try { free = PortAvailability.FindFirstFree(_otherSitePorts); }
            finally { Cursor = previous; }

            if (free < 0)
            {
                Warn("No se encontro ningun puerto libre entre los habituales ni en 8100-8999.");
                return;
            }

            _port.Text = free.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            ModernFolderPicker picker = new ModernFolderPicker();
            picker.InitialFolder = _root.Text;
            if (!picker.ShowDialog(Handle)) return;

            _root.Text = picker.SelectedPath;
            RefreshStartFiles(_startFile.Text);
        }

        /// <summary>
        /// La validacion va aqui y no en DialogResult para poder impedir el
        /// cierre: con DialogResult el dialogo se cierra antes de comprobar nada.
        /// </summary>
        private void OnAcceptClick(object sender, EventArgs e)
        {
            string root = _root.Text.Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Warn("Selecciona una carpeta valida.");
                return;
            }

            int port;
            if (!int.TryParse(_port.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                Warn("Ingresa un puerto entre 1 y 65535.");
                return;
            }

            PortInfo estado = PortAvailability.Describe(port, "", _otherSitePorts,
                                                        PortAvailability.GetListeningPorts(),
                                                        PortAvailability.GetListenerOwners());
            if (estado.Status != PortStatus.Free && !(CurrentPort > 0 && port == CurrentPort))
            {
                // Se avisa pero no se impide: el proceso que lo ocupa puede ser
                // uno que el usuario va a cerrar antes de iniciar el servidor.
                DialogResult sigue = MessageBox.Show(this,
                    "El puerto " + port + " no esta libre ahora mismo: " + estado.StatusText +
                    (estado.Owner.Length > 0 ? " (" + estado.Owner + ")" : "") +
                    ".\r\n\r\nGuardar de todos modos?",
                    "Puerto no disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (sigue != DialogResult.Yes) return;
            }

            SiteDefinition site = new SiteDefinition();
            site.RootPath = Path.GetFullPath(root);
            site.Port = port;
            site.LanMode = _lan.Checked;
            site.Label = _label.Text.Trim();
            site.Enabled = _enabled.Checked;

            string start = _startFile.Text.Trim();
            site.StartFile = string.Equals(start, ListFolderOption, StringComparison.Ordinal)
                ? ""
                : start;

            Result = site;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "Datos incompletos",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
