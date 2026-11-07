using System.Collections.Generic;
using System.IO;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// El puerto HTTPS de cada sitio: como se guarda, como se valida, como
    /// lleva al mismo sitio que su puerto HTTP y como cambia la comprobacion
    /// de origen de las subidas cuando la pagina llego por HTTPS.
    /// </summary>
    public static class HttpsSiteTests
    {
        private static SiteRuntime Site(int port, bool lanMode, int httpsPort)
        {
            return new SiteRuntime(port, lanMode, "web", @"C:\web\", @"C:\web\", "index.html", httpsPort);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / sitios con HTTPS");

            runner.Test("prefijo HTTPS con el mismo alcance que el HTTP", delegate
            {
                Check.Equal("https://+:8443/", Site(8080, true, 8443).HttpsPrefix, "red local");
                Check.Equal("https://localhost:8443/", Site(8080, false, 8443).HttpsPrefix, "solo este equipo");
                Check.Null(Site(8080, true, 0).HttpsPrefix, "sin HTTPS");
                Check.Equal("http://localhost:8080/", Site(8080, true, 8443).LocalUrl, "la URL local sigue en HTTP");
            });

            runner.Test("quitar el HTTPS conserva todo lo demas", delegate
            {
                SiteRuntime original = new SiteRuntime(8080, true, "tienda", @"C:\web\", @"D:\real\", "app.html", 8443);
                SiteRuntime plain = original.WithoutHttps();

                Check.Equal(0, plain.HttpsPort, "sin puerto HTTPS");
                Check.Null(plain.HttpsPrefix, "sin prefijo HTTPS");
                Check.Equal(8080, plain.Port, "puerto");
                Check.True(plain.LanMode, "alcance");
                Check.Equal("tienda", plain.Label, "etiqueta");
                Check.Equal(@"C:\web\", plain.RootPath, "raiz");
                Check.Equal(@"D:\real\", plain.RealRootPath, "raiz real");
                Check.Equal("app.html", plain.StartFile, "archivo de inicio");
                Check.Equal(8443, original.HttpsPort, "el original no cambia");
            });

            runner.Test("el puerto HTTPS se valida", delegate
            {
                Check.Throws(delegate { Site(8080, true, 8080); }, "igual que el HTTP");
                Check.Throws(delegate { Site(8080, true, -1); }, "negativo");
                Check.Throws(delegate { Site(8080, true, 65536); }, "mayor que 65535");
                Check.DoesNotThrow(delegate { Site(8080, true, 65535); }, "el ultimo valido");
            });

            runner.Test("los constructores antiguos dejan el sitio sin HTTPS", delegate
            {
                Check.Equal(0, new SiteRuntime(8080, true, "", @"C:\a\", @"C:\a\").HttpsPort, "cinco argumentos");
                Check.Equal(0, new SiteRuntime(8080, true, "", @"C:\a\", @"C:\a\", "").HttpsPort, "seis argumentos");
            });

            runner.Suite("Core / ServerOptions con HTTPS");

            runner.Test("el puerto HTTPS lleva al mismo sitio que el HTTP", delegate
            {
                List<SiteRuntime> sites = new List<SiteRuntime>();
                sites.Add(Site(8080, true, 8443));
                sites.Add(new SiteRuntime(3000, false, "b", @"C:\b\", @"C:\b\"));
                ServerOptions options = new ServerOptions(sites, false);

                Check.True(object.ReferenceEquals(options.FindByPort(8080), options.FindByPort(8443)), "mismo sitio");
                Check.Equal(8080, options.FindByPort(8443).Port, "puerto canonico");
                Check.NotNull(options.FindByPort(3000), "el otro sitio");
                Check.Equal(2, options.Sites.Count, "dos sitios, no tres");
            });

            runner.Test("un puerto HTTPS no puede repetir el de otro sitio", delegate
            {
                List<SiteRuntime> clash = new List<SiteRuntime>();
                clash.Add(Site(8080, true, 3000));
                clash.Add(new SiteRuntime(3000, false, "b", @"C:\b\", @"C:\b\"));
                Check.Throws(delegate { new ServerOptions(clash, false); }, "HTTPS de uno = HTTP de otro");

                List<SiteRuntime> twice = new List<SiteRuntime>();
                twice.Add(Site(8080, true, 8443));
                twice.Add(Site(9090, true, 8443));
                Check.Throws(delegate { new ServerOptions(twice, false); }, "dos sitios con el mismo HTTPS");
            });

            runner.Test("el certificado de la autoridad y el aislamiento empiezan apagados", delegate
            {
                List<SiteRuntime> sites = new List<SiteRuntime>();
                sites.Add(Site(8080, true, 8443));
                ServerOptions options = new ServerOptions(sites, false);
                Check.Null(options.HttpsAuthorityCertificate, "sin certificado hasta prepararlo");
                Check.Null(options.HttpsAuthorityFingerprint, "sin huella");
                Check.False(options.CrossOriginIsolation, "sin aislamiento entre origenes");
            });

            runner.Suite("Core / configuracion de HTTPS");

            runner.Test("por defecto ningun sitio usa HTTPS", delegate
            {
                Check.Equal(0, new SiteDefinition().HttpsPort, "sitio nuevo");
                Check.False(new AppSettings().HttpsPrepared, "nunca preparado");
            });

            runner.Test("la copia de un sitio lleva su puerto HTTPS", delegate
            {
                SiteDefinition original = new SiteDefinition();
                original.Port = 8080;
                original.HttpsPort = 8443;

                SiteDefinition copy = original.Clone();
                Check.Equal(8443, copy.HttpsPort, "copiado");
                copy.HttpsPort = 0;
                Check.Equal(8443, original.HttpsPort, "el original no cambia");
            });

            runner.Test("guardar y leer conserva el HTTPS", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = Path.Combine(tree.Root, "settings.json");
                    AppSettings original = new AppSettings();
                    original.HttpsPrepared = true;
                    SiteDefinition site = new SiteDefinition();
                    site.Port = 8080;
                    site.HttpsPort = 8443;
                    site.RootPath = @"C:\web";
                    original.Sites.Add(site);
                    Check.True(original.SaveTo(path), "guardado");

                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.True(loaded.HttpsPrepared, "preparado");
                    Check.Equal(8443, loaded.Sites[0].HttpsPort, "puerto HTTPS");
                }
            });

            runner.Test("un JSON anterior al HTTPS deja los sitios sin HTTPS", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string path = tree.File("settings.json",
                        "{\"Sites\":[{\"Port\":8080,\"RootPath\":\"C:\\\\web\",\"Enabled\":true}]}");
                    AppSettings loaded = AppSettings.LoadFrom(path);
                    Check.Equal(1, loaded.Sites.Count, "un sitio");
                    Check.Equal(0, loaded.Sites[0].HttpsPort, "sin HTTPS");
                    Check.False(loaded.HttpsPrepared, "nunca preparado");
                }
            });

            runner.Suite("Core / origen de las subidas por HTTPS");

            runner.Test("una pagina servida por HTTPS es del mismo origen", delegate
            {
                Check.True(UploadGuard.IsSameOrigin("https://192.168.1.8:8443", "192.168.1.8:8443", "same-origin", true),
                           "LAN por HTTPS");
                Check.True(UploadGuard.IsSameOrigin("https://localhost:8443", "localhost:8443", null, true),
                           "local por HTTPS sin Sec-Fetch");
                Check.True(UploadGuard.IsSameOrigin("https://pc.local:8443/", "pc.local:8443", null, true),
                           "nombre mDNS con barra final");
            });

            runner.Test("CLAVE: el esquema tiene que coincidir con el de la conexion", delegate
            {
                Check.False(UploadGuard.IsSameOrigin("http://192.168.1.8:8443", "192.168.1.8:8443", null, true),
                            "pagina HTTP contra conexion HTTPS");
                Check.False(UploadGuard.IsSameOrigin("https://192.168.1.8:8080", "192.168.1.8:8080", null, false),
                            "pagina HTTPS contra conexion HTTP");
                Check.True(UploadGuard.IsSameOrigin("http://192.168.1.8:8080", "192.168.1.8:8080", null),
                           "la sobrecarga antigua sigue siendo HTTP");
            });

            runner.Test("por HTTPS siguen valiendo las demas defensas", delegate
            {
                Check.False(UploadGuard.IsSameOrigin("https://192.168.1.8:8443", "192.168.1.8:8443", "cross-site", true),
                            "cross-site");
                Check.False(UploadGuard.IsSameOrigin("https://evil.example:8443", "evil.example:8443", null, true),
                            "DNS rebinding con un dominio de Internet");
                Check.False(UploadGuard.IsSameOrigin("null", "192.168.1.8:8443", null, true), "origen null");
            });
        }
    }
}
