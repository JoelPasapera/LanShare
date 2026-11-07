using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Recarga automatica: que decide hacer con cada lote de cambios, como se
    /// inyecta el script, y una prueba real con el vigilante de archivos.
    /// </summary>
    public static class LiveReloadTests
    {
        private static List<string> Paths(params string[] items)
        {
            return new List<string>(items);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / ReloadPlanner");

            runner.Test("solo hojas de estilo: cambio en caliente", delegate
            {
                ReloadPlan plan = ReloadPlanner.Plan(Paths("css\\app.css", "css\\tema.css"), false);
                Check.Equal(ReloadKind.Css, plan.Kind, "tipo");
                Check.Equal(2, plan.CssPaths.Count, "dos hojas");
                Check.Equal("/css/app.css", plan.CssPaths[0], "ruta URL");
            });

            runner.Test("cualquier otro archivo obliga a recargar entero", delegate
            {
                Check.Equal(ReloadKind.Full, ReloadPlanner.Plan(Paths("js\\main.js"), false).Kind, "JavaScript");
                Check.Equal(ReloadKind.Full, ReloadPlanner.Plan(Paths("index.html"), false).Kind, "HTML");
                Check.Equal(ReloadKind.Full, ReloadPlanner.Plan(Paths("a.css", "b.js"), false).Kind, "mezcla");
                Check.Equal(ReloadKind.Full, ReloadPlanner.Plan(Paths("img\\logo.svg"), false).Kind, "imagen");
            });

            runner.Test("el ruido de editores y herramientas no recarga", delegate
            {
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths(".git\\index"), false).Kind, ".git");
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths("node_modules\\x\\y.js"), false).Kind, "node_modules");
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths("app.js~", "app.js.tmp", ".#app.js"), false).Kind,
                            "temporales");
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths("app.js___jb_tmp___", "app.js___jb_old___"), false).Kind,
                            "guardado seguro de JetBrains");
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths("~$informe.docx", "Thumbs.db"), false).Kind,
                            "Office y Windows");
                Check.Equal(ReloadKind.None, ReloadPlanner.Plan(Paths(), false).Kind, "sin cambios");
            });

            runner.Test("el ruido no impide ver el cambio real que lo acompana", delegate
            {
                ReloadPlan plan = ReloadPlanner.Plan(Paths("css\\app.css.tmp", "css\\app.css"), false);
                Check.Equal(ReloadKind.Css, plan.Kind, "el temporal se ignora, la hoja cuenta");
                Check.Equal(1, plan.CssPaths.Count, "una hoja");
            });

            runner.Test("si se perdieron eventos, recarga completa", delegate
            {
                Check.Equal(ReloadKind.Full, ReloadPlanner.Plan(Paths(), true).Kind, "desbordamiento");
            });

            runner.Test("las rutas repetidas cuentan una vez", delegate
            {
                ReloadPlan plan = ReloadPlanner.Plan(Paths("a.css", "A.CSS", "a.css"), false);
                Check.Equal(1, plan.CssPaths.Count, "deduplicadas");
            });

            runner.Test("ruta de disco a ruta URL", delegate
            {
                Check.Equal("/css/app.css", ReloadPlanner.ToUrlPath("css\\app.css"), "barras invertidas");
                Check.Equal("/a.css", ReloadPlanner.ToUrlPath("\\a.css"), "barra inicial");
                Check.Equal("/mis estilos/a b.css", ReloadPlanner.ToUrlPath("mis estilos\\a b.css"), "espacios sin escapar");
            });

            runner.Test("el mensaje lleva orden y rutas, una por linea", delegate
            {
                Check.Equal("reload", ReloadPlanner.Plan(Paths("x.js"), false).ToMessage(), "recarga");
                Check.Equal("css\n/a.css\n/b/c.css", ReloadPlanner.Plan(Paths("a.css", "b\\c.css"), false).ToMessage(),
                            "estilos");
            });

            runner.Suite("Core / HtmlInjector");

            runner.Test("se inserta antes del ultimo </body>", delegate
            {
                string html = "<html><body><p>x</p><!-- </body> --></body></html>";
                string result = HtmlInjector.Inject(html, "<script>s</script>");
                Check.Equal("<html><body><p>x</p><!-- </body> --><script>s</script></body></html>", result, "ultimo cierre");
            });

            runner.Test("sin </body> se anade al final", delegate
            {
                Check.Equal("<p>parcial</p><script>s</script>",
                            HtmlInjector.Inject("<p>parcial</p>", "<script>s</script>"), "al final");
            });

            runner.Test("la busqueda del cierre no distingue mayusculas", delegate
            {
                Check.Equal("<BODY>a<i></i></BODY>", HtmlInjector.Inject("<BODY>a</BODY>", "<i></i>"), "mayusculas");
            });

            runner.Test("sin fragmento la pagina queda igual", delegate
            {
                Check.Equal("<p>x</p>", HtmlInjector.Inject("<p>x</p>", ""), "vacio");
                Check.Equal("<p>x</p>", HtmlInjector.Inject("<p>x</p>", null), "nulo");
            });

            runner.Test("cada combinacion de scripts tiene variante propia", delegate
            {
                Check.Equal("", HtmlInjector.Variant(false, false), "nada");
                string probe = HtmlInjector.Variant(true, false);
                string reload = HtmlInjector.Variant(false, true);
                string both = HtmlInjector.Variant(true, true);
                Check.NotEqual(probe, reload, "sonda frente a recarga");
                Check.NotEqual(probe, both, "sonda frente a ambas");
                Check.NotEqual(reload, both, "recarga frente a ambas");
            });

            runner.Test("el fragmento corresponde a lo activado", delegate
            {
                Check.Empty(HtmlInjector.Compose(false, false), "nada");
                Check.Contains(HtmlInjector.Compose(true, false), "__probe", "sonda");
                Check.DoesNotContain(HtmlInjector.Compose(true, false), "__events", "sin recarga");
                Check.Contains(HtmlInjector.Compose(false, true), "__events", "recarga");
                string both = HtmlInjector.Compose(true, true);
                Check.Contains(both, "__probe", "ambas: sonda");
                Check.Contains(both, "__events", "ambas: recarga");
            });

            runner.Test("el script de recarga respeta el codigo de desactivacion", delegate
            {
                Check.Contains(LiveReloadScript.Html, LiveReloadHub.DisabledCloseCode.ToString(), "mismo codigo en ambos lados");
                Check.Contains(LiveReloadScript.Html, LiveReloadHub.Endpoint, "mismo punto de conexion");
            });

            runner.Suite("Core / SiteWatcher (disco real)");

            runner.Test("guardar una hoja de estilo produce un cambio en caliente", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Dir("css");
                    ManualResetEvent signal = new ManualResetEvent(false);
                    object gate = new object();
                    ReloadPlan received = null;
                    int port = 0;

                    // Solo cuenta el primer aviso: uno tardio no debe pisarlo
                    // mientras se comprueba.
                    using (SiteWatcher watcher = new SiteWatcher(4321, tree.Root, delegate(int p, ReloadPlan plan)
                    {
                        lock (gate)
                        {
                            if (received != null) return;
                            port = p;
                            received = plan;
                        }
                        signal.Set();
                    }))
                    {
                        tree.File("css/app.css", "body{color:red}");
                        Check.True(signal.WaitOne(5000), "llego un aviso");
                    }

                    lock (gate)
                    {
                        Check.Equal(4321, port, "puerto del sitio");
                        Check.Equal(ReloadKind.Css, received.Kind, "en caliente");
                        Check.Equal("/css/app.css", received.CssPaths[0], "ruta");
                    }
                }
            });

            runner.Test("un temporal de editor no produce aviso", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    ManualResetEvent signal = new ManualResetEvent(false);
                    using (SiteWatcher watcher = new SiteWatcher(1, tree.Root, delegate(int p, ReloadPlan plan)
                    {
                        signal.Set();
                    }))
                    {
                        tree.File("guardando.tmp", "x");
                        Check.False(signal.WaitOne(SiteWatcher.QuietMilliseconds * 4), "sin aviso");
                    }
                }
            });

            runner.Test("una rafaga de cambios se agrupa en un solo aviso", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    int calls = 0;
                    ManualResetEvent signal = new ManualResetEvent(false);
                    using (SiteWatcher watcher = new SiteWatcher(1, tree.Root, delegate(int p, ReloadPlan plan)
                    {
                        Interlocked.Increment(ref calls);
                        signal.Set();
                    }))
                    {
                        for (int i = 0; i < 5; i++) tree.File("js/m" + i + ".js", "x");
                        Check.True(signal.WaitOne(5000), "llego un aviso");
                        Thread.Sleep(SiteWatcher.QuietMilliseconds * 3);
                    }
                    Check.InRange(calls, 1, 2, "agrupado (como mucho dos si el disco va lento)");
                }
            });
        }
    }
}
