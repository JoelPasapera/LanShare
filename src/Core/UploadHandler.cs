using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LanShare.Core
{
    /// <summary>
    /// Atiende /__subir: la pagina con GET y la recepcion de archivos con POST.
    ///
    /// Es la unica parte del servidor que escribe en disco desde la red, asi
    /// que todo lo que entra se trata como hostil: el origen se comprueba, el
    /// nombre se sanea, el tamano se limita mientras llega (no al final), cada
    /// archivo se escribe primero con un nombre temporal y solo se le da el
    /// definitivo si llega completo, y nunca se sobrescribe nada.
    /// </summary>
    public sealed class UploadHandler
    {
        public const string Endpoint = "/__subir";

        private const int MaxFilesPerRequest = 200;
        private const int CopyBufferSize = 64 * 1024;

        /// <summary>Espacio que se deja siempre libre: llenar el disco del sistema rompe Windows.</summary>
        private const long ReserveFreeBytes = 512L * 1024 * 1024;

        /// <summary>Nombre de los temporales: "~lanshare-{guid}.parte".</summary>
        private const string TempPrefix = "~lanshare-";
        private const string TempSuffix = ".parte";

        private sealed class SavedFile
        {
            public readonly string Original;
            public readonly string Saved;
            public readonly long Bytes;

            public SavedFile(string original, string saved, long bytes)
            {
                Original = original;
                Saved = saved;
                Bytes = bytes;
            }
        }

        private readonly ServerOptions _options;
        private readonly LogBus _log;

        public UploadHandler(ServerOptions options, LogBus log)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (log == null) throw new ArgumentNullException("log");
            _options = options;
            _log = log;
        }

        /// <returns>Bytes enviados en la respuesta, para la contabilidad del cliente.</returns>
        public async Task<long> HandleAsync(HttpListenerContext context, SiteRuntime site, string rawUrl,
                                            string clientIp, Stopwatch sw)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string method = request.HttpMethod;

            if (method == "GET" || method == "HEAD")
            {
                byte[] page = Encoding.UTF8.GetBytes(UploadPage.Render(_options.UploadMaxBytes));
                response.StatusCode = 200;
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = page.Length;
                if (method == "GET") await response.OutputStream.WriteAsync(page, 0, page.Length).ConfigureAwait(false);

                long sent = method == "GET" ? page.Length : 0;
                Log(sw, "INFO", method, rawUrl, 200, sent, " - Pagina de subida", clientIp, site.Port);
                return sent;
            }

            if (method != "POST")
            {
                response.StatusCode = 405;
                response.AddHeader("Allow", "GET, HEAD, POST");
                Log(sw, "WARN", method, rawUrl, 405, 0, " - Metodo no permitido", clientIp, site.Port);
                return 0;
            }

            // La pagina pide JSON; el formulario sin JavaScript recibe HTML.
            bool json = request.Headers["X-LanShare-Upload"] == "1";

            if (!UploadGuard.IsSameOrigin(request.Headers["Origin"], request.Headers["Host"],
                                          request.Headers["Sec-Fetch-Site"], request.IsSecureConnection))
            {
                return await RejectAsync(response, 403, json,
                    "La subida solo se acepta desde la propia pagina de LanShare.",
                    sw, method, rawUrl, clientIp, site.Port, "SECURITY WARN").ConfigureAwait(false);
            }

            string boundary;
            if (!MultipartReader.TryGetBoundary(request.ContentType, out boundary))
            {
                return await RejectAsync(response, 400, json, "Se esperaba un formulario multipart.",
                                         sw, method, rawUrl, clientIp, site.Port, "WARN").ConfigureAwait(false);
            }

            long maxBytes = _options.UploadMaxBytes;

            // Margen para el formulario sin JavaScript, que envia varios
            // archivos en una sola peticion.
            long maxRequest = maxBytes > long.MaxValue / 8 ? long.MaxValue : maxBytes * 8;
            if (request.ContentLength64 > maxRequest)
            {
                return await RejectAsync(response, 413, json,
                    "El envio supera el maximo de " + UploadPage.HumanSize(maxBytes) + " por archivo.",
                    sw, method, rawUrl, clientIp, site.Port, "WARN").ConfigureAwait(false);
            }

            string folder;
            string problem = PrepareFolder(out folder);
            if (problem != null)
            {
                return await RejectAsync(response, 500, json, problem,
                                         sw, method, rawUrl, clientIp, site.Port, "ERROR").ConfigureAwait(false);
            }

            if (!HasRoom(folder, request.ContentLength64))
            {
                return await RejectAsync(response, 507, json, "No hay espacio suficiente en el disco del PC.",
                                         sw, method, rawUrl, clientIp, site.Port, "WARN").ConfigureAwait(false);
            }

            List<SavedFile> saved = new List<SavedFile>();
            string failure = null;
            int status = 200;

            try
            {
                MultipartReader reader = new MultipartReader(request.InputStream, boundary);
                MultipartPart part;
                int files = 0;

                while ((part = await reader.ReadNextPartAsync().ConfigureAwait(false)) != null)
                {
                    // Campos de texto y entradas de archivo vacias se descartan.
                    if (!part.IsFile || string.IsNullOrEmpty(part.FileName)) continue;

                    if (++files > MaxFilesPerRequest)
                        throw new MultipartLimitException("Demasiados archivos en una sola peticion.");

                    SavedFile file = await SaveAsync(reader, part, folder, maxBytes).ConfigureAwait(false);
                    saved.Add(file);

                    _log.Write("INFO", "Subida desde " + clientIp + ": " + file.Original + " (" +
                                       UploadPage.HumanSize(file.Bytes) + ")" +
                                       (file.Saved != file.Original ? " guardado como " + file.Saved : ""),
                               site.Port);
                }

                if (saved.Count == 0)
                {
                    status = 400;
                    failure = "No llego ningun archivo.";
                }
            }
            catch (MultipartLimitException ex)
            {
                status = 413;
                failure = ex.Message.IndexOf("Demasiados", StringComparison.Ordinal) >= 0
                    ? ex.Message
                    : "El archivo supera el maximo de " + UploadPage.HumanSize(maxBytes) + ".";
            }
            catch (MultipartException ex)
            {
                status = 400;
                failure = "El envio llego incompleto o mal formado: " + ex.Message;
            }
            catch (HttpListenerException)
            {
                status = 400;
                failure = "Se corto la conexion durante el envio.";
            }
            catch (IOException ex)
            {
                status = 500;
                failure = "No se pudo guardar en el PC: " + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                status = 500;
                failure = "Sin permiso para escribir en la carpeta de destino: " + ex.Message;
            }

            string summary = saved.Count + (saved.Count == 1 ? " archivo guardado" : " archivos guardados");
            if (failure != null) summary = failure + (saved.Count > 0 ? " (" + summary + " antes del fallo)" : "");

            response.StatusCode = status;
            long bytes = await WriteAsync(response, json, failure == null, failure, saved, summary).ConfigureAwait(false);
            Log(sw, failure == null ? "INFO" : "WARN", method, rawUrl, status, bytes,
                " - Subida: " + summary, clientIp, site.Port);
            return bytes;
        }

        private static async Task<SavedFile> SaveAsync(MultipartReader reader, MultipartPart part,
                                                       string folder, long maxBytes)
        {
            string name = UploadNaming.Sanitize(part.FileName);

            // Primero con un nombre temporal: si la conexion se corta a mitad,
            // no queda un archivo incompleto con aspecto de bueno.
            string temp = Path.Combine(folder, TempPrefix + Guid.NewGuid().ToString("N") + TempSuffix);
            bool committed = false;

            try
            {
                long bytes;
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                                                          FileShare.None, CopyBufferSize, FileOptions.Asynchronous))
                {
                    bytes = await reader.CopyBodyAsync(stream, maxBytes).ConfigureAwait(false);
                }

                string final = Commit(temp, folder, name);
                committed = true;
                return new SavedFile(name, Path.GetFileName(final), bytes);
            }
            finally
            {
                if (!committed) TryDelete(temp);
            }
        }

        /// <summary>
        /// Da el nombre definitivo sin sobrescribir nunca. File.Move falla si el
        /// destino existe, asi que dos subidas simultaneas con el mismo nombre
        /// no pueden pisarse: la que llega segunda pasa al numero siguiente.
        /// </summary>
        private static string Commit(string temp, string folder, string name)
        {
            for (int attempt = 1; attempt <= 9999; attempt++)
            {
                string candidate = Path.Combine(folder, UploadNaming.Candidate(name, attempt));
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;

                try
                {
                    File.Move(temp, candidate);
                    return candidate;
                }
                catch (IOException)
                {
                    if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                    throw;
                }
            }
            throw new IOException("No queda ningun nombre libre para " + name + ".");
        }

        /// <summary>
        /// Comprueba y crea la carpeta de destino. Nunca puede estar dentro de un
        /// sitio servido: lo subido quedaria publicado al instante, incluido un
        /// .html con script ejecutandose en el origen del sitio.
        /// </summary>
        private string PrepareFolder(out string folder)
        {
            folder = null;
            string configured = _options.UploadFolder;

            // IsPathRooted lanza con comillas, < > | o caracteres de control en
            // la ruta: dentro del try, el usuario ve el motivo y no un 500 mudo.
            string full;
            try
            {
                if (string.IsNullOrEmpty(configured) || !Path.IsPathRooted(configured))
                    return "La carpeta de destino de las subidas no esta configurada.";
                full = Path.GetFullPath(configured);
            }
            catch (Exception) { return "La carpeta de destino no es una ruta valida."; }

            // Con separador final en ambos lados: "C:\web\sitio2" no esta dentro
            // de "C:\web\sitio" aunque empiece igual.
            string candidate = WithSeparator(full);
            foreach (SiteRuntime site in _options.Sites)
            {
                if (candidate.StartsWith(WithSeparator(site.RootPath), StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(WithSeparator(site.RealRootPath), StringComparison.OrdinalIgnoreCase))
                {
                    return "La carpeta de destino esta dentro de un sitio servido (" + site.RootPath +
                           "): lo subido quedaria publicado. Elige otra en Configuracion.";
                }
            }

            try { Directory.CreateDirectory(full); }
            catch (Exception ex) { return "No se pudo crear la carpeta de destino: " + ex.Message; }

            CleanStaleTemporaries(full);
            folder = full;
            return null;
        }

        private static string WithSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\\";
            return path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
        }

        /// <summary>
        /// Borra temporales de subidas que no terminaron. Normalmente se borran
        /// solos, pero no si el proceso murio a mitad o un antivirus los tenia
        /// abiertos. Solo los de mas de un dia, para no tocar una subida que
        /// este en curso ahora mismo desde otra pestana.
        /// </summary>
        private static void CleanStaleTemporaries(string folder)
        {
            try
            {
                DateTime limit = DateTime.UtcNow.AddDays(-1);
                foreach (string path in Directory.EnumerateFiles(folder, TempPrefix + "*" + TempSuffix))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) < limit) File.Delete(path);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        private static bool HasRoom(string folder, long incoming)
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(folder));
                return drive.AvailableFreeSpace >= Math.Max(0, incoming) + ReserveFreeBytes;
            }
            catch (Exception)
            {
                // Rutas de red: sin dato fiable, no se bloquea por ello.
                return true;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private async Task<long> RejectAsync(HttpListenerResponse response, int status, bool json, string error,
                                             Stopwatch sw, string method, string rawUrl, string clientIp,
                                             int port, string level)
        {
            response.StatusCode = status;
            long bytes = await WriteAsync(response, json, false, error, null, error).ConfigureAwait(false);
            Log(sw, level, method, rawUrl, status, bytes, " - Subida rechazada: " + error, clientIp, port);
            return bytes;
        }

        private static async Task<long> WriteAsync(HttpListenerResponse response, bool json, bool ok,
                                                   string error, List<SavedFile> saved, string summary)
        {
            string body;
            if (json)
            {
                List<object> files = new List<object>();
                if (saved != null)
                {
                    foreach (SavedFile file in saved)
                    {
                        Dictionary<string, object> entry = new Dictionary<string, object>();
                        entry["name"] = file.Original;
                        entry["saved"] = file.Saved;
                        entry["bytes"] = file.Bytes;
                        files.Add(entry);
                    }
                }

                Dictionary<string, object> payload = new Dictionary<string, object>();
                payload["ok"] = ok;
                payload["error"] = error;
                payload["files"] = files;

                response.ContentType = "application/json; charset=utf-8";
                body = new JavaScriptSerializer().Serialize(payload);
            }
            else
            {
                response.ContentType = "text/html; charset=utf-8";
                body = UploadPage.RenderResult(summary, ok);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                return bytes.Length;
            }
            catch (Exception)
            {
                // El cliente ya no escucha (cerro la pestana o corto la red).
                return 0;
            }
        }

        private void Log(Stopwatch sw, string level, string method, string rawUrl, int status, long bytes,
                         string note, string clientIp, int port)
        {
            _log.WriteRequest(level, method, rawUrl, status, sw.ElapsedMilliseconds, bytes, note, clientIp, port);
        }
    }
}
