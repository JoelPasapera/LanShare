using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Guardado de la configuracion. Todo sobre carpetas temporales: nunca se
    /// toca el settings.json real del usuario.
    /// </summary>
    public static class SettingsPersistenceTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / AppSettings - valores por defecto");

            runner.Test("lo nuevo que cambia conducta viene apagado", delegate
            {
                AppSettings settings = new AppSettings();
                Check.False(settings.LiveReload, "recarga automatica");
                Check.False(settings.RemoteConsole, "consola remota");
                Check.False(settings.UploadEnabled, "subida de archivos");
                Check.False(settings.MinimizeToTray, "minimizar a la bandeja");
                Check.False(new SiteDefinition().CrossOriginIsolation, "aislamiento entre origenes de un sitio");
            });

            runner.Test("lo que ya existia conserva su comportamiento", delegate
            {
                AppSettings settings = new AppSettings();
                Check.True(settings.InjectProbe, "sonda");
                Check.True(settings.PortScan, "escaneo de puertos");
                Check.True(settings.DeepName, "nombres por mDNS y NetBIOS");
                Check.True(settings.Compression, "gzip");
                Check.Equal(2048, settings.UploadMaxMegabytes, "tope de subida");
            });

            runner.Test("carpeta de subida: por defecto o la configurada", delegate
            {
                AppSettings settings = new AppSettings();
                Check.Equal(AppSettings.DefaultUploadFolder(), settings.GetUploadFolder(), "por defecto");
                Check.True(AppSettings.DefaultUploadFolder().EndsWith(Path.Combine("Downloads", "LanShare"),
                           StringComparison.OrdinalIgnoreCase), "en Descargas");

                settings.UploadFolder = @"D:\Recibidos";
                Check.Equal(@"D:\Recibidos", settings.GetUploadFolder(), "configurada");
            });

            runner.Suite("Core / AppSettings - guardado");

            runner.Test("guardar y leer devuelve lo mismo", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = Path.Combine(tree.Root, "settings.json");
                    AppSettings original = new AppSettings();
                    original.LiveReload = true;
                    original.UploadMaxMegabytes = 500;
                    SiteDefinition site = new SiteDefinition();
                    site.Port = 3000;
                    site.RootPath = @"C:\web\umbral";
                    site.StartFile = "";
                    original.Sites.Add(site);
                    Check.True(original.SaveTo(path), "guardado");

                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.True(loaded.LiveReload, "recarga");
                    Check.Equal(500, loaded.UploadMaxMegabytes, "tope");
                    Check.Equal(1, loaded.Sites.Count, "un sitio");
                    Check.Equal(3000, loaded.Sites[0].Port, "puerto");
                    Check.Empty(loaded.Sites[0].StartFile, "servido como carpeta");
                }
            });

            runner.Test("CLAVE: el segundo guardado deja copia de la version anterior", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = Path.Combine(tree.Root, "settings.json");
                    AppSettings settings = new AppSettings();
                    settings.UploadMaxMegabytes = 100;
                    Check.True(settings.SaveTo(path), "primer guardado");
                    settings.UploadMaxMegabytes = 200;
                    Check.True(settings.SaveTo(path), "segundo guardado");

                    Check.True(File.Exists(path + ".bak"), "existe la copia");
                    Check.Equal(100, AppSettings.LoadFrom(path + ".bak").UploadMaxMegabytes, "con la version anterior");
                    Check.False(File.Exists(path + ".tmp"), "sin temporal olvidado");
                }
            });

            runner.Test("CLAVE: un archivo danado recurre a la copia, no a fabrica", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = Path.Combine(tree.Root, "settings.json");
                    AppSettings settings = new AppSettings();
                    SiteDefinition site = new SiteDefinition();
                    site.Port = 8080;
                    site.RootPath = @"C:\web\pdf-editor";
                    settings.Sites.Add(site);
                    Check.True(settings.SaveTo(path), "primer guardado");
                    Check.True(settings.SaveTo(path), "segundo guardado: ahora hay .bak con el sitio");

                    // Simula el corte de luz a mitad de escritura.
                    File.WriteAllText(path, "{\"Sites\":[{\"Port\":80", Encoding.UTF8);

                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.Equal(1, loaded.Sites.Count, "los sitios sobreviven");
                    Check.Equal(8080, loaded.Sites[0].Port, "con sus datos");
                }
            });

            runner.Test("un guardado imposible lo dice y no deja temporales", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    // Una carpeta con el nombre del archivo: no se puede sustituir.
                    string path = tree.Dir("settings.json");
                    Check.False(new AppSettings().SaveTo(path), "devuelve false");
                    Check.False(File.Exists(path + ".tmp"), "sin temporal olvidado");
                }
            });

            runner.Test("sin archivo ni copia se arranca de fabrica", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    AppSettings loaded = AppSettings.LoadFrom(Path.Combine(tree.Root, "no-existe.json"));
                    Check.NotNull(loaded, "no nulo");
                    Check.Equal(0, loaded.Sites.Count, "sin sitios");
                }
            });

            runner.Test("un JSON antiguo sin los campos nuevos toma los valores por defecto", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = tree.File("settings.json", "{\"Cors\":false,\"Compression\":true}");
                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.False(loaded.Cors, "lo que habia se respeta");
                    Check.True(loaded.InjectProbe, "lo nuevo por defecto");
                    Check.False(loaded.UploadEnabled, "la subida sigue apagada");
                    Check.Equal(2048, loaded.UploadMaxMegabytes, "tope por defecto");
                }
            });

            runner.Test("un tope de subida absurdo se corrige al leer", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = tree.File("settings.json", "{\"UploadMaxMegabytes\":-7}");
                    Check.Equal(2048, AppSettings.LoadFrom(path).UploadMaxMegabytes, "negativo");
                }
            });

            runner.Test("la configuracion de un solo sitio se migra", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = tree.File("settings.json",
                        "{\"RootPath\":\"C:\\\\web\\\\viejo\",\"Port\":9000,\"LanMode\":true}");
                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.Equal(1, loaded.Sites.Count, "un sitio");
                    Check.Equal(9000, loaded.Sites[0].Port, "puerto conservado");
                    Check.True(loaded.Sites[0].LanMode, "alcance conservado");
                }
            });
        }
    }
}
