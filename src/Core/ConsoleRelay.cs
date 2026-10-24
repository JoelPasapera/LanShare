using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace LanShare.Core
{
    /// <summary>
    /// Atiende /__consola: recibe los lotes del script de la consola remota y
    /// los escribe en el registro, una linea por mensaje y con el puerto del
    /// sitio, para que el filtro del registro los separe por proyecto.
    ///
    /// Cualquier equipo de la red puede llamar aqui, asi que el cuerpo tiene
    /// tope aunque llegue troceado, el origen se comprueba como en la subida y
    /// cada cliente tiene un ritmo maximo: una pagina con un error dentro de
    /// requestAnimationFrame produce sesenta por segundo y ahogaria el resto
    /// del registro. Lo que se descarta se cuenta y se avisa.
    /// </summary>
    public sealed class ConsoleRelay
    {
        public const string Endpoint = "/__consola";

        /// <summary>El script trocea sus envios muy por debajo de esto.</summary>
        public const int MaxBodyBytes = 64 * 1024;

        /// <summary>Rafaga admitida por cliente...</summary>
        public const int Burst = 40;

        /// <summary>...y ritmo sostenido despues, en mensajes por segundo.</summary>
        public const double PerSecond = 10;

        /// <summary>Clientes recordados. En una red local nunca se llega; es un tope de memoria.</summary>
        private const int MaxTrackedClients = 512;

        private sealed class ClientState
        {
            public readonly TokenBucket Bucket;
            public long Dropped;

            public ClientState(long nowMs)
            {
                Bucket = new TokenBucket(Burst, PerSecond, nowMs);
            }
        }

        private readonly LogBus _log;
        private readonly ConcurrentDictionary<string, ClientState> _clients =
            new ConcurrentDictionary<string, ClientState>(StringComparer.Ordinal);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public ConsoleRelay(LogBus log)
        {
            if (log == null) throw new ArgumentNullException("log");
            _log = log;
        }

        public async Task HandleAsync(HttpListenerContext context, SiteRuntime site, string rawUrl,
                                      string clientIp, Stopwatch sw)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string method = request.HttpMethod;

            if (method != "POST")
            {
                response.StatusCode = 405;
                response.AddHeader("Allow", "POST");
                LogRequest(sw, "WARN", method, rawUrl, 405, " - /__consola solo acepta POST", clientIp, site.Port);
                return;
            }

            if (!UploadGuard.IsSameOrigin(request.Headers["Origin"], request.Headers["Host"],
                                          request.Headers["Sec-Fetch-Site"]))
            {
                response.StatusCode = 403;
                LogRequest(sw, "SECURITY WARN", method, rawUrl, 403, " - Consola remota desde otro origen",
                           clientIp, site.Port);
                return;
            }

            string body = request.ContentLength64 > MaxBodyBytes
                ? null
                : await BodyReader.ReadUtf8Async(request.InputStream, MaxBodyBytes).ConfigureAwait(false);
            if (body == null)
            {
                response.StatusCode = 413;
                LogRequest(sw, "WARN", method, rawUrl, 413, " - Lote de consola demasiado grande", clientIp, site.Port);
                return;
            }

            ConsoleReport report = ConsoleReport.Parse(body);
            if (report == null)
            {
                response.StatusCode = 400;
                LogRequest(sw, "WARN", method, rawUrl, 400, " - Lote de consola ilegible", clientIp, site.Port);
                return;
            }

            string host = request.Headers["Host"];
            Relay(report, clientIp, site.Port, string.IsNullOrEmpty(host) ? "" : "http://" + host,
                  _clock.ElapsedMilliseconds);

            // Sin linea de peticion: los mensajes ya estan en el registro y un
            // "POST /__consola 204" por lote solo seria ruido entre ellos.
            response.StatusCode = 204;
        }

        /// <summary>
        /// Escribe el lote en el registro respetando el ritmo del cliente.
        /// Separado de la parte HTTP para poder probarlo sin un listener.
        /// </summary>
        /// <returns>Mensajes escritos.</returns>
        public int Relay(ConsoleReport report, string clientIp, int port, string origin, long nowMs)
        {
            if (report == null) throw new ArgumentNullException("report");
            if (clientIp == null) clientIp = "?";

            ClientState state = StateFor(clientIp, nowMs);
            int granted;
            long dropped = 0;

            lock (state)
            {
                granted = state.Bucket.Take(report.Entries.Count, nowMs);
                state.Dropped += report.Dropped + (long)(report.Entries.Count - granted);

                // El aviso de descartados tambien gasta ritmo: si no, una
                // pagina desbocada llenaria el registro de avisos.
                if (state.Dropped > 0 && state.Bucket.Take(1, nowMs) == 1)
                {
                    dropped = state.Dropped;
                    state.Dropped = 0;
                }
            }

            for (int i = 0; i < granted; i++)
            {
                ConsoleEntry entry = report.Entries[i];
                _log.Write(ConsoleLine.LevelTag(entry.Level),
                           ConsoleLine.Format(entry, clientIp, report.Page, origin), port);
            }

            if (dropped > 0)
            {
                _log.Write("JS WARN", ConsoleLine.Flatten(clientIp, 64) + "  " +
                                      dropped.ToString(CultureInfo.InvariantCulture) +
                                      (dropped == 1 ? " mensaje de consola omitido" : " mensajes de consola omitidos") +
                                      " por llegar demasiados seguidos", port);
            }
            return granted;
        }

        private ClientState StateFor(string clientIp, long nowMs)
        {
            ClientState state;
            if (_clients.TryGetValue(clientIp, out state)) return state;

            if (_clients.Count >= MaxTrackedClients) _clients.Clear();
            return _clients.GetOrAdd(clientIp, new ClientState(nowMs));
        }

        private void LogRequest(Stopwatch sw, string level, string method, string rawUrl, int status,
                                string note, string clientIp, int port)
        {
            if (sw.IsRunning) sw.Stop();
            _log.WriteRequest(level, method, rawUrl, status, sw.ElapsedMilliseconds, 0, note, clientIp, port);
        }
    }
}
