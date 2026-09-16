using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
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
        private GroupBox _sitesGroup;
        private ListView _sitesList;
        private Button _addSiteButton;
        private Button _editSiteButton;
        private Button _removeSiteButton;
        private Button _startButton;
        private TextBox _statusTextBox;
        private Button _copyLanButton;
        private Button _qrButton;
        private TabControl _tabs;
        private TabPage _logTab;
        private TabPage _clientsTab;
        private TextBox _logTextBox;
        private ComboBox _logFilter;
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
            // La tabla de sitios subio el inicio de las pestanas a y=411, asi
            // que por debajo de esta altura el divisor no cabe entre sus dos
            // paneles minimos y SplitterDistance falla.
            MinimumSize = new Size(940, 740);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            BuildHeader();
            BuildSitesGroup();
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

        private void BuildSitesGroup()
        {
            _sitesGroup = new GroupBox();
            _sitesGroup.Location = new Point(20, 50);
            _sitesGroup.Size = new Size(1024, 196);
            _sitesGroup.Anchor = AnchorTopStretch;
            _sitesGroup.Text = "Sitios";

            // Casillas en la propia tabla: activar o desactivar un proyecto es
            // un clic, sin abrir nada.
            _sitesList = new ListView();
            _sitesList.Location = new Point(15, 22);
            _sitesList.Size = new Size(994, 128);
            _sitesList.Anchor = AnchorTopStretch;
            _sitesList.View = View.Details;
            _sitesList.CheckBoxes = true;
            _sitesList.FullRowSelect = true;
            _sitesList.GridLines = true;
            _sitesList.MultiSelect = false;
            _sitesList.HideSelection = false;
            _sitesList.Columns.Add("Puerto", 70);
            _sitesList.Columns.Add("Carpeta", 356);
            _sitesList.Columns.Add("Archivo", 124);
            _sitesList.Columns.Add("Alcance", 104);
            _sitesList.Columns.Add("Etiqueta", 120);
            _sitesList.Columns.Add("URL local", 216);   // ocupa el resto del ancho
            _sitesList.ItemChecked += OnSiteCheckedChanged;
            _sitesList.DoubleClick += OnEditSiteClick;

            // Marcan que hubo interaccion real, para distinguir un clic del
            // usuario de un evento que emite el propio control al recrearse.
            _sitesList.MouseDown += OnSiteListInput;
            _sitesList.MouseUp += OnSiteListInput;
            _sitesList.KeyDown += OnSiteListInput;
            _sitesList.KeyUp += OnSiteListInput;
            _sitesGroup.Controls.Add(_sitesList);

            _addSiteButton = MakeSiteButton("Anadir sitio", 15, 158, OnAddSiteClick);
            _editSiteButton = MakeSiteButton("Editar", 131, 158, OnEditSiteClick);
            _removeSiteButton = MakeSiteButton("Quitar", 247, 158, OnRemoveSiteClick);
            _sitesGroup.Controls.Add(_addSiteButton);
            _sitesGroup.Controls.Add(_editSiteButton);
            _sitesGroup.Controls.Add(_removeSiteButton);

            Label hint = new Label();
            hint.Location = new Point(373, 163);
            hint.Size = new Size(636, 18);
            hint.Anchor = AnchorTopStretch;
            hint.ForeColor = Color.DimGray;
            hint.Font = new Font("Segoe UI", 8f);
            hint.Text = "Doble clic para editar. Cada sitio necesita su propio puerto. " +
                        "El modo red local exige administrador.";
            _sitesGroup.Controls.Add(hint);

            Controls.Add(_sitesGroup);
        }

        private static Button MakeSiteButton(string text, int x, int y, EventHandler handler)
        {
            Button button = new Button();
            button.Location = new Point(x, y);
            button.Size = new Size(110, 26);
            button.Text = text;
            button.Click += handler;
            return button;
        }

        private void BuildStartButton()
        {
            _startButton = new Button();
            _startButton.Location = new Point(20, 256);
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
            group.Location = new Point(20, 303);
            group.Size = new Size(1024, 100);
            group.Anchor = AnchorTopStretch;
            group.Text = "Estado y Direcciones de Acceso";

            _statusTextBox = new TextBox();
            _statusTextBox.Location = new Point(15, 22);
            _statusTextBox.Size = new Size(834, 68);
            _statusTextBox.Anchor = AnchorTopStretch;
            _statusTextBox.Multiline = true;
            _statusTextBox.ReadOnly = true;
            // Sin esto, el formato de una linea por sitio se parte y se pierde
            // la alineacion en columnas: WordWrap viene activado por defecto.
            _statusTextBox.WordWrap = false;
            _statusTextBox.ScrollBars = ScrollBars.Both;
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
            _tabs.Location = new Point(20, 411);
            _tabs.Size = new Size(1024, 329);
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

            // Se anade DESPUES del cuadro de texto a proposito: WinForms acopla
            // de mayor a menor indice, asi que la barra toma su franja primero
            // y el cuadro se queda con el resto.
            _logTab.Controls.Add(BuildLogToolbar());
            _tabs.TabPages.Add(_logTab);

            _clientsTab = new TabPage("Clientes conectados");
            _clientsTab.UseVisualStyleBackColor = true;
            _clientsTab.Padding = new Padding(6);
            _clientsTab.Controls.Add(BuildClientsPanel());
            _tabs.TabPages.Add(_clientsTab);

            Controls.Add(_tabs);
        }

        private Panel BuildLogToolbar()
        {
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 32;

            Label label = new Label();
            label.Location = new Point(2, 8);
            label.Size = new Size(44, 18);
            label.Text = "Sitio:";
            bar.Controls.Add(label);

            _logFilter = new ComboBox();
            _logFilter.Location = new Point(46, 4);
            _logFilter.Size = new Size(150, 23);
            _logFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            _logFilter.SelectedIndexChanged += OnLogFilterChanged;
            bar.Controls.Add(_logFilter);

            bar.Controls.Add(MakeLogButton("Guardar...", 206, OnSaveLogClick));
            bar.Controls.Add(MakeLogButton("Limpiar", 300, OnClearLogClick));

            return bar;
        }

        private static Button MakeLogButton(string text, int x, EventHandler handler)
        {
            Button button = new Button();
            button.Location = new Point(x, 3);
            button.Size = new Size(88, 25);
            button.Text = text;
            button.Click += handler;
            return button;
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
