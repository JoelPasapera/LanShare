using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LanShare.Core
{
    /// <summary>
    /// Lee cuerpos de peticion pequenos con un tope estricto.
    ///
    /// Comprobar Content-Length no basta: una peticion troceada (chunked) no
    /// lo lleva, y leer hasta el final sin limite permitiria a cualquier
    /// equipo de la red llenar la memoria del servidor con un solo envio.
    /// </summary>
    public static class BodyReader
    {
        /// <returns>El cuerpo como texto UTF-8, o null si supera <paramref name="maxBytes"/>.</returns>
        public static async Task<string> ReadUtf8Async(Stream input, int maxBytes)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (maxBytes < 0) throw new ArgumentOutOfRangeException("maxBytes");

            byte[] buffer = new byte[Math.Min(maxBytes, 16 * 1024) + 1];
            using (MemoryStream collected = new MemoryStream())
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (collected.Length + read > maxBytes) return null;
                    collected.Write(buffer, 0, read);
                }

                string text = Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length);

                // StreamReader quitaba la marca de orden de bytes; se conserva
                // ese comportamiento para quien venia usandolo.
                return text.Length > 0 && text[0] == '\ufeff' ? text.Substring(1) : text;
            }
        }
    }
}
