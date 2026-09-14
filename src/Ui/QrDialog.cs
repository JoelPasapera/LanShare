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

        private readonly List<string> _urls;
        private readonly ComboBox _selector;
        private readonly PictureBox _preview;
        private readonly Label _urlLabel;

        private Bitmap _current;

        public QrDialog(List<string> urls)
        {
            if (urls == null || urls.Count == 0) throw new ArgumentException("urls");
            _urls = urls;

            Text = "Acceso desde el movil";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;

            int width = 420;
            int y = 16;

            // El selector solo aparece si el equipo tiene varias direcciones:
            // con una sola seria una lista de un elemento.
            if (urls.Count > 1)
            {
                Label hint = new Label();
                hint.Location = new Point(16, y);
                hint.Size = new Size(width - 32, 16);
                hint.ForeColor = Color.FromArgb(85, 95, 110);
                hint.Font = new Font("Segoe UI", 8.5f);
                hint.Text = "Este equipo tiene varias direcciones. Prueba con la de tu red:";
                Controls.Add(hint);
                y += 20;

                _selector = new ComboBox();
                _selector.Location = new Point(16, y);
                _selector.Size = new Size(width - 32, 23);
                _selector.DropDownStyle = ComboBoxStyle.DropDownList;
                foreach (string url in urls) _selector.Items.Add(url);
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
            get { return _selector != null ? (string)_selector.SelectedItem : _urls[0]; }
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
