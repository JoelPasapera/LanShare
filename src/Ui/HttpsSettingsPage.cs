using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LanShare.Https;

namespace LanShare.Ui
{
    /// <summary>
    /// Pestana HTTPS de Configuracion: el estado de la autoridad local, su
    /// huella para compararla con la del movil, la confianza en este PC y el
    /// borrado de todo.
    ///
    /// A diferencia del resto de opciones, estas acciones actuan al momento
    /// (son cambios en Windows, no preferencias): no esperan a Aceptar.
    /// </summary>
    internal sealed class HttpsSettingsPage
    {
        private const int Inset = 16;

        /// <summary>Dos renglones de 16 pares: la huella SHA-256 entera, sin partir ningun par.</summary>
        private const int FingerprintPairsPerLine = 16;

        private readonly IWin32Window _owner;
        private readonly bool _isAdmin;
        private readonly bool _serverRunning;

        private readonly Label _authorityStatus;
        private readonly TextBox _fingerprint;
        private readonly Button _copyFingerprint;
        private readonly Label _trustStatus;
        private readonly Button _trustButton;
        private readonly Button _removeButton;

        private LocalCertificateAuthority _authority;

        /// <summary>Si se borro todo desde aqui: el siguiente arranque crea una autoridad nueva.</summary>
        public bool RemovedEverything { get; private set; }

        public HttpsSettingsPage(TabPage page, IWin32Window owner, int width, bool isAdmin, bool serverRunning)
        {
            if (page == null) throw new ArgumentNullException("page");
            _owner = owner;
            _isAdmin = isAdmin;
            _serverRunning = serverRunning;

            int y = Inset;

            Label title = new Label();
            title.Location = new Point(Inset, y);
            title.AutoSize = true;
            title.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            title.Text = "HTTPS en la red local";
            page.Controls.Add(title);
            y += title.Height + 6;

            y = AddParagraph(page, y, width,
                "Activa HTTPS en cada sitio desde Editar sitio. LanShare crea una autoridad de certificacion propia " +
                "de este equipo y con ella firma el certificado del servidor. Instala la autoridad una vez en cada " +
                "movil (clic derecho en un sitio con HTTPS, codigo QR del certificado) y el navegador tratara tus " +
                "paginas como seguras: WebGPU, service workers, camara. Para SharedArrayBuffer y WebAssembly " +
                "con hilos, activa ademas el aislamiento de la pestana Desarrollo.\r\n" +
                "La autoridad solo puede avalar direcciones de la red local y nombres .local: aunque alguien robara " +
                "su clave, no serviria para suplantar webs de Internet. Y no se puede robar facilmente: no es " +
                "exportable y vive en el almacen de claves de tu usuario de Windows.");
            y += 10;

            _authorityStatus = new Label();
            _authorityStatus.Location = new Point(Inset, y);
            _authorityStatus.MaximumSize = new Size(width, 0);
            _authorityStatus.AutoSize = true;
            _authorityStatus.Font = new Font("Segoe UI", 9f);
            page.Controls.Add(_authorityStatus);
            y += 24;

            Label fingerprintLabel = new Label();
            fingerprintLabel.Location = new Point(Inset, y);
            fingerprintLabel.AutoSize = true;
            fingerprintLabel.Text = "Huella SHA-256 (tiene que coincidir con la que muestra el movil):";
            page.Controls.Add(fingerprintLabel);
            y += 20;

            _fingerprint = new TextBox();
            _fingerprint.Location = new Point(Inset, y);
            _fingerprint.Size = new Size(width - 96, 36);
            _fingerprint.Multiline = true;
            _fingerprint.ReadOnly = true;
            _fingerprint.Font = new Font("Consolas", 8.5f);
            page.Controls.Add(_fingerprint);

            _copyFingerprint = new Button();
            _copyFingerprint.Location = new Point(Inset + width - 88, y);
            _copyFingerprint.Size = new Size(88, 25);
            _copyFingerprint.Text = "Copiar";
            _copyFingerprint.Click += OnCopyFingerprintClick;
            page.Controls.Add(_copyFingerprint);
            y += 48;

            _trustStatus = new Label();
            _trustStatus.Location = new Point(Inset, y + 5);
            _trustStatus.Size = new Size(width - 190, 36);
            page.Controls.Add(_trustStatus);

            _trustButton = new Button();
            _trustButton.Location = new Point(Inset + width - 180, y);
            _trustButton.Size = new Size(180, 28);
            _trustButton.Click += OnTrustClick;
            page.Controls.Add(_trustButton);
            y += 50;

            Label divisor = new Label();
            divisor.Location = new Point(Inset, y);
            divisor.Size = new Size(width, 1);
            divisor.BorderStyle = BorderStyle.Fixed3D;
            page.Controls.Add(divisor);
            y += 14;

            _removeButton = new Button();
            _removeButton.Location = new Point(Inset, y);
            _removeButton.Size = new Size(240, 28);
            _removeButton.Text = "Borrar los certificados de LanShare...";
            _removeButton.Click += OnRemoveClick;
            page.Controls.Add(_removeButton);
            y += 34;

            AddParagraph(page, y, width,
                serverRunning
                    ? "Deten el servidor para poder borrarlos."
                    : "Quita de Windows la autoridad, el certificado del servidor y sus asociaciones en http.sys. " +
                      "Al volver a usar HTTPS se crea una autoridad nueva, que habra que instalar de nuevo en los " +
                      "moviles (borra alli la vieja).");

            Refresh();
        }

        private static int AddParagraph(Control parent, int y, int width, string text)
        {
            Label label = new Label();
            label.Location = new Point(Inset, y);
            label.MaximumSize = new Size(width, 0);
            label.AutoSize = true;
            label.ForeColor = Color.FromArgb(85, 95, 110);
            label.Font = new Font("Segoe UI", 8.5f);
            label.Text = text;
            parent.Controls.Add(label);
            return y + label.PreferredHeight;
        }

        /// <summary>Vuelve a leer el estado de Windows y lo pinta.</summary>
        private void Refresh()
        {
            string loadError = null;
            try
            {
                _authority = LocalCertificateAuthority.Load();
            }
            catch (Exception ex)
            {
                _authority = null;
                loadError = ex.Message;
            }

            bool exists = _authority != null;
            if (exists)
            {
                AuthorityDates dates = new AuthorityDates(_authority);
                _authorityStatus.Text = "Autoridad local creada el " + dates.Created + ", valida hasta el " +
                                        dates.Expires + ".";
                _fingerprint.Text = CertificateFingerprint.InLines(_authority.Fingerprint, FingerprintPairsPerLine);
            }
            else
            {
                _authorityStatus.Text = loadError != null
                    ? "No se pudo leer la autoridad local: " + loadError
                    : "Todavia no hay autoridad local: se crea al iniciar el primer sitio con HTTPS.";
                _fingerprint.Text = "";
            }

            _copyFingerprint.Enabled = exists;

            bool trusted = false;
            if (exists)
            {
                try { trusted = _authority.IsTrustedHere(); }
                catch (Exception) { }
            }

            _trustButton.Enabled = exists;
            _trustButton.Text = trusted ? "Dejar de confiar en este PC" : "Confiar en este PC";
            _trustStatus.Text = !exists
                ? ""
                : trusted
                    ? "Este PC confia en ella: Chrome y Edge abren https://localhost sin avisos."
                    : "Este PC no confia en ella: https://localhost mostraria un aviso. No hace falta para el movil.";

            _removeButton.Enabled = !_serverRunning && (exists || _isAdmin);
        }

        /// <summary>Se copia en una sola linea, como la muestran Windows y los moviles.</summary>
        private void OnCopyFingerprintClick(object sender, EventArgs e)
        {
            if (_authority == null) return;
            try { Clipboard.SetText(_authority.Fingerprint); }
            catch (Exception ex) { Warn("No se pudo acceder al portapapeles: " + ex.Message); }
        }

        /// <summary>
        /// Windows muestra su propio aviso de seguridad al anadir o quitar una
        /// raiz de confianza: la decision final es del usuario, en ese aviso.
        /// </summary>
        private void OnTrustClick(object sender, EventArgs e)
        {
            if (_authority == null) return;

            bool trusted;
            try { trusted = _authority.IsTrustedHere(); }
            catch (Exception) { trusted = false; }

            try
            {
                if (trusted) _authority.UntrustHere();
                else _authority.TrustHere();
            }
            catch (Exception ex)
            {
                Warn("Windows no permitio el cambio: " + ex.Message);
            }
            Refresh();
        }

        private void OnRemoveClick(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(_owner,
                "Se borraran de Windows la autoridad local de LanShare, el certificado del servidor y sus " +
                "asociaciones en http.sys.\r\n\r\nLos moviles que la tuvieran instalada dejaran de confiar en " +
                "LanShare hasta que instales la nueva. Continuar?",
                "Borrar certificados", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            try
            {
                List<string> pending = HttpsProvisioner.RemoveEverything(_isAdmin);
                RemovedEverything = true;
                if (pending.Count > 0)
                {
                    Warn("Se borro lo posible, pero queda pendiente:\r\n\r\n- " + string.Join("\r\n- ", pending.ToArray()));
                }
            }
            catch (Exception ex)
            {
                Warn("No se pudo borrar todo: " + ex.Message);
            }
            Refresh();
        }

        private void Warn(string message)
        {
            MessageBox.Show(_owner, message, "HTTPS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Fechas de la autoridad, escritas como las lee el usuario (dia/mes/ano).</summary>
        private struct AuthorityDates
        {
            public readonly string Created;
            public readonly string Expires;

            public AuthorityDates(LocalCertificateAuthority authority)
            {
                // El inicio de validez lleva un dia de margen hacia atras.
                Created = authority.Certificate.NotBefore.AddDays(LeafPolicy.BackdateDays)
                    .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                Expires = authority.Certificate.NotAfter.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
        }
    }
}
