using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Genera el HTML del listado de una carpeta sin index.html.
    ///
    /// Unica responsabilidad: producir el documento. No decide si procede
    /// mostrarlo ni lo escribe en la respuesta.
    /// </summary>
    public static class DirectoryListing
    {
        /// <summary>
        /// Tope de entradas. Sin el, una carpeta con decenas de miles de
        /// archivos genera varios MB de HTML y congela el navegador del movil.
        /// </summary>
        private const int MaxEntries = 3000;

        public static byte[] Build(string directoryPath, string urlPath)
        {
            StringBuilder html = new StringBuilder(8192);
            string titulo = WebUtility.HtmlEncode(urlPath);

            html.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.Append("<title>").Append(titulo).Append("</title>");
            html.Append(Style);
            html.Append("</head><body><main>");
            html.Append("<h1>").Append(titulo).Append("</h1>");
            html.Append("<table><thead><tr><th>Nombre</th><th class=\"n\">Tamano</th>");
            html.Append("<th class=\"n\">Modificado</th></tr></thead><tbody>");

            if (urlPath != "/")
            {
                html.Append("<tr><td colspan=\"3\"><a class=\"up\" href=\"..\">");
                html.Append("&#8593; Subir un nivel</a></td></tr>");
            }

            DirectoryInfo info = new DirectoryInfo(directoryPath);
            int carpetas = 0, archivos = 0, mostradas = 0;
            long total = 0;
            bool truncado = false;

            try
            {
                // Enumerar en flujo y no con GetDirectories/GetFiles: una carpeta
                // con cientos de miles de entradas materializaria todo el array
                // en memoria antes siquiera de aplicar el tope.
                foreach (DirectoryInfo sub in info.EnumerateDirectories())
                {
                    if (IsHidden(sub.Attributes)) continue;
                    carpetas++;
                    if (mostradas >= MaxEntries) { truncado = true; continue; }
                    AppendRow(html, sub.Name + "/", sub.Name, "", sub.LastWriteTime, true);
                    mostradas++;
                }

                foreach (FileInfo file in info.EnumerateFiles())
                {
                    if (IsHidden(file.Attributes)) continue;
                    archivos++;
                    total += file.Length;
                    if (mostradas >= MaxEntries) { truncado = true; continue; }
                    AppendRow(html, file.Name, file.Name, FormatSize(file.Length), file.LastWriteTime, false);
                    mostradas++;
                }
            }
            catch (Exception ex)
            {
                html.Append("<tr><td colspan=\"3\" class=\"err\">No se pudo leer la carpeta: ");
                html.Append(WebUtility.HtmlEncode(ex.Message)).Append("</td></tr>");
            }

            html.Append("</tbody></table>");

            if (truncado)
            {
                html.Append("<p class=\"aviso\">Mostrando las primeras ")
                    .Append(MaxEntries)
                    .Append(" entradas. La carpeta contiene mas.</p>");
            }

            html.Append("<p class=\"pie\">")
                .Append(carpetas).Append(carpetas == 1 ? " carpeta" : " carpetas")
                .Append(" &middot; ")
                .Append(archivos).Append(archivos == 1 ? " archivo" : " archivos")
                .Append(" &middot; ").Append(FormatSize(total))
                .Append("</p>");
            html.Append("</main></body></html>");

            return Encoding.UTF8.GetBytes(html.ToString());
        }

        private static void AppendRow(StringBuilder html, string linkTarget, string display,
                                      string size, DateTime modified, bool isDirectory)
        {
            // Escapado doble y distinto: el href se codifica como URL y el texto
            // visible como HTML. Confundirlos es como se cuelan las inyecciones.
            string href = Uri.EscapeDataString(linkTarget.TrimEnd('/'));
            if (isDirectory) href += "/";

            html.Append("<tr><td><a href=\"").Append(href).Append("\">");
            html.Append(isDirectory ? "<span class=\"ic d\"></span>" : "<span class=\"ic f\"></span>");
            html.Append(WebUtility.HtmlEncode(display));
            if (isDirectory) html.Append("/");
            html.Append("</a></td>");
            html.Append("<td class=\"n\">").Append(size).Append("</td>");
            html.Append("<td class=\"n\">")
                .Append(modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .Append("</td></tr>");
        }

        private static bool IsHidden(FileAttributes attributes)
        {
            return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden ||
                   (attributes & FileAttributes.System) == FileAttributes.System;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("N1", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1073741824) return (bytes / 1048576.0).ToString("N1", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1073741824.0).ToString("N2", CultureInfo.InvariantCulture) + " GB";
        }

        private const string Style =
@"<style>
:root{--bg:#151a23;--panel:#1d242f;--line:#2b3543;--txt:#e7edf4;--dim:#8fa1b5;--ok:#28af4a}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--txt);
 font:14px/1.5 -apple-system,'Segoe UI',Roboto,sans-serif}
main{max-width:900px;margin:0 auto;padding:28px 18px}
h1{font-size:17px;font-weight:600;margin:0 0 18px;word-break:break-all;color:var(--dim)}
table{width:100%;border-collapse:collapse;background:var(--panel);
 border:1px solid var(--line);border-radius:8px;overflow:hidden}
th,td{padding:9px 14px;text-align:left;border-bottom:1px solid var(--line)}
th{font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--dim);font-weight:600}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover{background:#232c39}
td.n,th.n{text-align:right;color:var(--dim);white-space:nowrap;font-variant-numeric:tabular-nums}
a{color:var(--txt);text-decoration:none;display:flex;align-items:center;gap:9px;word-break:break-all}
a:hover{color:var(--ok)}
a.up{color:var(--dim)}
.ic{width:9px;height:9px;border-radius:50%;flex:0 0 9px}
.ic.d{background:var(--ok)}
.ic.f{background:var(--dim);opacity:.55}
.pie{margin-top:14px;color:var(--dim);font-size:12px;text-align:right}
.err{color:#ff8f8f}
.aviso{margin-top:14px;color:#e0b34d;font-size:12px}
@media(max-width:560px){th:nth-child(3),td:nth-child(3){display:none}}
</style>";
    }
}
