using System;
using System.IO;

namespace LanShare.Core
{
    /// <summary>
    /// Un sitio ya resuelto y listo para servir: la definicion del usuario mas
    /// las rutas canonizadas. Se construye al arrancar y no cambia mientras el
    /// servidor corre, asi que los hilos de peticion pueden leerlo sin cerrojo.
    /// </summary>
    public sealed class SiteRuntime
    {
        public int Port { get; private set; }
        public bool LanMode { get; private set; }
        public string Label { get; private set; }

        /// <summary>Vacio significa que el sitio se sirve como carpeta.</summary>
        public string StartFile { get; private set; }

        /// <summary>Raiz servida, siempre terminada en separador de directorio.</summary>
        public string RootPath { get; private set; }

        /// <summary>Raiz canonizada por handle Win32, terminada en separador.</summary>
        public string RealRootPath { get; private set; }

        public SiteRuntime(int port, bool lanMode, string label, string rootPath, string realRootPath)
            : this(port, lanMode, label, rootPath, realRootPath, StaticFileResolver.DefaultIndexFile)
        {
        }

        public SiteRuntime(int port, bool lanMode, string label, string rootPath,
                           string realRootPath, string startFile)
        {
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentNullException("rootPath");
            if (string.IsNullOrEmpty(realRootPath)) throw new ArgumentNullException("realRootPath");

            Port = port;
            LanMode = lanMode;
            Label = label ?? "";
            StartFile = startFile ?? "";
            RootPath = EnsureTrailingSeparator(rootPath);
            RealRootPath = EnsureTrailingSeparator(realRootPath);
        }

        /// <summary>
        /// El comodin abre el puerto a toda la red y exige administrador;
        /// localhost no, y por eso es el valor por defecto.
        /// </summary>
        public string Prefix
        {
            get
            {
                return LanMode
                    ? "http://+:" + Port + "/"
                    : "http://localhost:" + Port + "/";
            }
        }

        public string LocalUrl
        {
            get { return "http://localhost:" + Port + "/"; }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string separator = Path.DirectorySeparatorChar.ToString();
            return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
        }
    }
}
