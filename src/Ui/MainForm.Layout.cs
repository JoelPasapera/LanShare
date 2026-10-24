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
    ///
    /// Las cajas van apiladas con Dock y separadas por divisores (PaneGrip):
    /// sitios, boton de inicio, estado y pestanas. Cada borde entre cajas se
    /// arrastra para cambiar su altura (ver MainForm.Panes).
    /// </summary>
    public partial class MainForm
    {
        private const AnchorStyles AnchorTopLeft = AnchorStyles.Top | AnchorStyles.Left;
        private const AnchorStyles AnchorTopRight = AnchorStyles.Top | AnchorStyles.Right;
        private const AnchorStyles AnchorTopStretch = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        private const AnchorStyles AnchorBottomLeft = AnchorStyles.Bottom | AnchorStyles.Left;
        private const AnchorStyles AnchorBottomStretch = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        private const AnchorStyles AnchorFill = AnchorStyles.Top | AnchorStyles.Bottom |
                                                AnchorStyles.Left | AnchorStyles.Right;

        /// <summary>Ancho del area de cliente inicial: los paneles nacen con el para que las anclas cuadren.</summary>
        private const int InitialClientWidth = 1064;
        private const int HeaderHeight = 44;

        private Label _permissionLabel;
        private Label _creditLabel;
        private Button _elevateButton;
        private Button _settingsButton;
        private GroupBox _sitesGroup;
        private ListView _sitesList;
        private Button _addSiteButton;
        private Button _editSiteButton;
        private Button _removeSiteButton;
        private Label _sitesHint;

        private const string SitesHintIdle =
            "Doble clic o F2 para editar, clic derecho para mas. Arrastra aqui una carpeta para anadirla.";
        private const string SitesHintRunning =
            "En marcha: los cambios se aplican al momento. Intro abre el sitio y Ctrl+C copia su URL.";
        private Button _startButton;
        private GroupBox _statusGroup;
        private TextBox _statusTextBox;
        private Button _copyLanButton;
        private Button _qrButton;
        private TabControl _tabs;
        private TabPage _logTab;
        private TabPage _clientsTab;
        private TextBox _logTextBox;
        private PaneGrip _logGrip;
        private PaneGrip _clientsTopGrip;
        private ComboBox _logFilter;
        private Label _statsLabel;
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
            ClientSize = new Size(InitialClientWidth, 760);
            // Por debajo de este alto las cajas ya estan en su minimo: cabecera
            // 44, sitios 135, boton 38, estado 110 y pestanas 265, mas 53 de
            // divisores y margenes, suman 645 de area de cliente.
            MinimumSize = new Size(940, 690);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            // La franja de abajo hace de borde de la ventana (ver WndProc): tirar
            // de justo debajo del registro alarga la ventana.
            Padding = new Padding(0, 0, 0, BottomResizeBand);

            InitializeGripTips();

            Panel header = BuildHeader();
            Panel body = BuildBody();

            // WinForms acopla de mayor a menor indice: la cabecera, anadida la
            // ultima, toma su franja antes de que el cuerpo rellene el resto.
            // El orden del tabulador va aparte y sigue siendo de arriba abajo.
            header.TabIndex = 0;
            body.TabIndex = 1;
            Controls.Add(body);
            Controls.Add(header);

            ResumeLayout(false);
        }

        /// <summary>
        /// Anade controles acoplados de arriba abajo. WinForms acopla de mayor a
        /// menor indice, asi que entran al reves: el primero de la lista, el
        /// ultimo en entrar, es el que toma la franja de arriba. El orden del
        /// tabulador se fija aparte para que siga yendo de arriba abajo.
        /// </summary>
        private static void AddStacked(Control container, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                topToBottom[i].TabIndex = i;
                container.Controls.Add(topToBottom[i]);
            }
        }

        private Panel BuildHeader()
        {
            // El panel nace con su tamano final antes de meter nada dentro: las
            // anclas a la derecha se calculan con el tamano que tenga entonces.
            Panel header = new Panel();
            header.Size = new Size(InitialClientWidth, HeaderHeight);
            header.Dock = DockStyle.Top;

            _permissionLabel = new Label();
            _permissionLabel.Location = new Point(20, 15);
            _permissionLabel.Size = new Size(400, 20);
            _permissionLabel.Anchor = AnchorTopLeft;
            _permissionLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            header.Controls.Add(_permissionLabel);

            _elevateButton = new Button();
            _elevateButton.Location = new Point(894, 10);
            _elevateButton.Size = new Size(150, 25);
            _elevateButton.Anchor = AnchorTopRight;
            _elevateButton.Text = "Escalar Privilegios";
            _elevateButton.Click += OnElevateClick;
            header.Controls.Add(_elevateButton);

            _settingsButton = new Button();
            _settingsButton.Location = new Point(744, 10);
            _settingsButton.Size = new Size(140, 25);
            _settingsButton.Anchor = AnchorTopRight;
            _settingsButton.Text = "Configuracion...";
            _settingsButton.Click += OnSettingsClick;
            header.Controls.Add(_settingsButton);

            _creditLabel = new Label();
            _creditLabel.Location = new Point(360, 16);
            _creditLabel.Size = new Size(374, 18);
            _creditLabel.Anchor = AnchorTopRight;
            _creditLabel.TextAlign = ContentAlignment.MiddleRight;
            _creditLabel.ForeColor = Color.DimGray;
            _creditLabel.Font = new Font("Segoe UI", 8f);
            _creditLabel.Text = AppInfo.Credit;
            header.Controls.Add(_creditLabel);

            Label separator = new Label();
            separator.Location = new Point(20, 40);
            separator.Size = new Size(1024, 2);
            separator.Anchor = AnchorTopStretch;
            separator.BorderStyle = BorderStyle.Fixed3D;
            header.Controls.Add(separator);

            return header;
        }

        /// <summary>
        /// Sitios, boton de inicio, estado y pestanas, con un divisor en cada
        /// borde. El boton de inicio va montado sobre el limite entre sitios y
        /// estado: los divisores de encima y de debajo mueven ese mismo limite,
        /// asi que se puede tirar del borde inferior de la tabla de sitios o
        /// del superior del estado. Las medidas de fabrica reproducen la
        /// ventana de siempre al pixel.
        /// </summary>
        private Panel BuildBody()
        {
            Panel body = new Panel();
            body.Size = new Size(InitialClientWidth, 760 - HeaderHeight - BottomResizeBand);
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(20, 6, 20, 0);

            BuildSitesGroup();
            BuildStartButton();
            BuildStatusGroup();
            BuildTabs();

            PaneGrip belowSites = MakeGrip(10);
            PaneGrip aboveStatus = MakeGrip(9);
            PaneGrip belowStatus = MakeGrip(8);

            AddStacked(body, _sitesGroup, belowSites, _startButton, aboveStatus, _statusGroup, belowStatus, _tabs);

            _mainPanes = new PaneStack(body, new Control[] { _sitesGroup, _statusGroup, _tabs },
                                       new int[] { SitesMinHeight, StatusMinHeight, TabsMinHeight });
            _mainPanes.Attach(belowSites, 0);
            _mainPanes.Attach(aboveStatus, 0);
            _mainPanes.Attach(belowStatus, 1);
            // Los de lo alto de cada pestana mueven el mismo limite que el de
            // encima de las pestanas: son el borde superior del registro y de
            // la tabla de clientes.
            _mainPanes.Attach(_logGrip, 1);
            _mainPanes.Attach(_clientsTopGrip, 1);
            _mainPanes.Committed += OnPanesCommitted;

            AttachEdges(_sitesList, null, belowSites);
            AttachEdges(_statusTextBox, aboveStatus, belowStatus);
            AttachEdges(_logTextBox, _logGrip, null);

            return body;
        }

        private void BuildSitesGroup()
        {
            _sitesGroup = new GroupBox();
            _sitesGroup.Size = new Size(1024, 174);
            _sitesGroup.Dock = DockStyle.Top;
            _sitesGroup.Text = "Sitios";

            // Casillas en la propia tabla: activar o desactivar un proyecto es
            // un clic, sin abrir nada. Crece con el grupo; los botones bajan.
            _sitesList = new ListView();
            _sitesList.Location = new Point(15, 22);
            _sitesList.Size = new Size(994, 106);
            _sitesList.Anchor = AnchorFill;
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
            _sitesList.ItemCheck += OnSiteItemCheck;
            _sitesList.MouseDown += OnSiteListMouseDown;
            _sitesList.DoubleClick += OnEditSiteClick;
            _sitesList.ContextMenuStrip = BuildSitesMenu();

            // Marcan que hubo interaccion real, para distinguir un clic del
            // usuario de un evento que emite el propio control al recrearse.
            _sitesList.MouseDown += OnSiteListInput;
            _sitesList.MouseUp += OnSiteListInput;
            _sitesList.KeyDown += OnSiteListInput;
            _sitesList.KeyUp += OnSiteListInput;
            _sitesGroup.Controls.Add(_sitesList);

            _addSiteButton = MakeSiteButton("Anadir sitio", 15, 136, OnAddSiteClick);
            _editSiteButton = MakeSiteButton("Editar", 131, 136, OnEditSiteClick);
            _removeSiteButton = MakeSiteButton("Quitar", 247, 136, OnRemoveSiteClick);
            _sitesGroup.Controls.Add(_addSiteButton);
            _sitesGroup.Controls.Add(_editSiteButton);
            _sitesGroup.Controls.Add(_removeSiteButton);

            _sitesHint = new Label();
            _sitesHint.Location = new Point(373, 141);
            _sitesHint.Size = new Size(636, 18);
            _sitesHint.Anchor = AnchorBottomStretch;
            _sitesHint.ForeColor = Color.DimGray;
            _sitesHint.Font = new Font("Segoe UI", 8f);
            _sitesHint.Text = SitesHintIdle;
            _sitesGroup.Controls.Add(_sitesHint);
        }

        private static Button MakeSiteButton(string text, int x, int y, EventHandler handler)
        {
            Button button = new Button();
            button.Location = new Point(x, y);
            button.Size = new Size(110, 26);
            button.Anchor = AnchorBottomLeft;
            button.Text = text;
            button.Click += handler;
            return button;
        }

        private void BuildStartButton()
        {
            _startButton = new Button();
            _startButton.Size = new Size(1024, 38);
            _startButton.Dock = DockStyle.Top;
            _startButton.Text = "Iniciar Servidor";
            _startButton.BackColor = Color.FromArgb(40, 167, 69);
            _startButton.ForeColor = Color.White;
            _startButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _startButton.Click += OnStartClick;
        }

        private void BuildStatusGroup()
        {
            _statusGroup = new GroupBox();
            _statusGroup.Size = new Size(1024, 122);
            _statusGroup.Dock = DockStyle.Top;
            _statusGroup.Text = "Estado y Direcciones de Acceso";

            _statusTextBox = new TextBox();
            _statusTextBox.Location = new Point(15, 22);
            // 90 px caben la cabecera y cinco sitios sin desplazarse; con mas,
            // se agranda tirando del borde. La barra horizontal se quita: robaba
            // una linea entera y los nombres largos ya se recortan al componer
            // el texto.
            _statusTextBox.Size = new Size(834, 90);
            _statusTextBox.Anchor = AnchorFill;
            _statusTextBox.Multiline = true;
            _statusTextBox.ReadOnly = true;
            // Sin esto, el formato de una linea por sitio se parte y se pierde
            // la alineacion en columnas: WordWrap viene activado por defecto.
            _statusTextBox.WordWrap = false;
            _statusTextBox.ScrollBars = ScrollBars.Vertical;
            _statusTextBox.Font = new Font("Consolas", 8.5f);
            _statusTextBox.Text = "Estado: Detenido";
            _statusGroup.Controls.Add(_statusTextBox);

            _qrButton = new Button();
            _qrButton.Location = new Point(859, 30);
            _qrButton.Size = new Size(150, 30);
            _qrButton.Anchor = AnchorTopRight;
            _qrButton.Text = "Codigo QR";
            _qrButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _qrButton.Enabled = false;
            _qrButton.Click += OnQrClick;
            _statusGroup.Controls.Add(_qrButton);

            _copyLanButton = new Button();
            _copyLanButton.Location = new Point(859, 70);
            _copyLanButton.Size = new Size(150, 30);
            _copyLanButton.Anchor = AnchorTopRight;
            _copyLanButton.Text = "Copiar URL LAN";
            _copyLanButton.Enabled = false;
            _copyLanButton.Click += OnCopyLanClick;
            _statusGroup.Controls.Add(_copyLanButton);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Size = new Size(1024, 329);
            _tabs.Dock = DockStyle.Fill;

            // Sin margen abajo: el borde inferior del registro queda pegado al de
            // la ventana, que es el que lo estira.
            _logTab = new TabPage("Registro de Telemetria");
            _logTab.UseVisualStyleBackColor = true;
            _logTab.Padding = new Padding(6, 6, 6, 0);

            _logTextBox = new TextBox();
            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Vertical;
            _logTextBox.BackColor = Color.FromArgb(30, 30, 30);
            _logTextBox.ForeColor = Color.FromArgb(220, 220, 220);
            _logTextBox.Font = new Font("Consolas", 8.5f);

            // Borde superior del registro: lo engancha BuildBody a la pila
            // principal, porque mover este borde es mover las pestanas enteras.
            _logGrip = MakeGrip(6);

            AddStacked(_logTab, BuildLogToolbar(), _logGrip, _logTextBox);
            _tabs.TabPages.Add(_logTab);

            _clientsTab = new TabPage("Clientes conectados");
            _clientsTab.UseVisualStyleBackColor = true;
            _clientsTab.Padding = new Padding(6, 6, 6, 0);
            BuildClientsPanel(_clientsTab);
            _tabs.TabPages.Add(_clientsTab);
        }

        private Panel BuildLogToolbar()
        {
            // Con el ancho aproximado de la pestana antes de meter nada: el
            // contador de la derecha se estira con el y debe conservar su margen.
            Panel bar = new Panel();
            bar.Size = new Size(1004, 32);
            bar.Dock = DockStyle.Top;

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

            _statsLabel = new Label();
            _statsLabel.Location = new Point(400, 8);
            _statsLabel.Size = new Size(600, 18);
            _statsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _statsLabel.AutoEllipsis = true;
            _statsLabel.ForeColor = Color.DimGray;
            _statsLabel.Font = new Font("Segoe UI", 8.5f);
            bar.Controls.Add(_statsLabel);

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

        /// <summary>
        /// Tabla de clientes encima y detalle debajo, con la barra de botones
        /// montada sobre el limite entre ambas: los divisores de encima y de
        /// debajo de la barra mueven ese limite, asi que el detalle se agranda
        /// tirando de su propio borde superior.
        /// </summary>
        private void BuildClientsPanel(TabPage page)
        {
            _clientsList = new ListView();
            _clientsList.Dock = DockStyle.Top;
            _clientsList.Height = ClientListDefaultHeight;
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

            // Con el ancho aproximado de la pestana antes de meter nada: la
            // etiqueta de la derecha se estira con el y debe conservar su margen.
            Panel buttons = new Panel();
            buttons.Size = new Size(1000, 32);
            buttons.Dock = DockStyle.Top;

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

            // El de arriba lo engancha BuildBody a la pila principal.
            _clientsTopGrip = MakeGrip(6);
            PaneGrip belowList = MakeGrip(6);
            PaneGrip aboveDetail = MakeGrip(6);

            AddStacked(page, _clientsTopGrip, _clientsList, belowList, buttons, aboveDetail, _detailTextBox);

            _clientPanes = new PaneStack(page, new Control[] { _clientsList, _detailTextBox },
                                         new int[] { ClientListMinHeight, ClientDetailMinHeight });
            _clientPanes.Attach(belowList, 0);
            _clientPanes.Attach(aboveDetail, 0);
            _clientPanes.Committed += OnPanesCommitted;

            AttachEdges(_clientsList, null, belowList);
            AttachEdges(_detailTextBox, aboveDetail, null);
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
