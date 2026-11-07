using System;
using System.Windows.Forms;
using LanShare.Platform;

namespace LanShare.Ui
{
    /// <summary>
    /// Bandeja del sistema y arranque: --tray, --start, minimizar a la bandeja
    /// y la parte del inicio que no depende de que la ventana se llegue a ver.
    /// </summary>
    public partial class MainForm
    {
        private TrayController _tray;
        private bool _startHidden;
        private bool _startupDone;
        private bool _quietStart;
        private bool _trayHintShown;

        /// <summary>Normal o maximizada antes de minimizar: al volver de la bandeja se respeta.</summary>
        private FormWindowState _restoreState = FormWindowState.Normal;

        private void InitializeTray()
        {
            _startHidden = _startup.StartInTray;

            _tray = new TrayController(AppIcon.LoadSmall());
            _tray.ShowRequested += OnTrayShow;
            _tray.ToggleServerRequested += OnTrayToggle;
            _tray.ExitRequested += OnTrayExit;

            Resize += OnMainResize;
            FormClosed += OnMainFormClosed;
        }

        /// <summary>
        /// Con --tray la ventana no llega a mostrarse. Se crea igualmente el
        /// handle, que lo necesitan BeginInvoke y el aviso de segunda instancia,
        /// y el arranque se completa sin pasar por el evento Shown.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            if (value && _startHidden)
            {
                _startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                BeginInvoke((MethodInvoker)CompleteStartup);
                return;
            }
            base.SetVisibleCore(value);
        }

        /// <summary>
        /// Lo que hay que hacer una vez tras crear la ventana, se vea o no.
        /// Se llama desde Shown o desde el arranque oculto, lo que llegue antes.
        /// </summary>
        private void CompleteStartup()
        {
            if (_startupDone) return;
            _startupDone = true;

            AppIcon.Apply(this);
            NativeUi.EnableListViewDoubleBuffer(_clientsList.Handle);

            foreach (string unknown in _startup.Unknown)
            {
                _log.Write("WARN", "Argumento de linea de comandos no reconocido: " + unknown);
            }

            AutoStart.RefreshRunKeyPath(Application.ExecutablePath);

            if (_startup.StartServer && _server == null)
            {
                // Arranque desde la linea de comandos o tras escalar: sin abrir
                // pestanas. Al iniciar sesion en Windows abriria una por proyecto.
                _quietStart = true;
                OnStartClick(this, EventArgs.Empty);
            }
        }

        /// <summary>Devuelve y consume la marca de arranque silencioso.</summary>
        private bool ConsumeQuietStart()
        {
            bool quiet = _quietStart || !Visible;
            _quietStart = false;
            return quiet;
        }

        private void NotifyServerState(bool running, int siteCount)
        {
            if (_tray != null) _tray.SetRunning(running, siteCount);
        }

        private void OnMainResize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
            {
                _restoreState = WindowState;
                return;
            }
            if (!_settings.MinimizeToTray) return;

            Hide();
            if (_trayHintShown) return;
            _trayHintShown = true;
            _tray.Notify(AppInfo.Name, "Sigue funcionando en la bandeja. Doble clic en el icono para abrirlo.",
                         ToolTipIcon.Info);
        }

        private void OnTrayShow(object sender, EventArgs e)
        {
            BringToFrontFromAnywhere();
        }

        private void OnTrayToggle(object sender, EventArgs e)
        {
            // Con el boton deshabilitado hay una parada en curso: el menu de la
            // bandeja no debe poder hacer lo que la ventana no permite.
            if (!_startButton.Enabled) return;
            OnStartClick(this, EventArgs.Empty);
        }

        private void OnTrayExit(object sender, EventArgs e)
        {
            Close();
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            if (_tray == null) return;
            _tray.Dispose();
            _tray = null;
        }
    }
}
