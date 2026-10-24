using System;

namespace LanShare.Core
{
    /// <summary>
    /// Inserta scripts en una pagina HTML al servirla, sin tocar el archivo.
    ///
    /// Antes la misma insercion estaba copiada en tres sitios (archivo, HTML
    /// leido y listado de carpetas). Ahora hay tres scripts posibles, sonda,
    /// recarga y consola remota, y la combinacion decide tambien el validador
    /// y la clave de la cache: tenerlo en un solo sitio evita que diverjan.
    /// </summary>
    public static class HtmlInjector
    {
        /// <summary>
        /// Inserta antes del ultimo &lt;/body&gt;. Si la pagina no lo tiene
        /// (HTML parcial o mal formado), se anade al final: los navegadores
        /// ejecutan igualmente un script suelto tras el documento.
        /// </summary>
        public static string Inject(string html, string snippet)
        {
            if (string.IsNullOrEmpty(snippet)) return html ?? "";
            if (string.IsNullOrEmpty(html)) return snippet;

            int close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return close >= 0
                ? html.Substring(0, close) + snippet + html.Substring(close)
                : html + snippet;
        }

        /// <summary>
        /// Inserta justo despues de la etiqueta de apertura de &lt;head&gt;, para
        /// que el script se ejecute antes que cualquier otro de la pagina. Sin
        /// &lt;head&gt;, despues de &lt;html&gt;; sin ninguna de las dos, tras el
        /// DOCTYPE o al principio.
        ///
        /// Nunca antes del DOCTYPE: el navegador pasaria a modo quirks y la
        /// pagina se veria distinta solo por haber activado una opcion.
        /// </summary>
        public static string InjectHead(string html, string snippet)
        {
            if (string.IsNullOrEmpty(snippet)) return html ?? "";
            if (string.IsNullOrEmpty(html)) return snippet;

            int at = AfterOpeningTag(html, "head");
            if (at < 0) at = AfterOpeningTag(html, "html");
            if (at < 0) at = AfterDoctype(html);
            return html.Substring(0, at) + snippet + html.Substring(at);
        }

        /// <summary>El fragmento a inyectar al final segun lo que este activo. Vacio si nada.</summary>
        public static string Compose(bool probe, bool liveReload)
        {
            if (!probe && !liveReload) return "";
            if (probe && !liveReload) return Identity.ProbeScript.Html;
            if (!probe) return LiveReloadScript.Html;
            return Identity.ProbeScript.Html + LiveReloadScript.Html;
        }

        /// <summary>
        /// Sufijo que distingue cada combinacion en el ETag y en la cache de
        /// compresion. Sin el, activar la recarga con la pagina ya en cache
        /// serviria la version sin el script hasta que cambiase el archivo.
        /// </summary>
        public static string Variant(bool probe, bool liveReload)
        {
            return Variant(probe, liveReload, false);
        }

        public static string Variant(bool probe, bool liveReload, bool remoteConsole)
        {
            if (!probe && !liveReload && !remoteConsole) return "";
            return "i" + (probe ? "p" : "") + (liveReload ? "r" : "") + (remoteConsole ? "c" : "");
        }

        /// <summary>
        /// Posicion tras el '&gt;' de la primera etiqueta &lt;name ...&gt;, o -1.
        /// "&lt;header&gt;" no cuenta como "&lt;head&gt;": tras el nombre tiene que
        /// venir un espacio, '/' o '&gt;'.
        /// </summary>
        private static int AfterOpeningTag(string html, string name)
        {
            string open = "<" + name;
            int from = 0;
            while (from < html.Length)
            {
                int start = html.IndexOf(open, from, StringComparison.OrdinalIgnoreCase);
                if (start < 0) return -1;

                int next = start + open.Length;
                if (next < html.Length && IsTagNameEnd(html[next]))
                {
                    int close = html.IndexOf('>', next);
                    return close < 0 ? -1 : close + 1;
                }
                from = next;
            }
            return -1;
        }

        private static bool IsTagNameEnd(char c)
        {
            return c == '>' || c == '/' || c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f';
        }

        /// <summary>Posicion tras el DOCTYPE, o 0 si no hay.</summary>
        private static int AfterDoctype(string html)
        {
            int start = html.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return 0;
            int close = html.IndexOf('>', start);
            return close < 0 ? 0 : close + 1;
        }
    }
}
