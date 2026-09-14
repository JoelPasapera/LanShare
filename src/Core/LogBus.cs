using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace LanShare.Core
{
    /// <summary>
    /// Cola de telemetria entre los hilos de peticion y la interfaz. Los
    /// productores nunca se bloquean; la UI drena por lotes en su propio hilo.
    /// </summary>
    public sealed class LogBus
    {
        // Tope de seguridad: si la interfaz no drena tan rapido como entran las
        // peticiones, la cola dejaria de ser un buffer para ser una fuga.
        private const int MaxPending = 20000;

        private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private int _pending;
        private int _dropped;

        public bool IsEmpty { get { return _queue.IsEmpty; } }

        private void Enqueue(string entry)
        {
            if (Volatile.Read(ref _pending) >= MaxPending)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }
            Interlocked.Increment(ref _pending);
            _queue.Enqueue(entry);
        }

        public void Write(string level, string message)
        {
            Enqueue(string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}]\r\n{2}\r\n",
                                  DateTime.Now, level, message));
        }

        public void WriteRequest(string level, string method, string url, int statusCode,
                                 long elapsedMs, long bytes, string note, string clientIp)
        {
            Enqueue(string.Format(
                "{0:yyyy-MM-dd HH:mm:ss} [{1}]\r\n{2} {3} {4} {5}ms {6}B{7} - Client: {8}\r\n",
                DateTime.Now, level, method, url, statusCode, elapsedMs, bytes,
                string.IsNullOrEmpty(note) ? "" : note, clientIp));
        }

        /// <summary>
        /// Extrae hasta maxEntries mensajes concatenados. Devuelve cadena vacia
        /// si no habia nada, para que la UI pueda saltarse el repintado.
        /// </summary>
        public string Drain(int maxEntries)
        {
            if (_queue.IsEmpty) return string.Empty;

            StringBuilder sb = new StringBuilder();
            string item;
            int drained = 0;
            while (drained < maxEntries && _queue.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _pending);
                sb.Append(item);
                drained++;
            }

            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                sb.Append(string.Format("{0:yyyy-MM-dd HH:mm:ss} [WARN]\r\n" +
                                        "{1} entradas de registro descartadas por saturacion.\r\n",
                                        DateTime.Now, dropped));
            }

            return sb.ToString();
        }
    }
}
