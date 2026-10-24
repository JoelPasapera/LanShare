using System;
using LanShare.Identity;

namespace LanShare.Tests
{
    /// <summary>
    /// Fusion de senales. Cada fuente vota con un peso segun lo dificil que sea
    /// falsearla, y una contradiccion debe reportarse en vez de taparse.
    /// </summary>
    public static class VerdictEngineTests
    {
        private static ClientInfo Cliente()
        {
            ClientInfo client = new ClientInfo();
            client.Ip = "192.168.1.50";
            return client;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Identity / VerdictEngine");

            runner.Test("sin senales no se inventa nada", delegate
            {
                ClientInfo client = Cliente();
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Sin senales", "lo admite");
                Check.Equal(0, client.VerdictScore, "puntuacion nula");
            });

            runner.Test("un puerto delator pesa mas que el User-Agent", delegate
            {
                ClientInfo client = Cliente();
                client.OpenPorts = "62078/lockdownd";
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Apple", "iPhone por lockdownd");
                Check.Contains(client.VerdictDetail, "62078", "se explica el motivo");
            });

            runner.Test("SMB y RDP apuntan a Windows", delegate
            {
                ClientInfo client = Cliente();
                client.OpenPorts = "445/SMB, 3389/RDP";
                VerdictEngine.Evaluate(client);
                Check.Contains(client.Verdict, "Windows", "veredicto");
            });

            runner.Test("los puertos de impresion identifican una impresora", delegate
            {
                ClientInfo client = Cliente();
                client.OpenPorts = "9100/JetDirect, 631/IPP";
                VerdictEngine.Evaluate(client);
                Check.Contains(client.Verdict, "Impresora", "veredicto");
            });

            runner.Test("el TTL de Windows inclina la balanza", delegate
            {
                ClientInfo client = Cliente();
                client.Ttl = 128;
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Windows", "veredicto");
                Check.Contains(client.VerdictDetail, "TTL 128", "se explica");
            });

            runner.Test("el TTL de familia Unix reparte voto entre varios", delegate
            {
                ClientInfo client = Cliente();
                client.Ttl = 64;
                VerdictEngine.Evaluate(client);

                Check.Contains(client.VerdictDetail, "Unix", "se declara la familia");
                Check.Contains(client.Verdict, "confianza baja", "no basta para decidir");
            });

            runner.Test("la GPU movil delata un Android", delegate
            {
                ClientInfo client = Cliente();
                client.Gpu = "Adreno (TM) 740 | Qualcomm";
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Android", "veredicto");
                Check.Contains(client.VerdictDetail, "GPU movil", "se explica");
            });

            runner.Test("varias senales de acuerdo suben la confianza", delegate
            {
                ClientInfo client = Cliente();
                client.DeviceLabel = "Android 13 \u00B7 SM-S918B";
                client.Gpu = "Mali-G715";
                client.OpenPorts = "5555/ADB";
                client.Ttl = 64;
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Android", "veredicto");
                Check.Contains(client.Verdict, "alta", "confianza alta");
            });

            runner.Test("CLAVE: una contradiccion se reporta, no se tapa", delegate
            {
                // TTL de Windows con User-Agent de Android: hay un proxy por
                // medio o la cadena esta falseada. Vale mas saberlo que recibir
                // un veredicto limpio y equivocado.
                ClientInfo client = Cliente();
                client.DeviceLabel = "Android 13";
                client.Ttl = 128;
                VerdictEngine.Evaluate(client);

                Check.Contains(client.VerdictDetail, "DISCREPANCIA", "se avisa del conflicto");
            });

            runner.Test("una MAC aleatoria invalida el fabricante", delegate
            {
                ClientInfo conOui = Cliente();
                conOui.Vendor = "Apple";
                conOui.MacRandomized = false;
                VerdictEngine.Evaluate(conOui);
                Check.Contains(conOui.VerdictDetail, "OUI Apple", "cuenta cuando la MAC es real");

                ClientInfo aleatoria = Cliente();
                aleatoria.Vendor = "Apple";
                aleatoria.MacRandomized = true;
                VerdictEngine.Evaluate(aleatoria);
                Check.DoesNotContain(aleatoria.VerdictDetail, "OUI Apple",
                                     "no cuenta cuando la MAC es aleatoria");
            });

            runner.Test("un OUI de hipervisor identifica una maquina virtual", delegate
            {
                ClientInfo client = Cliente();
                client.Vendor = "VMware";
                client.MacRandomized = false;
                VerdictEngine.Evaluate(client);
                Check.Contains(client.Verdict, "Maquina virtual", "veredicto");
            });

            runner.Test("Espressif senala un dispositivo IoT", delegate
            {
                ClientInfo client = Cliente();
                client.Vendor = "Espressif (ESP32)";
                client.MacRandomized = false;
                VerdictEngine.Evaluate(client);
                Check.Contains(client.Verdict, "IoT", "veredicto");
            });

            runner.Test("SSDP con Windows refuerza el veredicto", delegate
            {
                ClientInfo client = Cliente();
                client.SsdpServer = "Microsoft-Windows/10.0 UPnP/1.0";
                VerdictEngine.Evaluate(client);

                Check.Contains(client.Verdict, "Windows", "veredicto");
                Check.Contains(client.VerdictDetail, "SSDP", "se explica");
            });

            runner.Test("evaluar un cliente nulo no revienta", delegate
            {
                Check.DoesNotThrow(delegate { VerdictEngine.Evaluate(null); }, "nulo");
            });

            runner.Test("el detalle enumera todas las senales usadas", delegate
            {
                ClientInfo client = Cliente();
                client.DeviceLabel = "Windows 10/11";
                client.Ttl = 128;
                client.OpenPorts = "445/SMB";
                VerdictEngine.Evaluate(client);

                Check.Contains(client.VerdictDetail, "UA dice Windows", "senal de UA");
                Check.Contains(client.VerdictDetail, "TTL 128", "senal de TTL");
                Check.Contains(client.VerdictDetail, "SMB", "senal de puertos");
            });
        }
    }
}
