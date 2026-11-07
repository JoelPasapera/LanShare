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

        /// <summary>Puerto HTTPS del sitio; 0 si se sirve solo por HTTP.</summary>
        public int HttpsPort { get; private set; }
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
            : this(port, lanMode, label, rootPath, realRootPath, startFile, 0)
        {
        }

        public SiteRuntime(int port, bool lanMode, string label, string rootPath,
                           string realRootPath, string startFile, int httpsPort)
        {
            if (string.IsNullOrEmpty(rootPath)) throw new ArgumentNullException("rootPath");
            if (string.IsNullOrEmpty(realRootPath)) throw new ArgumentNullException("realRootPath");
            if (httpsPort < 0 || httpsPort > 65535) throw new ArgumentOutOfRangeException("httpsPort");
            if (httpsPort != 0 && httpsPort == port)
            {
                throw new ArgumentException("El puerto HTTPS no puede ser el mismo que el HTTP.", "httpsPort");
            }

            Port = port;
            HttpsPort = httpsPort;
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

        /// <summary>Prefijo HTTPS, con el mismo alcance que el HTTP; null si el sitio no tiene HTTPS.</summary>
        public string HttpsPrefix
        {
            get
            {
                if (HttpsPort == 0) return null;
                return LanMode
                    ? "https://+:" + HttpsPort + "/"
                    : "https://localhost:" + HttpsPort + "/";
            }
        }

        public string LocalUrl
        {
            get { return "http://localhost:" + Port + "/"; }
        }

        /// <summary>Copia del sitio sin HTTPS: para cuando no se pudo preparar su certificado.</summary>
        public SiteRuntime WithoutHttps()
        {
            return new SiteRuntime(Port, LanMode, Label, RootPath, RealRootPath, StartFile, 0);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            string separator = Path.DirectorySeparatorChar.ToString();
            return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
        }
    }
}
