using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanShare.Core
{
    /// <summary>
    /// Conexiones abiertas de recarga automatica, una por pestana, agrupadas por
    /// el puerto del sitio al que pertenecen.
    ///
    /// Quien difunde nunca escribe en un socket: deja el mensaje en la cola del
    /// suscriptor y lo despierta. Cada conexion envia desde su propia tarea, asi
    /// que un movil que dejo de responder no frena el aviso a los demas.
    /// </summary>
    public sealed class LiveReloadHub
    {
        public const string Endpoint = "/__events";

        /// <summary>Codigo de cierre que le dice al script que no reintente.</summary>
        public const int DisabledCloseCode = 4000;

        /// <summary>Mensajes en cola por pestana; por encima se descartan.</summary>
        private const int MaxPendingPerClient = 32;

        private sealed class Subscriber
        {
            public readonly int Port;
            public readonly int Generation;
            public readonly ConcurrentQueue<string> Pending = new ConcurrentQueue<string>();
            public readonly SemaphoreSlim Signal = new SemaphoreSlim(0);
            public int PendingCount;

            public Subscriber(int port, int generation)
            {
                Port = port;
                Generation = generation;
            }
        }

        private readonly ConcurrentDictionary<Subscriber, byte> _subscribers =
            new ConcurrentDictionary<Subscriber, byte>();

        // Cerrar todo incrementa la generacion: cada conexion compara la suya y
        // sale. Un simple indicador de cierre no permitiria volver a abrir.
        private int _generation;
        private volatile int _closeCode = (int)WebSocketCloseStatus.EndpointUnavailable;

        public int Count
        {
            get { return _subscribers.Count; }
        }

        /// <summary>
        /// Atiende una pestana hasta que cierra o hasta que se cierra el canal.
        /// La tarea dura lo que dure la pestana abierta.
        /// </summary>
        public async Task ServeAsync(HttpListenerContext context, int port)
        {
            Subscriber me = new Subscriber(port, Volatile.Read(ref _generation));
            WebSocket socket = null;

            try
            {
                HttpListenerWebSocketContext upgraded =
                    await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = upgraded.WebSocket;
                _subscribers[me] = 0;

                Task closed = WaitForCloseAsync(socket);

                while (socket.State == WebSocketState.Open &&
                       me.Generation == Volatile.Read(ref _generation))
                {
                    Task<bool> signal = me.Signal.WaitAsync(30000);
                    Task first = await Task.WhenAny(signal, closed).ConfigureAwait(false);
                    if (first == closed) break;

                    string message;
                    while (me.Pending.TryDequeue(out message))
                    {
                        Interlocked.Decrement(ref me.PendingCount);
                        byte[] bytes = Encoding.UTF8.GetBytes(message);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                                               true, CancellationToken.None).ConfigureAwait(false);
                    }
                }

                // Cierre ordenado: si lo pidio la pestana, se confirma; si lo
                // pide el servidor, se indica si debe reintentar o no.
                if (socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "",
                                                  CancellationToken.None).ConfigureAwait(false);
                }
                else if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseOutputAsync((WebSocketCloseStatus)_closeCode, "LanShare",
                                                  CancellationToken.None).ConfigureAwait(false);

                    // Se espera la respuesta de la pestana antes de liberar el
                    // socket: liberarlo a mitad del cierre puede cortar la
                    // conexion y perder el codigo, y sin el codigo 4000 la
                    // pestana seguiria reintentando con la recarga apagada.
                    await Task.WhenAny(closed, Task.Delay(1000)).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Pestana cerrada de golpe, red caida o servidor detenido: no
                // hay nada que hacer salvo dejar de atenderla.
            }
            finally
            {
                byte ignored;
                _subscribers.TryRemove(me, out ignored);
                if (socket != null) socket.Dispose();
            }
        }

        /// <summary>Envia un mensaje a todas las pestanas abiertas de un sitio.</summary>
        public int Broadcast(int port, string message)
        {
            int delivered = 0;
            foreach (Subscriber subscriber in _subscribers.Keys)
            {
                if (subscriber.Port != port) continue;

                // Una pestana que no drena no debe acumular memoria sin limite.
                if (Interlocked.Increment(ref subscriber.PendingCount) > MaxPendingPerClient)
                {
                    Interlocked.Decrement(ref subscriber.PendingCount);
                    continue;
                }

                subscriber.Pending.Enqueue(message);
                try
                {
                    subscriber.Signal.Release();
                    delivered++;
                }
                catch (Exception) { }
            }
            return delivered;
        }

        /// <param name="disabled">
        /// Verdadero si la recarga se desactivo: las pestanas no reintentaran.
        /// Falso si el servidor se detiene: reintentaran hasta que vuelva.
        /// </param>
        public void CloseAll(bool disabled)
        {
            _closeCode = disabled ? DisabledCloseCode : (int)WebSocketCloseStatus.EndpointUnavailable;
            Interlocked.Increment(ref _generation);

            foreach (Subscriber subscriber in _subscribers.Keys)
            {
                try { subscriber.Signal.Release(); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// Una pestana con el script de una sesion anterior pide conectar con
        /// la recarga apagada. Se acepta y se cierra con el codigo 4000, que le
        /// dice que no reintente: ante un 404 el script no distingue "apagada"
        /// de "red caida" y reintentaria cada diez segundos para siempre,
        /// llenando el registro de avisos.
        /// </summary>
        public static async Task RejectAsync(HttpListenerContext context)
        {
            WebSocket socket = null;
            try
            {
                HttpListenerWebSocketContext upgraded =
                    await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                socket = upgraded.WebSocket;

                Task closed = WaitForCloseAsync(socket);
                await socket.CloseOutputAsync((WebSocketCloseStatus)DisabledCloseCode, "LanShare",
                                              CancellationToken.None).ConfigureAwait(false);
                await Task.WhenAny(closed, Task.Delay(1000)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // La pestana ya se fue: nada que avisar.
            }
            finally
            {
                if (socket != null) socket.Dispose();
            }
        }

        /// <summary>
        /// Lee y descarta lo que llegue hasta que la pestana cierra. Sin una
        /// lectura pendiente, el cierre del otro extremo no se detectaria hasta
        /// el siguiente envio.
        /// </summary>
        private static async Task WaitForCloseAsync(WebSocket socket)
        {
            byte[] buffer = new byte[256];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                }
            }
            catch (Exception) { }
        }
    }
}
