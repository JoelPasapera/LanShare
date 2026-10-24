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
    ///
    /// La ruta de arriba es navegable tramo a tramo, la carpeta entera se puede
    /// descargar en ZIP y la tabla se ordena por cualquier columna y se filtra
    /// por nombre con un script pequeno incluido en el propio documento. Sin
    /// JavaScript sigue siendo una tabla normal.
    /// </summary>
    public static class DirectoryListing
    {
        /// <summary>
        /// Tope de entradas. Sin el, una carpeta con decenas de miles de
        /// archivos genera varios MB de HTML y congela el navegador del movil.
        /// </summary>
        private const int MaxEntries = 3000;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static byte[] Build(string directoryPath, string urlPath)
        {
            return Build(directoryPath, urlPath, false);
        }

        /// <param name="uploadLink">
        /// Muestra un enlace a /__subir. Desde el movil se llega al listado con
        /// el QR; sin el enlace habria que teclear la ruta de la subida a mano.
        /// </param>
        public static byte[] Build(string directoryPath, string urlPath, bool uploadLink)
        {
            if (string.IsNullOrEmpty(urlPath)) urlPath = "/";

            StringBuilder html = new StringBuilder(8192);
            string titulo = WebUtility.HtmlEncode(urlPath);

            html.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.Append("<title>").Append(titulo).Append("</title>");
            html.Append(Style);
            html.Append("</head><body><main>");
            AppendBreadcrumb(html, directoryPath, urlPath);

            // La descarga en ZIP va siempre: quien ve el listado ya puede bajar
            // cada archivo, asi que el ZIP no expone nada nuevo.
            html.Append("<p class=\"acciones\"><a href=\"?zip\">&#8681; Descargar la carpeta (.zip)</a>");
            if (uploadLink)
            {
                html.Append("<a href=\"").Append(UploadHandler.Endpoint)
                    .Append("\">&#8679; Enviar archivos a este PC</a>");
            }
            html.Append("</p>");
            html.Append("<table id=\"lista\"><thead><tr><th data-k=\"n\">Nombre</th>");
            html.Append("<th class=\"n\" data-k=\"s\">Tamano</th>");
            html.Append("<th class=\"n\" data-k=\"t\">Modificado</th></tr></thead><tbody>");

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
                    AppendRow(html, sub.Name, -1, sub.LastWriteTime, sub.LastWriteTimeUtc, true);
                    mostradas++;
                }

                foreach (FileInfo file in info.EnumerateFiles())
                {
                    if (IsHidden(file.Attributes)) continue;
                    archivos++;
                    total += file.Length;
                    if (mostradas >= MaxEntries) { truncado = true; continue; }
                    AppendRow(html, file.Name, file.Length, file.LastWriteTime, file.LastWriteTimeUtc, false);
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
            html.Append(Script);
            html.Append("</main></body></html>");

            return Encoding.UTF8.GetBytes(html.ToString());
        }

        /// <summary>
        /// La ruta como migas navegables: cada tramo enlaza a su carpeta y el
        /// primero lleva el nombre de la carpeta del sitio. Desde el movil es la
        /// unica forma comoda de subir varios niveles de golpe.
        /// </summary>
        private static void AppendBreadcrumb(StringBuilder html, string directoryPath, string urlPath)
        {
            string[] segments = urlPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string rootName = WebUtility.HtmlEncode(RootName(directoryPath, segments.Length));

            html.Append("<h1 class=\"ruta\">");
            if (segments.Length == 0)
            {
                html.Append("<b>").Append(rootName).Append("</b>");
            }
            else
            {
                html.Append("<a href=\"/\">").Append(rootName).Append("</a>");

                // Mismo doble escapado que las filas: el enlace como URL, el
                // texto como HTML.
                StringBuilder href = new StringBuilder("/");
                for (int i = 0; i < segments.Length; i++)
                {
                    href.Append(Uri.EscapeDataString(segments[i])).Append('/');
                    string text = WebUtility.HtmlEncode(segments[i]);

                    html.Append("<i>/</i>");
                    if (i == segments.Length - 1) html.Append("<b>").Append(text).Append("</b>");
                    else html.Append("<a href=\"").Append(href.ToString()).Append("\">").Append(text).Append("</a>");
                }
            }
            html.Append("</h1>");
        }

        /// <summary>
        /// Nombre de la carpeta raiz del sitio: se sube desde la carpeta listada
        /// tantos niveles como tramos tiene la URL. "/" si no se puede saber.
        /// </summary>
        private static string RootName(string directoryPath, int depth)
        {
            try
            {
                string root = Path.GetFullPath(directoryPath)
                                  .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                for (int i = 0; i < depth && !string.IsNullOrEmpty(root); i++) root = Path.GetDirectoryName(root);

                string name = string.IsNullOrEmpty(root) ? "" : Path.GetFileName(root);
                return string.IsNullOrEmpty(name) ? "/" : name;
            }
            catch (Exception) { return "/"; }
        }

        /// <param name="bytes">Tamano en bytes; -1 en las carpetas.</param>
        private static void AppendRow(StringBuilder html, string name, long bytes,
                                      DateTime modified, DateTime modifiedUtc, bool isDirectory)
        {
            // Valores en bruto para ordenar en el navegador: el tamano exacto y
            // la fecha en milisegundos Unix, que caben sin perdida en un Number
            // de JavaScript (los ticks de .NET no).
            html.Append("<tr data-s=\"").Append(bytes.ToString(CultureInfo.InvariantCulture))
                .Append("\" data-t=\"").Append(UnixMilliseconds(modifiedUtc).ToString(CultureInfo.InvariantCulture))
                .Append("\">");

            // Escapado doble y distinto: el href se codifica como URL y el texto
            // visible como HTML. Confundirlos es como se cuelan las inyecciones.
            string href = Uri.EscapeDataString(name);
            if (isDirectory) href += "/";

            html.Append("<td><a href=\"").Append(href).Append("\">");
            html.Append(isDirectory ? "<span class=\"ic d\"></span>" : "<span class=\"ic f\"></span>");
            html.Append(WebUtility.HtmlEncode(name));
            if (isDirectory) html.Append("/");
            html.Append("</a></td>");
            html.Append("<td class=\"n\">").Append(isDirectory ? "" : FormatSize(bytes)).Append("</td>");
            html.Append("<td class=\"n\">")
                .Append(modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                .Append("</td></tr>");
        }

        private static long UnixMilliseconds(DateTime utc)
        {
            if (utc <= UnixEpoch) return 0;
            return (long)(utc - UnixEpoch).TotalMilliseconds;
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
.ruta a{color:var(--dim);text-decoration:none}
.ruta a:hover{color:var(--ok)}
.ruta b{color:var(--txt);font-weight:600}
.ruta i{font-style:normal;margin:0 7px;opacity:.45}
.acciones{margin:0 0 14px;display:flex;flex-wrap:wrap;gap:6px 22px}
.acciones a{color:var(--ok);text-decoration:none}
.acciones a:hover{text-decoration:underline}
.filtro{width:100%;margin:0 0 12px;padding:9px 12px;border:1px solid var(--line);border-radius:8px;
 background:var(--panel);color:var(--txt);font:inherit}
table{width:100%;border-collapse:collapse;background:var(--panel);
 border:1px solid var(--line);border-radius:8px;overflow:hidden}
th,td{padding:9px 14px;text-align:left;border-bottom:1px solid var(--line)}
th{font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--dim);font-weight:600}
th[data-k]{cursor:pointer;user-select:none;-webkit-user-select:none}
th[aria-sort]::after{content:'';display:inline-block;margin-left:6px;vertical-align:1px;
 border:4px solid transparent}
th[aria-sort=ascending]::after{border-bottom-color:currentColor;border-top-width:0}
th[aria-sort=descending]::after{border-top-color:currentColor;border-bottom-width:0}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover{background:#232c39}
td.n,th.n{text-align:right;color:var(--dim);white-space:nowrap;font-variant-numeric:tabular-nums}
td a{color:var(--txt);text-decoration:none;display:flex;align-items:center;gap:9px;word-break:break-all}
td a:hover{color:var(--ok)}
td a.up{color:var(--dim)}
.ic{width:9px;height:9px;border-radius:50%;flex:0 0 9px}
.ic.d{background:var(--ok)}
.ic.f{background:var(--dim);opacity:.55}
.pie{margin-top:14px;color:var(--dim);font-size:12px;text-align:right}
.err{color:#ff8f8f}
.aviso{margin-top:14px;color:#e0b34d;font-size:12px}
@media(max-width:560px){th:nth-child(3),td:nth-child(3){display:none}}
</style>";

        /// <summary>
        /// Ordenar por columna (las carpetas siempre delante; tamano y fecha
        /// empiezan por lo mayor y lo mas reciente) y filtrar por nombre a
        /// partir de ocho entradas. Sin comillas dobles, como los demas scripts.
        /// </summary>
        private const string Script =
@"<script>(function(){
var table=document.getElementById('lista');
if(!table||!table.tHead||!table.tBodies.length)return;
var body=table.tBodies[0],rows=[],heads=table.tHead.rows[0].cells,key='',dir=1,i;
for(i=0;i<body.rows.length;i++)if(body.rows[i].hasAttribute('data-s'))rows.push(body.rows[i]);
if(rows.length<2)return;
function num(r,a){return Number(r.getAttribute(a))||0;}
function name(r){return String(r.cells[0].textContent||'').toLowerCase();}
function compare(a,b){
var da=a.getAttribute('data-s')==='-1',db=b.getAttribute('data-s')==='-1';
if(da!==db)return da?-1:1;
var c=key==='s'?num(a,'data-s')-num(b,'data-s'):key==='t'?num(a,'data-t')-num(b,'data-t'):
name(a).localeCompare(name(b),undefined,{numeric:true});
return c*dir;}
function sort(k){
dir=key===k?-dir:(k==='n'?1:-1);key=k;
rows.sort(compare);
for(var j=0;j<rows.length;j++)body.appendChild(rows[j]);
for(j=0;j<heads.length;j++){
if(heads[j].getAttribute('data-k')===k)heads[j].setAttribute('aria-sort',dir>0?'ascending':'descending');
else heads[j].removeAttribute('aria-sort');}}
function bind(cell){
var k=cell.getAttribute('data-k');if(!k)return;
cell.tabIndex=0;
cell.onclick=function(){sort(k);};
cell.onkeydown=function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();sort(k);}};}
for(i=0;i<heads.length;i++)bind(heads[i]);
if(rows.length<8)return;
var input=document.createElement('input');
input.type='search';input.className='filtro';input.placeholder='Filtrar por nombre';
input.setAttribute('aria-label','Filtrar por nombre');
table.parentNode.insertBefore(input,table);
input.oninput=function(){
var q=input.value.toLowerCase();
for(var j=0;j<rows.length;j++)rows[j].style.display=!q||name(rows[j]).indexOf(q)>=0?'':'none';};
})();</script>";
    }
}
