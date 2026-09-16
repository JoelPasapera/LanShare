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

        public static int Count { get { return _entries.Count; } }
        public static long Bytes { get { return Interlocked.Read(ref _bytes); } }

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
            return _entries.TryGetValue(key, out value) ? value : null;
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
