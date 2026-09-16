using System;
using System.Text;
using LanShare.Identity;

namespace LanShare.Tests
{
    public static class OuiAndProbeTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Identity / OuiTable");

            runner.Test("la tabla integrada reconoce prefijos conocidos", delegate
            {
                Check.Contains(OuiTable.Lookup("B827EB"), "Raspberry", "Raspberry Pi");
                Check.Contains(OuiTable.Lookup("080027"), "VirtualBox", "VirtualBox");
                Check.Contains(OuiTable.Lookup("00155D"), "Hyper-V", "Hyper-V");
                Check.Contains(OuiTable.Lookup("240AC4"), "Espressif", "Espressif");
            });

            runner.Test("el prefijo no distingue mayusculas", delegate
            {
                Check.Equal(OuiTable.Lookup("B827EB"), OuiTable.Lookup("b827eb"), "minusculas");
            });

            runner.Test("un prefijo desconocido devuelve vacio", delegate
            {
                Check.Empty(OuiTable.Lookup("ZZZZZZ"), "desconocido");
                Check.Empty(OuiTable.Lookup(""), "vacio");
                Check.Empty(OuiTable.Lookup(null), "nulo");
            });

            runner.Test("se carga un archivo con formato IEEE", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string contenido =
                        "OUI/MA-L                                 Organization\r\n" +
                        "company_id                               Organization\r\n" +
                        "\r\n" +
                        "AA-BB-CC   (hex)\t\tEmpresa De Prueba SL\r\n" +
                        "DD-EE-FF   (hex)\t\tOtra Empresa\r\n";

                    string path = tree.File("oui.txt", contenido);
                    int cargados = OuiTable.TryLoadFile(path);

                    Check.Equal(2, cargados, "entradas leidas");
                    Check.Equal("Empresa De Prueba SL", OuiTable.Lookup("AABBCC"), "primera");
                    Check.Equal("Otra Empresa", OuiTable.Lookup("DDEEFF"), "segunda");
                    Check.Contains(OuiTable.Lookup("B827EB"), "Raspberry",
                                   "la tabla integrada sigue cubriendo lo que el archivo no trae");
                    Check.Contains(OuiTable.Source, "oui.txt", "se declara el origen");
                }
            });

            runner.Test("un archivo inexistente o vacio no rompe la tabla", delegate
            {
                Check.Equal(0, OuiTable.TryLoadFile(@"C:\no-existe-" + Guid.NewGuid().ToString("N")), "inexistente");
                Check.Equal(0, OuiTable.TryLoadFile(null), "nulo");
                Check.Contains(OuiTable.Lookup("B827EB"), "Raspberry", "la tabla sigue en pie");
            });

            runner.Suite("Identity / ProbePayload");

            runner.Test("una sonda valida rellena los datos", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track("127.0.0.1", "curl/8.0", null, 0);

                string json = "{\"screen\":\"1080x2340 @2.75x 24bit\",\"viewport\":\"412x915\"," +
                              "\"gpu\":\"Adreno (TM) 740 | Qualcomm\",\"cores\":8,\"ram\":8," +
                              "\"touch\":5,\"platform\":\"Linux armv8l\",\"langs\":\"es-PE,es\"," +
                              "\"tz\":\"America/Lima\",\"net\":\"4g ~10Mbps\"}";

                Check.True(ProbePayload.ApplyTo("127.0.0.1", json), "aceptada");

                ClientInfo client = ClientRegistry.Find("127.0.0.1");
                Check.Contains(client.Gpu, "Adreno", "GPU");
                Check.Contains(client.ScreenInfo, "1080x2340", "pantalla");
                Check.Contains(client.ProbeSummary, "America/Lima", "zona horaria en el resumen");
                Check.Contains(client.ProbeSummary, "8 nucleos", "nucleos en el resumen");
                ClientRegistry.Clear();
            });

            runner.Test("los datos de alta entropia se recogen si vienen", delegate
            {
                ClientRegistry.Clear();
                string json = "{\"gpu\":\"Mali-G715\",\"screen\":\"1440x3088\"," +
                              "\"hints\":{\"model\":\"SM-S918B\",\"platformVersion\":\"14.0.0\"," +
                              "\"architecture\":\"arm\",\"bitness\":\"64\"}}";

                Check.True(ProbePayload.ApplyTo("127.0.0.1", json), "aceptada");
                Check.Contains(ClientRegistry.Find("127.0.0.1").ProbeSummary, "SM-S918B", "modelo exacto");
                ClientRegistry.Clear();
            });

            runner.Test("un JSON ilegible se rechaza sin romper nada", delegate
            {
                Check.False(ProbePayload.ApplyTo("127.0.0.1", "esto no es json"), "basura");
                Check.False(ProbePayload.ApplyTo("127.0.0.1", "{roto"), "incompleto");
                Check.False(ProbePayload.ApplyTo("127.0.0.1", ""), "vacio");
                Check.False(ProbePayload.ApplyTo("127.0.0.1", null), "nulo");
            });

            runner.Test("faltan campos y aun asi no revienta", delegate
            {
                ClientRegistry.Clear();
                Check.True(ProbePayload.ApplyTo("127.0.0.1", "{\"gpu\":\"Intel\"}"), "solo un campo");

                ClientInfo client = ClientRegistry.Find("127.0.0.1");
                Check.Equal("Intel", client.Gpu, "lo que vino");
                Check.NotNull(client.ProbeSummary, "resumen construido igualmente");
                ClientRegistry.Clear();
            });

            runner.Test("sin WebGL se dice que no hay dato", delegate
            {
                ClientRegistry.Clear();
                ProbePayload.ApplyTo("127.0.0.1", "{\"gpu\":\"\",\"screen\":\"800x600\"}");
                Check.Contains(ClientRegistry.Find("127.0.0.1").ProbeSummary, "WebGL no disponible",
                               "se declara la ausencia");
                ClientRegistry.Clear();
            });
        }
    }
}
