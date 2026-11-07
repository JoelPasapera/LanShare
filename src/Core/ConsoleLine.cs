using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Convierte un mensaje de consola en una sola linea del registro:
    /// <c>192.168.1.23 /index.html  TypeError: x no es una funcion  @ /js/app.js:42:7 &lt;- /js/main.js:10:3  (x5)</c>
    ///
    /// El texto llega de la red, asi que ademas de acortarlo se neutraliza:
    /// sin saltos de linea (una entrada, una linea), sin caracteres de control
    /// y sin los de direccion bidireccional, que permitirian que una linea del
    /// registro se leyera distinta de lo que dice.
    /// </summary>
    public static class ConsoleLine
    {
        public const int MaxTextLength = 600;
        public const int MaxPageLength = 160;
        public const int MaxLocationLength = 200;

        /// <summary>Marcos de la pila que se muestran.</summary>
        public const int MaxFrames = 3;

        public static string LevelTag(string level)
        {
            switch (level)
            {
                case "error": return "JS ERROR";
                case "warn": return "JS WARN";
                case "info": return "JS INFO";
                case "debug": return "JS DEBUG";
                default: return "JS LOG";
            }
        }

        /// <param name="origin">"http://host:puerto" del sitio, para acortar las rutas de la pila.</param>
        public static string Format(ConsoleEntry entry, string client, string page, string origin)
        {
            if (entry == null) throw new ArgumentNullException("entry");

            StringBuilder line = new StringBuilder(160);
            line.Append(Flatten(client, 64));

            string where = Flatten(page, MaxPageLength);
            if (where.Length > 0) line.Append(' ').Append(where);

            line.Append("  ").Append(Flatten(entry.Text, MaxTextLength));

            string frames = Frames(entry.Where, entry.Stack, origin);
            if (frames.Length > 0) line.Append("  @ ").Append(frames);

            if (entry.Count > 1)
            {
                line.Append("  (x").Append(entry.Count.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
            return line.ToString();
        }

        /// <summary>
        /// De donde viene el mensaje. Si hay pila, sus primeros marcos: son el
        /// origen real del error, que es lo que se busca. Si no, la ubicacion
        /// de la llamada que mando el script.
        /// </summary>
        public static string Frames(string where, string stack, string origin)
        {
            List<string> frames = StackLocations(stack, origin, MaxFrames);
            if (frames.Count == 0) return Flatten(Shorten(where, origin), MaxLocationLength);
            return string.Join(" <- ", frames.ToArray());
        }

        /// <summary>
        /// Ubicaciones de una pila de V8 ("    at f (url:1:2)"), Gecko o
        /// JavaScriptCore ("f@url:1:2"). La primera linea de V8 repite el
        /// mensaje y se descarta: solo cuentan las lineas con forma de marco.
        /// </summary>
        public static List<string> StackLocations(string stack, string origin, int max)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(stack)) return found;

            string[] lines = stack.Split('\n');
            for (int i = 0; i < lines.Length && found.Count < max; i++)
            {
                string line = lines[i];
                if (!IsFrame(line)) continue;

                string location = Locate(line);
                if (location == null) continue;

                found.Add(Flatten(Shorten(location, origin), MaxLocationLength));
            }
            return found;
        }

        private static bool IsFrame(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
            bool v8 = i + 3 <= line.Length && line[i] == 'a' && line[i + 1] == 't' &&
                      (line[i + 2] == ' ' || line[i + 2] == '\t');
            return v8 || line.IndexOf('@') >= 0;
        }

        /// <summary>
        /// "url:linea:columna" al final de un marco, o null. Se recorre desde el
        /// final porque la URL tambien lleva dos puntos (http://host:puerto).
        /// </summary>
        public static string Locate(string line)
        {
            if (line == null) return null;

            int end = line.Length;
            while (end > 0 && (char.IsWhiteSpace(line[end - 1]) || line[end - 1] == ')')) end--;

            int i = end;
            while (i > 0 && IsAsciiDigit(line[i - 1])) i--;
            if (i == end || i == 0 || line[i - 1] != ':') return null;

            int lineEnd = --i;
            while (i > 0 && IsAsciiDigit(line[i - 1])) i--;
            if (i == lineEnd || i == 0 || line[i - 1] != ':') return null;

            int urlEnd = --i;
            while (i > 0 && !char.IsWhiteSpace(line[i - 1]) && line[i - 1] != '(' && line[i - 1] != '@') i--;
            if (i == urlEnd) return null;

            return line.Substring(i, end - i);
        }

        /// <summary>Quita el origen del sitio: "http://192.168.1.5:3000/js/a.js:1:2" pasa a "/js/a.js:1:2".</summary>
        public static string Shorten(string location, string origin)
        {
            if (string.IsNullOrEmpty(location)) return "";
            if (string.IsNullOrEmpty(origin) || location.Length <= origin.Length) return location;
            if (!location.StartsWith(origin, StringComparison.OrdinalIgnoreCase)) return location;

            string rest = location.Substring(origin.Length);
            return rest.Length > 0 && rest[0] == '/' ? rest : location;
        }

        /// <summary>
        /// Una sola linea, sin caracteres de control ni de direccion, y como
        /// mucho <paramref name="max"/> caracteres mas "...". Los saltos de linea
        /// se ven como " | " para que un texto de varias lineas siga legible.
        /// </summary>
        public static string Flatten(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || max <= 0) return "";

            StringBuilder sb = new StringBuilder(Math.Min(text.Length, max) + 4);
            bool pendingBreak = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\u0085' || c == '\u2028' || c == '\u2029')
                {
                    pendingBreak = sb.Length > 0;
                    continue;
                }

                if (pendingBreak)
                {
                    sb.Append(" | ");
                    pendingBreak = false;
                }

                sb.Append(IsUnsafe(c) ? ' ' : c);

                if (sb.Length > max)
                {
                    int cut = max;
                    if (char.IsHighSurrogate(sb[cut - 1])) cut--;
                    sb.Length = cut;
                    sb.Append("...");
                    return sb.ToString();
                }
            }
            return sb.ToString();
        }

        private static bool IsUnsafe(char c)
        {
            if (c < ' ' || c == '\u007f') return true;
            if (c >= '\u0080' && c <= '\u009f') return true;

            // Marcas y anulaciones de direccion: LRE, RLE, PDF, LRO, RLO,
            // LRI, RLI, FSI, PDI, LRM, RLM y ALM.
            if (c >= '\u202a' && c <= '\u202e') return true;
            if (c >= '\u2066' && c <= '\u2069') return true;
            return c == '\u200e' || c == '\u200f' || c == '\u061c';
        }

        private static bool IsAsciiDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
