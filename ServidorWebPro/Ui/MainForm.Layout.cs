using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServidorWebPro.Ui
{
    /// <summary>
    /// Construccion de la interfaz. Va en su propio archivo parcial para que
    /// MainForm.cs contenga solo comportamiento.
    ///
    /// Se arma en codigo y no con el disenador: asi el proyecto compila con el
    /// csc.exe que trae Windows, sin necesidad de resgen ni de archivos .resx.
    /// </summary>
    public partial class MainForm
    {
        private const AnchorStyles AnchorTopLeft = AnchorStyles.Top | AnchorStyles.Left;
        private const AnchorStyles AnchorTopRight = AnchorStyles.Top | AnchorStyles.Right;
        private const AnchorStyles AnchorTopStretch = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        private const AnchorStyles AnchorFill = AnchorStyles.Top | AnchorStyles.Bottom |
                                                AnchorStyles.Left | AnchorStyles.Right;

        private Label _permissionLabel;
        private Label _creditLabel;
        private Button _elevateButton;
        private Button _settingsButton;
        private GroupBox _scopeGroup;
        private RadioButton _localRadio;
        private RadioButton _lanRadio;

        private ComboBox _rootCombo;
        private Button _browseButton;
        private TextBox _portTextBox;
        private Button _startButton;
        private TextBox _statusTextBox;
        private Button _copyLanButton;
        private Button _qrButton;
        private TabControl _tabs;
        private TabPage _logTab;
        private TabPage _clientsTab;
        private TextBox _logTextBox;
        private SplitContainer _split;
        private ListView _clientsList;
        private TextBox _detailTextBox;
        private Button _reprobeButton;
        private Button _clearButton;
        private Button _exportButton;
        private Button _aliasButton;
        private Label _ouiLabel;

        private void BuildUi()
        {
            SuspendLayout();

            Text = AppInfo.WindowTitle;
            ClientSize = new Size(1064, 760);
            MinimumSize = new Size(940, 660);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            BuildHeader();
            BuildScopeGroup();
            BuildRootRow();
            BuildStartButton();
            BuildStatusGroup();
            BuildTabs();

            ResumeLayout(false);
        }

        private void BuildHeader()
        {
            _permissionLabel = new Label();
            _permissionLabel.Location = new Point(20, 15);
            _permissionLabel.Size = new Size(400, 20);
            _permissionLabel.Anchor = AnchorTopLeft;
            _permissionLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Controls.Add(_permissionLabel);

            _elevateButton = new Button();
            _elevateButton.Location = new Point(894, 10);
            _elevateButton.Size = new Size(150, 25);
            _elevateButton.Anchor = AnchorTopRight;
            _elevateButton.Text = "Escalar Privilegios";
            _elevateButton.Click += OnElevateClick;
            Controls.Add(_elevateButton);

            _settingsButton = new Button();
            _settingsButton.Location = new Point(744, 10);
            _settingsButton.Size = new Size(140, 25);
            _settingsButton.Anchor = AnchorTopRight;
            _settingsButton.Text = "Configuracion...";
            _settingsButton.Click += OnSettingsClick;
            Controls.Add(_settingsButton);

            _creditLabel = new Label();
            _creditLabel.Location = new Point(360, 16);
            _creditLabel.Size = new Size(374, 18);
            _creditLabel.Anchor = AnchorTopRight;
            _creditLabel.TextAlign = ContentAlignment.MiddleRight;
            _creditLabel.ForeColor = Color.DimGray;
            _creditLabel.Font = new Font("Segoe UI", 8f);
            _creditLabel.Text = AppInfo.Credit;
            Controls.Add(_creditLabel);

            Label separator = new Label();
            separator.Location = new Point(20, 40);
            separator.Size = new Size(1024, 2);
            separator.Anchor = AnchorTopStretch;
            separator.BorderStyle = BorderStyle.Fixed3D;
            Controls.Add(separator);
        }

        private void BuildScopeGroup()
        {
            _scopeGroup = new GroupBox();
            _scopeGroup.Location = new Point(20, 50);
            _scopeGroup.Size = new Size(1024, 50);
            _scopeGroup.Anchor = AnchorTopStretch;
            _scopeGroup.Text = "Alcance del Servidor";

            _localRadio = new RadioButton();
            _localRadio.Location = new Point(15, 20);
            _localRadio.Size = new Size(220, 22);
            _localRadio.Text = "Solo este equipo (localhost)";
            _localRadio.Checked = true;
            _scopeGroup.Controls.Add(_localRadio);

            _lanRadio = new RadioButton();
            _lanRadio.Location = new Point(245, 20);
            _lanRadio.Size = new Size(330, 22);
            _lanRadio.Text = "Red local (accesible desde otros dispositivos)";
            _scopeGroup.Controls.Add(_lanRadio);

            Controls.Add(_scopeGroup);
        }

        private void BuildRootRow()
        {
            Label rootLabel = new Label();
            rootLabel.Location = new Point(20, 107);
            rootLabel.Size = new Size(400, 18);
            rootLabel.Anchor = AnchorTopLeft;
            rootLabel.Text = "Carpeta raiz de la aplicacion web:";
            Controls.Add(rootLabel);

            // Desplegable en vez de caja de texto: guarda las ultimas carpetas
            // servidas, que es lo que uno reabre el 90% de las veces.
            _rootCombo = new ComboBox();
            _rootCombo.Location = new Point(20, 127);
            _rootCombo.Size = new Size(774, 23);
            _rootCombo.Anchor = AnchorTopStretch;
            _rootCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _rootCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _rootCombo.AutoCompleteSource = AutoCompleteSource.FileSystemDirectories;
            Controls.Add(_rootCombo);

            _browseButton = new Button();
            _browseButton.Location = new Point(804, 125);
            _browseButton.Size = new Size(90, 27);
            _browseButton.Anchor = AnchorTopRight;
            _browseButton.Text = "Examinar...";
            _browseButton.Click += OnBrowseClick;
            Controls.Add(_browseButton);

            Label portLabel = new Label();
            portLabel.Location = new Point(909, 107);
            portLabel.Size = new Size(80, 18);
            portLabel.Anchor = AnchorTopRight;
            portLabel.Text = "Puerto TCP:";
            Controls.Add(portLabel);

            _portTextBox = new TextBox();
            _portTextBox.Location = new Point(909, 127);
            _portTextBox.Size = new Size(135, 23);
            _portTextBox.Anchor = AnchorTopRight;
            _portTextBox.Text = "8080";
            Controls.Add(_portTextBox);
        }

        private void BuildStartButton()
        {
            _startButton = new Button();
            _startButton.Location = new Point(20, 163);
            _startButton.Size = new Size(1024, 38);
            _startButton.Anchor = AnchorTopStretch;
            _startButton.Text = "Iniciar Servidor";
            _startButton.BackColor = Color.FromArgb(40, 167, 69);
            _startButton.ForeColor = Color.White;
            _startButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _startButton.Click += OnStartClick;
            Controls.Add(_startButton);
        }

        private void BuildStatusGroup()
        {
            GroupBox group = new GroupBox();
            group.Location = new Point(20, 210);
            group.Size = new Size(1024, 100);
            group.Anchor = AnchorTopStretch;
            group.Text = "Estado y Direcciones de Acceso";

            _statusTextBox = new TextBox();
            _statusTextBox.Location = new Point(15, 22);
            _statusTextBox.Size = new Size(834, 68);
            _statusTextBox.Anchor = AnchorTopStretch;
            _statusTextBox.Multiline = true;
            _statusTextBox.ReadOnly = true;
            _statusTextBox.ScrollBars = ScrollBars.Vertical;
            _statusTextBox.Font = new Font("Consolas", 8.5f);
            _statusTextBox.Text = "Estado: Detenido";
            group.Controls.Add(_statusTextBox);

            _qrButton = new Button();
            _qrButton.Location = new Point(859, 24);
            _qrButton.Size = new Size(150, 30);
            _qrButton.Anchor = AnchorTopRight;
            _qrButton.Text = "Codigo QR";
            _qrButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _qrButton.Enabled = false;
            _qrButton.Click += OnQrClick;
            group.Controls.Add(_qrButton);

            _copyLanButton = new Button();
            _copyLanButton.Location = new Point(859, 58);
            _copyLanButton.Size = new Size(150, 30);
            _copyLanButton.Anchor = AnchorTopRight;
            _copyLanButton.Text = "Copiar URL LAN";
            _copyLanButton.Enabled = false;
            _copyLanButton.Click += OnCopyLanClick;
            group.Controls.Add(_copyLanButton);

            Controls.Add(group);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Location = new Point(20, 318);
            _tabs.Size = new Size(1024, 422);
            _tabs.Anchor = AnchorFill;

            _logTab = new TabPage("Registro de Telemetria");
            _logTab.UseVisualStyleBackColor = true;
            _logTab.Padding = new Padding(6);

            _logTextBox = new TextBox();
            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Vertical;
            _logTextBox.BackColor = Color.FromArgb(30, 30, 30);
            _logTextBox.ForeColor = Color.FromArgb(220, 220, 220);
            _logTextBox.Font = new Font("Consolas", 8.5f);
            _logTab.Controls.Add(_logTextBox);
            _tabs.TabPages.Add(_logTab);

            _clientsTab = new TabPage("Clientes conectados");
            _clientsTab.UseVisualStyleBackColor = true;
            _clientsTab.Padding = new Padding(6);
            _clientsTab.Controls.Add(BuildClientsPanel());
            _tabs.TabPages.Add(_clientsTab);

            Controls.Add(_tabs);
        }

        private SplitContainer BuildClientsPanel()
        {
            // El divisor es arrastrable: el usuario reparte el espacio entre la
            // tabla y el detalle segun lo que este mirando.
            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.Panel1MinSize = 90;
            _split.Panel2MinSize = 120;

            _clientsList = new ListView();
            _clientsList.Dock = DockStyle.Fill;
            _clientsList.View = View.Details;
            _clientsList.FullRowSelect = true;
            _clientsList.GridLines = true;
            _clientsList.MultiSelect = false;
            _clientsList.HideSelection = false;
            _clientsList.Font = new Font("Consolas", 8.5f);
            _clientsList.SelectedIndexChanged += OnClientSelectionChanged;
            _clientsList.DoubleClick += OnClientDoubleClick;

            _clientsList.Columns.Add("IP", 100);
            _clientsList.Columns.Add("Nombre / alias", 140);
            _clientsList.Columns.Add("MAC", 120);
            _clientsList.Columns.Add("Fabricante", 105);
            _clientsList.Columns.Add("Dispositivo (UA)", 125);
            _clientsList.Columns.Add("Navegador", 100);
            _clientsList.Columns.Add("Veredicto", 175);
            _clientsList.Columns.Add("GPU / SoC", 130);
            _clientsList.Columns.Add("Pet.", 45);
            _clientsList.Columns.Add("Ultima", 55);
            _split.Panel1.Controls.Add(_clientsList);

            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Top;
            buttons.Height = 32;

            _reprobeButton = MakeToolButton("Re-identificar", 0, OnReprobeClick);
            _clearButton = MakeToolButton("Limpiar lista", 126, OnClearClientsClick);
            _exportButton = MakeToolButton("Exportar CSV", 252, OnExportClick);
            _aliasButton = MakeToolButton("Poner alias", 378, OnAliasClick);
            buttons.Controls.Add(_reprobeButton);
            buttons.Controls.Add(_clearButton);
            buttons.Controls.Add(_exportButton);
            buttons.Controls.Add(_aliasButton);

            _ouiLabel = new Label();
            _ouiLabel.Location = new Point(508, 8);
            _ouiLabel.Size = new Size(480, 18);
            _ouiLabel.Anchor = AnchorTopStretch;
            _ouiLabel.ForeColor = Color.DimGray;
            _ouiLabel.Font = new Font("Segoe UI", 8f);
            buttons.Controls.Add(_ouiLabel);

            _detailTextBox = new TextBox();
            _detailTextBox.Dock = DockStyle.Fill;
            _detailTextBox.Multiline = true;
            _detailTextBox.ReadOnly = true;
            _detailTextBox.ScrollBars = ScrollBars.Both;
            _detailTextBox.WordWrap = false;
            _detailTextBox.BackColor = Color.FromArgb(248, 248, 248);
            _detailTextBox.Font = new Font("Consolas", 8.5f);
            _detailTextBox.Text = "Selecciona un cliente para ver su detalle completo.";

            _split.Panel2.Controls.Add(_detailTextBox);
            _split.Panel2.Controls.Add(buttons);

            return _split;
        }

        private static Button MakeToolButton(string text, int x, EventHandler handler)
        {
            Button button = new Button();
            button.Location = new Point(x, 2);
            button.Size = new Size(120, 26);
            button.Text = text;
            button.Click += handler;
            return button;
        }
    }
}
