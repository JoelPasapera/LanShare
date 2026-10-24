using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Consola remota: lectura del lote, linea del registro, ritmo por
    /// cliente, lectura acotada del cuerpo e insercion al principio de head.
    /// </summary>
    public static class RemoteConsoleTests
    {
        private const string Origin = "http://192.168.1.5:3000";

        private const string V8Stack =
            "TypeError: x is not a function at data.json:3:5\n" +
            "    at render (http://192.168.1.5:3000/js/app.js:42:7)\n" +
            "    at http://192.168.1.5:3000/js/main.js:10:3\n" +
            "    at new Promise (<anonymous>)\n" +
            "    at async load (http://192.168.1.5:3000/js/net.js:5:1)\n" +
            "    at x (http://cdn.example.com/lib.js:1:99)";

        private const string GeckoStack =
            "render@http://192.168.1.5:3000/js/app.js:42:7\n" +
            "@http://192.168.1.5:3000/js/main.js:10:3\n";

        private const string SafariStack =
            "render@http://192.168.1.5:3000/js/app.js:42:7\n" +
            "global code@http://192.168.1.5:3000/index.html:57:12\n" +
            "apply@[native code]";

        private static ConsoleReport Report(int entries)
        {
            StringBuilder json = new StringBuilder("{\"p\":\"/index.html\",\"d\":0,\"e\":[");
            for (int i = 0; i < entries; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"l\":\"log\",\"m\":\"mensaje ").Append(i).Append("\",\"s\":\"\",\"k\":\"\",\"n\":1}");
            }
            json.Append("]}");
            return ConsoleReport.Parse(json.ToString());
        }

        private static string ReadBody(byte[] data, int chunk, int max)
        {
            using (ChunkedStream stream = new ChunkedStream(data, chunk))
            {
                return BodyReader.ReadUtf8Async(stream, max).GetAwaiter().GetResult();
            }
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / ConsoleReport - lectura del lote");

            runner.Test("un lote completo se lee campo a campo", delegate
            {
                ConsoleReport report = ConsoleReport.Parse(
                    "{\"p\":\"/index.html?x=1\",\"d\":3,\"e\":[" +
                    "{\"l\":\"error\",\"m\":\"boom\",\"s\":\"/js/app.js:1:2\",\"k\":\"pila\",\"n\":5}," +
                    "{\"l\":\"log\",\"m\":\"hola\",\"s\":\"\",\"k\":\"\",\"n\":1}]}");

                Check.NotNull(report, "interpretable");
                Check.Equal("/index.html?x=1", report.Page, "pagina");
                Check.Equal(3, report.Dropped, "omitidos en el navegador");
                Check.Equal(2, report.Entries.Count, "dos mensajes");
                Check.Equal("error", report.Entries[0].Level, "nivel");
                Check.Equal("boom", report.Entries[0].Text, "texto");
                Check.Equal("/js/app.js:1:2", report.Entries[0].Where, "ubicacion");
                Check.Equal("pila", report.Entries[0].Stack, "pila");
                Check.Equal(5, report.Entries[0].Count, "repeticiones");
            });

            runner.Test("lo que no es un objeto JSON se rechaza", delegate
            {
                Check.Null(ConsoleReport.Parse(null), "nulo");
                Check.Null(ConsoleReport.Parse(""), "vacio");
                Check.Null(ConsoleReport.Parse("no es json"), "texto");
                Check.Null(ConsoleReport.Parse("[1,2,3]"), "un array");
                Check.Null(ConsoleReport.Parse("\"cadena\""), "una cadena");
                Check.Null(ConsoleReport.Parse("{\"e\":["), "truncado");
            });

            runner.Test("SEGURIDAD: tipos inesperados se toman como vacios", delegate
            {
                ConsoleReport report = ConsoleReport.Parse(
                    "{\"p\":42,\"d\":\"muchos\",\"e\":[7,\"x\",null," +
                    "{\"l\":[1],\"m\":{\"a\":1},\"s\":true,\"k\":null,\"n\":\"9\"}]}");

                Check.NotNull(report, "el lote se acepta");
                Check.Equal("", report.Page, "pagina numerica");
                Check.Equal(0, report.Dropped, "omitidos de texto");
                Check.Equal(1, report.Entries.Count, "solo cuenta el objeto");
                Check.Equal("", report.Entries[0].Level, "nivel no textual");
                Check.Equal("", report.Entries[0].Text, "texto no textual");
                Check.Equal(1, report.Entries[0].Count, "repeticiones minimas");
            });

            runner.Test("numeros negativos, decimales y enormes se acotan", delegate
            {
                Check.Equal(0, ConsoleReport.Parse("{\"d\":-5}").Dropped, "negativo");
                Check.Equal(2, ConsoleReport.Parse("{\"d\":2.9}").Dropped, "decimal truncado");
                Check.Equal(int.MaxValue, ConsoleReport.Parse("{\"d\":99999999999}").Dropped, "enorme");
            });

            runner.Test("SEGURIDAD: un lote no admite mas de MaxEntries mensajes", delegate
            {
                ConsoleReport report = Report(ConsoleReport.MaxEntries + 50);
                Check.Equal(ConsoleReport.MaxEntries, report.Entries.Count, "recortado");
            });

            runner.Test("SEGURIDAD: un anidamiento profundo no tumba el servidor", delegate
            {
                string deep = new string('[', 5000) + new string(']', 5000);
                Check.DoesNotThrow(delegate { ConsoleReport.Parse("{\"e\":" + deep + "}"); }, "sin excepcion");
                Check.Null(ConsoleReport.Parse("{\"e\":" + deep + "}"), "rechazado");
            });

            runner.Suite("Core / ConsoleLine - linea del registro");

            runner.Test("niveles del navegador a etiquetas del registro", delegate
            {
                Check.Equal("JS ERROR", ConsoleLine.LevelTag("error"), "error");
                Check.Equal("JS WARN", ConsoleLine.LevelTag("warn"), "warn");
                Check.Equal("JS INFO", ConsoleLine.LevelTag("info"), "info");
                Check.Equal("JS DEBUG", ConsoleLine.LevelTag("debug"), "debug");
                Check.Equal("JS LOG", ConsoleLine.LevelTag("log"), "log");
                Check.Equal("JS LOG", ConsoleLine.LevelTag("inventado"), "desconocido");
                Check.Equal("JS LOG", ConsoleLine.LevelTag(null), "nulo");
            });

            runner.Test("pila de Chrome: tres marcos, sin el origen y sin la cabecera", delegate
            {
                List<string> frames = ConsoleLine.StackLocations(V8Stack, Origin, 3);
                Check.Equal(3, frames.Count, "tres marcos");
                Check.Equal("/js/app.js:42:7", frames[0], "primero");
                Check.Equal("/js/main.js:10:3", frames[1], "anonimo");
                Check.Equal("/js/net.js:5:1", frames[2], "async; se salta <anonymous>");

                List<string> all = ConsoleLine.StackLocations(V8Stack, Origin, 10);
                Check.Equal("http://cdn.example.com/lib.js:1:99", all[all.Count - 1], "otro origen intacto");
            });

            runner.Test("pilas de Firefox y Safari", delegate
            {
                List<string> gecko = ConsoleLine.StackLocations(GeckoStack, Origin, 3);
                Check.Equal(2, gecko.Count, "Firefox");
                Check.Equal("/js/main.js:10:3", gecko[1], "funcion anonima");

                List<string> safari = ConsoleLine.StackLocations(SafariStack, Origin, 3);
                Check.Equal(2, safari.Count, "Safari, sin [native code]");
                Check.Equal("/index.html:57:12", safari[1], "codigo global");
            });

            runner.Test("ubicacion: se lee desde el final porque la URL lleva dos puntos", delegate
            {
                Check.Equal("http://h:3000/a.js:1:2", ConsoleLine.Locate("    at f (http://h:3000/a.js:1:2)"), "V8");
                Check.Equal("<anonymous>:1:1",
                            ConsoleLine.Locate("    at eval (eval at <anonymous> (http://h/a.js:1:1), <anonymous>:1:1)"),
                            "eval");
                Check.Null(ConsoleLine.Locate("at f (http://h:3000/x:5)"), "sin columna");
                Check.Null(ConsoleLine.Locate("at f (http://h:3000/x)"), "sin linea");
                Check.Null(ConsoleLine.Locate(":1:2"), "sin URL");
                Check.Null(ConsoleLine.Locate(null), "nulo");
            });

            runner.Test("acortar solo quita el origen exacto", delegate
            {
                Check.Equal("/a.js:1:2", ConsoleLine.Shorten("http://192.168.1.5:3000/a.js:1:2", Origin), "mismo origen");
                Check.Equal("/a.js:1:2", ConsoleLine.Shorten("HTTP://192.168.1.5:3000/a.js:1:2", Origin), "mayusculas");
                Check.Equal("http://192.168.1.5:30001/a.js:1:2",
                            ConsoleLine.Shorten("http://192.168.1.5:30001/a.js:1:2", Origin), "otro puerto que empieza igual");
                Check.Equal("/a.js:1:2", ConsoleLine.Shorten("/a.js:1:2", ""), "sin origen");
            });

            runner.Test("con pila mandan sus marcos; sin pila, la ubicacion de la llamada", delegate
            {
                Check.Equal("/js/app.js:42:7 <- /js/main.js:10:3",
                            ConsoleLine.Frames("/index.html:5:1", GeckoStack, Origin), "pila");
                Check.Equal("/index.html:5:1", ConsoleLine.Frames("/index.html:5:1", "", Origin), "llamada");
                Check.Equal("", ConsoleLine.Frames("", "", Origin), "nada");
            });

            runner.Test("CLAVE: una entrada es siempre una sola linea", delegate
            {
                Check.Equal("linea1 | linea2 | linea3", ConsoleLine.Flatten("linea1\r\nlinea2\n\n\nlinea3\n", 100), "saltos");
                Check.Equal("hola", ConsoleLine.Flatten("\n\nhola", 100), "saltos iniciales");
                Check.Equal("a | b", ConsoleLine.Flatten("a\u2028b", 100), "separador Unicode");
            });

            runner.Test("SEGURIDAD: sin caracteres de control ni de direccion", delegate
            {
                Check.Equal("a b c d e f", ConsoleLine.Flatten("a\tb\u001bc\u202ed\u2066e\u200ff", 100), "neutralizados");
                Check.Equal("", ConsoleLine.Flatten(null, 100), "nulo");
            });

            runner.Test("recorte con puntos suspensivos y sin partir un emoji", delegate
            {
                Check.Equal("xxxxx...", ConsoleLine.Flatten("xxxxxxxxxx", 5), "recortado");
                Check.Equal("abcd...", ConsoleLine.Flatten("abcd\U0001F600z", 5), "emoji en el corte");
                Check.Equal("abc\U0001F600...", ConsoleLine.Flatten("abc\U0001F600z", 5), "emoji entero");
                Check.Equal("abcde", ConsoleLine.Flatten("abcde", 5), "justo en el limite");
            });

            runner.Test("la linea completa: cliente, pagina, texto, origen y repeticiones", delegate
            {
                ConsoleEntry entry = new ConsoleEntry("error", "Uncaught TypeError: x\nmas", "/js/app.js:42:7", "", 4);
                string line = ConsoleLine.Format(entry, "192.168.1.23", "/index.html", Origin);
                Check.Equal("192.168.1.23 /index.html  Uncaught TypeError: x | mas  @ /js/app.js:42:7  (x4)", line, "formato");

                string sola = ConsoleLine.Format(new ConsoleEntry("log", "hola", "", "", 1), "::1", "", Origin);
                Check.Equal("::1  hola", sola, "sin pagina, sin ubicacion, sin repeticion");
            });

            runner.Suite("Core / TokenBucket");

            runner.Test("rafaga hasta la capacidad y luego nada", delegate
            {
                TokenBucket bucket = new TokenBucket(5, 1, 0);
                Check.Equal(3, bucket.Take(3, 0), "tres");
                Check.Equal(2, bucket.Take(10, 0), "las dos que quedan");
                Check.Equal(0, bucket.Take(1, 0), "vacia");
            });

            runner.Test("se repone con el tiempo sin pasar de la capacidad", delegate
            {
                TokenBucket bucket = new TokenBucket(10, 10, 0);
                bucket.Take(10, 0);
                Check.Equal(0, bucket.Take(1, 50), "en 50 ms no llega ni una");
                Check.Equal(1, bucket.Take(5, 150), "en 150 ms, una y media: una");
                Check.Equal(10, bucket.Take(100, 60000), "tras un minuto, solo la capacidad");
            });

            runner.Test("un reloj que retrocede no regala fichas", delegate
            {
                TokenBucket bucket = new TokenBucket(2, 1000, 1000);
                bucket.Take(2, 1000);
                Check.Equal(0, bucket.Take(1, 0), "hacia atras");
                Check.Equal(1, bucket.Take(1, 1001), "un milisegundo despues del ultimo");
            });

            runner.Test("argumentos invalidos", delegate
            {
                Check.Throws(delegate { new TokenBucket(0, 1, 0); }, "capacidad cero");
                Check.Throws(delegate { new TokenBucket(1, 0, 0); }, "ritmo cero");
                Check.Throws(delegate { new TokenBucket(1, double.NaN, 0); }, "ritmo NaN");
                Check.Equal(0, new TokenBucket(1, 1, 0).Take(-3, 0), "pedir negativo");
            });

            runner.Suite("Core / ConsoleRelay - escritura en el registro");

            runner.Test("cada mensaje es una linea con el puerto del sitio", delegate
            {
                LogBus log = new LogBus();
                ConsoleRelay relay = new ConsoleRelay(log);
                ConsoleReport report = ConsoleReport.Parse(
                    "{\"p\":\"/index.html\",\"d\":0,\"e\":[" +
                    "{\"l\":\"error\",\"m\":\"boom\",\"s\":\"/js/app.js:1:2\",\"k\":\"\",\"n\":1}," +
                    "{\"l\":\"warn\",\"m\":\"cuidado\",\"s\":\"\",\"k\":\"\",\"n\":2}]}");

                Check.Equal(2, relay.Relay(report, "192.168.1.23", 3000, Origin, 0), "dos escritos");

                List<LogEntry> lines = log.Drain(10);
                Check.Equal(2, lines.Count, "dos lineas");
                Check.Contains(lines[0].Text, "[JS ERROR] 192.168.1.23 /index.html  boom  @ /js/app.js:1:2", "error");
                Check.Contains(lines[1].Text, "[JS WARN]", "aviso");
                Check.Contains(lines[1].Text, "(x2)", "repeticiones");
                Check.Equal(3000, lines[0].Port, "puerto para el filtro");
            });

            runner.Test("CLAVE: una pagina desbocada no ahoga el registro", delegate
            {
                LogBus log = new LogBus();
                ConsoleRelay relay = new ConsoleRelay(log);

                int written = relay.Relay(Report(100), "10.0.0.7", 3000, Origin, 0);
                Check.Equal(ConsoleRelay.Burst, written, "solo la rafaga");

                // El aviso de omitidos necesita una ficha; llegara cuando la haya.
                List<LogEntry> lines = log.Drain(500);
                Check.Equal(ConsoleRelay.Burst, lines.Count, "sin aviso todavia: no quedan fichas");

                relay.Relay(Report(0), "10.0.0.7", 3000, Origin, 1000);
                lines = log.Drain(500);
                Check.Equal(1, lines.Count, "el aviso, en cuanto hay ritmo");
                Check.Contains(lines[0].Text, "60 mensajes de consola omitidos", "con la cuenta exacta");
            });

            runner.Test("el ritmo es por cliente", delegate
            {
                LogBus log = new LogBus();
                ConsoleRelay relay = new ConsoleRelay(log);
                relay.Relay(Report(ConsoleRelay.Burst), "10.0.0.1", 3000, Origin, 0);
                Check.Equal(5, relay.Relay(Report(5), "10.0.0.2", 3000, Origin, 0), "otro cliente no se ve afectado");
                Check.Equal(0, relay.Relay(Report(5), "10.0.0.1", 3000, Origin, 0), "el primero sigue sin fichas");
            });

            runner.Test("los omitidos en el navegador tambien se avisan", delegate
            {
                LogBus log = new LogBus();
                ConsoleRelay relay = new ConsoleRelay(log);
                relay.Relay(ConsoleReport.Parse("{\"p\":\"/\",\"d\":1,\"e\":[]}"), "10.0.0.3", 3000, Origin, 0);
                List<LogEntry> lines = log.Drain(10);
                Check.Equal(1, lines.Count, "un aviso");
                Check.Contains(lines[0].Text, "1 mensaje de consola omitido", "singular");
            });

            runner.Suite("Core / BodyReader - cuerpo con tope");

            runner.Test("lee el cuerpo entero aunque llegue troceado", delegate
            {
                byte[] data = Encoding.UTF8.GetBytes("{\"p\":\"/caf\u00e9\"}");
                Check.Equal("{\"p\":\"/caf\u00e9\"}", ReadBody(data, 1, 1024), "de byte en byte");
                Check.Equal("{\"p\":\"/caf\u00e9\"}", ReadBody(data, 7, data.Length), "justo en el tope");
            });

            runner.Test("SEGURIDAD: un byte por encima del tope devuelve null", delegate
            {
                byte[] data = new byte[1025];
                Check.Null(ReadBody(data, 100, 1024), "rechazado");
                Check.Null(ReadBody(new byte[1], 1, 0), "tope cero");
                Check.Equal("", ReadBody(new byte[0], 1, 0), "cuerpo vacio");
            });

            runner.Test("la marca de orden de bytes se quita", delegate
            {
                byte[] data = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' };
                Check.Equal("{}", ReadBody(data, 2, 100), "sin BOM");
            });

            runner.Suite("Core / HtmlInjector - principio de head");

            runner.Test("despues de la etiqueta de apertura de head", delegate
            {
                Check.Equal("<!DOCTYPE html><html><head><S><meta charset=utf-8></head></html>",
                            HtmlInjector.InjectHead("<!DOCTYPE html><html><head><meta charset=utf-8></head></html>", "<S>"),
                            "minusculas");
                Check.Equal("<HTML lang=es><HEAD class=\"x\"><S><title>t</title>",
                            HtmlInjector.InjectHead("<HTML lang=es><HEAD class=\"x\"><title>t</title>", "<S>"),
                            "mayusculas y atributos");
                Check.Equal("<head\n><S>", HtmlInjector.InjectHead("<head\n>", "<S>"), "salto dentro de la etiqueta");
            });

            runner.Test("header no es head", delegate
            {
                Check.Equal("<html><S><body><header>h</header></body></html>",
                            HtmlInjector.InjectHead("<html><body><header>h</header></body></html>", "<S>"),
                            "tras <html>");
                Check.Equal("<S><html-x><head-y>", HtmlInjector.InjectHead("<html-x><head-y>", "<S>"),
                            "elementos personalizados");
            });

            runner.Test("CLAVE: nunca antes del DOCTYPE", delegate
            {
                Check.Equal("<!DOCTYPE html><S><header>x</header>",
                            HtmlInjector.InjectHead("<!DOCTYPE html><header>x</header>", "<S>"), "sin head ni html");
                Check.Equal("<!-- c --><!DOCTYPE html><S><p>x",
                            HtmlInjector.InjectHead("<!-- c --><!DOCTYPE html><p>x", "<S>"), "comentario delante");
                Check.Equal("\ufeff<!DOCTYPE html><S><p>",
                            HtmlInjector.InjectHead("\ufeff<!DOCTYPE html><p>", "<S>"), "con BOM");
            });

            runner.Test("fragmentos y vacios", delegate
            {
                Check.Equal("<S><p>fragmento</p>", HtmlInjector.InjectHead("<p>fragmento</p>", "<S>"), "al principio");
                Check.Equal("<S>", HtmlInjector.InjectHead("", "<S>"), "documento vacio");
                Check.Equal("<p>", HtmlInjector.InjectHead("<p>", ""), "sin fragmento");
                Check.Equal("", HtmlInjector.InjectHead(null, null), "todo nulo");
            });

            runner.Suite("Core / HtmlInjection - combinaciones");

            runner.Test("ocho combinaciones con ocho sufijos distintos", delegate
            {
                Dictionary<string, bool> seen = new Dictionary<string, bool>();
                for (int i = 0; i < 8; i++)
                {
                    HtmlInjection injection = HtmlInjection.For((i & 1) != 0, (i & 2) != 0, (i & 4) != 0);
                    Check.False(seen.ContainsKey(injection.Variant), "sufijo repetido: " + injection.Variant);
                    seen[injection.Variant] = true;
                }
                Check.True(HtmlInjection.None.IsEmpty, "nada que inyectar");
                Check.Equal("", HtmlInjection.None.Variant, "sin sufijo");
                Check.Equal("ic", HtmlInjection.For(false, false, true).Variant, "solo consola");
                Check.Equal("ipr", HtmlInjection.For(true, true, false).Variant, "compatible con el sufijo anterior");
            });

            runner.Test("pedir una combinacion no crea objetos", delegate
            {
                Check.True(object.ReferenceEquals(HtmlInjection.For(true, false, true),
                                                  HtmlInjection.For(true, false, true)), "misma instancia");
                Check.True(object.ReferenceEquals(HtmlInjection.None, HtmlInjection.For(false, false, false)),
                           "None es la combinacion vacia");
            });

            runner.Test("CLAVE: la consola va al principio y la sonda al final", delegate
            {
                string page = "<!DOCTYPE html><html><head><title>t</title></head><body><script>app()</script></body></html>";
                string html = HtmlInjection.For(true, false, true).Apply(page);

                int consola = html.IndexOf("__lanshareConsole", StringComparison.Ordinal);
                int app = html.IndexOf("app()", StringComparison.Ordinal);
                int sonda = html.IndexOf(Identity.ProbeScript.Html, StringComparison.Ordinal);
                int body = html.LastIndexOf("</body>", StringComparison.Ordinal);

                Check.True(consola > 0 && consola < html.IndexOf("<title>", StringComparison.Ordinal),
                           "consola antes que todo lo de head");
                Check.True(consola < app, "consola antes que los scripts de la pagina");
                Check.True(sonda > app && sonda < body, "sonda al final del body");
                Check.Equal(page, HtmlInjection.None.Apply(page), "sin nada, la pagina intacta");
            });

            runner.Suite("Core / RemoteConsoleScript");

            runner.Test("el script apunta al endpoint y no rompe el HTML", delegate
            {
                string script = RemoteConsoleScript.Html;
                Check.StartsWith(script, "<script>", "abre script");
                Check.True(script.EndsWith("</script>", StringComparison.Ordinal), "cierra script");
                Check.Contains(script, "'" + ConsoleRelay.Endpoint + "'", "endpoint");

                string inner = script.Substring(8, script.Length - 17);
                Check.DoesNotContain(inner, "</", "ningun cierre de etiqueta dentro");
                Check.DoesNotContain(inner, "<!--", "ningun comentario HTML dentro");
                Check.DoesNotContain(inner, "\"", "sin comillas dobles");
            });
        }
    }
}
