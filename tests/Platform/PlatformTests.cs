using System;
using System.Xml;
using LanShare.Platform;

namespace LanShare.Tests
{
    /// <summary>Linea de comandos y definicion de la tarea de inicio con Windows.</summary>
    public static class PlatformTests
    {
        private const string Exe = @"C:\Herramientas\LanShare\LanShare.exe";
        private const string User = @"DESKTOP-0Q23CVG\Joel";

        private static XmlDocument Parse(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
            return doc;
        }

        private static string Value(XmlDocument doc, string element)
        {
            XmlNodeList nodes = doc.GetElementsByTagName(element);
            return nodes.Count > 0 ? nodes[0].InnerText : null;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("StartupOptions");

            runner.Test("sin argumentos no se activa nada", delegate
            {
                StartupOptions options = StartupOptions.Parse(new string[0]);
                Check.False(options.StartServer, "start");
                Check.False(options.StartInTray, "tray");
                Check.False(options.WaitForPreviousInstance, "wait");
                Check.Equal(0, options.Unknown.Count, "desconocidos");

                Check.False(StartupOptions.Parse(null).StartServer, "argumentos nulos");
            });

            runner.Test("se aceptan --, - y / sin distinguir mayusculas", delegate
            {
                Check.True(StartupOptions.Parse(new string[] { "--start" }).StartServer, "--start");
                Check.True(StartupOptions.Parse(new string[] { "/START" }).StartServer, "/START");
                Check.True(StartupOptions.Parse(new string[] { "-Tray" }).StartInTray, "-Tray");
                Check.True(StartupOptions.Parse(new string[] { "--wait-instance" }).WaitForPreviousInstance, "--wait-instance");
                Check.True(StartupOptions.Parse(new string[] { "--iniciar", "--bandeja" }).StartInTray, "alias en espanol");
            });

            runner.Test("una palabra sin prefijo no es una opcion", delegate
            {
                StartupOptions options = StartupOptions.Parse(new string[] { "start" });
                Check.False(options.StartServer, "no activa");
                Check.Equal(1, options.Unknown.Count, "se informa como desconocida");
            });

            runner.Test("los desconocidos se recogen sin romper los demas", delegate
            {
                StartupOptions options = StartupOptions.Parse(new string[] { "--start", "--volar", "", "  " });
                Check.True(options.StartServer, "el conocido funciona");
                Check.Equal(1, options.Unknown.Count, "un desconocido; los vacios se ignoran");
                Check.Equal("--volar", options.Unknown[0], "texto original");
            });

            runner.Test("construir la linea y releerla da lo mismo", delegate
            {
                string line = StartupOptions.Build(true, true, true);
                StartupOptions options = StartupOptions.Parse(line.Split(' '));
                Check.True(options.StartServer && options.StartInTray && options.WaitForPreviousInstance, "ida y vuelta");
                Check.Equal("", StartupOptions.Build(false, false, false), "vacia");
                Check.Equal("--start --wait-instance", StartupOptions.Build(true, false, true), "seleccion");
            });

            runner.Suite("Platform / ScheduledTaskXml");

            runner.Test("el XML es valido y declara UTF-16", delegate
            {
                string xml = ScheduledTaskXml.Build(Exe, AutoStart.Arguments, @"C:\Herramientas\LanShare", User, true);
                Check.StartsWith(xml, "<?xml version=\"1.0\" encoding=\"UTF-16\"?>", "declaracion");
                Check.DoesNotThrow(delegate { Parse(xml); }, "bien formado");
            });

            runner.Test("CLAVE: no se detiene con bateria ni a los tres dias", delegate
            {
                XmlDocument doc = Parse(ScheduledTaskXml.Build(Exe, "--start --tray", null, User, true));
                Check.Equal("false", Value(doc, "DisallowStartIfOnBatteries"), "arranca con bateria");
                Check.Equal("false", Value(doc, "StopIfGoingOnBatteries"), "sigue al desenchufar");
                Check.Equal("PT0S", Value(doc, "ExecutionTimeLimit"), "sin limite de tiempo");
                Check.Equal("IgnoreNew", Value(doc, "MultipleInstancesPolicy"), "una sola instancia");
            });

            runner.Test("el nivel de ejecucion sigue a la elevacion", delegate
            {
                Check.Equal("HighestAvailable",
                            Value(Parse(ScheduledTaskXml.Build(Exe, "", null, User, true)), "RunLevel"), "elevada");
                Check.Equal("LeastPrivilege",
                            Value(Parse(ScheduledTaskXml.Build(Exe, "", null, User, false)), "RunLevel"), "sin elevar");
            });

            runner.Test("arranca al iniciar sesion, con retraso, para el usuario", delegate
            {
                XmlDocument doc = Parse(ScheduledTaskXml.Build(Exe, "--start", null, User, true));
                Check.Equal(1, doc.GetElementsByTagName("LogonTrigger").Count, "disparador de inicio de sesion");
                Check.Equal(ScheduledTaskXml.LogonDelay, Value(doc, "Delay"), "retraso");
                Check.Equal(User, Value(doc, "UserId"), "usuario");
                Check.Equal(Exe, Value(doc, "Command"), "ejecutable");
                Check.Equal("--start", Value(doc, "Arguments"), "argumentos");
            });

            runner.Test("SEGURIDAD: rutas y usuarios con caracteres XML se escapan", delegate
            {
                string exe = @"C:\R&D <pruebas>\it's\LanShare.exe";
                XmlDocument doc = Parse(ScheduledTaskXml.Build(exe, "", null, "PC\\Ana & Luis", true));
                Check.Equal(exe, Value(doc, "Command"), "ruta recuperada intacta");
                Check.Equal("PC\\Ana & Luis", Value(doc, "UserId"), "usuario recuperado intacto");
            });

            runner.Test("sin argumentos ni carpeta, esos elementos no aparecen", delegate
            {
                XmlDocument doc = Parse(ScheduledTaskXml.Build(Exe, "", null, User, true));
                Check.Equal(0, doc.GetElementsByTagName("Arguments").Count, "sin Arguments");
                Check.Equal(0, doc.GetElementsByTagName("WorkingDirectory").Count, "sin WorkingDirectory");
            });

            runner.Test("faltan datos obligatorios: lanza", delegate
            {
                Check.Throws(delegate { ScheduledTaskXml.Build("", "", null, User, true); }, "sin ejecutable");
                Check.Throws(delegate { ScheduledTaskXml.Build(Exe, "", null, null, true); }, "sin usuario");
            });
        }
    }
}
