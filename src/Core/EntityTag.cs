using System;
using System.Globalization;
using System.IO;

namespace LanShare.Core
{
    /// <summary>
    /// Validadores de cache para el modo distribucion.
    ///
    /// El identificador se deriva del tamano y la fecha de modificacion, no de
    /// un hash del contenido: calcular un hash exigiria leer el archivo entero
    /// en cada peticion, que es justo el trabajo que se quiere evitar.
    /// </summary>
    public static class EntityTag
    {
        /// <param name="variant">
        /// Marca de la representacion concreta que se va a enviar (comprimida,
        /// con sonda inyectada). Sin ella, el cuerpo gzip y el plano
        /// compartirian validador y un cliente podria reutilizar bytes
        /// comprimidos como si fueran texto plano.
        /// </param>
        public static string Build(long length, DateTime lastWriteUtc, string variant)
        {
            string suffix = string.IsNullOrEmpty(variant) ? "" : "-" + variant;
            return "\"" + length.ToString("x", CultureInfo.InvariantCulture) + "-" +
                   lastWriteUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + suffix + "\"";
        }

        public static DateTime GetLastWriteUtc(string path)
        {
            try
            {
                // Sin milisegundos: HTTP solo transmite segundos y si no se
                // trunca, If-Modified-Since nunca coincide.
                DateTime raw = File.GetLastWriteTimeUtc(path);
                return new DateTime(raw.Year, raw.Month, raw.Day,
                                    raw.Hour, raw.Minute, raw.Second, DateTimeKind.Utc);
            }
            catch (Exception) { return DateTime.MinValue; }
        }

        /// <summary>
        /// El cliente ya tiene una copia valida. If-None-Match manda sobre
        /// If-Modified-Since cuando llegan los dos, como indica RFC 7232.
        /// </summary>
        public static bool ClientHasFreshCopy(string ifNoneMatch, string ifModifiedSince,
                                              string etag, DateTime lastWriteUtc)
        {
            if (!string.IsNullOrEmpty(ifNoneMatch))
            {
                if (ifNoneMatch.Trim() == "*") return true;

                foreach (string candidate in ifNoneMatch.Split(','))
                {
                    string trimmed = candidate.Trim();
                    if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed.Substring(2);
                    if (trimmed == etag) return true;
                }
                return false;
            }

            if (!string.IsNullOrEmpty(ifModifiedSince) && lastWriteUtc != DateTime.MinValue)
            {
                DateTime since;
                if (DateTime.TryParse(ifModifiedSince, CultureInfo.InvariantCulture,
                                      DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                      out since))
                {
                    return lastWriteUtc <= since;
                }
            }

            return false;
        }

        public static string ToHttpDate(DateTime utc)
        {
            return utc.ToString("r", CultureInfo.InvariantCulture);
        }
    }
}
