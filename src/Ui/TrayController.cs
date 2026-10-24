using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Icono de LanShare en la bandeja del sistema y su menu.
    ///
    /// No conoce el servidor ni la ventana: solo avisa de lo que pide el
    /// usuario y muestra el estado que le dicen. Asi la ventana principal sigue
    /// siendo la unica que decide.
    /// </summary>
    internal sealed class TrayController : IDisposable
    {
        /// <summary>Limite de NotifyIcon.Text; por encima lanza una excepcion.</summary>
        private const int MaxTooltip = 63;

        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _toggle;

        public event EventHandler ShowRequested;
        public event EventHandler ToggleServerRequested;
        public event EventHandler ExitRequested;

        public TrayController(Icon icon)
        {
            _menu = new ContextMenuStrip();

            ToolStripMenuItem show = new ToolStripMenuItem("Mostrar LanShare");
            show.Font = new Font(show.Font, FontStyle.Bold);
            show.Click += OnShowClick;
            _menu.Items.Add(show);

            _toggle = new ToolStripMenuItem("Iniciar servidor");
            _toggle.Click += OnToggleClick;
            _menu.Items.Add(_toggle);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("Salir");
            exit.Click += OnExitClick;
            _menu.Items.Add(exit);

            _icon = new NotifyIcon();
            _icon.Icon = icon ?? SystemIcons.Application;
            _icon.ContextMenuStrip = _menu;
            _icon.DoubleClick += OnShowClick;
            SetRunning(false, 0);
            _icon.Visible = true;
        }

        public void SetRunning(bool running, int siteCount)
        {
            _toggle.Text = running ? "Detener servidor" : "Iniciar servidor";

            string text = running
                ? AppInfo.Name + " - " + siteCount + (siteCount == 1 ? " sitio" : " sitios") + " en marcha"
                : AppInfo.Name + " - detenido";
            _icon.Text = text.Length > MaxTooltip ? text.Substring(0, MaxTooltip) : text;
        }

        public void Notify(string title, string text, ToolTipIcon kind)
        {
            try { _icon.ShowBalloonTip(4000, title, text, kind); }
            catch (Exception) { }
        }

        private void OnShowClick(object sender, EventArgs e)
        {
            Raise(ShowRequested);
        }

        private void OnToggleClick(object sender, EventArgs e)
        {
            Raise(ToggleServerRequested);
        }

        private void OnExitClick(object sender, EventArgs e)
        {
            Raise(ExitRequested);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            // Ocultarlo antes de liberarlo: si no, el icono queda como fantasma
            // en la bandeja hasta que se pasa el raton por encima.
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
