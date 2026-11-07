using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using LanShare.Core;

namespace LanShare.Ui
{
    /// <summary>
    /// Opciones de comportamiento, fuera de la ventana principal.
    ///
    /// Cada una lleva su explicacion al lado porque son decisiones con un
    /// compromiso real detras, no interruptores obvios: quien las toca seis
    /// meses despues no tiene por que acordarse de que hacia cada una. Van en
    /// pestanas porque, apiladas, ya no cabian en la pantalla de un portatil.
    /// </summary>
    public sealed class SettingsDialog : Form
    {
        private const int Inset = 16;
        private const int OptionGap = 16;
        private const int DialogWidth = 690;
        private const int TabsHeight = 470;

        private readonly CheckBox _listing;
        private readonly CheckBox _compression;
        private readonly CheckBox _distribution;
        private readonly CheckBox _cors;
        private readonly CheckBox _liveReload;
        private readonly CheckBox _remoteConsole;
        private readonly CheckBox _isolation;
        private readonly CheckBox _upload;
        private readonly TextBox _uploadFolder;
        private readonly NumericUpDown _uploadMax;
        private readonly CheckBox _probe;
        private readonly CheckBox _portScan;
        private readonly CheckBox _deepName;
        private readonly CheckBox _autoStart;
        private readonly CheckBox _minimizeToTray;
        private readonly HttpsSettingsPage _httpsPage;

        private readonly bool _autoStartInitial;
        private int _optionWidth;

        public bool AutoStartRequested { get { return _autoStart.Checked; } }
        public bool AutoStartChanged { get { return _autoStart.Checked != _autoStartInitial; } }

        /// <summary>Si en la pestana HTTPS se borraron los certificados (ocurre al momento, no al Aceptar).</summary>
        public bool HttpsRemoved { get { return _httpsPage.RemovedEverything; } }

        public SettingsDialog(AppSettings current, bool autoStartEnabled, bool isAdmin, bool serverRunning)
        {
            if (current == null) throw new ArgumentNullException("current");
            _autoStartInitial = autoStartEnabled;

            Text = "Configuracion";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            int y = Inset;
            if (serverRunning)
            {
                Label aviso = new Label();
                aviso.Location = new Point(Inset, y);
                aviso.MaximumSize = new Size(DialogWidth - Inset * 2, 0);
                aviso.AutoSize = true;
                aviso.ForeColor = Color.FromArgb(150, 110, 20);
                aviso.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
                aviso.Text = "El servidor esta en marcha. Los cambios se aplican de inmediato, sin reiniciarlo.";
                Controls.Add(aviso);
                y += aviso.Height + 8;
            }

            TabControl tabs = new TabControl();
            tabs.Location = new Point(Inset, y);
            tabs.Size = new Size(DialogWidth - Inset * 2, TabsHeight);
            Controls.Add(tabs);

            // Ancho util dentro de cada pestana, descontando margenes y una
            // posible barra de desplazamiento vertical.
            _optionWidth = tabs.Width - Inset * 2 - 26;

            // ------------------------------------------------------ Servidor
            TabPage server = NewPage(tabs, "Servidor");
            int py = Inset;
            _listing = AddOption(server, ref py, current.DirectoryListing,
                "Listar carpetas sin archivo de inicio",
                "Cuando el navegador pide una carpeta en lugar de un archivo (por ejemplo /fotos/), el " +
                "servidor busca dentro el archivo de inicio del sitio (index.html si no indicaste otro). " +
                "Esta opcion decide que pasa cuando no lo encuentra: marcada, se muestra una tabla " +
                "navegable con su contenido; desmarcada, se responde 404.\r\n" +
                "Desmarcala si sirves en red local una carpeta con material que no quieres que sea " +
                "descubrible: sin listado hay que acertar el nombre exacto del archivo.");
            _compression = AddOption(server, ref py, current.Compression,
                "Comprimir texto con gzip",
                "Reduce entre un 60% y un 80% el tamano de HTML, CSS, JavaScript, JSON y SVG antes de " +
                "enviarlos. Sobre Wi-Fi la diferencia se nota. El resultado se guarda en memoria, asi que " +
                "cada archivo se comprime una sola vez hasta que lo modificas.\r\n" +
                "Solo se aplica si el cliente declara que lo admite, nunca sobre imagenes, video o fuentes " +
                "(ya vienen comprimidos) y nunca sobre descargas parciales.");
            _distribution = AddOption(server, ref py, current.DistributionMode,
                "Modo distribucion (cache con ETag y 304)",
                "Desmarcada (desarrollo): se prohibe cachear. Editas un archivo, recargas y ves el cambio " +
                "siempre, sin Ctrl+F5. A cambio cada recarga vuelve a descargarlo todo.\r\n" +
                "Marcada (distribucion): el navegador guarda los archivos pero pregunta antes de usarlos. " +
                "Si nada cambio recibe una respuesta de unos 200 bytes en vez del archivo entero.\r\n" +
                "Aviso: la version se calcula con el tamano y la fecha al segundo. Si modificas un archivo " +
                "dentro del mismo segundo y conserva el tamano exacto, el cliente seguira con su copia vieja.");
            _cors = AddOption(server, ref py, current.Cors,
                "CORS abierto (Access-Control-Allow-Origin: *)",
                "Permite que una pagina servida desde otro origen (otro puerto, otro dominio, un archivo " +
                "local) lea estos archivos por fetch. Marcala si tu aplicacion pide datos a este servidor " +
                "desde otra direccion; si todo se sirve desde aqui, no hace falta.\r\n" +
                "En red local tiene un coste: cualquier web que visites mientras el servidor corre podria " +
                "leer su contenido desde tu propio navegador.");

            // ---------------------------------------------------- Desarrollo
            TabPage dev = NewPage(tabs, "Desarrollo");
            py = Inset;
            _liveReload = AddOption(dev, ref py, current.LiveReload,
                "Recargar las paginas al guardar un archivo",
                "Vigila la carpeta de cada sitio y, al guardar un archivo, recarga las paginas abiertas en " +
                "todos los dispositivos conectados a la vez: el PC, el movil, la tablet.\r\n" +
                "Si solo cambio una hoja de estilos, se reemplaza en caliente sin recargar, asi que no " +
                "pierdes el estado de la pagina ni la posicion de desplazamiento.\r\n" +
                "Funciona con un script pequeno que se anade a las paginas HTML al servirlas (tus archivos " +
                "no se tocan) y una conexion abierta por pestana en /__events. Se ignoran .git y " +
                "node_modules.\r\n" +
                "Desmarcala si tu pagina guarda estado que no quieres perder al recargar, o si algun " +
                "proceso escribe en la carpeta del sitio continuamente.");
            _remoteConsole = AddOption(dev, ref py, current.RemoteConsole,
                "Consola remota: ver aqui los errores de JavaScript de los moviles",
                "Muestra en el registro lo que las paginas escriben en la consola del navegador de los demas " +
                "dispositivos: errores sin capturar, promesas rechazadas, imagenes o scripts que no cargan y " +
                "las llamadas a console.log, warn y error, con el archivo y la linea. Es la forma de saber " +
                "por que una pagina falla en el movil, donde no hay herramientas de desarrollo.\r\n" +
                "Aparecen como JS ERROR, JS WARN o JS LOG en el filtro de cada sitio. Funciona con un script " +
                "que se anade al principio de las paginas HTML al servirlas; tus archivos no se tocan.\r\n" +
                "No se anade en las paginas que abres en este PC, donde ya tienes la consola del navegador. " +
                "Al activarla, recarga en el movil las paginas que ya estuvieran abiertas.");
            _isolation = AddOption(dev, ref py, current.CrossOriginIsolation,
                "Aislar las paginas de otros origenes (COOP y COEP)",
                "Lo exigen SharedArrayBuffer y WebAssembly con hilos, junto con una conexion segura: localhost " +
                "en este PC, la direccion HTTPS del sitio en el movil. A cambio, la pagina no puede cargar " +
                "imagenes, scripts ni iframes de otros dominios que no lo autoricen.");

            // -------------------------------------------------------- Subida
            TabPage upload = NewPage(tabs, "Subida");
            py = Inset;
            _upload = AddOption(upload, ref py, current.UploadEnabled,
                "Permitir subir archivos desde otros dispositivos",
                "Anade a cada sitio una pagina en /__subir desde la que el movil puede enviar archivos al " +
                "PC: fotos, capturas, documentos, a su calidad original.\r\n" +
                "Se guardan en la carpeta de abajo, nunca en la carpeta del sitio, y nunca sobrescriben: si " +
                "ya existe foto.jpg, la nueva se guarda como foto (2).jpg.\r\n" +
                "Es la unica funcion que escribe en tu disco desde la red: mientras este activa, cualquiera " +
                "conectado a tu Wi-Fi podria dejarte archivos ahi. Activala cuando la vayas a usar.");

            Label folderLabel = MakeLabel(upload, "Carpeta de destino:", Inset + 20, py);
            py += folderLabel.Height + 4;

            _uploadFolder = new TextBox();
            _uploadFolder.Location = new Point(Inset + 20, py);
            _uploadFolder.Size = new Size(_optionWidth - 20 - 190, 23);
            _uploadFolder.Text = current.GetUploadFolder();
            upload.Controls.Add(_uploadFolder);

            Button browse = new Button();
            browse.Location = new Point(_uploadFolder.Right + 8, py - 1);
            browse.Size = new Size(96, 25);
            browse.Text = "Examinar...";
            browse.Click += OnBrowseUploadFolder;
            upload.Controls.Add(browse);

            Button open = new Button();
            open.Location = new Point(browse.Right + 6, py - 1);
            open.Size = new Size(80, 25);
            open.Text = "Abrir";
            open.Click += OnOpenUploadFolder;
            upload.Controls.Add(open);
            py += 34;

            Label maxLabel = MakeLabel(upload, "Tamano maximo por archivo (MB):", Inset + 20, py + 3);
            _uploadMax = new NumericUpDown();
            _uploadMax.Location = new Point(Inset + 20 + maxLabel.Width + 8, py);
            _uploadMax.Size = new Size(90, 23);
            _uploadMax.Minimum = AppSettings.MinUploadMegabytes;
            _uploadMax.Maximum = AppSettings.MaxUploadMegabytes;
            _uploadMax.Value = Clamp(current.UploadMaxMegabytes, AppSettings.MinUploadMegabytes,
                                     AppSettings.MaxUploadMegabytes);
            upload.Controls.Add(_uploadMax);

            // ------------------------------------------------------ Clientes
            TabPage clients = NewPage(tabs, "Clientes");
            py = Inset;
            _probe = AddOption(clients, ref py, current.InjectProbe,
                "Medir el dispositivo con un script en las paginas HTML",
                "Anade a cada pagina HTML un script que informa de la GPU, la pantalla real, los nucleos y " +
                "la zona horaria del dispositivo. Es lo que permite distinguir un iPhone de un Android " +
                "cuando el User-Agent miente.\r\n" +
                "No modifica tus archivos: se anade al servirlos. Desmarcala si estas depurando el HTML " +
                "exacto que entrega el servidor o si tu pagina tiene una politica CSP estricta.");
            _portScan = AddOption(clients, ref py, current.PortScan,
                "Escanear puertos de los dispositivos conectados",
                "Abre conexiones TCP a 21 puertos de cada dispositivo que se conecta (SMB, RDP, AirPlay, " +
                "ADB, impresoras...) para reconocer su sistema. Es la senal mas dificil de falsear.\r\n" +
                "En tu red es tu decision. En una red ajena o de empresa, un escaneo de puertos suele " +
                "disparar las alertas de deteccion de intrusos: desmarcala ahi.");
            _deepName = AddOption(clients, ref py, current.DeepName,
                "Preguntar el nombre del equipo (mDNS y NetBIOS)",
                "Pregunta a cada dispositivo como se llama, con los mismos mecanismos que usa el explorador " +
                "de Windows. Envia unos pocos paquetes UDP a su direccion; no abre conexiones.");

            // -------------------------------------------------------- Inicio
            TabPage startup = NewPage(tabs, "Inicio");
            py = Inset;
            _autoStart = AddOption(startup, ref py, autoStartEnabled,
                "Iniciar con Windows",
                "Al entrar en Windows, LanShare arranca en la bandeja y sirve los sitios marcados, sin abrir " +
                "el navegador.\r\n" +
                (isAdmin
                    ? "Ahora eres administrador: se registra como tarea programada con privilegios elevados, " +
                      "asi que los sitios en red local arrancan sin pedirte permiso."
                    : "Ahora NO eres administrador: se registrara sin privilegios y los sitios en red local no " +
                      "podran arrancar solos. Abre LanShare como administrador para registrarlo elevado."));
            _minimizeToTray = AddOption(startup, ref py, current.MinimizeToTray,
                "Al minimizar, ocultar en la bandeja",
                "La ventana desaparece de la barra de tareas y LanShare sigue sirviendo desde el icono de la " +
                "bandeja. Doble clic en el icono para volver. Cerrar la ventana sigue cerrando la aplicacion.");

            // --------------------------------------------------------- HTTPS
            TabPage https = NewPage(tabs, "HTTPS");
            _httpsPage = new HttpsSettingsPage(https, this, _optionWidth, isAdmin, serverRunning);

            // ------------------------------------------------------- Botones
            y = tabs.Bottom + 12;

            Button ok = new Button();
            ok.Size = new Size(86, 28);
            ok.Location = new Point(DialogWidth - Inset - 86 * 2 - 8, y);
            ok.Text = "Aceptar";
            ok.Click += OnAcceptClick;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Size = new Size(86, 28);
            cancel.Location = new Point(DialogWidth - Inset - 86, y);
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(DialogWidth, y + 28 + Inset);
        }

        /// <summary>Vuelca lo elegido sobre la configuracion. Solo tras Aceptar.</summary>
        public void ApplyTo(AppSettings target)
        {
            target.DirectoryListing = _listing.Checked;
            target.Compression = _compression.Checked;
            target.DistributionMode = _distribution.Checked;
            target.Cors = _cors.Checked;
            target.LiveReload = _liveReload.Checked;
            target.RemoteConsole = _remoteConsole.Checked;
            target.CrossOriginIsolation = _isolation.Checked;
            target.UploadEnabled = _upload.Checked;
            target.UploadMaxMegabytes = (int)_uploadMax.Value;
            target.InjectProbe = _probe.Checked;
            target.PortScan = _portScan.Checked;
            target.DeepName = _deepName.Checked;
            target.MinimizeToTray = _minimizeToTray.Checked;

            // La carpeta por defecto no se fija en el JSON: si se deja tal cual,
            // sigue el perfil del usuario aunque cambie.
            string folder = _uploadFolder.Text.Trim().Trim('"');
            target.UploadFolder = string.Equals(folder, AppSettings.DefaultUploadFolder(),
                                                StringComparison.OrdinalIgnoreCase) ? "" : folder;
        }

        /// <summary>
        /// Una carpeta de subida invalida se rechaza aqui y no al primer envio,
        /// cuando el movil ya esta esperando respuesta.
        /// </summary>
        private void OnAcceptClick(object sender, EventArgs e)
        {
            string folder = _uploadFolder.Text.Trim().Trim('"');
            if (_upload.Checked)
            {
                string problem = null;
                if (folder.Length == 0) problem = "Indica una carpeta de destino para las subidas.";
                else if (!IsFullPath(folder)) problem = "La carpeta de destino debe ser una ruta completa y valida.";

                if (problem != null)
                {
                    MessageBox.Show(this, problem, "Carpeta de destino", MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// IsPathRooted lanza ArgumentException si la ruta lleva comillas,
        /// &lt; &gt; | o caracteres de control: sin esto, una ruta mal pegada
        /// tumbaba el dialogo con el aviso de error no controlado.
        /// </summary>
        private static bool IsFullPath(string path)
        {
            try { return Path.IsPathRooted(path) && Path.GetFullPath(path).Length > 0; }
            catch (Exception) { return false; }
        }

        private void OnBrowseUploadFolder(object sender, EventArgs e)
        {
            ModernFolderPicker picker = new ModernFolderPicker();
            picker.InitialFolder = _uploadFolder.Text;
            if (picker.ShowDialog(Handle)) _uploadFolder.Text = picker.SelectedPath;
        }

        private void OnOpenUploadFolder(object sender, EventArgs e)
        {
            string folder = _uploadFolder.Text.Trim().Trim('"');
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Process.Start(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir la carpeta: " + ex.Message, "Carpeta de destino",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static TabPage NewPage(TabControl tabs, string title)
        {
            TabPage page = new TabPage(title);
            page.AutoScroll = true;
            page.UseVisualStyleBackColor = true;
            tabs.TabPages.Add(page);
            return page;
        }

        private static Label MakeLabel(Control parent, string text, int x, int y)
        {
            Label label = new Label();
            label.Location = new Point(x, y);
            label.AutoSize = true;
            label.Text = text;
            parent.Controls.Add(label);
            return label;
        }

        private static decimal Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Coloca casilla y explicacion, y avanza el cursor vertical. La altura
        /// del parrafo la calcula la propia etiqueta al ajustar el texto, asi
        /// que cambiar una explicacion no obliga a recolocar nada a mano.
        /// </summary>
        private CheckBox AddOption(Control parent, ref int y, bool value, string title, string description)
        {
            if (y > Inset)
            {
                Label divisor = new Label();
                divisor.Location = new Point(Inset, y);
                divisor.Size = new Size(_optionWidth, 1);
                divisor.BorderStyle = BorderStyle.Fixed3D;
                parent.Controls.Add(divisor);
                y += OptionGap;
            }

            CheckBox box = new CheckBox();
            box.Location = new Point(Inset, y);
            box.AutoSize = true;
            box.Checked = value;
            box.Text = title;
            box.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            parent.Controls.Add(box);
            y += box.Height + 4;

            Label text = new Label();
            text.Location = new Point(Inset + 20, y);
            text.MaximumSize = new Size(_optionWidth - 20, 0);
            text.AutoSize = true;
            text.ForeColor = Color.FromArgb(85, 95, 110);
            text.Font = new Font("Segoe UI", 8.5f);
            text.Text = description;
            parent.Controls.Add(text);
            y += text.Height + OptionGap;

            return box;
        }
    }
}
