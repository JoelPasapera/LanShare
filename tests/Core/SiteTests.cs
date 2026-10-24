using System;
using System.Collections.Generic;
using System.IO;
using LanShare.Core;

namespace LanShare.Tests
{
    public static class SiteTests
    {
        private static SiteRuntime Runtime(int port, bool lan)
        {
            return new SiteRuntime(port, lan, "", @"C:\web\", @"C:\web\");
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / SiteDefinition");

            runner.Test("el nombre visible usa la etiqueta si la hay", delegate
            {
                SiteDefinition site = new SiteDefinition();
                site.RootPath = @"C:\proyectos\tienda";
                site.Label = "mi tienda";
                Check.Equal("mi tienda", site.DisplayName, "con etiqueta");
            });

            runner.Test("sin etiqueta usa el nombre de la carpeta", delegate
            {
                SiteDefinition site = new SiteDefinition();
                site.RootPath = @"C:\proyectos\tienda";
                Check.Equal("tienda", site.DisplayName, "sin etiqueta");
            });

            runner.Test("la barra final no descuadra el nombre", delegate
            {
                SiteDefinition site = new SiteDefinition();
                site.RootPath = @"C:\proyectos\tienda\";
                Check.Equal("tienda", site.DisplayName, "con barra final");
            });

            runner.Test("sin carpeta lo dice", delegate
            {
                Check.Contains(new SiteDefinition().DisplayName, "sin carpeta", "vacia");
            });

            runner.Test("la copia es independiente del original", delegate
            {
                SiteDefinition original = new SiteDefinition();
                original.Port = 8080;
                original.RootPath = @"C:\a";
                original.Label = "uno";
                original.LanMode = true;
                original.Enabled = true;

                SiteDefinition copia = original.Clone();
                copia.Port = 3000;
                copia.Label = "dos";
                copia.LanMode = false;
                copia.Enabled = false;
                copia.StartFile = "otro.html";

                Check.Equal(8080, original.Port, "puerto intacto");
                Check.Equal("uno", original.Label, "etiqueta intacta");
                Check.True(original.LanMode, "alcance intacto");
                Check.True(original.Enabled, "activacion intacta");
                Check.Equal(StaticFileResolver.DefaultIndexFile, original.StartFile, "archivo de inicio intacto");
            });

            runner.Test("por defecto el archivo de inicio es index.html", delegate
            {
                Check.Equal(StaticFileResolver.DefaultIndexFile, new SiteDefinition().StartFile, "por defecto");
            });

            runner.Test("el archivo de inicio se muestra o se etiqueta como carpeta", delegate
            {
                SiteDefinition site = new SiteDefinition();
                Check.Equal("index.html", site.StartFileDisplay, "por defecto");

                site.StartFile = "inicio.html";
                Check.Equal("inicio.html", site.StartFileDisplay, "nombre propio");

                site.StartFile = "";
                Check.Equal("(carpeta)", site.StartFileDisplay, "servido como carpeta");
                Check.NotEmpty(site.StartFileDisplay,
                               "nunca en blanco: es una decision, no un dato ausente");
            });

            runner.Suite("Core / SiteRuntime");

            runner.Test("el archivo de inicio llega al sitio en ejecucion", delegate
            {
                SiteRuntime propio = new SiteRuntime(8080, false, "", @"C:\web\", @"C:\web\", "inicio.html");
                Check.Equal("inicio.html", propio.StartFile, "nombre propio");

                SiteRuntime carpeta = new SiteRuntime(8080, false, "", @"C:\web\", @"C:\web\", "");
                Check.Empty(carpeta.StartFile, "servido como carpeta");

                SiteRuntime porDefecto = new SiteRuntime(8080, false, "", @"C:\web\", @"C:\web\");
                Check.Equal(StaticFileResolver.DefaultIndexFile, porDefecto.StartFile, "constructor corto");
            });

            runner.Test("el prefijo depende del alcance", delegate
            {
                Check.Equal("http://localhost:8080/", Runtime(8080, false).Prefix, "solo este equipo");
                Check.Equal("http://+:8080/", Runtime(8080, true).Prefix, "red local");
            });

            runner.Test("la URL local siempre apunta a localhost", delegate
            {
                Check.Equal("http://localhost:3000/", Runtime(3000, true).LocalUrl, "en modo LAN");
                Check.Equal("http://localhost:3000/", Runtime(3000, false).LocalUrl, "en modo local");
            });

            runner.Test("las rutas acaban siempre en separador", delegate
            {
                SiteRuntime site = new SiteRuntime(8080, false, "", @"C:\web", @"C:\web");
                string separador = Path.DirectorySeparatorChar.ToString();
                Check.True(site.RootPath.EndsWith(separador, StringComparison.Ordinal), "raiz");
                Check.True(site.RealRootPath.EndsWith(separador, StringComparison.Ordinal), "raiz real");
            });

            runner.Test("no se acepta una raiz vacia", delegate
            {
                Check.Throws(delegate { new SiteRuntime(8080, false, "", "", @"C:\web\"); }, "raiz vacia");
                Check.Throws(delegate { new SiteRuntime(8080, false, "", @"C:\web\", ""); }, "raiz real vacia");
            });

            runner.Suite("Core / ServerOptions");

            runner.Test("cada puerto encuentra su sitio", delegate
            {
                List<SiteRuntime> sitios = new List<SiteRuntime>();
                sitios.Add(new SiteRuntime(8080, false, "a", @"C:\a\", @"C:\a\"));
                sitios.Add(new SiteRuntime(3000, true, "b", @"C:\b\", @"C:\b\"));

                ServerOptions options = new ServerOptions(sitios, true);
                Check.NotNull(options.FindByPort(8080), "puerto 8080");
                Check.NotNull(options.FindByPort(3000), "puerto 3000");
                Check.Null(options.FindByPort(9999), "puerto sin sitio");
                Check.Equal(@"C:\b\", options.FindByPort(3000).RootPath, "la carpeta correcta");
            });

            runner.Test("dos sitios en el mismo puerto se rechazan", delegate
            {
                List<SiteRuntime> sitios = new List<SiteRuntime>();
                sitios.Add(new SiteRuntime(8080, false, "a", @"C:\a\", @"C:\a\"));
                sitios.Add(new SiteRuntime(8080, true, "b", @"C:\b\", @"C:\b\"));

                Check.Throws(delegate { new ServerOptions(sitios, true); }, "puerto duplicado");
            });

            runner.Test("sin sitios no hay servidor", delegate
            {
                Check.Throws(delegate { new ServerOptions(new List<SiteRuntime>(), true); }, "lista vacia");
                Check.Throws(delegate { new ServerOptions(null, true); }, "lista nula");
            });

            runner.Test("los interruptores se pueden cambiar en caliente", delegate
            {
                List<SiteRuntime> sitios = new List<SiteRuntime>();
                sitios.Add(new SiteRuntime(8080, false, "a", @"C:\a\", @"C:\a\"));
                ServerOptions options = new ServerOptions(sitios, true);

                Check.True(options.CorsEnabled, "CORS inicial");
                Check.True(options.EnableCompression, "compresion inicial");
                Check.True(options.EnableDirectoryListing, "listado inicial");
                Check.False(options.DistributionMode, "distribucion inicial");

                options.CorsEnabled = false;
                options.DistributionMode = true;
                Check.False(options.CorsEnabled, "CORS cambiado");
                Check.True(options.DistributionMode, "distribucion cambiada");
            });

            runner.Suite("Core / AppSettings");

            runner.Test("el historial pone lo ultimo delante y sin repetir", delegate
            {
                AppSettings settings = new AppSettings();
                settings.RememberRoot(@"C:\a");
                settings.RememberRoot(@"C:\b");
                settings.RememberRoot(@"C:\a");

                Check.Equal(2, settings.RecentRoots.Count, "sin duplicados");
                Check.Equal(@"C:\a", settings.RecentRoots[0], "lo ultimo primero");
            });

            runner.Test("el historial no distingue mayusculas al deduplicar", delegate
            {
                AppSettings settings = new AppSettings();
                settings.RememberRoot(@"C:\Proyectos");
                settings.RememberRoot(@"c:\proyectos");
                Check.Equal(1, settings.RecentRoots.Count, "misma carpeta");
            });

            runner.Test("el historial tiene tope", delegate
            {
                AppSettings settings = new AppSettings();
                for (int i = 0; i < 30; i++) settings.RememberRoot(@"C:\carpeta" + i);
                Check.InRange(settings.RecentRoots.Count, 1, 8, "acotado");
            });

            runner.Test("una carpeta vacia o nula se ignora", delegate
            {
                AppSettings settings = new AppSettings();
                settings.RememberRoot(null);
                settings.RememberRoot("");
                Check.Equal(0, settings.RecentRoots.Count, "sin entradas basura");
            });
        }
    }
}
