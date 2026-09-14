using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ServidorWebPro.Identity;

namespace ServidorWebPro.Core
{
    /// <summary>
    /// Servidor de archivos estaticos sobre HttpListener.
    ///
    /// Cada peticion es una maquina de estados asincrona sobre el thread pool
    /// de .NET: no hay un hilo dedicado por conexion ni estructura de despacho
    /// propia. Mientras un archivo viaja por la red el hilo queda libre para
    /// atender otras peticiones, que es justo lo contrario de lo que hacia la
    /// version anterior en PowerShell.
    /// </summary>
    public sealed class HttpFileServer : IDisposable
    {
        private const string ProbeEndpoint = "/__probe";
        private const long MaxInjectableSize = 4L * 1024 * 1024;
        private const long MaxProbeBodySize = 64 * 1024;

        private readonly ServerOptions _options;
        private readonly LogBus _log;
        private readonly HttpListener _listener;

        // Un bool volatil en lugar de CancellationTokenSource: el token no se
        // usa para nada mas, y liberarlo mientras una copia en vuelo todavia
        // lo consulta provocaba ObjectDisposedException a media transferencia.
        private volatile bool _stopping;

        private Task _acceptLoop;
        private int _inFlight;
        private int _disposed;

        public bool InjectProbe { get; set; }

        /// <summary>Opciones vivas: cambiarlas surte efecto en la siguiente peticion.</summary>
        public ServerOptions Options { get { return _options; } }

        /// <summary>
        /// Se dispara si el bucle de aceptacion cae por un error inesperado.
        /// Sin esto el servidor dejaria de atender sin que nadie se entere.
        /// Llega desde un hilo del pool: hay que marshalear a la interfaz.
        /// </summary>
        public event EventHandler Faulted;

        public HttpFileServer(ServerOptions options, LogBus log)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (log == null) throw new ArgumentNullException("log");

            _options = options;
            _log = log;

            _listener = new HttpListener();
            _listener.Prefixes.Add(options.Prefix);
        }

        /// <summary>
        /// Arranca el listener de forma sincrona: si el puerto esta ocupado o
        /// falta la ACL, la excepcion sale aqui y la interfaz puede mostrarla
        /// antes de dar el servidor por iniciado.
        /// </summary>
        public void Start()
        {
            _listener.Start();
            _acceptLoop = AcceptLoopAsync();
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _stopping = true;
            try { _listener.Stop(); } catch (Exception) { }

            if (_acceptLoop != null)
            {
                try { await Task.WhenAny(_acceptLoop, Task.Delay(2000)).ConfigureAwait(false); }
                catch (Exception) { }
            }

            // Margen breve para que las transferencias en curso cierren limpio.
            for (int i = 0; i < 20 && Volatile.Read(ref _inFlight) > 0; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }

            try { _listener.Close(); } catch (Exception) { }
        }

        public void Dispose()
        {
            try { StopAsync().Wait(3000); } catch (Exception) { }
        }

        // ------------------------------------------------------------------

        private async Task AcceptLoopAsync()
        {
            while (!_stopping && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) { break; }   // Stop() en curso
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
                catch (Exception ex)
                {
                    // Cualquier otro fallo tumbaria el bucle en silencio.
                    if (_stopping) break;
                    _log.Write("ERROR", "El bucle de aceptacion se detuvo: " +
                                        ex.GetType().Name + ": " + ex.Message);
                    RaiseFaulted();
                    break;
                }

                // Sin await: la aceptacion de la siguiente conexion no debe
                // esperar a que esta termine de transferirse.
                Task ignored = HandleSafelyAsync(context);
            }
        }

        private void RaiseFaulted()
        {
            EventHandler handler = Faulted;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); } catch (Exception) { }
            }
        }

        private async Task HandleSafelyAsync(HttpListenerContext context)
        {
            Interlocked.Increment(ref _inFlight);
            try
            {
                await HandleAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Write("ERROR", "Fallo no controlado en el handler: " + ex.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            Stopwatch sw = Stopwatch.StartNew();
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            string clientIp = "?";
            int sourcePort = 0;
            if (request.RemoteEndPoint != null)
            {
                clientIp = request.RemoteEndPoint.Address.ToString();
                sourcePort = request.RemoteEndPoint.Port;
            }

            string method = request.HttpMethod;
            string rawUrl = request.Url != null ? request.Url.PathAndQuery : "?";
            long bytesSent = 0;

            try
            {
                if (_options.CorsEnabled)
                {
                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, POST, OPTIONS");
                    response.AddHeader("Access-Control-Allow-Headers", "*");
                }
                // En desarrollo se prohibe cachear para que nunca veas una
                // version vieja de tu propio trabajo. En distribucion se deja
                // revalidar: el cliente pregunta y suele recibir un 304.
                response.AddHeader("Cache-Control", _options.DistributionMode
                    ? "no-cache"
                    : "no-store, no-cache, must-revalidate, max-age=0");
                response.AddHeader("X-Content-Type-Options", "nosniff");

                if (method == "OPTIONS")
                {
                    response.StatusCode = 204;
                    Log(sw, "INFO", method, rawUrl, response, 0, "", clientIp);
                    return;
                }

                if (request.Url == null)
                {
                    response.StatusCode = 400;
                    Log(sw, "WARN", method, rawUrl, response, 0, " - URL invalida", clientIp);
                    return;
                }

                if (string.Equals(request.Url.LocalPath, ProbeEndpoint, StringComparison.Ordinal))
                {
                    await HandleProbeAsync(request, response, sw, method, rawUrl, clientIp).ConfigureAwait(false);
                    return;
                }

                if (method != "GET" && method != "HEAD")
                {
                    response.StatusCode = 405;
                    response.AddHeader("Allow", "GET, HEAD, OPTIONS");
                    Log(sw, "WARN", method, rawUrl, response, 0, " - Metodo no permitido", clientIp);
                    return;
                }

                Resolution resolution = StaticFileResolver.Resolve(
                    request.Url.LocalPath, request.Url.AbsolutePath, request.Url.Query,
                    _options.RootPath, _options.EnableDirectoryListing);

                switch (resolution.Kind)
                {
                    case ResolutionKind.BadRequest:
                        response.StatusCode = 400;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp);
                        return;

                    case ResolutionKind.Forbidden:
                        response.StatusCode = 403;
                        Log(sw, "SECURITY WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp);
                        return;

                    case ResolutionKind.NotFound:
                        response.StatusCode = 404;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp);
                        return;

                    case ResolutionKind.RedirectToDirectory:
                        response.StatusCode = 301;
                        response.AddHeader("Location", resolution.RedirectLocation);
                        Log(sw, "INFO", method, rawUrl, response, 0, " - Redirigido a directorio", clientIp);
                        return;

                    case ResolutionKind.DirectoryListing:
                        bytesSent = await ServeListingAsync(resolution.DirectoryPath, request, response,
                                                            sw, method, rawUrl, clientIp).ConfigureAwait(false);
                        return;
                }

                bytesSent = await ServeFileAsync(resolution.FilePath, request, response, sw,
                                                 method, rawUrl, clientIp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { response.StatusCode = 500; } catch (Exception) { }
                sw.Stop();
                _log.Write("ERROR", string.Format("{0} {1} - Client: {2}\r\nException [{3}]:\r\n{4}",
                                                  method, rawUrl, clientIp, ex.GetType().FullName, ex.Message));
            }
            finally
            {
                TrackClient(request, clientIp, sourcePort, bytesSent);
                try { response.Close(); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Punto unico de salida para respuestas que caben en memoria (listado
        /// e HTML con sonda). Centralizar aqui la compresion evita duplicar la
        /// negociacion en cada ruta.
        /// </summary>
        private async Task<long> WriteBufferedAsync(HttpListenerResponse response, byte[] payload,
                                                    string acceptEncoding, bool allowCompression,
                                                    Stopwatch sw, string level, string method,
                                                    string rawUrl, string clientIp, string note)
        {
            byte[] body = payload;

            if (allowCompression && _options.EnableCompression &&
                CompressionPolicy.ClientAcceptsGzip(acceptEncoding) &&
                CompressionPolicy.IsCompressible(response.ContentType, payload.Length))
            {
                byte[] compressed = CompressionPolicy.Gzip(payload);
                if (compressed != null)
                {
                    body = compressed;
                    response.AddHeader("Content-Encoding", "gzip");
                    response.AddHeader("Vary", "Accept-Encoding");
                    note += string.Format(" [gzip {0}%]", 100 - (compressed.Length * 100 / payload.Length));
                }
            }

            response.ContentLength64 = body.Length;

            bool aborted = false;
            try
            {
                await response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            }
            catch (HttpListenerException) { aborted = true; }
            catch (IOException) { aborted = true; }
            catch (ObjectDisposedException) { aborted = true; }

            long sent = aborted ? 0 : body.Length;
            Log(sw, level, method, rawUrl, response, sent,
                aborted ? " - Transferencia interrumpida" : note, clientIp);
            return sent;
        }

        private async Task<long> ServeListingAsync(string directoryPath, HttpListenerRequest request,
                                                   HttpListenerResponse response, Stopwatch sw,
                                                   string method, string rawUrl, string clientIp)
        {
            byte[] html;
            try
            {
                string markup = Encoding.UTF8.GetString(
                    DirectoryListing.Build(directoryPath, request.Url.LocalPath));

                // El listado es HTML generado, no un archivo, asi que no pasa
                // por la ruta de inyeccion. Sin esto, navegar carpetas no
                // identificaba el dispositivo.
                if (InjectProbe && method == "GET")
                {
                    int close = markup.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    markup = close >= 0
                        ? markup.Substring(0, close) + ProbeScript.Html + markup.Substring(close)
                        : markup + ProbeScript.Html;
                }

                html = Encoding.UTF8.GetBytes(markup);
            }
            catch (Exception)
            {
                response.StatusCode = 500;
                Log(sw, "ERROR", method, rawUrl, response, 0, " - No se pudo listar la carpeta", clientIp);
                return 0;
            }

            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";

            if (method != "GET")
            {
                response.ContentLength64 = html.Length;
                Log(sw, "INFO", method, rawUrl, response, 0, " - Listado de directorio", clientIp);
                return 0;
            }

            return await WriteBufferedAsync(response, html, request.Headers["Accept-Encoding"], true,
                                            sw, "INFO", method, rawUrl, clientIp,
                                            " - Listado de directorio").ConfigureAwait(false);
        }

        private async Task<long> ServeFileAsync(string filePath, HttpListenerRequest request,
                                                HttpListenerResponse response, Stopwatch sw,
                                                string method, string rawUrl, string clientIp)
        {
            // El handle se abre y se valida en el mismo paso: comprobar la ruta
            // y abrirla despues dejaria una ventana en la que un enlace puede
            // cambiar entre ambos momentos.
            SafeFileHandle handle;
            GuardResult guard = PathGuard.TryOpenInsideRoot(filePath, _options.RealRootPath, out handle);

            if (guard == GuardResult.NotFound)
            {
                response.StatusCode = 404;
                Log(sw, "WARN", method, rawUrl, response, 0, " - No se pudo abrir el handle", clientIp);
                return 0;
            }
            if (guard == GuardResult.Forbidden)
            {
                response.StatusCode = 403;
                Log(sw, "SECURITY WARN", method, rawUrl, response, 0, " - Symlink/TOCTOU bloqueado", clientIp);
                return 0;
            }

            // FileStream toma posesion del handle y lo cierra al liberarse.
            // Si el constructor lanza, el handle quedaria abierto: se cierra aqui.
            FileStream opened;
            try
            {
                opened = new FileStream(handle, FileAccess.Read, BufferPool.BufferSize, true);
            }
            catch (Exception)
            {
                handle.Dispose();
                response.StatusCode = 500;
                Log(sw, "ERROR", method, rawUrl, response, 0, " - No se pudo envolver el handle", clientIp);
                return 0;
            }

            using (FileStream stream = opened)
            {
                string contentType = MimeRegistry.Resolve(filePath);
                response.ContentType = contentType;
                long fileLength = stream.Length;

                string acceptEncoding = request.Headers["Accept-Encoding"];
                bool isRangeRequest = !string.IsNullOrEmpty(request.Headers["Range"]);

                bool injectHtml = InjectProbe && method == "GET" && MimeRegistry.IsHtml(contentType) &&
                                  fileLength > 0 && fileLength < MaxInjectableSize;

                // La negociacion va ANTES del validador: el cuerpo comprimido y
                // el plano son representaciones distintas del mismo recurso y no
                // pueden compartir ETag.
                bool negotiable = _options.EnableCompression && !isRangeRequest &&
                                  CompressionPolicy.IsCompressibleType(contentType);
                if (negotiable) response.AddHeader("Vary", "Accept-Encoding");

                bool willCompress = negotiable && method == "GET" &&
                                    CompressionPolicy.ClientAcceptsGzip(acceptEncoding) &&
                                    (injectHtml || CompressionPolicy.IsCompressible(contentType, fileLength));

                // --- Validadores de cache (solo en modo distribucion) ---
                if (_options.DistributionMode)
                {
                    DateTime lastWrite = EntityTag.GetLastWriteUtc(filePath);

                    string variant = "";
                    if (injectHtml) variant += "i";
                    if (willCompress) variant += "g";
                    string etag = EntityTag.Build(fileLength, lastWrite, variant);

                    response.AddHeader("ETag", etag);
                    if (lastWrite != DateTime.MinValue)
                        response.AddHeader("Last-Modified", EntityTag.ToHttpDate(lastWrite));

                    if (EntityTag.ClientHasFreshCopy(request.Headers["If-None-Match"],
                                                     request.Headers["If-Modified-Since"],
                                                     etag, lastWrite))
                    {
                        // 304 va sin cuerpo: son ~200 bytes en vez del archivo.
                        response.StatusCode = 304;
                        response.ContentLength64 = 0;
                        Log(sw, "INFO", method, rawUrl, response, 0, " - Cache del cliente vigente", clientIp);
                        return 0;
                    }
                }

                if (injectHtml)
                {
                    return await ServeInjectedHtmlAsync(stream, fileLength, response, acceptEncoding,
                                                        sw, method, rawUrl, clientIp).ConfigureAwait(false);
                }

                // --- Compresion de archivos de texto ---
                // Nunca junto a Range: al comprimir, los desplazamientos dejan
                // de corresponder al archivo original y el cliente leeria basura.
                if (willCompress)
                {
                    byte[] raw = await ReadAllAsync(stream, fileLength).ConfigureAwait(false);
                    response.StatusCode = 200;
                    return await WriteBufferedAsync(response, raw, acceptEncoding, true,
                                                    sw, "INFO", method, rawUrl, clientIp, "")
                                 .ConfigureAwait(false);
                }

                response.AddHeader("Accept-Ranges", "bytes");
                RangeSpec range = RangeParser.Parse(request.Headers["Range"], fileLength);

                if (range.Present && !range.Satisfiable)
                {
                    response.StatusCode = 416;
                    response.AddHeader("Content-Range", "bytes */" + fileLength);
                    Log(sw, "WARN", method, rawUrl, response, 0, " - Range no satisfacible", clientIp);
                    return 0;
                }

                long start, length;
                if (range.Satisfiable)
                {
                    response.StatusCode = 206;
                    start = range.Start;
                    length = range.Length;
                    response.AddHeader("Content-Range",
                        string.Format("bytes {0}-{1}/{2}", range.Start, range.End, fileLength));
                }
                else
                {
                    response.StatusCode = 200;
                    start = 0;
                    length = fileLength;
                }
                response.ContentLength64 = length;

                if (method != "GET" || length == 0)
                {
                    Log(sw, "INFO", method, rawUrl, response, 0, "", clientIp);
                    return 0;
                }

                CopyResult result = await CopyRangeAsync(stream, response.OutputStream, start, length)
                                          .ConfigureAwait(false);

                Log(sw, "INFO", method, rawUrl, response, result.Written,
                    result.Aborted ? " - Transferencia interrumpida" : "", clientIp);
                return result.Written;
            }
        }

        /// <summary>
        /// Copia asincrona con buffer reutilizado. Devuelve true si el cliente
        /// corto la conexion, caso que se registra como INFO y no como error:
        /// un navegador que cancela una descarga es comportamiento normal.
        /// </summary>
        private struct CopyResult
        {
            public long Written;
            public bool Aborted;
        }

        private async Task<CopyResult> CopyRangeAsync(FileStream source, Stream destination,
                                                      long start, long length)
        {
            byte[] buffer = BufferPool.Rent();
            long written = 0;
            bool aborted = false;

            try
            {
                if (start > 0) source.Seek(start, SeekOrigin.Begin);

                long remaining = length;
                while (remaining > 0 && !_stopping)
                {
                    int toRead = (int)Math.Min((long)buffer.Length, remaining);
                    int read = await source.ReadAsync(buffer, 0, toRead).ConfigureAwait(false);
                    if (read <= 0) break;

                    await destination.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    remaining -= read;
                    written += read;
                }

                if (remaining > 0) aborted = true;
            }
            catch (HttpListenerException) { aborted = true; }
            catch (IOException) { aborted = true; }
            catch (ObjectDisposedException) { aborted = true; }
            finally
            {
                BufferPool.Return(buffer);
            }

            CopyResult result = new CopyResult();
            result.Written = written;
            result.Aborted = aborted;
            return result;
        }

        private async Task<long> ServeInjectedHtmlAsync(FileStream stream, long fileLength,
                                                        HttpListenerResponse response, string acceptEncoding,
                                                        Stopwatch sw, string method, string rawUrl,
                                                        string clientIp)
        {
            byte[] raw = await ReadAllAsync(stream, fileLength).ConfigureAwait(false);

            string html = Encoding.UTF8.GetString(raw, 0, raw.Length);
            int close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            html = close >= 0
                ? html.Substring(0, close) + ProbeScript.Html + html.Substring(close)
                : html + ProbeScript.Html;

            response.StatusCode = 200;
            return await WriteBufferedAsync(response, Encoding.UTF8.GetBytes(html), acceptEncoding, true,
                                            sw, "INFO", method, rawUrl, clientIp,
                                            " - HTML con sonda inyectada").ConfigureAwait(false);
        }

        /// <summary>Lee el archivo completo. El llamante ya limito el tamano.</summary>
        private static async Task<byte[]> ReadAllAsync(FileStream stream, long length)
        {
            byte[] buffer = new byte[(int)length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset).ConfigureAwait(false);
                if (read <= 0) break;
                offset += read;
            }

            if (offset == buffer.Length) return buffer;

            byte[] exact = new byte[offset];
            Buffer.BlockCopy(buffer, 0, exact, 0, offset);
            return exact;
        }

        private async Task HandleProbeAsync(HttpListenerRequest request, HttpListenerResponse response,
                                            Stopwatch sw, string method, string rawUrl, string clientIp)
        {
            if (method != "POST")
            {
                response.StatusCode = 405;
                Log(sw, "WARN", method, rawUrl, response, 0, " - /__probe solo acepta POST", clientIp);
                return;
            }

            if (request.ContentLength64 > MaxProbeBodySize)
            {
                response.StatusCode = 413;
                Log(sw, "WARN", method, rawUrl, response, 0, " - Sonda demasiado grande", clientIp);
                return;
            }

            string body;
            using (StreamReader reader = new StreamReader(request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            if (ProbePayload.ApplyTo(clientIp, body))
            {
                response.StatusCode = 204;
                Log(sw, "INFO", method, rawUrl, response, 0, " - Sonda JS recibida", clientIp);
            }
            else
            {
                response.StatusCode = 400;
                Log(sw, "WARN", method, rawUrl, response, 0, " - Sonda JS ilegible", clientIp);
            }
        }

        // ------------------------------------------------------------------

        private void TrackClient(HttpListenerRequest request, string clientIp, int sourcePort, long bytesSent)
        {
            try
            {
                ClientRegistry.Track(clientIp, request.UserAgent, request.Headers, bytesSent);
                ClientRegistry.SetHttpDetails(clientIp, request.Headers,
                                              request.ProtocolVersion != null ? request.ProtocolVersion.ToString() : "",
                                              request.KeepAlive, sourcePort);
            }
            catch (Exception) { }
        }

        private void Log(Stopwatch sw, string level, string method, string url,
                         HttpListenerResponse response, long bytes, string note, string clientIp)
        {
            if (sw.IsRunning) sw.Stop();
            int status = 0;
            try { status = response.StatusCode; } catch (Exception) { }
            _log.WriteRequest(level, method, url, status, sw.ElapsedMilliseconds, bytes, note, clientIp);
        }
    }
}
