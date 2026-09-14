using System;
using System.IO;

namespace ServidorWebPro.Core
{
    /// <summary>
    /// Configuracion inmutable de una sesion de servidor. Se construye una vez
    /// al pulsar Iniciar y no cambia mientras el servidor corre.
    /// </summary>
    public sealed class ServerOptions
    {
        /// <summary>Prefijo de HttpListener, p.ej. "http://+:8080/".</summary>
        public string Prefix { get; private set; }

        /// <summary>Raiz servida, siempre terminada en separador de directorio.</summary>
        public string RootPath { get; private set; }

        /// <summary>Raiz canonizada por handle Win32, terminada en separador.</summary>
        public string RealRootPath { get; private set; }

        // Estos cuatro se pueden cambiar con el servidor en marcha: los hilos
        // de peticion los leen en cada llamada. Campos volatiles explicitos
        // porque una propiedad automatica no admite el modificador.
        private volatile bool _corsEnabled;
        private volatile bool _enableCompression;
        private volatile bool _enableDirectoryListing;
        private volatile bool _distributionMode;

        public bool CorsEnabled
        {
            get { return _corsEnabled; }
            set { _corsEnabled = value; }
        }

        public bool EnableCompression
        {
            get { return _enableCompression; }
            set { _enableCompression = value; }
        }

        public bool EnableDirectoryListing
        {
            get { return _enableDirectoryListing; }
            set { _enableDirectoryListing = value; }
        }

        /// <summary>
        /// En desarrollo se envia no-store para que el navegador nunca sirva
        /// una version vieja. En distribucion se emiten validadores y se
        /// responde 304, que ahorra reenviar lo que el cliente ya tiene.
        /// </summary>
        public bool DistributionMode
        {
            get { return _distributionMode; }
            set { _distributionMode = value; }
        }

        public ServerOptions(string prefix, string rootPath, string realRootPath, bool corsEnabled)
        {
            if (string.IsNullOrEmpty(prefix)) throw new ArgumentNullException("prefix");
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentNullException("rootPath");
            if (string.IsNullOrEmpty(realRootPath)) throw new ArgumentNullException("realRootPath");

            Prefix = prefix;
            RootPath = EnsureTrailingSeparator(rootPath);
            RealRootPath = EnsureTrailingSeparator(realRootPath);
            CorsEnabled = corsEnabled;
            EnableCompression = true;
            EnableDirectoryListing = true;
            DistributionMode = false;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string sep = Path.DirectorySeparatorChar.ToString();
            return path.EndsWith(sep, StringComparison.Ordinal) ? path : path + sep;
        }
    }
}
