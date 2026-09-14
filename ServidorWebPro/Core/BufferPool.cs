using System.Collections.Concurrent;
using System.Threading;

namespace ServidorWebPro.Core
{
    /// <summary>
    /// Reserva de buffers de copia. Con una pagina de 40 recursos, asignar
    /// 64 KB por peticion genera basura suficiente para provocar pausas del GC
    /// perceptibles. Reutilizarlos elimina esa presion.
    /// </summary>
    public static class BufferPool
    {
        public const int BufferSize = 65536;
        private const int MaxRetained = 64;

        private static readonly ConcurrentBag<byte[]> _pool = new ConcurrentBag<byte[]>();
        private static int _retained;

        public static byte[] Rent()
        {
            byte[] buffer;
            if (_pool.TryTake(out buffer))
            {
                Interlocked.Decrement(ref _retained);
                return buffer;
            }
            return new byte[BufferSize];
        }

        public static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length != BufferSize) return;

            // Por encima del tope se deja que el GC lo recoja: retener sin
            // limite convertiria la reserva en una fuga lenta.
            if (Interlocked.Increment(ref _retained) > MaxRetained)
            {
                Interlocked.Decrement(ref _retained);
                return;
            }
            _pool.Add(buffer);
        }
    }
}
