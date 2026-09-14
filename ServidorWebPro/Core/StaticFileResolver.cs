using System;
using System.IO;

namespace ServidorWebPro.Core
{
    public enum ResolutionKind
    {
        File,
        DirectoryListing,
        RedirectToDirectory,
        NotFound,
        Forbidden,
        BadRequest
    }

    public sealed class Resolution
    {
        public ResolutionKind Kind { get; private set; }
        public string FilePath { get; private set; }
        public string DirectoryPath { get; private set; }
        public string RedirectLocation { get; private set; }
        public string Reason { get; private set; }

        private Resolution(ResolutionKind kind, string filePath, string redirect, string reason)
        {
            Kind = kind;
            FilePath = filePath;
            RedirectLocation = redirect;
            Reason = reason;
        }

        public static Resolution File(string path) { return new Resolution(ResolutionKind.File, path, null, null); }

        public static Resolution Listing(string directoryPath)
        {
            Resolution resolution = new Resolution(ResolutionKind.DirectoryListing, null, null, null);
            resolution.DirectoryPath = directoryPath;
            return resolution;
        }
        public static Resolution Redirect(string location) { return new Resolution(ResolutionKind.RedirectToDirectory, null, location, null); }
        public static Resolution NotFound(string reason) { return new Resolution(ResolutionKind.NotFound, null, null, reason); }
        public static Resolution Forbidden(string reason) { return new Resolution(ResolutionKind.Forbidden, null, null, reason); }
        public static Resolution BadRequest(string reason) { return new Resolution(ResolutionKind.BadRequest, null, null, reason); }
    }

    /// <summary>
    /// Unica responsabilidad: traducir una URL a una decision de servicio. No
    /// abre archivos ni escribe respuestas, lo que permite probarla aislada.
    /// </summary>
    public static class StaticFileResolver
    {
        public const string IndexFile = "index.html";

        /// <param name="localPath">Url.LocalPath, YA decodificado por Uri.</param>
        /// <param name="absolutePath">Url.AbsolutePath, aun escapado, para la cabecera Location.</param>
        public static Resolution Resolve(string localPath, string absolutePath, string query,
                                         string rootPath, bool allowDirectoryListing)
        {
            if (localPath == null) return Resolution.BadRequest("URL invalida");

            bool directoryRequested = localPath.EndsWith("/", StringComparison.Ordinal);
            string relative = localPath.TrimStart('/', '\\');

            if (!PathGuard.RelativePathIsSafe(relative))
                return Resolution.BadRequest("Ruta con caracteres prohibidos");

            string fullPath;
            if (relative.Length == 0)
            {
                // La raiz se toma tal cual y NO se sustituye por index.html: si
                // se sustituia, una carpeta sin indice moria en 404 antes de
                // llegar a la rama de directorio que decide si listar.
                fullPath = rootPath;
            }
            else
            {
                try
                {
                    fullPath = Path.GetFullPath(Path.Combine(rootPath, relative));
                }
                catch (Exception)
                {
                    return Resolution.BadRequest("Ruta malformada");
                }

                if (!PathGuard.LooksInsideRoot(fullPath, rootPath))
                    return Resolution.Forbidden("Path traversal bloqueado");
            }

            if (System.IO.File.Exists(fullPath))
                return Resolution.File(fullPath);

            if (Directory.Exists(fullPath))
            {
                // Sin barra final el navegador resuelve mal las rutas relativas
                // del documento, asi que se corrige con un 301 antes de servir.
                if (!directoryRequested)
                {
                    string location = absolutePath + "/";
                    if (!string.IsNullOrEmpty(query)) location += query;
                    return Resolution.Redirect(location);
                }

                string directoryIndex = Path.Combine(fullPath, IndexFile);
                if (System.IO.File.Exists(directoryIndex))
                    return Resolution.File(directoryIndex);

                // Sin indice: o se lista el contenido o no hay nada que servir.
                // El repliegue de pagina unica no debe secuestrar este caso.
                if (allowDirectoryListing) return Resolution.Listing(fullPath);
                return Resolution.NotFound("Directorio sin index.html");
            }

            // Repliegue para aplicaciones de pagina unica: solo rutas sin
            // extension, para no devolver HTML cuando falta un .js o un .png.
            if (string.IsNullOrEmpty(Path.GetExtension(relative)))
            {
                string spaIndex = Path.Combine(rootPath, IndexFile);
                if (System.IO.File.Exists(spaIndex))
                    return Resolution.File(spaIndex);
            }

            return Resolution.NotFound("Archivo no encontrado");
        }
    }
}
