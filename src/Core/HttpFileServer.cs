using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using LanShare.Identity;

namespace LanShare.Core
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

        private readonly LiveReloadHub _liveReload = new LiveReloadHub();
        private readonly UploadHandler _upload;
        private readonly ConsoleRelay _console;
        private readonly List<SiteWatcher> _watchers = new List<SiteWatcher>();
        private readonly object _watchersGate = new object();

        /// <summary>Pestanas conectadas a la recarga automatica.</summary>
        public int LiveReloadClients
        {
            get { return _liveReload.Count; }
        }

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
            _upload = new UploadHandler(options, log);
            _console = new ConsoleRelay(log);

            _listener = new HttpListener();

            // Un unico listener con varios prefijos: HttpListener multiplexa
            // los puertos por si mismo, asi que no hacen falta N bucles de
            // aceptacion ni N pools de peticiones. El TLS de los prefijos https
            // lo hace http.sys con el certificado asociado a su puerto.
            foreach (SiteRuntime site in options.Sites)
            {
                _listener.Prefixes.Add(site.Prefix);
                if (site.HttpsPrefix != null) _listener.Prefixes.Add(site.HttpsPrefix);
            }
        }

        /// <summary>
        /// Lo que se inyecta en esta respuesta, con la configuracion de este
        /// momento. La consola remota no se pone en las paginas que abre este
        /// mismo PC: ahi ya estan las herramientas del navegador, y envolver
        /// console.log haria que estas atribuyesen cada mensaje al script de
        /// LanShare en lugar de a la linea de tu archivo que lo escribio.
        /// </summary>
        private HtmlInjection CurrentInjection(HttpListenerRequest request)
        {
            bool remoteConsole = _options.EnableRemoteConsole && !IsFromThisMachine(request);
            return HtmlInjection.For(InjectProbe, _options.EnableLiveReload, remoteConsole);
        }

        private static bool IsFromThisMachine(HttpListenerRequest request)
        {
            try { return request.IsLocal; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Activa o desactiva la recarga: crea o libera los vigilantes de cada
        /// sitio y, al desactivarla, cierra las pestanas conectadas pidiendoles
        /// que no reintenten.
        /// </summary>
        public void SetLiveReload(bool enabled)
        {
            lock (_watchersGate)
            {
                _options.EnableLiveReload = enabled;

                if (enabled && _watchers.Count == 0 && !_stopping)
                {
                    foreach (SiteRuntime site in _options.Sites)
                    {
                        try { _watchers.Add(new SiteWatcher(site.Port, site.RootPath, OnSiteChanged)); }
                        catch (Exception ex)
                        {
                            _log.Write("WARN", "No se pudo vigilar " + site.RootPath + ": " + ex.Message, site.Port);
                        }
                    }
                    _log.Write("INFO", "Recarga automatica activa en " + _watchers.Count +
                                       (_watchers.Count == 1 ? " sitio." : " sitios."));
                }
                else if (!enabled && _watchers.Count > 0)
                {
                    foreach (SiteWatcher watcher in _watchers) watcher.Dispose();
                    _watchers.Clear();
                    _liveReload.CloseAll(true);
                    _log.Write("INFO", "Recarga automatica desactivada.");
                }
            }
        }

        private void DisposeWatchers()
        {
            lock (_watchersGate)
            {
                foreach (SiteWatcher watcher in _watchers) watcher.Dispose();
                _watchers.Clear();
            }
        }

        /// <summary>Llega desde el temporizador del vigilante, en un hilo del pool.</summary>
        private void OnSiteChanged(int port, ReloadPlan plan)
        {
            if (_stopping || !_options.EnableLiveReload) return;

            int tabs = _liveReload.Broadcast(port, plan.ToMessage());
            if (tabs == 0) return;

            string what = plan.Kind == ReloadKind.Css
                ? "estilos en caliente (" + string.Join(", ", new List<string>(plan.CssPaths).ToArray()) + ")"
                : "recarga completa";
            _log.Write("INFO", "Cambio en disco: " + what + " en " + tabs +
                               (tabs == 1 ? " pestana." : " pestanas."), port);
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

            // Las pestanas conectadas reintentaran solas cuando vuelva.
            _liveReload.CloseAll(false);
            DisposeWatchers();
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
            int sitePort = CanonicalSitePort(request);
            long bytesSent = 0;

            try
            {
                if (_options.CorsEnabled)
                {
                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, POST, OPTIONS");
                    response.AddHeader("Access-Control-Allow-Headers", "*");

                    // Que una pagina aislada (COEP) de otro sitio o de otro puerto
                    // pueda incrustar estos recursos sin pedirlos con crossorigin.
                    // No abre nada que el "*" de arriba no abriera ya.
                    response.AddHeader("Cross-Origin-Resource-Policy", "cross-origin");
                }
                // En desarrollo se prohibe cachear para que nunca veas una
                // version vieja de tu propio trabajo. En distribucion se deja
                // revalidar: el cliente pregunta y suele recibir un 304.
                response.AddHeader("Cache-Control", _options.DistributionMode
                    ? "no-cache"
                    : "no-store, no-cache, must-revalidate, max-age=0");
                response.AddHeader("X-Content-Type-Options", "nosniff");

                // Aislamiento entre origenes, sitio a sitio: junto con una conexion
                // segura (localhost o HTTPS), da crossOriginIsolated a sus paginas.
                // Va en todas sus respuestas porque tambien lo exigen los workers.
                SiteRuntime servedSite = _options.FindByPort(sitePort);
                if (servedSite != null && servedSite.CrossOriginIsolation)
                {
                    response.AddHeader("Cross-Origin-Opener-Policy", "same-origin");
                    response.AddHeader("Cross-Origin-Embedder-Policy", "require-corp");
                }

                if (method == "OPTIONS")
                {
                    response.StatusCode = 204;
                    Log(sw, "INFO", method, rawUrl, response, 0, "", clientIp, sitePort);
                    return;
                }

                if (request.Url == null)
                {
                    response.StatusCode = 400;
                    Log(sw, "WARN", method, rawUrl, response, 0, " - URL invalida", clientIp, sitePort);
                    return;
                }

                if (string.Equals(request.Url.LocalPath, ProbeEndpoint, StringComparison.Ordinal))
                {
                    await HandleProbeAsync(request, response, sw, method, rawUrl, clientIp, sitePort).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(request.Url.LocalPath, LiveReloadHub.Endpoint, StringComparison.Ordinal))
                {
                    if (!request.IsWebSocketRequest || _options.FindByPort(sitePort) == null)
                    {
                        response.StatusCode = 404;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - Recarga automatica: no es una conexion WebSocket",
                            clientIp, sitePort);
                        return;
                    }

                    if (!_options.EnableLiveReload)
                    {
                        _log.WriteRequest("INFO", method, rawUrl, 101, sw.ElapsedMilliseconds, 0,
                                          " - Recarga automatica desactivada: la pestana deja de reintentar",
                                          clientIp, sitePort);
                        await LiveReloadHub.RejectAsync(context).ConfigureAwait(false);
                        return;
                    }

                    // La conexion queda abierta mientras la pestana viva: se
                    // registra al abrirse, no al cerrarse horas despues.
                    _log.WriteRequest("INFO", method, rawUrl, 101, sw.ElapsedMilliseconds, 0,
                                      " - Recarga automatica conectada", clientIp, sitePort);
                    await _liveReload.ServeAsync(context, sitePort).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(request.Url.LocalPath, ConsoleRelay.Endpoint, StringComparison.Ordinal))
                {
                    // Apagada responde 404, y el script deja de enviar al verlo.
                    SiteRuntime consoleSite = _options.FindByPort(sitePort);
                    if (!_options.EnableRemoteConsole || consoleSite == null)
                    {
                        response.StatusCode = 404;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - Consola remota desactivada", clientIp, sitePort);
                        return;
                    }

                    await _console.HandleAsync(context, consoleSite, rawUrl, clientIp, sw).ConfigureAwait(false);
                    return;
                }

                if (CertificatePage.IsPage(request.Url.LocalPath) || CertificatePage.IsFile(request.Url.LocalPath))
                {
                    bytesSent = await ServeCertificateAsync(request, response, sw, method, rawUrl, clientIp,
                                                            sitePort).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(request.Url.LocalPath, UploadHandler.Endpoint, StringComparison.Ordinal))
                {
                    // Desactivada responde lo mismo que una ruta inexistente: no
                    // se anuncia que la funcion existe.
                    SiteRuntime uploadSite = _options.FindByPort(sitePort);
                    if (!_options.EnableUpload || uploadSite == null)
                    {
                        response.StatusCode = 404;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - Subida desactivada", clientIp, sitePort);
                        return;
                    }

                    bytesSent = await _upload.HandleAsync(context, uploadSite, rawUrl, clientIp, sw).ConfigureAwait(false);
                    return;
                }

                if (method != "GET" && method != "HEAD")
                {
                    response.StatusCode = 405;
                    response.AddHeader("Allow", "GET, HEAD, OPTIONS");
                    Log(sw, "WARN", method, rawUrl, response, 0, " - Metodo no permitido", clientIp, sitePort);
                    return;
                }

                // El puerto de entrada decide que carpeta se sirve.
                SiteRuntime site = _options.FindByPort(sitePort);
                if (site == null)
                {
                    response.StatusCode = 404;
                    Log(sw, "WARN", method, rawUrl, response, 0, " - Puerto sin sitio asociado", clientIp, sitePort);
                    return;
                }

                Resolution resolution = StaticFileResolver.Resolve(
                    request.Url.LocalPath, request.Url.AbsolutePath, request.Url.Query,
                    site.RootPath, site.StartFile, _options.EnableDirectoryListing);

                switch (resolution.Kind)
                {
                    case ResolutionKind.BadRequest:
                        response.StatusCode = 400;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp, sitePort);
                        return;

                    case ResolutionKind.Forbidden:
                        response.StatusCode = 403;
                        Log(sw, "SECURITY WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp, sitePort);
                        return;

                    case ResolutionKind.NotFound:
                        response.StatusCode = 404;
                        Log(sw, "WARN", method, rawUrl, response, 0, " - " + resolution.Reason, clientIp, sitePort);
                        return;

                    case ResolutionKind.RedirectToDirectory:
                        response.StatusCode = 301;
                        response.AddHeader("Location", resolution.RedirectLocation);
                        Log(sw, "INFO", method, rawUrl, response, 0, " - Redirigido a directorio", clientIp, sitePort);
                        return;

                    case ResolutionKind.DirectoryListing:
                        if (FolderArchive.IsRequested(request.Url.Query))
                        {
                            bytesSent = await ServeFolderZipAsync(resolution.DirectoryPath, site, request, response,
                                                                  sw, method, rawUrl, clientIp).ConfigureAwait(false);
                            return;
                        }
                        bytesSent = await ServeListingAsync(resolution.DirectoryPath, request, response,
                                                            sw, method, rawUrl, clientIp, sitePort).ConfigureAwait(false);
                        return;
                }

                bytesSent = await ServeFileAsync(resolution.FilePath, site, request, response, sw,
                                                 method, rawUrl, clientIp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { response.StatusCode = 500; } catch (Exception) { }
                sw.Stop();
                // Una linea, como el resto del registro: el mensaje de algunas
                // excepciones trae saltos de linea propios.
                _log.Write("ERROR", string.Format("{0} {1} - Client: {2} - {3}: {4}",
                                                  method, rawUrl, clientIp, ex.GetType().FullName,
                                                  ex.Message.Replace("\r", " ").Replace("\n", " ")), sitePort);
            }
            finally
            {
                TrackClient(request, clientIp, sourcePort, bytesSent);
                try { response.Close(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Puerto del sitio que atiende la peticion: el puerto HTTP principal
        /// aunque haya llegado por el HTTPS, para que el registro, el filtro
        /// por sitio y la recarga automatica traten igual las dos entradas.
        ///
        /// Se mira el puerto local de la conexion y no el de la cabecera Host,
        /// que escribe el cliente: con Host se podia pedir, por la conexion de
        /// un sitio, el contenido de otro.
        /// </summary>
        private int CanonicalSitePort(HttpListenerRequest request)
        {
            int port = 0;
            try
            {
                if (request.LocalEndPoint != null) port = request.LocalEndPoint.Port;
            }
            catch (Exception) { }

            if (port == 0 && request.Url != null) port = request.Url.Port;

            SiteRuntime site = _options.FindByPort(port);
            return site != null ? site.Port : port;
        }

        /// <summary>
        /// /__certificado y el archivo de la autoridad, solo en sitios con
        /// HTTPS: en los demas la ruta no existe, como cualquier otra.
        /// </summary>
        private async Task<long> ServeCertificateAsync(HttpListenerRequest request, HttpListenerResponse response,
                                                       Stopwatch sw, string method, string rawUrl, string clientIp,
                                                       int sitePort)
        {
            SiteRuntime site = _options.FindByPort(sitePort);
            byte[] authority = _options.HttpsAuthorityCertificate;
            if (site == null || site.HttpsPort == 0 || authority == null)
            {
                response.StatusCode = 404;
                Log(sw, "WARN", method, rawUrl, response, 0, " - Este sitio no se sirve por HTTPS", clientIp, sitePort);
                return 0;
            }

            if (method != "GET" && method != "HEAD")
            {
                response.StatusCode = 405;
                response.AddHeader("Allow", "GET, HEAD");
                Log(sw, "WARN", method, rawUrl, response, 0, " - Metodo no permitido", clientIp, sitePort);
                return 0;
            }

            byte[] body;
            string note;
            if (CertificatePage.IsFile(request.Url.LocalPath))
            {
                response.ContentType = CertificatePage.CertificateContentType;
                body = authority;
                note = " - Certificado de la autoridad local";
            }
            else
            {
                // La direccion segura desde este mismo movil: el nombre o la IP
                // con la que ha llegado, en el puerto HTTPS del sitio.
                string httpsUrl = "https://" + request.Url.Host + ":" + site.HttpsPort + "/";
                string html = CertificatePage.Render(_options.HttpsAuthorityFingerprint, httpsUrl,
                                                     CertificatePage.Detect(request.UserAgent));
                response.ContentType = "text/html; charset=utf-8";
                body = Encoding.UTF8.GetBytes(html);
                note = " - Pagina para instalar el certificado";
            }

            response.StatusCode = 200;
            if (method == "HEAD")
            {
                response.ContentLength64 = body.Length;
                Log(sw, "INFO", method, rawUrl, response, 0, note, clientIp, sitePort);
                return 0;
            }

            return await WritePayloadAsync(response, body, sw, "INFO", method, rawUrl, clientIp, sitePort, note)
                .ConfigureAwait(false);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Escritura y registro de un cuerpo ya preparado. No decide nada sobre
        /// compresion: quien llama es responsable de haber puesto las cabeceras
        /// que describan lo que va dentro.
        /// </summary>
        private async Task<long> WritePayloadAsync(HttpListenerResponse response, byte[] body,
                                                   Stopwatch sw, string level, string method,
                                                   string rawUrl, string clientIp, int sitePort,
                                                   string note)
        {
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
                aborted ? " - Transferencia interrumpida" : note, clientIp, sitePort);
            return sent;
        }

        /// <summary>
        /// Envia un cuerpo ya comprimido que estaba en la reserva. Las
        /// cabeceras van aqui y no en el llamante porque omitirlas entregaria
        /// bytes gzip sin declararlos, y el cliente leeria basura.
        /// </summary>
        private async Task<long> WriteCachedGzipAsync(HttpListenerResponse response, byte[] compressed,
                                                      Stopwatch sw, string method, string rawUrl,
                                                      string clientIp, int sitePort)
        {
            response.StatusCode = 200;
            response.AddHeader("Content-Encoding", "gzip");
            response.AddHeader("Vary", "Accept-Encoding");

            return await WritePayloadAsync(response, compressed, sw, "INFO", method, rawUrl,
                                           clientIp, sitePort, " [gzip cache]").ConfigureAwait(false);
        }

        /// <summary>
        /// Punto unico de salida para respuestas que caben en memoria (listado
        /// e HTML con sonda). Centralizar aqui la negociacion evita duplicarla.
        /// </summary>
        private async Task<long> WriteBufferedAsync(HttpListenerResponse response, byte[] payload,
                                                    string acceptEncoding, Stopwatch sw, string level,
                                                    string method, string rawUrl, string clientIp,
                                                    int sitePort, string note, string cacheKey)
        {
            byte[] body = payload;

            if (_options.EnableCompression &&
                CompressionPolicy.ClientAcceptsGzip(acceptEncoding) &&
                CompressionPolicy.IsCompressible(response.ContentType, payload.Length))
            {
                byte[] compressed = CompressionPolicy.Gzip(payload);
                if (compressed != null)
                {
                    CompressionCache.Store(cacheKey, compressed);

                    body = compressed;
                    response.AddHeader("Content-Encoding", "gzip");
                    response.AddHeader("Vary", "Accept-Encoding");
                    note += string.Format(" [gzip {0}%]",
                                          100 - (compressed.Length * 100 / payload.Length));
                }
            }

            return await WritePayloadAsync(response, body, sw, level, method, rawUrl,
                                           clientIp, sitePort, note).ConfigureAwait(false);
        }

        private async Task<long> ServeListingAsync(string directoryPath, HttpListenerRequest request,
                                                   HttpListenerResponse response, Stopwatch sw,
                                                   string method, string rawUrl, string clientIp,
                                                   int sitePort)
        {
            byte[] html;
            try
            {
                string markup = Encoding.UTF8.GetString(
                    DirectoryListing.Build(directoryPath, request.Url.LocalPath, _options.EnableUpload));

                // El listado es HTML generado, no un archivo, asi que no pasa
                // por la ruta de inyeccion. Sin esto, navegar carpetas no
                // identificaba el dispositivo.
                if (method == "GET") markup = CurrentInjection(request).Apply(markup);

                html = Encoding.UTF8.GetBytes(markup);
            }
            catch (Exception)
            {
                response.StatusCode = 500;
                Log(sw, "ERROR", method, rawUrl, response, 0, " - No se pudo listar la carpeta", clientIp, sitePort);
                return 0;
            }

            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";

            if (method != "GET")
            {
                response.ContentLength64 = html.Length;
                Log(sw, "INFO", method, rawUrl, response, 0, " - Listado de directorio", clientIp, sitePort);
                return 0;
            }

            // El listado se genera al vuelo y cambia con la carpeta: no se guarda.
            return await WriteBufferedAsync(response, html, request.Headers["Accept-Encoding"],
                                            sw, "INFO", method, rawUrl, clientIp, sitePort,
                                            " - Listado de directorio", null).ConfigureAwait(false);
        }

        /// <summary>
        /// La carpeta entera en un ZIP que se genera mientras se envia, con
        /// memoria constante. Solo se llega desde un listado, que ya ensena
        /// todo lo que va dentro; aun asi se comprueba la ruta real de la
        /// carpeta, que podria ser una union que apunta fuera del sitio.
        /// </summary>
        private async Task<long> ServeFolderZipAsync(string directoryPath, SiteRuntime site,
                                                    HttpListenerRequest request, HttpListenerResponse response,
                                                    Stopwatch sw, string method, string rawUrl, string clientIp)
        {
            if (!PathGuard.DirectoryInsideRoot(directoryPath, site.RealRootPath))
            {
                response.StatusCode = 403;
                Log(sw, "SECURITY WARN", method, rawUrl, response, 0,
                    " - ZIP de una carpeta fuera de la raiz real", clientIp, site.Port);
                return 0;
            }

            string name = ArchiveName(directoryPath, site);
            response.StatusCode = 200;
            response.ContentType = "application/zip";
            response.AddHeader("Content-Disposition", ContentDisposition.Attachment(name + ".zip"));

            if (method != "GET")
            {
                Log(sw, "INFO", method, rawUrl, response, 0, " - ZIP de la carpeta", clientIp, site.Port);
                return 0;
            }

            // Sin Content-Length: el tamano no se sabe hasta el ultimo byte. En
            // HTTP/1.1 va por trozos; en HTTP/1.0, que no los admite y con el
            // que SendChunked lanzaria, HttpListener cierra la conexion al final.
            // En HTTP/2 los trozos los pone el propio protocolo: los de
            // HttpListener irian dentro del cuerpo y corromperian el ZIP.
            Version protocol = request.ProtocolVersion;
            if (protocol != null && protocol >= HttpVersion.Version11 && protocol.Major < 2) response.SendChunked = true;

            // El bufer agrupa las cabeceras pequenas de cada entrada, que si no
            // saldrian en un trozo HTTP cada una. No se cierra con using: cerrar
            // el flujo de la respuesta envia el ultimo trozo, y tras un fallo eso
            // haria que el navegador diese por buena una descarga a medias.
            BufferedStream buffered = new BufferedStream(response.OutputStream, 64 * 1024);
            FolderArchive.Result result;
            try
            {
                result = await FolderArchive.WriteAsync(directoryPath, name, site.RealRootPath, buffered)
                                            .ConfigureAwait(false);
                await buffered.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    Log(sw, "INFO", method, rawUrl, response, 0, " - ZIP interrumpido por el cliente",
                        clientIp, site.Port);
                    return 0;
                }

                // Se corta la conexion en seco para que la descarga conste como
                // fallida en el navegador, y se dice por que.
                try { response.Abort(); }
                catch (Exception) { }
                if (sw.IsRunning) sw.Stop();
                _log.WriteRequest("ERROR", method, rawUrl, 200, sw.ElapsedMilliseconds, 0,
                                  " - ZIP abortado: " + ex.Message.Replace("\r", " ").Replace("\n", " "),
                                  clientIp, site.Port);
                return 0;
            }

            string note = " - ZIP: " + result.Files + (result.Files == 1 ? " archivo" : " archivos");
            if (result.Skipped > 0) note += ", " + result.Skipped + " sin poder leer";
            if (result.Truncated) note += ", recortado a " + FolderArchive.MaxEntries + " entradas";
            Log(sw, result.Skipped > 0 || result.Truncated ? "WARN" : "INFO", method, rawUrl, response,
                result.ArchiveBytes, note, clientIp, site.Port);
            return result.ArchiveBytes;
        }

        /// <summary>Nombre de la carpeta, o del sitio si es la raiz de una unidad.</summary>
        private static string ArchiveName(string directoryPath, SiteRuntime site)
        {
            string name = "";
            try { name = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)); }
            catch (Exception) { }

            if (string.IsNullOrEmpty(name)) name = site.Label.Length > 0 ? site.Label : "sitio-" + site.Port;
            return name;
        }

        private async Task<long> ServeFileAsync(string filePath, SiteRuntime site,
                                                HttpListenerRequest request,
                                                HttpListenerResponse response, Stopwatch sw,
                                                string method, string rawUrl, string clientIp)
        {
            // El handle se abre y se valida en el mismo paso: comprobar la ruta
            // y abrirla despues dejaria una ventana en la que un enlace puede
            // cambiar entre ambos momentos.
            SafeFileHandle handle;
            GuardResult guard = PathGuard.TryOpenInsideRoot(filePath, site.RealRootPath, out handle);

            if (guard == GuardResult.NotFound)
            {
                response.StatusCode = 404;
                Log(sw, "WARN", method, rawUrl, response, 0, " - No se pudo abrir el handle", clientIp, site.Port);
                return 0;
            }
            if (guard == GuardResult.Forbidden)
            {
                response.StatusCode = 403;
                Log(sw, "SECURITY WARN", method, rawUrl, response, 0, " - Symlink/TOCTOU bloqueado", clientIp, site.Port);
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
                Log(sw, "ERROR", method, rawUrl, response, 0, " - No se pudo envolver el handle", clientIp, site.Port);
                return 0;
            }

            using (FileStream stream = opened)
            {
                string contentType = MimeRegistry.Resolve(filePath);
                response.ContentType = contentType;
                long fileLength = stream.Length;

                string acceptEncoding = request.Headers["Accept-Encoding"];
                bool isRangeRequest = !string.IsNullOrEmpty(request.Headers["Range"]);

                // Se fija una vez por peticion: si la configuracion cambia a
                // mitad, el validador, la clave de cache y el cuerpo siguen
                // describiendo exactamente la misma representacion.
                // Se decide una sola vez: de aqui salen el HTML, el validador y
                // la clave de la cache, que tienen que coincidir siempre.
                HtmlInjection injection = CurrentInjection(request);

                bool injectHtml = !injection.IsEmpty && method == "GET" && MimeRegistry.IsHtml(contentType) &&
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

                // Una sola consulta de fecha para dos usos: el validador de
                // cache del cliente y la clave de la reserva de compresion.
                DateTime lastWrite = DateTime.MinValue;
                if (_options.DistributionMode || willCompress || injectHtml)
                {
                    lastWrite = EntityTag.GetLastWriteUtc(filePath);
                }

                // --- Validadores de cache (solo en modo distribucion) ---
                if (_options.DistributionMode)
                {
                    string variant = "";
                    if (injectHtml) variant += injection.Variant;
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
                        Log(sw, "INFO", method, rawUrl, response, 0, " - Cache del cliente vigente", clientIp, site.Port);
                        return 0;
                    }
                }

                if (injectHtml)
                {
                    string injectedKey = CompressionCache.BuildKey(filePath, fileLength, lastWrite, injection.Variant);
                    return await ServeInjectedHtmlAsync(stream, fileLength, response, acceptEncoding,
                                                        sw, method, rawUrl, clientIp, site.Port,
                                                        injectedKey, injection).ConfigureAwait(false);
                }

                // --- Compresion de archivos de texto ---
                // Nunca junto a Range: al comprimir, los desplazamientos dejan
                // de corresponder al archivo original y el cliente leeria basura.
                if (willCompress)
                {
                    string cacheKey = CompressionCache.BuildKey(filePath, fileLength, lastWrite, "");

                    // Si ya esta comprimido, ni se relee el archivo ni se vuelve
                    // a comprimir: se envia tal cual con su cabecera.
                    byte[] cached = CompressionCache.Get(cacheKey);
                    if (cached != null)
                    {
                        return await WriteCachedGzipAsync(response, cached, sw, method, rawUrl,
                                                          clientIp, site.Port).ConfigureAwait(false);
                    }

                    byte[] raw = await ReadAllAsync(stream, fileLength).ConfigureAwait(false);
                    response.StatusCode = 200;
                    return await WriteBufferedAsync(response, raw, acceptEncoding,
                                                    sw, "INFO", method, rawUrl, clientIp, site.Port,
                                                    "", cacheKey).ConfigureAwait(false);
                }

                response.AddHeader("Accept-Ranges", "bytes");
                RangeSpec range = RangeParser.Parse(request.Headers["Range"], fileLength);

                if (range.Present && !range.Satisfiable)
                {
                    response.StatusCode = 416;
                    response.AddHeader("Content-Range", "bytes */" + fileLength);
                    Log(sw, "WARN", method, rawUrl, response, 0, " - Range no satisfacible", clientIp, site.Port);
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
                    Log(sw, "INFO", method, rawUrl, response, 0, "", clientIp, site.Port);
                    return 0;
                }

                CopyResult result = await CopyRangeAsync(stream, response.OutputStream, start, length)
                                          .ConfigureAwait(false);

                Log(sw, "INFO", method, rawUrl, response, result.Written,
                    result.Aborted ? " - Transferencia interrumpida" : "", clientIp, site.Port);
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
                                                        string clientIp, int sitePort, string cacheKey,
                                                        HtmlInjection injection)
        {
            if (CompressionPolicy.ClientAcceptsGzip(acceptEncoding))
            {
                byte[] cached = CompressionCache.Get(cacheKey);
                if (cached != null)
                {
                    response.ContentType = "text/html; charset=utf-8";
                    return await WriteCachedGzipAsync(response, cached, sw, method, rawUrl,
                                                      clientIp, sitePort).ConfigureAwait(false);
                }
            }

            byte[] raw = await ReadAllAsync(stream, fileLength).ConfigureAwait(false);

            string html = injection.Apply(Encoding.UTF8.GetString(raw, 0, raw.Length));

            response.StatusCode = 200;
            return await WriteBufferedAsync(response, Encoding.UTF8.GetBytes(html), acceptEncoding,
                                            sw, "INFO", method, rawUrl, clientIp, sitePort,
                                            " - HTML con script inyectado", cacheKey).ConfigureAwait(false);
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
                                            Stopwatch sw, string method, string rawUrl, string clientIp,
                                            int sitePort)
        {
            if (method != "POST")
            {
                response.StatusCode = 405;
                Log(sw, "WARN", method, rawUrl, response, 0, " - /__probe solo acepta POST", clientIp, sitePort);
                return;
            }

            // Content-Length no basta: una peticion troceada no lo lleva y se
            // leeria sin limite. El tope se aplica tambien mientras se lee.
            string body = request.ContentLength64 > MaxProbeBodySize
                ? null
                : await BodyReader.ReadUtf8Async(request.InputStream, (int)MaxProbeBodySize).ConfigureAwait(false);
            if (body == null)
            {
                response.StatusCode = 413;
                Log(sw, "WARN", method, rawUrl, response, 0, " - Sonda demasiado grande", clientIp, sitePort);
                return;
            }

            if (ProbePayload.ApplyTo(clientIp, body))
            {
                response.StatusCode = 204;
                Log(sw, "INFO", method, rawUrl, response, 0, " - Sonda JS recibida", clientIp, sitePort);
            }
            else
            {
                response.StatusCode = 400;
                Log(sw, "WARN", method, rawUrl, response, 0, " - Sonda JS ilegible", clientIp, sitePort);
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
                         HttpListenerResponse response, long bytes, string note,
                         string clientIp, int port)
        {
            if (sw.IsRunning) sw.Stop();
            int status = 0;
            try { status = response.StatusCode; } catch (Exception) { }
            _log.WriteRequest(level, method, url, status, sw.ElapsedMilliseconds, bytes, note, clientIp, port);
        }
    }
}
