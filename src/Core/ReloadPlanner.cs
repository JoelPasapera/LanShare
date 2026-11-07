using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LanShare.Core
{
    public enum ReloadKind
    {
        /// <summary>Solo cambiaron archivos que no afectan a la pagina.</summary>
        None,
        /// <summary>Solo cambiaron hojas de estilo: se cambian en caliente.</summary>
        Css,
        /// <summary>Cualquier otra cosa: recarga completa.</summary>
        Full
    }

    public sealed class ReloadPlan
    {
        public ReloadKind Kind { get; private set; }

        /// <summary>Rutas URL de las hojas de estilo cambiadas, con '/' inicial y sin escapar.</summary>
        public IList<string> CssPaths { get; private set; }

        public ReloadPlan(ReloadKind kind, IList<string> cssPaths)
        {
            Kind = kind;
            CssPaths = cssPaths ?? new List<string>();
        }

        /// <summary>
        /// Mensaje para el navegador: la primera linea es la orden y, para CSS,
        /// cada linea siguiente una ruta. Los nombres de archivo de Windows no
        /// pueden llevar saltos de linea, asi que el separador es seguro.
        /// </summary>
        public string ToMessage()
        {
            if (Kind != ReloadKind.Css) return "reload";

            StringBuilder sb = new StringBuilder("css");
            foreach (string path in CssPaths) sb.Append('\n').Append(path);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Decide que hacer con un lote de cambios en disco. Es logica pura: no
    /// toca el sistema de archivos, y por eso se puede probar sin vigilar nada.
    /// </summary>
    public static class ReloadPlanner
    {
        private static readonly string[] IgnoredFolders = { ".git", "node_modules", ".vs", ".idea", ".vscode" };

        /// <summary>
        /// Archivos temporales de editores, descargas y del sistema. Guardar en
        /// muchos editores es escribir un temporal y renombrarlo: el temporal no
        /// debe provocar una recarga, el renombrado al nombre real si. Los
        /// "___jb_" son los del guardado seguro de los IDE de JetBrains.
        /// </summary>
        private static readonly string[] IgnoredSuffixes =
            { "~", ".tmp", ".temp", ".swp", ".swx", ".swo", ".crdownload", ".part", ".bak",
              "___jb_tmp___", "___jb_old___" };

        private static readonly string[] IgnoredNames = { "thumbs.db", "desktop.ini", ".ds_store" };

        public static bool IsIgnored(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return true;

            string[] parts = relativePath.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return true;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                foreach (string folder in IgnoredFolders)
                {
                    if (string.Equals(parts[i], folder, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            string name = parts[parts.Length - 1];
            string lower = name.ToLowerInvariant();

            // La propia carpeta ignorada, si el cambio es sobre ella.
            foreach (string folder in IgnoredFolders)
            {
                if (lower == folder) return true;
            }
            foreach (string ignored in IgnoredNames)
            {
                if (lower == ignored) return true;
            }
            foreach (string suffix in IgnoredSuffixes)
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal)) return true;
            }

            // Bloqueos de Office (~$doc.docx) y de Emacs (.#archivo, #archivo#).
            if (lower.StartsWith("~$", StringComparison.Ordinal) || lower.StartsWith(".#", StringComparison.Ordinal))
                return true;
            if (lower.Length > 1 && lower[0] == '#' && lower[lower.Length - 1] == '#') return true;

            return false;
        }

        /// <summary>"css\app.css" pasa a "/css/app.css".</summary>
        public static string ToUrlPath(string relativePath)
        {
            if (relativePath == null) return "/";
            string path = relativePath.Replace('\\', '/');
            while (path.StartsWith("/", StringComparison.Ordinal)) path = path.Substring(1);
            return "/" + path;
        }

        public static ReloadPlan Plan(IEnumerable<string> relativePaths, bool overflow)
        {
            // Si el vigilante perdio eventos, no se sabe que cambio: lo seguro
            // es recargar entero.
            if (overflow) return new ReloadPlan(ReloadKind.Full, null);

            List<string> css = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool any = false;
            bool onlyCss = true;

            if (relativePaths != null)
            {
                foreach (string path in relativePaths)
                {
                    if (IsIgnored(path)) continue;
                    any = true;

                    if (string.Equals(Path.GetExtension(path), ".css", StringComparison.OrdinalIgnoreCase))
                    {
                        string url = ToUrlPath(path);
                        if (seen.Add(url)) css.Add(url);
                    }
                    else
                    {
                        onlyCss = false;
                    }
                }
            }

            if (!any) return new ReloadPlan(ReloadKind.None, null);
            if (onlyCss) return new ReloadPlan(ReloadKind.Css, css);
            return new ReloadPlan(ReloadKind.Full, null);
        }
    }
}
