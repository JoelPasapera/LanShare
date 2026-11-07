using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LanShare.Core
{
    /// <summary>
    /// Vigila la carpeta de un sitio y agrupa los cambios en rafagas.
    ///
    /// Guardar un archivo suele producir varios eventos seguidos (escritura,
    /// cambio de tamano, renombrado del temporal). Sin agruparlos, cada
    /// guardado recargaria la pagina dos o tres veces. Se espera a que pasen
    /// unos milisegundos sin eventos y se decide una sola vez para todo el lote.
    /// </summary>
    public sealed class SiteWatcher : IDisposable
    {
        public const int QuietMilliseconds = 180;

        private readonly int _port;
        private readonly string _root;
        private readonly Action<int, ReloadPlan> _onChange;
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounce;
        private readonly object _gate = new object();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _overflow;
        private int _disposed;

        public int Port { get { return _port; } }

        /// <param name="onChange">Recibe el puerto del sitio y lo que hay que hacer.</param>
        public SiteWatcher(int port, string root, Action<int, ReloadPlan> onChange)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Falta la carpeta.", "root");
            if (onChange == null) throw new ArgumentNullException("onChange");

            _port = port;
            _root = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            _onChange = onChange;
            _debounce = new Timer(OnQuiet, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _watcher = new FileSystemWatcher(_root);
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                        NotifyFilters.LastWrite | NotifyFilters.Size;
                // 64 KB es el maximo que admite el sistema para recursos no paginados;
                // el valor por defecto (8 KB) se desborda al guardar un proyecto entero.
                _watcher.InternalBufferSize = 64 * 1024;
                _watcher.Changed += OnFileEvent;
                _watcher.Created += OnFileEvent;
                _watcher.Deleted += OnFileEvent;
                _watcher.Renamed += OnRenamed;
                _watcher.Error += OnError;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                // Una carpeta que no se puede vigilar (red, sin permiso) no debe
                // dejar atras un temporizador ni un vigilante a medio crear.
                if (_watcher != null) _watcher.Dispose();
                _debounce.Dispose();
                throw;
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // Crear, borrar o renombrar dentro de una carpeta cambia su fecha, y
            // eso llega como Changed de la propia carpeta. No aporta nada (el
            // cambio real tiene su propio evento) y, como una carpeta no tiene
            // extension, convertia el cambio en caliente de una hoja de estilos
            // en una recarga completa cada vez que un editor guardaba con un
            // temporal y un renombrado.
            if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
            Record(e.FullPath, false);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            // Cuenta el nombre nuevo: es el que el navegador va a pedir. El
            // viejo suele ser un temporal del editor, que se descarta solo.
            Record(e.FullPath, false);
            Record(e.OldFullPath, false);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            // Desbordamiento del buffer interno: se perdieron eventos.
            Record(null, true);
        }

        private void Record(string fullPath, bool overflow)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            lock (_gate)
            {
                if (overflow) _overflow = true;
                else if (fullPath != null && fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
                    _pending.Add(fullPath.Substring(_root.Length));
            }

            try { _debounce.Change(QuietMilliseconds, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        private void OnQuiet(object state)
        {
            List<string> batch;
            bool overflow;

            lock (_gate)
            {
                batch = new List<string>(_pending);
                overflow = _overflow;
                _pending.Clear();
                _overflow = false;
            }

            ReloadPlan plan = ReloadPlanner.Plan(batch, overflow);
            if (plan.Kind == ReloadKind.None) return;

            try { _onChange(_port, plan); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounce.Dispose();
        }
    }
}
