using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace LanShare.Core
{
    /// <summary>
    /// Una linea de registro con el puerto al que entro la peticion, o cero si
    /// no viene de ninguna. El puerto va aparte del texto para poder filtrar
    /// sin volver a analizar la cadena.
    /// </summary>
    public struct LogEntry
    {
        public readonly string Text;
        public readonly int Port;

        public LogEntry(string text, int port)
        {
            Text = text;
            Port = port;
        }
    }

    /// <summary>
    /// Cola de telemetria entre los hilos de peticion y la interfaz. Los
    /// productores nunca se bloquean; la UI drena por lotes en su propio hilo.
    ///
    /// Una linea por entrada, con la hora sin fecha: la fecha va en el nombre
    /// del archivo al guardar, y repetirla en cada linea duplicaba la altura
    /// del registro sin aportar nada dentro de una sesion.
    /// </summary>
    public sealed class LogBus
    {
        // Tope de seguridad: si la interfaz no drena tan rapido como entran las
        // peticiones, la cola dejaria de ser un buffer para ser una fuga.
        private const int MaxPending = 20000;

        private readonly ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
        private int _pending;
        private int _dropped;

        public bool IsEmpty { get { return _queue.IsEmpty; } }

        private void Enqueue(string text, int port)
        {
            if (Volatile.Read(ref _pending) >= MaxPending)
            {
                Interlocked.Increment(ref _dropped);
                return;
            }
            Interlocked.Increment(ref _pending);
            _queue.Enqueue(new LogEntry(text, port));
        }

        /// <summary>Mensaje del servidor, sin puerto asociado.</summary>
        public void Write(string level, string message)
        {
            Write(level, message, 0);
        }

        /// <summary>Mensaje del servidor sobre un sitio concreto: respeta el filtro por sitio.</summary>
        public void Write(string level, string message, int port)
        {
            Enqueue(string.Format("{0:HH:mm:ss} [{1}] {2}\r\n",
                                  DateTime.Now, level, message), port);
        }

        /// <summary>Con varios sitios a la vez, el puerto dice a cual entro la peticion.</summary>
        public void WriteRequest(string level, string method, string url, int statusCode,
                                 long elapsedMs, long bytes, string note, string clientIp, int port)
        {
            Enqueue(string.Format(
                "{0:HH:mm:ss} [{1}] {2} :{3}{4} {5} {6}ms {7}B{8} - Client: {9}\r\n",
                DateTime.Now, level, method, port, url, statusCode, elapsedMs, bytes,
                string.IsNullOrEmpty(note) ? "" : note, clientIp), port);
        }

        /// <summary>
        /// Extrae hasta maxEntries entradas. Lista vacia si no habia nada, para
        /// que la interfaz pueda saltarse el repintado.
        /// </summary>
        public List<LogEntry> Drain(int maxEntries)
        {
            List<LogEntry> batch = new List<LogEntry>();
            if (_queue.IsEmpty) return batch;

            LogEntry item;
            while (batch.Count < maxEntries && _queue.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _pending);
                batch.Add(item);
            }

            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                // El aviso de saturacion no lleva puerto: debe verse con
                // cualquier filtro, porque afecta a todos los sitios.
                batch.Add(new LogEntry(string.Format(
                    "{0:HH:mm:ss} [WARN] {1} entradas de registro descartadas por saturacion.\r\n",
                    DateTime.Now, dropped), 0));
            }

            return batch;
        }
    }
}
