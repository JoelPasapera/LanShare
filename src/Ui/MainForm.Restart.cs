using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using LanShare.Core;

namespace LanShare.Ui
{
    /// <summary>
    /// Cambios en los sitios con el servidor en marcha.
    ///
    /// Antes habia que detenerlo, editar y volver a iniciarlo, y al iniciar se
    /// abria una pestana por proyecto. Ahora el cambio se guarda y el servidor
    /// se reinicia solo y sin abrir pestanas; las paginas con recarga
    /// automatica se reconectan por si mismas. Un sitio recien anadido si se
    /// abre en el navegador: es el que se quiere ver.
    ///
    /// Varios cambios seguidos (marcar tres casillas) se agrupan en un unico
    /// reinicio, que llega un momento despues del ultimo. Los cambios que no
    /// tocan lo que se sirve (un sitio desmarcado, aceptar sin cambiar nada)
    /// no reinician: cada reinicio corta las descargas en curso.
    /// </summary>
    public partial class MainForm
    {
        private const int RestartDelayMilliseconds = 700;

        private System.Windows.Forms.Timer _restartTimer;
        private bool _restarting;

        /// <summary>Windows esta cerrando la sesion: no se arranca nada mas.</summary>
        private bool _sessionEnding;

        /// <summary>Puerto del sitio anadido en caliente, que se abre tras el reinicio. 0 si ninguno.</summary>
        private int _openAfterRestart;

        /// <summary>
        /// Carpetas que se estaban sirviendo justo antes del reinicio. Servir
        /// la raiz de una unidad exige confirmarlo; si ya se confirmo al iniciar
        /// a mano, el reinicio no debe descartarla por no poder preguntar.
        /// </summary>
        private readonly HashSet<string> _confirmedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void InitializeRestart()
        {
            _restartTimer = new System.Windows.Forms.Timer();
            _restartTimer.Interval = RestartDelayMilliseconds;
            _restartTimer.Tick += OnRestartTimerTick;
            FormClosed += OnRestartFormClosed;
        }

        private void OnRestartFormClosed(object sender, FormClosedEventArgs e)
        {
            _restartTimer.Stop();
            _restartTimer.Dispose();
        }

        /// <summary>Pide un reinicio si el servidor esta en marcha. Reinicia la espera si ya habia uno pedido.</summary>
        private void ScheduleRestart()
        {
            if (_server == null || _closing || _sessionEnding) return;
            _restartTimer.Stop();
            _restartTimer.Start();
        }

        /// <summary>Un arranque o una parada manual dejan sin efecto lo que estuviera pendiente.</summary>
        private void CancelPendingRestart()
        {
            if (_restarting) return;
            _restartTimer.Stop();
            _openAfterRestart = 0;
        }

        private async void OnRestartTimerTick(object sender, EventArgs e)
        {
            _restartTimer.Stop();

            // Una parada en curso (boton, bandeja, cierre) manda sobre el
            // reinicio. Si lo que esta en curso es otro reinicio, el sitio por
            // abrir se conserva: ese reinicio lee la configuracion despues de
            // parar y lo recogera.
            // Tampoco dentro de otro reinicio (por ejemplo, mientras su aviso de
            // sitios omitidos sigue abierto): ese reinicio ya leyo la
            // configuracion despues de parar, asi que no se pierde nada.
            if (_server == null || _stopping != null || _closing || _sessionEnding || _restarting ||
                !_startButton.Enabled)
            {
                if (!_restarting) _openAfterRestart = 0;
                return;
            }

            _log.Write("INFO", "Aplicando los cambios de los sitios: reiniciando el servidor.");
            _restarting = true;
            _confirmedRoots.Clear();
            foreach (SiteRuntime site in _server.Options.Sites) _confirmedRoots.Add(site.RootPath);

            try
            {
                await StopServerAsync();
                if (_server != null || _closing || _sessionEnding) return;

                // El arranque es sincrono cuando no hay servidor: termina antes
                // de que se restablezcan las marcas.
                _quietStart = true;
                OnStartClick(this, EventArgs.Empty);

                // Si no llego a arrancar, la parada se salto el cierre de reglas
                // de firewall y de la escucha mDNS pensando que seguirian en uso.
                if (_server == null) ReleaseNetworkExposure();
            }
            finally
            {
                _restarting = false;
                _openAfterRestart = 0;
                _confirmedRoots.Clear();
            }
        }

        /// <summary>
        /// Lo que el arranque hace distinto cuando es un reinicio: abrir solo el
        /// sitio recien anadido, si de verdad arranco, y contar delante de la
        /// ventana que sitios se quedaron fuera.
        /// </summary>
        private void FinishRestart(IList<SiteRuntime> runtimes, IList<string> omitted)
        {
            _log.Write("INFO", "Servidor reiniciado con " + runtimes.Count + (runtimes.Count == 1 ? " sitio." : " sitios."));

            int port = _openAfterRestart;
            _openAfterRestart = 0;

            if (omitted.Count > 0)
            {
                if (Visible)
                {
                    StringBuilder text = new StringBuilder("Al aplicar los cambios no pudo arrancar:\r\n");
                    foreach (string reason in omitted) text.Append("\r\n- ").Append(reason);
                    Warn(text.ToString(), "Sitios omitidos");
                }
                else if (_tray != null)
                {
                    _tray.Notify(AppInfo.Name, omitted.Count + (omitted.Count == 1 ? " sitio omitido" : " sitios omitidos") +
                                 " al aplicar los cambios: abre LanShare para ver el motivo.", ToolTipIcon.Warning);
                }
            }

            if (port == 0 || !Visible) return;
            foreach (SiteRuntime runtime in runtimes)
            {
                if (runtime.Port != port) continue;
                try { System.Diagnostics.Process.Start(runtime.LocalUrl); }
                catch (Exception ex) { _log.Write("WARN", "No se pudo abrir " + runtime.LocalUrl + ": " + ex.Message); }
                return;
            }
        }

        /// <summary>
        /// Tras anadir un sitio con el servidor en marcha: si esta marcado para
        /// servirse, se reinicia para incluirlo y se abre al terminar.
        /// </summary>
        private void OnSiteAddedWhileRunning(SiteDefinition site)
        {
            if (_server == null || site == null || !site.Enabled) return;
            _openAfterRestart = site.Port;
            ScheduleRestart();
        }

        /// <summary>
        /// Si un cambio altera lo que se sirve: el sitio se sirve antes o
        /// despues, y algo de lo que usa el servidor es distinto.
        /// </summary>
        private static bool AffectsServing(SiteDefinition before, SiteDefinition after)
        {
            if (before == null || after == null) return true;
            if (!before.Enabled && !after.Enabled) return false;

            return before.Enabled != after.Enabled ||
                   before.Port != after.Port ||
                   before.HttpsPort != after.HttpsPort ||
                   before.LanMode != after.LanMode ||
                   !string.Equals(before.RootPath ?? "", after.RootPath ?? "", StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(before.StartFile ?? "", after.StartFile ?? "", StringComparison.Ordinal) ||
                   !string.Equals(before.Label ?? "", after.Label ?? "", StringComparison.Ordinal);
        }
    }
}
