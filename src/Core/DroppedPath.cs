using System;
using System.IO;

namespace LanShare.Core
{
    /// <summary>
    /// Que sitio proponer para algo arrastrado sobre la ventana: una carpeta
    /// se sirve tal cual; un archivo, desde su carpeta y, si es una pagina,
    /// abriendo esa pagina.
    /// </summary>
    public static class DroppedPath
    {
        /// <param name="startFile">
        /// Pagina de inicio propuesta: "" para listar la carpeta. Nunca null
        /// si devuelve true.
        /// </param>
        /// <returns>false si la ruta no existe.</returns>
        public static bool TryResolve(string path, out string root, out string startFile)
        {
            root = null;
            startFile = null;
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                if (Directory.Exists(path))
                {
                    root = Path.GetFullPath(path);
                    startFile = SuggestStartFile(root);
                    return true;
                }

                if (File.Exists(path))
                {
                    string full = Path.GetFullPath(path);
                    root = Path.GetDirectoryName(full);
                    if (string.IsNullOrEmpty(root)) return false;

                    string name = Path.GetFileName(full);
                    startFile = IsPage(name) ? name : SuggestStartFile(root);
                    return true;
                }
            }
            catch (Exception) { }

            root = null;
            startFile = null;
            return false;
        }

        /// <summary>
        /// index.html si existe; si no, la unica pagina de la carpeta; con
        /// varias o ninguna, el listado, para elegir desde el navegador.
        /// </summary>
        public static string SuggestStartFile(string root)
        {
            string only = null;
            int pages = 0;

            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.htm*"))
                {
                    string name = Path.GetFileName(file);
                    if (!IsPage(name)) continue;
                    if (string.Equals(name, StaticFileResolver.DefaultIndexFile, StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                    only = name;
                    pages++;
                }
            }
            catch (Exception) { return ""; }

            return pages == 1 ? only : "";
        }

        /// <summary>
        /// Solo .html y .htm. El patron "*.htm*" de Windows tambien devuelve
        /// .html~ o .htmlx; esos no son paginas.
        /// </summary>
        private static bool IsPage(string name)
        {
            string extension = Path.GetExtension(name);
            return string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase);
        }
    }
}
