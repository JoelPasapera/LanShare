using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Opciones de comportamiento del servidor, fuera de la ventana principal.
    ///
    /// Cada una lleva su explicacion al lado porque son decisiones con un
    /// compromiso real detras, no interruptores obvios: quien las toca seis
    /// meses despues no tiene por que acordarse de que hacia cada una.
    /// </summary>
    public sealed class SettingsDialog : Form
    {
        private const int Inset = 18;
        private const int OptionGap = 18;

        private readonly CheckBox _listing;
        private readonly CheckBox _compression;
        private readonly CheckBox _distribution;
        private readonly CheckBox _cors;

        public bool DirectoryListing { get { return _listing.Checked; } }
        public bool Compression { get { return _compression.Checked; } }
        public bool DistributionMode { get { return _distribution.Checked; } }
        public bool Cors { get { return _cors.Checked; } }

        public SettingsDialog(bool listing, bool compression, bool distribution, bool cors,
                              bool serverRunning)
        {
            Text = "Configuracion del servidor";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;

            int width = 660;
            int contentWidth = width - Inset * 2;
            int y = Inset;

            if (serverRunning)
            {
                Label aviso = new Label();
                aviso.Location = new Point(Inset, y);
                aviso.MaximumSize = new Size(contentWidth, 0);
                aviso.AutoSize = true;
                aviso.ForeColor = Color.FromArgb(150, 110, 20);
                aviso.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
                aviso.Text = "El servidor esta en marcha. Los cambios se aplican de inmediato, " +
                             "sin reiniciarlo.";
                Controls.Add(aviso);
                y += aviso.Height + OptionGap;
            }

            _listing = AddOption(ref y, contentWidth, listing,
                "Listar carpetas sin index.html",
                "Cuando el navegador pide una carpeta en lugar de un archivo (por ejemplo /fotos/), " +
                "el servidor busca dentro un index.html y lo sirve. Esta opcion decide que pasa " +
                "cuando esa carpeta no tiene ninguno: marcada, se muestra una tabla navegable con " +
                "su contenido; desmarcada, se responde 404.\r\n" +
                "No afecta a tus paginas. Un archivo llamado contacto.html se sirve igual en ambos casos: " +
                "index.html no es el nombre obligatorio de tus paginas, solo el archivo por defecto de " +
                "una carpeta.\r\n" +
                "Desmarcala si sirves en red local una carpeta con material que no quieres que sea " +
                "descubrible: sin listado hay que acertar el nombre exacto del archivo.");

            _compression = AddOption(ref y, contentWidth, compression,
                "Comprimir texto con gzip",
                "Reduce entre un 60% y un 80% el tamano de HTML, CSS, JavaScript, JSON y SVG antes " +
                "de enviarlos. Sobre Wi-Fi la diferencia se nota.\r\n" +
                "Solo se aplica si el cliente declara que lo admite, nunca sobre imagenes, video o " +
                "fuentes (ya vienen comprimidos) y nunca sobre descargas parciales, donde romperia " +
                "los desplazamientos.\r\n" +
                "Dejala marcada salvo que estes persiguiendo un fallo y quieras descartar la compresion " +
                "como causa, o que sirvas a un cliente casero que anuncia gzip sin saber descomprimirlo.");

            _distribution = AddOption(ref y, contentWidth, distribution,
                "Modo distribucion (cache con ETag y 304)",
                "Cambia la politica de cache que se le pide al navegador.\r\n" +
                "Desmarcada (desarrollo): se prohibe cachear. Editas un archivo, recargas y ves el " +
                "cambio siempre, sin Ctrl+F5. A cambio cada recarga vuelve a descargarlo todo.\r\n" +
                "Marcada (distribucion): el navegador guarda los archivos pero pregunta antes de usarlos. " +
                "Si nada cambio recibe una respuesta de unos 200 bytes en vez del archivo entero. Util " +
                "cuando pasas la misma carpeta al movil una y otra vez.\r\n" +
                "Aviso: la version se calcula con el tamano y la fecha al segundo. Si modificas un archivo " +
                "dentro del mismo segundo y conserva el mismo tamano exacto, el cliente seguira con su " +
                "copia vieja.");

            _cors = AddOption(ref y, contentWidth, cors,
                "CORS abierto (Access-Control-Allow-Origin: *)",
                "Anade esa cabecera a todas las respuestas, lo que permite que una pagina servida desde " +
                "otro origen (otro puerto, otro dominio, un archivo local) lea estos archivos por fetch " +
                "o XMLHttpRequest.\r\n" +
                "Marcala si tu aplicacion pide datos a este servidor desde otra direccion. Si todo se " +
                "sirve desde aqui, no hace falta.\r\n" +
                "En red local tiene un coste: cualquier web que visites mientras el servidor corre podria " +
                "leer su contenido desde tu propio navegador.");

            y += 6;

            Button ok = new Button();
            ok.Size = new Size(86, 28);
            ok.Location = new Point(width - Inset - 86 * 2 - 8, y);
            ok.Text = "Aceptar";
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Size = new Size(86, 28);
            cancel.Location = new Point(width - Inset - 86, y);
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ClientSize = new Size(width, y + 28 + Inset);
        }

        /// <summary>
        /// Coloca casilla y explicacion, y avanza el cursor vertical. La altura
        /// del parrafo la calcula la propia etiqueta al ajustar el texto, asi
        /// que cambiar una explicacion no obliga a recolocar nada a mano.
        /// </summary>
        private CheckBox AddOption(ref int y, int contentWidth, bool value, string title, string description)
        {
            if (y > Inset)
            {
                Label divisor = new Label();
                divisor.Location = new Point(Inset, y);
                divisor.Size = new Size(contentWidth, 1);
                divisor.BorderStyle = BorderStyle.Fixed3D;
                Controls.Add(divisor);
                y += OptionGap;
            }

            CheckBox box = new CheckBox();
            box.Location = new Point(Inset, y);
            box.AutoSize = true;
            box.Checked = value;
            box.Text = title;
            box.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Controls.Add(box);
            y += box.Height + 4;

            Label text = new Label();
            text.Location = new Point(Inset + 20, y);
            text.MaximumSize = new Size(contentWidth - 20, 0);
            text.AutoSize = true;
            text.ForeColor = Color.FromArgb(85, 95, 110);
            text.Font = new Font("Segoe UI", 8.5f);
            text.Text = description;
            Controls.Add(text);
            y += text.Height + OptionGap;

            return box;
        }
    }
}
