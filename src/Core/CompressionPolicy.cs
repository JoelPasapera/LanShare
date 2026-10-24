using System;
using System.IO;
using System.IO.Compression;

namespace LanShare.Core
{
    /// <summary>
    /// Unica responsabilidad: decidir si comprimir y hacerlo.
    ///
    /// Comprimir lo que ya viene comprimido (jpg, mp4, woff2) solo gasta CPU y
    /// a veces engorda la respuesta, asi que la lista es explicita y corta.
    /// </summary>
    public static class CompressionPolicy
    {
        /// <summary>Por debajo de este tamano el encabezado gzip no compensa.</summary>
        private const int MinimumSize = 1024;

        /// <summary>Se comprime en memoria, asi que hay que poner un techo.</summary>
        public const long MaximumSize = 8L * 1024 * 1024;

        private static readonly string[] CompressiblePrefixes =
        {
            "text/",
            "application/json",
            "application/javascript",
            "application/xml",
            "application/manifest+json",
            "application/wasm",
            "image/svg+xml"
        };

        public static bool ClientAcceptsGzip(string acceptEncoding)
        {
            return !string.IsNullOrEmpty(acceptEncoding) &&
                   acceptEncoding.IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Solo el tipo, sin mirar el tamano: sirve para negociar antes de leer.</summary>
        public static bool IsCompressibleType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return false;

            foreach (string prefix in CompressiblePrefixes)
            {
                if (contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsCompressible(string contentType, long length)
        {
            if (length < MinimumSize || length > MaximumSize) return false;
            return IsCompressibleType(contentType);
        }

        /// <summary>
        /// Devuelve el original si comprimir no ganaria nada: con contenido casi
        /// incompresible el resultado puede ser mayor que la entrada.
        /// </summary>
        public static byte[] Gzip(byte[] payload)
        {
            try
            {
                using (MemoryStream output = new MemoryStream(payload.Length / 2 + 256))
                {
                    using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                    {
                        gzip.Write(payload, 0, payload.Length);
                    }

                    byte[] compressed = output.ToArray();
                    return compressed.Length < payload.Length ? compressed : null;
                }
            }
            catch (Exception) { return null; }
        }
    }
}
