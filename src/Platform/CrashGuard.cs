using System;
using System.Globalization;
using System.IO;

namespace LanShare.Platform
{
    /// <summary>
    /// Proteccion para una llamada nativa que, si fallase de la peor manera,
    /// cerraria LanShare sin dejar atrapar nada: en .NET 4 un acceso a memoria
    /// invalido no llega a ningun catch. Antes de la llamada se deja una marca
    /// en disco y despues se quita. Si al volver a abrir la marca sigue ahi,
    /// la ultima vez el proceso murio dentro y esa llamada ya no se repite:
    /// un fallo asi cuesta una funcion, no la aplicacion entera en cada arranque.
    /// </summary>
    public sealed class CrashGuard
    {
        private readonly string _markerPath;

        /// <param name="folder">Carpeta de la marca; se crea si no existe.</param>
        /// <param name="name">Nombre de lo que se protege, para el archivo de la marca.</param>
        public CrashGuard(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException("folder");
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException("name");
            _markerPath = Path.Combine(folder, name + ".pendiente");
        }

        /// <summary>Ruta de la marca: borrarla vuelve a activar la llamada.</summary>
        public string MarkerPath
        {
            get { return _markerPath; }
        }

        /// <summary>Si una llamada anterior se quedo a medias porque el proceso murio dentro.</summary>
        public bool Tripped
        {
            get { return File.Exists(_markerPath); }
        }

        /// <summary>
        /// Hace la llamada entre marca y desmarca. Una excepcion normal quita la
        /// marca igual y sigue su camino: solo una muerte del proceso la deja.
        /// Si la marca no se puede escribir, se llama igualmente.
        /// </summary>
        public void Run(Action call)
        {
            if (call == null) throw new ArgumentNullException("call");

            Mark();
            try
            {
                call();
            }
            finally
            {
                Clear();
            }
        }

        private void Mark()
        {
            try
            {
                string folder = Path.GetDirectoryName(_markerPath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // Sin marca no hay proteccion, pero tampoco motivo para no llamar.
            }
        }

        private void Clear()
        {
            try
            {
                if (File.Exists(_markerPath)) File.Delete(_markerPath);
            }
            catch (Exception)
            {
            }
        }
    }
}
