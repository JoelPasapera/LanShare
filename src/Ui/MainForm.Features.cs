using System;
using System.Globalization;
using System.Text;
using LanShare.Core;

namespace LanShare.Ui
{
    /// <summary>
    /// Funciones opcionales del servidor que dependen de la configuracion:
    /// recarga automatica, consola remota y subida de archivos. Se aplican al
    /// arrancar y cada vez que se aceptan cambios en Configuracion, sin
    /// reiniciar.
    /// </summary>
    public partial class MainForm
    {
        private bool _uploadAnnounced;
        private bool _consoleAnnounced;

        /// <summary>
        /// Resumen de la cache de compresion y de la recarga en la barra del
        /// registro. Es la unica forma de ver si la cache esta rindiendo.
        /// </summary>
        private void UpdateStatsLabel()
        {
            if (_server == null)
            {
                _statsLabel.Text = "";
                return;
            }

            int files = CompressionCache.Count;
            StringBuilder sb = new StringBuilder("Cache gzip: ");
            sb.Append(files).Append(files == 1 ? " archivo, " : " archivos, ")
              .Append(ByteSize.Format(CompressionCache.Bytes));

            int rate = CompressionCache.HitRatePercent;
            if (rate >= 0) sb.Append(", ").Append(rate).Append("% de aciertos");

            if (_settings.LiveReload)
            {
                int tabs = _server.LiveReloadClients;
                sb.Append("     Recarga: ").Append(tabs).Append(tabs == 1 ? " pestana" : " pestanas");
            }

            _statsLabel.Text = sb.ToString();
        }

        private void ApplyDevFeatures()
        {
            if (_server == null) return;

            _server.SetLiveReload(_settings.LiveReload);

            _server.Options.EnableRemoteConsole = _settings.RemoteConsole;
            if (_settings.RemoteConsole && !_consoleAnnounced)
            {
                _consoleAnnounced = true;
                _log.Write("INFO", "Consola remota activa: los errores y mensajes de consola de los otros " +
                                   "dispositivos apareceran aqui como JS ERROR, JS WARN o JS LOG. Recarga en " +
                                   "ellos las paginas que ya estuvieran abiertas.");
            }
            else if (!_settings.RemoteConsole && _consoleAnnounced)
            {
                _consoleAnnounced = false;
                _log.Write("INFO", "Consola remota desactivada.");
            }

            _server.Options.UploadFolder = _settings.GetUploadFolder();
            _server.Options.UploadMaxBytes = (long)_settings.UploadMaxMegabytes * 1024 * 1024;
            _server.Options.EnableUpload = _settings.UploadEnabled;

            // Se avisa al activarla, no en cada cambio de configuracion: es la
            // unica funcion que escribe en disco y conviene que conste cuando.
            if (_settings.UploadEnabled && !_uploadAnnounced)
            {
                _uploadAnnounced = true;
                _log.Write("INFO", "Subida de archivos activa en /__subir. Destino: " +
                                   _settings.GetUploadFolder() + " (maximo " +
                                   _settings.UploadMaxMegabytes.ToString(CultureInfo.InvariantCulture) +
                                   " MB por archivo).");
            }
            else if (!_settings.UploadEnabled && _uploadAnnounced)
            {
                _uploadAnnounced = false;
                _log.Write("INFO", "Subida de archivos desactivada.");
            }
        }
    }
}
