using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace LanShare.Core
{
    /// <summary>
    /// Guarda el resultado de comprimir un archivo.
    ///
    /// Sin esto, servir el mismo JavaScript a tres dispositivos comprimia el
    /// mismo contenido tres veces. La clave incluye tamano y fecha de
    /// modificacion, asi que editar el archivo invalida la entrada sola: eso
    /// es imprescindible en modo desarrollo, donde se edita constantemente.
    /// </summary>
    public static class CompressionCache
    {
        /// <summary>Techo de memoria. Al superarlo se vacia entera.</summary>
        private const long MaxBytes = 32L * 1024 * 1024;

        private static readonly ConcurrentDictionary<string, byte[]> _entries =
            new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        private static long _bytes;
        private static long _hits;
        private static long _misses;

        public static int Count { get { return _entries.Count; } }
        public static long Bytes { get { return Interlocked.Read(ref _bytes); } }

        /// <summary>Consultas que encontraron el resultado ya comprimido.</summary>
        public static long Hits { get { return Interlocked.Read(ref _hits); } }

        /// <summary>Consultas que obligaron a comprimir.</summary>
        public static long Misses { get { return Interlocked.Read(ref _misses); } }

        /// <summary>Porcentaje de aciertos, o -1 si aun no hubo consultas.</summary>
        public static int HitRatePercent
        {
            get
            {
                long hits = Hits;
                long total = hits + Misses;
                return total == 0 ? -1 : (int)(hits * 100 / total);
            }
        }

        public static void ResetStatistics()
        {
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
        }

        /// <param name="variant">
        /// Distingue representaciones del mismo archivo: el HTML con la sonda
        /// inyectada no es el mismo cuerpo que el HTML tal cual.
        /// </param>
        public static string BuildKey(string path, long length, DateTime lastWriteUtc, string variant)
        {
            return path + "|" +
                   length.ToString("x", CultureInfo.InvariantCulture) + "|" +
                   lastWriteUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + "|" +
                   (variant ?? "");
        }

        public static byte[] Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            byte[] value;
            if (_entries.TryGetValue(key, out value))
            {
                Interlocked.Increment(ref _hits);
                return value;
            }
            Interlocked.Increment(ref _misses);
            return null;
        }

        public static void Store(string key, byte[] compressed)
        {
            if (string.IsNullOrEmpty(key) || compressed == null) return;

            // Vaciado total en vez de expulsion por antiguedad: para un
            // servidor de desarrollo el comportamiento predecible vale mas que
            // exprimir el ultimo acierto, y evita llevar contabilidad de uso.
            if (Interlocked.Read(ref _bytes) + compressed.Length > MaxBytes) Clear();

            if (_entries.TryAdd(key, compressed))
            {
                Interlocked.Add(ref _bytes, compressed.Length);
            }
        }

        public static void Clear()
        {
            _entries.Clear();
            Interlocked.Exchange(ref _bytes, 0);
        }
    }
}
