using System;
using System.Text.RegularExpressions;

namespace LanShare.Identity
{
    /// <summary>Unica responsabilidad: interpretar la cadena User-Agent.</summary>
    public static class UaParser
    {
        // Literal congelado que Chrome 110+ envia en Android: ni la version ni
        // el modelo son reales, asi que etiquetarlos seria mentir.
        private const string FrozenAndroid = "Android 10; K";

        private static readonly Regex RxAndroidVersion = new Regex(@"Android\s+([\d.]+)", RegexOptions.Compiled);
        private static readonly Regex RxAndroidModel =
            new Regex(@"Android[^;)]*;\s*(?:[a-z]{2}(?:-[a-zA-Z]{2})?;\s*)?([^;)]+?)\s*(?:Build/|\))", RegexOptions.Compiled);
        private static readonly Regex RxIphone = new Regex(@"CPU iPhone OS (\d+)[_.](\d+)", RegexOptions.Compiled);
        private static readonly Regex RxIpad = new Regex(@"CPU OS (\d+)[_.](\d+)", RegexOptions.Compiled);
        private static readonly Regex RxMac = new Regex(@"Mac OS X (\d+)[_.](\d+)", RegexOptions.Compiled);

        public static string Browser(string ua)
        {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";

            // El orden importa: Edge, Opera y Samsung incluyen "Chrome/" en su UA.
            if (Has(ua, "Edg/")) return Join("Edge", VersionAfter(ua, "Edg/"));
            if (Has(ua, "EdgA/")) return Join("Edge Android", VersionAfter(ua, "EdgA/"));
            if (Has(ua, "EdgiOS/")) return Join("Edge iOS", VersionAfter(ua, "EdgiOS/"));
            if (Has(ua, "OPR/")) return Join("Opera", VersionAfter(ua, "OPR/"));
            if (Has(ua, "SamsungBrowser/")) return Join("Samsung Internet", VersionAfter(ua, "SamsungBrowser/"));
            if (Has(ua, "YaBrowser/")) return Join("Yandex", VersionAfter(ua, "YaBrowser/"));
            if (Has(ua, "Vivaldi/")) return Join("Vivaldi", VersionAfter(ua, "Vivaldi/"));
            if (Has(ua, "FxiOS/")) return Join("Firefox iOS", VersionAfter(ua, "FxiOS/"));
            if (Has(ua, "Firefox/")) return Join("Firefox", VersionAfter(ua, "Firefox/"));
            if (Has(ua, "CriOS/")) return Join("Chrome iOS", VersionAfter(ua, "CriOS/"));
            if (Has(ua, "Chrome/")) return Join("Chrome", VersionAfter(ua, "Chrome/"));
            if (Has(ua, "Version/") && Has(ua, "Safari/")) return Join("Safari", VersionAfter(ua, "Version/"));
            if (Has(ua, "curl/")) return Join("curl", VersionAfter(ua, "curl/"));
            if (Has(ua, "Wget")) return "wget";
            if (Has(ua, "PostmanRuntime")) return "Postman";
            if (Has(ua, "python-requests")) return "python-requests";
            if (Has(ua, "PowerShell")) return "PowerShell";
            if (Has(ua, "Dart/")) return "Dart / Flutter";
            if (Has(ua, "okhttp")) return "OkHttp (app nativa)";
            return "Otro";
        }

        public static string Device(string ua)
        {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";

            if (Has(ua, "Android TV")) return "Android TV";

            if (Has(ua, "Android"))
            {
                if (Has(ua, FrozenAndroid)) return "Android (version y modelo ocultos)";

                Match mv = RxAndroidVersion.Match(ua);
                string version = mv.Success ? mv.Groups[1].Value : "";

                Match mm = RxAndroidModel.Match(ua);
                string model = mm.Success ? mm.Groups[1].Value.Trim() : "";

                if (model == "K" || model == "Android" || model.Length == 0)
                    return Join("Android", version) + " (modelo oculto)";

                return Join("Android", version) + " \u00B7 " + model;
            }

            if (Has(ua, "Windows Phone")) return "Windows Phone";

            if (Has(ua, "iPhone"))
            {
                Match m = RxIphone.Match(ua);
                return m.Success
                    ? "iPhone \u00B7 iOS " + m.Groups[1].Value + "." + m.Groups[2].Value
                    : "iPhone";
            }
            if (Has(ua, "iPad"))
            {
                Match m = RxIpad.Match(ua);
                return m.Success
                    ? "iPad \u00B7 iPadOS " + m.Groups[1].Value + "." + m.Groups[2].Value
                    : "iPad";
            }
            if (Has(ua, "iPod")) return "iPod touch";

            if (Has(ua, "CrOS")) return "ChromeOS";

            if (Has(ua, "Windows NT 10.0")) return "Windows 10/11";
            if (Has(ua, "Windows NT 6.3")) return "Windows 8.1";
            if (Has(ua, "Windows NT 6.2")) return "Windows 8";
            if (Has(ua, "Windows NT 6.1")) return "Windows 7";
            if (Has(ua, "Windows NT")) return "Windows (antiguo)";

            if (Has(ua, "Mac OS X"))
            {
                // Safari congela macOS en 10.15.7 desde Big Sur.
                Match m = RxMac.Match(ua);
                return m.Success ? "macOS " + m.Groups[1].Value + "." + m.Groups[2].Value : "macOS";
            }

            if (Has(ua, "SMART-TV") || Has(ua, "Tizen") || Has(ua, "Web0S")) return "Smart TV";
            if (Has(ua, "PlayStation")) return "PlayStation";
            if (Has(ua, "Nintendo")) return "Nintendo";
            if (Has(ua, "Linux") || Has(ua, "X11")) return "Linux";

            return "Desconocido";
        }

        private static bool Has(string ua, string token)
        {
            return ua.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        /// <summary>Version mayor que sigue a un token, sin recurrir a regex.</summary>
        private static string VersionAfter(string ua, string token)
        {
            int i = ua.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return "";

            i += token.Length;
            int j = i;
            while (j < ua.Length && (char.IsDigit(ua[j]) || ua[j] == '.')) j++;

            string v = ua.Substring(i, j - i);
            int dot = v.IndexOf('.');
            return dot > 0 ? v.Substring(0, dot) : v;
        }

        private static string Join(string name, string version)
        {
            return version.Length > 0 ? name + " " + version : name;
        }
    }
}
