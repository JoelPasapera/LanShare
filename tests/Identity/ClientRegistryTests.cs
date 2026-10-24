using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using LanShare.Identity;

namespace LanShare.Tests
{
    /// <summary>
    /// Registro concurrente de clientes. Los contadores los tocan varios hilos
    /// de peticion a la vez, asi que la atomicidad no es teorica.
    /// </summary>
    public static class ClientRegistryTests
    {
        private const string Loopback = "127.0.0.1";

        private static NameValueCollection Cabeceras()
        {
            NameValueCollection headers = new NameValueCollection();
            headers.Add("Host", "localhost:8080");
            headers.Add("Accept-Language", "es-PE,es;q=0.9");
            headers.Add("Accept-Encoding", "gzip, deflate");
            headers.Add("Referer", "http://localhost:8080/");
            return headers;
        }

        public static void Register(TestRunner runner)
        {
            // Sondeo desactivado: estas pruebas miden la contabilidad, no la red.
            ClientRegistry.EnablePortScan = false;
            ClientRegistry.EnableDeepName = false;

            runner.Suite("Identity / ClientRegistry");

            runner.Test("registrar una peticion crea el cliente", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 1234);

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.NotNull(client, "cliente creado");
                Check.Equal(1L, client.RequestCount, "una peticion");
                Check.Equal(1234L, client.BytesSent, "bytes");
                Check.Contains(client.BrowserLabel, "curl", "navegador interpretado");
                ClientRegistry.Clear();
            });

            runner.Test("las peticiones se acumulan", delegate
            {
                ClientRegistry.Clear();
                for (int i = 0; i < 10; i++) ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.Equal(10L, client.RequestCount, "diez peticiones");
                Check.Equal(1000L, client.BytesSent, "bytes sumados");
                ClientRegistry.Clear();
            });

            runner.Test("CONCURRENCIA: los contadores no pierden cuentas", delegate
            {
                ClientRegistry.Clear();

                const int hilos = 8;
                const int porHilo = 250;

                Thread[] equipo = new Thread[hilos];
                for (int i = 0; i < hilos; i++)
                {
                    equipo[i] = new Thread(delegate()
                    {
                        NameValueCollection headers = Cabeceras();
                        for (int v = 0; v < porHilo; v++)
                            ClientRegistry.Track(Loopback, "curl/8.0", headers, 10);
                    });
                    equipo[i].Start();
                }
                foreach (Thread hilo in equipo) hilo.Join(20000);

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.Equal((long)(hilos * porHilo), client.RequestCount, "ni una peticion perdida");
                Check.Equal((long)(hilos * porHilo * 10), client.BytesSent, "ni un byte perdido");
                ClientRegistry.Clear();
            });

            runner.Test("clientes distintos no se mezclan", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track("127.0.0.1", "curl/8.0", Cabeceras(), 100);
                ClientRegistry.Track("127.0.0.2", "Wget/1.21", Cabeceras(), 200);

                Check.Equal(2, ClientRegistry.Count, "dos clientes");
                Check.Equal(100L, ClientRegistry.Find("127.0.0.1").BytesSent, "el primero");
                Check.Equal(200L, ClientRegistry.Find("127.0.0.2").BytesSent, "el segundo");
                ClientRegistry.Clear();
            });

            runner.Test("una IP vacia se ignora", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track("", "curl/8.0", Cabeceras(), 100);
                ClientRegistry.Track(null, "curl/8.0", Cabeceras(), 100);
                Check.Equal(0, ClientRegistry.Count, "sin entradas basura");
            });

            runner.Test("los detalles HTTP se guardan", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);
                ClientRegistry.SetHttpDetails(Loopback, Cabeceras(), "1.1", true, 54321);

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.Equal("1.1", client.Protocol, "protocolo");
                Check.True(client.KeepAlive, "keep-alive");
                Check.Equal(54321, client.SourcePort, "puerto origen");
                Check.Contains(client.AcceptEncoding, "gzip", "compresion admitida");
                ClientRegistry.Clear();
            });

            runner.Test("el volcado de cabeceras se toma como muestra", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);
                ClientRegistry.SetHttpDetails(Loopback, Cabeceras(), "1.1", true, 1);

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.Contains(client.RawHeaders, "Accept-Language", "una cabecera");
                Check.Contains(client.HeaderSignature, "Host", "firma de nombres");
                ClientRegistry.Clear();
            });

            runner.Test("la sonda JS rellena GPU y pantalla", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);
                ClientRegistry.SetProbe(Loopback, "Adreno (TM) 740", "1080x2340 @2.75x", "resumen");

                ClientInfo client = ClientRegistry.Find(Loopback);
                Check.Equal("Adreno (TM) 740", client.Gpu, "GPU");
                Check.Equal("1080x2340 @2.75x", client.ScreenInfo, "pantalla");
                Check.Contains(client.Verdict, "Android", "la GPU reevalua el veredicto");
                ClientRegistry.Clear();
            });

            runner.Test("Clear vacia el registro pero no los alias", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.SetAlias("AA:BB:CC:DD:EE:FF", "mi telefono");
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);

                ClientRegistry.Clear();
                Check.Equal(0, ClientRegistry.Count, "sin clientes");
                Check.Equal(1, ClientRegistry.ExportAliases().Count, "los alias sobreviven");

                ClientRegistry.SetAlias("AA:BB:CC:DD:EE:FF", "");
            });

            runner.Suite("Identity / ClientRegistry - alias");

            runner.Test("un alias por IP se aplica al cliente", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);
                ClientRegistry.SetAlias(Loopback, "mi portatil");

                Check.Equal("mi portatil", ClientRegistry.Find(Loopback).Alias, "alias aplicado");
                Check.Equal("mi portatil", ClientRegistry.Find(Loopback).BestName, "manda sobre lo deducido");

                ClientRegistry.SetAlias(Loopback, "");
                ClientRegistry.Clear();
            });

            runner.Test("un alias vacio lo borra", delegate
            {
                ClientRegistry.Clear();
                ClientRegistry.Track(Loopback, "curl/8.0", Cabeceras(), 100);
                ClientRegistry.SetAlias(Loopback, "temporal");
                ClientRegistry.SetAlias(Loopback, "");

                Check.Empty(ClientRegistry.Find(Loopback).Alias, "borrado");
                Check.Equal(0, ClientRegistry.ExportAliases().Count, "fuera de la tabla");
                ClientRegistry.Clear();
            });

            runner.Test("los alias se exportan e importan", delegate
            {
                ClientRegistry.Clear();
                Dictionary<string, string> tabla = new Dictionary<string, string>();
                tabla["AA:BB:CC:DD:EE:01"] = "telefono";
                tabla["AA:BB:CC:DD:EE:02"] = "tablet";

                ClientRegistry.LoadAliases(tabla);
                Dictionary<string, string> devuelta = ClientRegistry.ExportAliases();

                Check.Equal(2, devuelta.Count, "dos alias");
                Check.Equal("telefono", devuelta["AA:BB:CC:DD:EE:01"], "primero");

                ClientRegistry.LoadAliases(new Dictionary<string, string>());
            });

            runner.Test("la clave prefiere la MAC sobre la IP", delegate
            {
                ClientInfo client = new ClientInfo();
                client.Ip = "192.168.1.50";
                Check.Equal("192.168.1.50", client.AliasKey, "sin MAC usa la IP");

                client.Mac = "AA:BB:CC:DD:EE:FF";
                Check.Equal("AA:BB:CC:DD:EE:FF", client.AliasKey, "con MAC usa la MAC");
            });

            runner.Test("importar nulo deja la tabla vacia", delegate
            {
                ClientRegistry.LoadAliases(null);
                Check.Equal(0, ClientRegistry.ExportAliases().Count, "vacia");
            });

            runner.Suite("Identity / ClientInfo");

            runner.Test("el mejor nombre sigue el orden de fiabilidad", delegate
            {
                ClientInfo client = new ClientInfo();
                client.Ip = "192.168.1.50";
                Check.Empty(client.BestName, "sin nada");

                client.NbName = "EQUIPO-PC";
                Check.Equal("EQUIPO-PC", client.BestName, "NetBIOS como ultimo recurso");

                client.MdnsName = "mi-equipo";
                Check.Equal("mi-equipo", client.BestName, "mDNS por encima de NetBIOS");

                client.HostName = "pc.lan";
                Check.Equal("pc.lan", client.BestName, "DNS inverso por encima de mDNS");

                client.Alias = "el de Joel";
                Check.Equal("el de Joel", client.BestName, "el alias manda sobre todo");
            });

            runner.Test("se sabe cuando falta informacion", delegate
            {
                ClientInfo client = new ClientInfo();
                Check.True(client.IsIncomplete, "recien creado esta incompleto");

                client.Mac = "AA:BB:CC:DD:EE:FF";
                client.HostName = "pc.lan";
                client.Ttl = 64;
                Check.False(client.IsIncomplete, "con MAC, nombre y TTL esta completo");
            });
        }
    }
}
