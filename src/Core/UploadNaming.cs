using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace LanShare.Core
{
    /// <summary>
    /// Convierte el nombre que envia el cliente en uno seguro para Windows.
    ///
    /// El nombre viene de la red y no se confia en el: puede traer una ruta
    /// para salir de la carpeta, caracteres que Windows prohibe, nombres de
    /// dispositivo como CON o NUL que se comportan de forma extrana, o puntos
    /// finales que Windows elimina en silencio (y harian chocar dos nombres
    /// distintos).
    /// </summary>
    public static class UploadNaming
    {
        public const int MaxNameLength = 150;
        public const string Fallback = "archivo";

        private const string Forbidden = "<>:\"/\\|?*";

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string Sanitize(string raw)
        {
            if (raw == null) raw = "";

            // Navegadores antiguos enviaban la ruta completa del cliente; solo
            // interesa el ultimo componente.
            int slash = Math.Max(raw.LastIndexOf('/'), raw.LastIndexOf('\\'));
            string name = slash >= 0 ? raw.Substring(slash + 1) : raw;

            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 32 || c == 127) continue;

                // Caracteres de formato invisibles, como la anulacion de
                // direccion U+202E: con ella "fotogpj.exe" se ve en el
                // Explorador como "fotoexe.jpg" y engana sobre la extension.
                if (char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;

                sb.Append(Forbidden.IndexOf(c) >= 0 ? '_' : c);
            }

            string clean = sb.ToString().Trim().TrimEnd('.', ' ');
            if (clean.Length == 0 || clean == "." || clean == "..") clean = Fallback;

            if (IsReserved(clean)) clean = "_" + clean;

            if (clean.Length > MaxNameLength)
            {
                string extension = SafeExtension(clean);
                int cut = MaxNameLength - extension.Length;

                // Substring cuenta unidades UTF-16: un emoji son dos, y cortar
                // entre ambas dejaria medio caracter, que Windows guarda como
                // un nombre corrupto.
                if (char.IsHighSurrogate(clean[cut - 1])) cut--;
                clean = clean.Substring(0, cut).TrimEnd('.', ' ') + extension;
            }

            return clean;
        }

        /// <summary>CON, con.txt o "NUL .log" bloquean la creacion del archivo en Windows.</summary>
        public static bool IsReserved(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ');

            foreach (string reserved in ReservedNames)
            {
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Nombre para el intento n: el primero es el original; los siguientes
        /// siguen la convencion del explorador de Windows, "foto (2).jpg".
        /// </summary>
        public static string Candidate(string name, int attempt)
        {
            if (attempt <= 1) return name;

            string extension = SafeExtension(name);
            string stem = name.Substring(0, name.Length - extension.Length);

            // ".gitignore" no tiene raiz: el numero va al final.
            if (stem.Length == 0)
            {
                stem = name;
                extension = "";
            }

            return stem + " (" + attempt + ")" + extension;
        }

        /// <summary>Extension razonable o cadena vacia. Path.GetExtension lanza con caracteres invalidos.</summary>
        private static string SafeExtension(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || dot == name.Length - 1) return "";

            string extension = name.Substring(dot);
            return extension.Length <= 16 ? extension : "";
        }
    }

    /// <summary>
    /// Impide que una web cualquiera suba archivos usando el navegador del
    /// usuario.
    ///
    /// Un formulario multipart es una peticion simple: el navegador la envia a
    /// otro origen sin preguntar, y CORS solo impide leer la respuesta, no que
    /// llegue. Sin esta comprobacion, cualquier pagina visitada mientras la
    /// subida esta activa podria dejar archivos en el PC.
    /// </summary>
    public static class UploadGuard
    {
        /// <summary>Dominios reservados para redes locales: nadie puede registrarlos en Internet.</summary>
        private static readonly string[] LocalSuffixes = { ".localhost", ".local", ".lan", ".home.arpa", ".internal" };

        public static bool IsSameOrigin(string origin, string host, string secFetchSite)
        {
            return IsSameOrigin(origin, host, secFetchSite, false);
        }

        /// <param name="secure">
        /// Si la peticion llego por HTTPS: entonces el origen de la propia
        /// pagina empieza por https://. Comparar siempre con http:// rechazaba
        /// las subidas y la consola remota de los sitios servidos por HTTPS.
        /// </param>
        public static bool IsSameOrigin(string origin, string host, string secFetchSite, bool secure)
        {
            // DNS rebinding: una web puede hacer que su propio dominio resuelva
            // a la IP de este PC y entonces Origin y Host coinciden. Solo se
            // admiten nombres que no se pueden controlar desde Internet.
            if (!IsLocalHostName(host)) return false;

            // Los navegadores modernos declaran la relacion directamente.
            if (!string.IsNullOrEmpty(secFetchSite))
            {
                if (!string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // Sin Origin: clientes que no son navegadores (curl, scripts).
            if (string.IsNullOrEmpty(origin)) return true;

            // "null" lo envian los iframes aislados y las paginas locales.
            if (string.Equals(origin, "null", StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(host)) return false;

            string scheme = secure ? "https://" : "http://";
            return string.Equals(origin.TrimEnd('/'), scheme + host, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Si la cabecera Host nombra a este equipo de una forma que un atacante
        /// no puede suplantar con DNS: una direccion IP escrita tal cual, un
        /// nombre sin puntos (el del equipo en la red local), localhost o un
        /// dominio reservado para redes locales (.local de mDNS, .lan...).
        /// </summary>
        public static bool IsLocalHostName(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string name = host.Trim();

            // IPv6 entre corchetes, con o sin puerto: "[::1]:3000".
            if (name.StartsWith("[", StringComparison.Ordinal))
            {
                int close = name.IndexOf(']');
                IPAddress v6;
                return close > 1 && IPAddress.TryParse(name.Substring(1, close - 1), out v6);
            }

            int colon = name.LastIndexOf(':');
            if (colon >= 0) name = name.Substring(0, colon);
            name = name.TrimEnd('.');
            if (name.Length == 0) return false;

            if (IsIpv4Literal(name)) return true;
            if (name.IndexOf('.') < 0) return true;

            foreach (string suffix in LocalSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsIpv4Literal(string name)
        {
            foreach (char c in name)
            {
                if ((c < '0' || c > '9') && c != '.') return false;
            }
            IPAddress address;
            return IPAddress.TryParse(name, out address);
        }
    }
}
