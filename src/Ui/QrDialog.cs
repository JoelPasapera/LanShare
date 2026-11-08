using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using LanShare.Core;

namespace LanShare.Ui
{
    /// <summary>
    /// Muestra la URL de acceso como codigo QR para abrirla desde el telefono
    /// sin teclear una direccion IP.
    /// </summary>
    public sealed class QrDialog : Form
    {
        private const int PreviewPixels = 340;
        private const int ExportPixels = 1024;

        private readonly List<UrlChoice> _urls;
        private readonly ComboBox _selector;
        private readonly PictureBox _preview;
        private readonly Label _urlLabel;
        private readonly NetworkNotice _notice;

        private Label _noticeLabel;
        private Button _noticeButton;
        private Bitmap _current;

        public QrDialog(List<UrlChoice> urls)
            : this(urls, null)
        {
        }

        /// <param name="notice">Aviso sobre la red con su arreglo (red publica, por ejemplo); null si no hay.</param>
        public QrDialog(List<UrlChoice> urls, NetworkNotice notice)
        {
            if (urls == null || urls.Count == 0) throw new ArgumentException("urls");
            _urls = urls;
            _notice = notice;

            Text = "Acceso desde el movil";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;

            int width = 420;
            int y = 16;

            // El aviso va arriba del todo: si la red no deja entrar al movil, da
            // igual lo bien que se lea el codigo.
            if (notice != null) y = AddNotice(notice, width, y);

            // El selector solo aparece si hay donde elegir (varias direcciones o
            // varios sitios): con una sola URL seria una lista de un elemento.
            if (urls.Count > 1)
            {
                Label hint = new Label();
                hint.Location = new Point(16, y);
                hint.Size = new Size(width - 32, 16);
                hint.ForeColor = Color.FromArgb(85, 95, 110);
                hint.Font = new Font("Segoe UI", 8.5f);
                // Van ordenadas: primero la del adaptador que llega al router.
                hint.Text = DistinctHosts(urls) > 1
                    ? "Varias direcciones: la primera suele ser la de tu red."
                    : "Elige el sitio:";
                Controls.Add(hint);
                y += 20;

                _selector = new ComboBox();
                _selector.Location = new Point(16, y);
                _selector.Size = new Size(width - 32, 23);
                _selector.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (UrlChoice choice in urls) _selector.Items.Add(choice);
                _selector.DropDownWidth = WidestCaption(urls, _selector);
                _selector.SelectedIndex = 0;
                _selector.SelectedIndexChanged += OnSelectionChanged;
                Controls.Add(_selector);
                y += 32;
            }

            _preview = new PictureBox();
            _preview.Location = new Point((width - PreviewPixels) / 2, y);
            _preview.Size = new Size(PreviewPixels, PreviewPixels);
            _preview.SizeMode = PictureBoxSizeMode.CenterImage;
            _preview.BackColor = Color.White;
            _preview.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_preview);
            y += PreviewPixels + 12;

            _urlLabel = new Label();
            _urlLabel.Location = new Point(16, y);
            _urlLabel.Size = new Size(width - 32, 20);
            _urlLabel.TextAlign = ContentAlignment.MiddleCenter;
            _urlLabel.Font = new Font("Consolas", 10f, FontStyle.Bold);
            Controls.Add(_urlLabel);
            y += 24;

            Label tip = new Label();
            tip.Location = new Point(16, y);
            tip.Size = new Size(width - 32, 32);
            tip.TextAlign = ContentAlignment.MiddleCenter;
            tip.ForeColor = Color.FromArgb(85, 95, 110);
            tip.Font = new Font("Segoe UI", 8.5f);
            tip.Text = "Apunta la camara del telefono. El telefono debe estar en la misma red Wi-Fi.";
            Controls.Add(tip);
            y += 40;

            Button copy = MakeButton("Copiar URL", 16, y, OnCopyClick);
            Button save = MakeButton("Guardar PNG", 142, y, OnSaveClick);
            Button close = MakeButton("Cerrar", 290, y, null);
            close.DialogResult = DialogResult.Cancel;
            Controls.Add(copy);
            Controls.Add(save);
            Controls.Add(close);

            AcceptButton = close;
            CancelButton = close;
            ClientSize = new Size(width, y + 28 + 16);

            UpdatePreview();
        }

        /// <summary>Franja de aviso con su boton; devuelve donde sigue el dialogo.</summary>
        private int AddNotice(NetworkNotice notice, int width, int y)
        {
            Panel banner = new Panel();
            banner.Location = new Point(16, y);
            banner.Size = new Size(width - 32, 70);
            banner.BackColor = Color.FromArgb(255, 244, 214);
            banner.BorderStyle = BorderStyle.FixedSingle;

            _noticeLabel = new Label();
            _noticeLabel.Location = new Point(8, 6);
            _noticeLabel.Size = new Size(banner.Width - 18, 30);
            _noticeLabel.Font = new Font("Segoe UI", 8.5f);
            _noticeLabel.ForeColor = Color.FromArgb(110, 70, 0);
            _noticeLabel.Text = notice.Text;
            banner.Controls.Add(_noticeLabel);

            _noticeButton = new Button();
            _noticeButton.Size = new Size(140, 25);
            _noticeButton.Location = new Point(banner.Width - 10 - _noticeButton.Width, 38);
            _noticeButton.Text = notice.FixLabel;
            _noticeButton.UseVisualStyleBackColor = true;
            _noticeButton.Click += OnNoticeClick;
            banner.Controls.Add(_noticeButton);

            Controls.Add(banner);
            return y + banner.Height + 10;
        }

        /// <summary>
        /// Aplica el arreglo del aviso y deja el resultado a la vista: si se
        /// pudo, el codigo ya sirve sin cerrar el dialogo.
        /// </summary>
        private void OnNoticeClick(object sender, EventArgs e)
        {
            string error;
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                error = _notice.Fix();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                Cursor.Current = previous;
            }

            if (error == null)
            {
                _noticeLabel.Text = "Listo: la red ya es privada y el movil puede conectar.";
                _noticeLabel.ForeColor = Color.FromArgb(20, 120, 45);
                _noticeButton.Visible = false;
                return;
            }

            _noticeLabel.Text = "No se pudo: " + error;
            _noticeLabel.ForeColor = Color.Firebrick;
        }

        /// <summary>Cuantos equipos distintos aparecen en las URL (no cuantos sitios).</summary>
        private static int DistinctHosts(List<UrlChoice> urls)
        {
            HashSet<string> hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UrlChoice choice in urls)
            {
                Uri uri;
                if (Uri.TryCreate(choice.Url, UriKind.Absolute, out uri)) hosts.Add(uri.Host);
            }
            return hosts.Count;
        }

        /// <summary>
        /// Ancho de la lista desplegada: el nombre del adaptador va al final de
        /// cada etiqueta y, con el ancho del cuadro, era lo primero en cortarse.
        /// </summary>
        private static int WidestCaption(List<UrlChoice> urls, ComboBox selector)
        {
            const int MaxWidth = 900;
            int widest = selector.Width;
            foreach (UrlChoice choice in urls)
            {
                int width = TextRenderer.MeasureText(choice.Caption, selector.Font).Width +
                            SystemInformation.VerticalScrollBarWidth + 8;
                if (width > widest) widest = width;
            }
            return Math.Min(widest, MaxWidth);
        }

        private static Button MakeButton(string text, int x, int y, EventHandler handler)
        {
            Button button = new Button();
            button.Location = new Point(x, y);
            button.Size = new Size(114, 28);
            button.Text = text;
            if (handler != null) button.Click += handler;
            return button;
        }

        private string SelectedUrl
        {
            get
            {
                UrlChoice choice = _selector != null
                    ? (UrlChoice)_selector.SelectedItem
                    : _urls[0];
                return choice.Url;
            }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            string url = SelectedUrl;
            _urlLabel.Text = url;

            Bitmap generated;
            try
            {
                generated = QrRenderer.Render(QrCode.Encode(url), PreviewPixels);
            }
            catch (Exception ex)
            {
                // El mapa de bits anterior hay que soltarlo igualmente: dejarlo
                // colgado por la rama de error es una fuga silenciosa.
                _preview.Image = null;
                if (_current != null) { _current.Dispose(); _current = null; }
                _urlLabel.Text = "No se pudo generar el codigo: " + ex.Message;
                return;
            }

            Bitmap previous = _current;
            _preview.Image = generated;
            _current = generated;
            if (previous != null) previous.Dispose();
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            try { Clipboard.SetText(SelectedUrl); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo acceder al portapapeles: " + ex.Message, "Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Imagen PNG (*.png)|*.png";
                dialog.FileName = "acceso-qr.png";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // Se regenera a mayor resolucion: el tamano de la vista
                    // previa serviria para la pantalla pero no para imprimir.
                    using (Bitmap large = QrRenderer.Render(QrCode.Encode(SelectedUrl), ExportPixels))
                    {
                        large.Save(dialog.FileName, ImageFormat.Png);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo guardar: " + ex.Message, "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _current != null)
            {
                _preview.Image = null;
                _current.Dispose();
                _current = null;
            }
            base.Dispose(disposing);
        }
    }
}
