using System;
using System.Text.RegularExpressions;

namespace ServidorWebPro.Core
{
    /// <summary>Resultado de interpretar una cabecera Range.</summary>
    public struct RangeSpec
    {
        /// <summary>El cliente envio una cabecera Range interpretable.</summary>
        public bool Present;

        /// <summary>El rango es servible sobre este archivo. Si es false y Present es true, toca 416.</summary>
        public bool Satisfiable;

        public long Start;
        public long End;

        public long Length { get { return End - Start + 1; } }
    }

    /// <summary>Unica responsabilidad: interpretar "Range: bytes=..." de RFC 7233.</summary>
    public static class RangeParser
    {
        // Solo rango simple. Los multiples ("0-50,100-150") se ignoran a
        // proposito: la norma permite responder con el recurso completo.
        private static readonly Regex Pattern =
            new Regex(@"^\s*bytes\s*=\s*(\d*)\s*-\s*(\d*)\s*$", RegexOptions.Compiled);

        public static RangeSpec Parse(string header, long fileLength)
        {
            RangeSpec spec = new RangeSpec();
            spec.Present = false;
            spec.Satisfiable = false;
            spec.Start = 0;
            spec.End = fileLength - 1;

            if (string.IsNullOrEmpty(header)) return spec;

            Match m = Pattern.Match(header);
            if (!m.Success) return spec;

            spec.Present = true;
            string rawStart = m.Groups[1].Value;
            string rawEnd = m.Groups[2].Value;

            // "bytes=-" no designa nada: no es un rango valido.
            if (rawStart.Length == 0 && rawEnd.Length == 0) return spec;

            long start, end;

            if (rawStart.Length == 0)
            {
                // Sufijo: los ultimos N bytes.
                long suffix;
                if (!long.TryParse(rawEnd, out suffix) || suffix <= 0) return spec;
                start = Math.Max(0L, fileLength - suffix);
                end = fileLength - 1;
            }
            else
            {
                if (!long.TryParse(rawStart, out start)) return spec;

                if (rawEnd.Length == 0)
                {
                    end = fileLength - 1;
                }
                else
                {
                    if (!long.TryParse(rawEnd, out end)) return spec;
                }
            }

            if (end >= fileLength) end = fileLength - 1;
            if (fileLength == 0 || start >= fileLength || start > end) return spec;

            spec.Start = start;
            spec.End = end;
            spec.Satisfiable = true;
            return spec;
        }
    }
}
