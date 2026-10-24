using System;
using LanShare.Identity;

namespace LanShare.Tests
{
    /// <summary>
    /// Interpretacion del User-Agent. El orden de las comprobaciones importa:
    /// Edge, Opera y Samsung Internet incluyen "Chrome/" en su cadena.
    /// </summary>
    public static class UaParserTests
    {
        private const string ChromeAndroid =
            "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/152.0.0.0 Mobile Safari/537.36";

        private const string ChromeWindows =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/152.0.0.0 Safari/537.36";

        private const string Safari17 =
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 " +
            "(KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1";

        public static void Register(TestRunner runner)
        {
            runner.Suite("Identity / UaParser - dispositivo");

            runner.Test("CLAVE: la UA congelada de Chrome no se toma por buena", delegate
            {
                // Chrome 110+ envia siempre "Android 10; K". Ni la version ni el
                // modelo son reales, y darlos por ciertos seria informar un dato
                // falso con aspecto de dato cierto.
                string resultado = UaParser.Device(ChromeAndroid);
                Check.Contains(resultado, "oculto", "se declara oculto");
                Check.DoesNotContain(resultado, "Android 10 ", "no afirma la version congelada");
            });

            runner.Test("un Android con modelo real lo muestra", delegate
            {
                string ua = "Mozilla/5.0 (Linux; Android 13; SM-S918B) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
                string resultado = UaParser.Device(ua);
                Check.Contains(resultado, "Android 13", "version");
                Check.Contains(resultado, "SM-S918B", "modelo");
            });

            runner.Test("se ignora el sufijo Build del modelo", delegate
            {
                string ua = "Mozilla/5.0 (Linux; Android 11; Redmi Note 9 Build/RP1A.200720.011) " +
                            "AppleWebKit/537.36 Chrome/100.0.0.0 Mobile Safari/537.36";
                string resultado = UaParser.Device(ua);
                Check.Contains(resultado, "Redmi Note 9", "modelo limpio");
                Check.DoesNotContain(resultado, "Build/", "sin el sufijo");
            });

            runner.Test("un modelo marcador se declara oculto", delegate
            {
                string ua = "Mozilla/5.0 (Linux; Android 12; K) AppleWebKit/537.36 Chrome/99.0 Mobile Safari/537.36";
                Check.Contains(UaParser.Device(ua), "oculto", "modelo K");
            });

            runner.Test("iPhone con su version de iOS", delegate
            {
                string resultado = UaParser.Device(Safari17);
                Check.Contains(resultado, "iPhone", "dispositivo");
                Check.Contains(resultado, "17.4", "version de iOS");
            });

            runner.Test("iPad con su version", delegate
            {
                string ua = "Mozilla/5.0 (iPad; CPU OS 16_6 like Mac OS X) AppleWebKit/605.1.15 " +
                            "(KHTML, like Gecko) Version/16.6 Safari/604.1";
                string resultado = UaParser.Device(ua);
                Check.Contains(resultado, "iPad", "dispositivo");
                Check.Contains(resultado, "16.6", "version");
            });

            runner.Test("iOS no se confunde con macOS pese al 'like Mac OS X'", delegate
            {
                Check.DoesNotContain(UaParser.Device(Safari17), "macOS", "iPhone no es macOS");
            });

            runner.Test("versiones de Windows", delegate
            {
                Check.Contains(UaParser.Device(ChromeWindows), "Windows 10/11", "NT 10.0");
                Check.Contains(UaParser.Device("Mozilla/5.0 (Windows NT 6.1)"), "Windows 7", "NT 6.1");
                Check.Contains(UaParser.Device("Mozilla/5.0 (Windows NT 6.3)"), "Windows 8.1", "NT 6.3");
            });

            runner.Test("macOS, Linux y ChromeOS", delegate
            {
                Check.Contains(UaParser.Device("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)"),
                               "macOS", "macOS");
                Check.Contains(UaParser.Device("Mozilla/5.0 (X11; Linux x86_64)"), "Linux", "Linux");
                Check.Contains(UaParser.Device("Mozilla/5.0 (X11; CrOS x86_64 14541.0.0)"),
                               "ChromeOS", "ChromeOS");
            });

            runner.Test("televisores y consolas", delegate
            {
                Check.Contains(UaParser.Device("Mozilla/5.0 (SMART-TV; LINUX; Tizen 6.0)"), "TV", "Tizen");
                Check.Contains(UaParser.Device("Mozilla/5.0 (PlayStation 5/2.26)"), "PlayStation", "consola");
            });

            runner.Test("Android TV antes que Android a secas", delegate
            {
                Check.Equal("Android TV", UaParser.Device("Mozilla/5.0 (Linux; Android TV 9)"), "orden");
            });

            runner.Test("sin datos devuelve desconocido", delegate
            {
                Check.Equal("Desconocido", UaParser.Device(null), "nulo");
                Check.Equal("Desconocido", UaParser.Device(""), "vacio");
                Check.Equal("Desconocido", UaParser.Device("algo sin sentido"), "irreconocible");
            });

            runner.Suite("Identity / UaParser - navegador");

            runner.Test("ORDEN: Edge no se confunde con Chrome", delegate
            {
                string ua = ChromeWindows + " Edg/120.0.2210.91";
                Check.StartsWith(UaParser.Browser(ua), "Edge", "Edge gana");
            });

            runner.Test("ORDEN: Opera no se confunde con Chrome", delegate
            {
                string ua = ChromeWindows + " OPR/106.0.0.0";
                Check.StartsWith(UaParser.Browser(ua), "Opera", "Opera gana");
            });

            runner.Test("ORDEN: Samsung Internet no se confunde con Chrome", delegate
            {
                string ua = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 " +
                            "SamsungBrowser/23.0 Chrome/115.0.0.0 Mobile Safari/537.36";
                Check.StartsWith(UaParser.Browser(ua), "Samsung Internet", "Samsung gana");
            });

            runner.Test("Chrome con su version mayor", delegate
            {
                Check.Equal("Chrome 152", UaParser.Browser(ChromeWindows), "version mayor");
            });

            runner.Test("Safari exige Version/ ademas de Safari/", delegate
            {
                Check.StartsWith(UaParser.Browser(Safari17), "Safari", "Safari real");
                Check.DoesNotContain(UaParser.Browser(ChromeWindows), "Safari",
                                     "Chrome lleva Safari/ pero no es Safari");
            });

            runner.Test("variantes en iOS", delegate
            {
                Check.Contains(UaParser.Browser("Mozilla/5.0 (iPhone) CriOS/120.0"), "Chrome iOS", "Chrome iOS");
                Check.Contains(UaParser.Browser("Mozilla/5.0 (iPhone) FxiOS/121.0"), "Firefox iOS", "Firefox iOS");
            });

            runner.Test("Firefox de escritorio", delegate
            {
                Check.Equal("Firefox 122",
                            UaParser.Browser("Mozilla/5.0 (Windows NT 10.0; rv:122.0) Gecko/20100101 Firefox/122.0"),
                            "version");
            });

            runner.Test("herramientas de linea de comandos y clientes propios", delegate
            {
                Check.StartsWith(UaParser.Browser("curl/8.4.0"), "curl", "curl");
                Check.Equal("wget", UaParser.Browser("Wget/1.21.4"), "wget");
                Check.Equal("Postman", UaParser.Browser("PostmanRuntime/7.36.0"), "Postman");
                Check.Equal("python-requests", UaParser.Browser("python-requests/2.31.0"), "python");
                Check.Contains(UaParser.Browser("okhttp/4.12.0"), "OkHttp", "app nativa");
            });

            runner.Test("sin datos devuelve desconocido", delegate
            {
                Check.Equal("Desconocido", UaParser.Browser(null), "nulo");
                Check.Equal("Desconocido", UaParser.Browser(""), "vacio");
                Check.Equal("Otro", UaParser.Browser("NavegadorRaro/1.0"), "irreconocible");
            });
        }
    }
}
