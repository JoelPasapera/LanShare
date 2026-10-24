using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LanShare.Net;

namespace LanShare.Tests
{
    /// <summary>
    /// Deteccion de puertos libres. Estas pruebas abren sockets de verdad: es
    /// la unica forma de comprobar que la lectura de la tabla del sistema, con
    /// sus desplazamientos de estructura, es correcta.
    /// </summary>
    public static class PortAvailabilityTests
    {
        /// <summary>Puerto alto poco habitual, para no chocar con nada real.</summary>
        private static int PuertoLibre()
        {
            // IPAddress.Any y no Loopback: CanBind prueba sobre todas las
            // interfaces, y un puerto puede estar libre solo en una de ellas.
            TcpListener sonda = new TcpListener(IPAddress.Any, 0);
            sonda.Start();
            int port = ((IPEndPoint)sonda.LocalEndpoint).Port;
            sonda.Stop();
            return port;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Net / PortAvailability");

            runner.Test("un puerto que nadie usa se puede reservar", delegate
            {
                Check.True(PortAvailability.CanBind(PuertoLibre()), "libre");
            });

            runner.Test("un puerto ocupado por nosotros no se puede reservar", delegate
            {
                TcpListener ocupado = new TcpListener(IPAddress.Any, 0);
                ocupado.Start();
                try
                {
                    int port = ((IPEndPoint)ocupado.LocalEndpoint).Port;
                    Check.False(PortAvailability.CanBind(port), "ocupado");
                }
                finally { ocupado.Stop(); }
            });

            runner.Test("numeros fuera de rango se rechazan", delegate
            {
                HashSet<int> vacio = new HashSet<int>();
                Check.Equal(PortStatus.Invalid, PortAvailability.Check(0, null, vacio), "cero");
                Check.Equal(PortStatus.Invalid, PortAvailability.Check(-1, null, vacio), "negativo");
                Check.Equal(PortStatus.Invalid, PortAvailability.Check(65536, null, vacio), "por encima");
            });

            runner.Test("CLAVE: la tabla del sistema ve un puerto que acabamos de abrir", delegate
            {
                TcpListener ocupado = new TcpListener(IPAddress.Any, 0);
                ocupado.Start();
                try
                {
                    int port = ((IPEndPoint)ocupado.LocalEndpoint).Port;
                    Check.True(PortAvailability.GetListeningPorts().Contains(port),
                               "el puerto abierto aparece entre los que escuchan");
                }
                finally { ocupado.Stop(); }
            });

            runner.Test("CLAVE: se identifica nuestro propio proceso como dueno", delegate
            {
                // Esto valida los desplazamientos de la estructura nativa: si
                // estuvieran mal, saldrian identificadores de proceso basura.
                TcpListener ocupado = new TcpListener(IPAddress.Any, 0);
                ocupado.Start();
                try
                {
                    int port = ((IPEndPoint)ocupado.LocalEndpoint).Port;
                    Dictionary<int, int> duenos = PortAvailability.GetListenerOwners();

                    int pid;
                    Check.True(duenos.TryGetValue(port, out pid), "el puerto tiene dueno");
                    Check.Equal(Process.GetCurrentProcess().Id, pid, "y el dueno somos nosotros");
                }
                finally { ocupado.Stop(); }
            });

            runner.Test("el nombre del proceso se describe", delegate
            {
                string descripcion = PortAvailability.DescribeOwner(Process.GetCurrentProcess().Id);
                Check.NotEmpty(descripcion, "descripcion");
                Check.Contains(descripcion, "PID", "incluye el identificador");
            });

            runner.Test("un proceso inexistente no revienta", delegate
            {
                Check.DoesNotThrow(delegate { PortAvailability.DescribeOwner(999999); }, "pid alto");
                Check.Empty(PortAvailability.DescribeOwner(0), "pid cero");
            });

            runner.Test("un puerto ocupado por otro sitio propio se distingue", delegate
            {
                List<int> otros = new List<int>();
                otros.Add(8080);

                PortStatus status = PortAvailability.Check(8080, otros, new HashSet<int>());
                Check.Equal(PortStatus.UsedByAnotherSite, status, "conflicto con otro sitio");
            });

            runner.Test("el estado de un puerto ocupado es Listening", delegate
            {
                TcpListener ocupado = new TcpListener(IPAddress.Any, 0);
                ocupado.Start();
                try
                {
                    int port = ((IPEndPoint)ocupado.LocalEndpoint).Port;
                    PortStatus status = PortAvailability.Check(port, null,
                                                               PortAvailability.GetListeningPorts());
                    Check.Equal(PortStatus.Listening, status, "escuchando");
                }
                finally { ocupado.Stop(); }
            });

            runner.Test("un puerto libre se declara libre", delegate
            {
                PortStatus status = PortAvailability.Check(PuertoLibre(), null,
                                                           PortAvailability.GetListeningPorts());
                Check.Equal(PortStatus.Free, status, "libre");
            });

            runner.Test("el sondeo devuelve los puertos habituales con su estado", delegate
            {
                List<PortInfo> puertos = PortAvailability.Survey(new List<int>());

                Check.True(puertos.Count >= 15, "hay una lista razonable");
                foreach (PortInfo info in puertos)
                {
                    Check.InRange(info.Port, 1, 65535, "puerto valido");
                    Check.NotEmpty(info.StatusText, "estado descrito");
                }
            });

            runner.Test("el sondeo respeta los puertos ya usados por otros sitios", delegate
            {
                List<int> otros = new List<int>();
                otros.Add(8080);
                otros.Add(3000);

                foreach (PortInfo info in PortAvailability.Survey(otros))
                {
                    if (info.Port == 8080 || info.Port == 3000)
                        Check.Equal(PortStatus.UsedByAnotherSite, info.Status, "puerto " + info.Port);
                }
            });

            runner.Test("siempre se encuentra algun puerto libre", delegate
            {
                int libre = PortAvailability.FindFirstFree(new List<int>());
                Check.InRange(libre, 1, 65535, "puerto propuesto");
                Check.True(PortAvailability.CanBind(libre), "y de verdad esta libre");
            });

            runner.Test("se puede reservar en http.sys sobre localhost", delegate
            {
                // Es la prueba definitiva: el mismo mecanismo que usara el
                // servidor, no un socket cualquiera.
                Check.True(PortAvailability.CanReserveHttp(PuertoLibre(), false), "localhost");
            });

            runner.Test("un puerto invalido no se puede reservar", delegate
            {
                Check.False(PortAvailability.CanReserveHttp(0, false), "cero");
                Check.False(PortAvailability.CanReserveHttp(70000, false), "fuera de rango");
            });

            runner.Test("los rangos excluidos se consultan sin reventar", delegate
            {
                Check.DoesNotThrow(delegate { PortAvailability.GetExcludedRanges(); }, "consulta");

                foreach (int[] rango in PortAvailability.GetExcludedRanges())
                {
                    Check.Equal(2, rango.Length, "par de numeros");
                    Check.InRange(rango[0], 1, 65535, "inicio");
                    Check.True(rango[1] >= rango[0], "fin no menor que el inicio");
                }
            });

            runner.Test("describir la exclusion de un puerto no falla", delegate
            {
                Check.DoesNotThrow(delegate { PortAvailability.DescribeExclusion(8080); }, "consulta");
            });

            runner.Suite("Net / LanAddresses");

            runner.Test("las direcciones detectadas son IPv4 utilizables", delegate
            {
                List<string> direcciones = LanAddresses.GetActive();

                foreach (string ip in direcciones)
                {
                    IPAddress parsed;
                    Check.True(IPAddress.TryParse(ip, out parsed), "es una IP valida: " + ip);
                    Check.Equal(AddressFamily.InterNetwork, parsed.AddressFamily, "es IPv4: " + ip);
                    Check.False(IPAddress.IsLoopback(parsed), "no es loopback: " + ip);
                    Check.False(ip.StartsWith("169.254.", StringComparison.Ordinal),
                                "no es autoasignada: " + ip);
                }
            });

            runner.Test("no se repiten direcciones", delegate
            {
                List<string> direcciones = LanAddresses.GetActive();
                HashSet<string> unicas = new HashSet<string>(direcciones);
                Check.Equal(direcciones.Count, unicas.Count, "sin duplicados");
            });
        }
    }
}
