using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace LanShare.Net
{
    public enum PortStatus
    {
        Free,
        Listening,          // otro proceso ya escucha ahi
        Blocked,            // el sistema no deja reservarlo (rango excluido, ACL)
        UsedByAnotherSite,  // lo ocupa otro sitio de esta misma configuracion
        Invalid
    }

    public sealed class PortInfo
    {
        public int Port { get; private set; }
        public PortStatus Status { get; private set; }

        /// <summary>Para que suele usarse. Ayuda a elegir uno que no choque manana.</summary>
        public string CommonUse { get; private set; }

        /// <summary>Programa que lo ocupa, cuando se puede averiguar.</summary>
        public string Owner { get; private set; }

        public PortInfo(int port, PortStatus status, string commonUse)
            : this(port, status, commonUse, "")
        {
        }

        public PortInfo(int port, PortStatus status, string commonUse, string owner)
        {
            Port = port;
            Status = status;
            CommonUse = commonUse ?? "";
            Owner = owner ?? "";
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case PortStatus.Free: return "Libre";
                    case PortStatus.Listening: return "Ocupado por otro programa";
                    case PortStatus.Blocked: return "Reservado por el sistema";
                    case PortStatus.UsedByAnotherSite: return "Usado por otro sitio tuyo";
                    default: return "Invalido";
                }
            }
        }
    }

    /// <summary>
    /// Averigua que puertos se pueden usar.
    ///
    /// Dos comprobaciones complementarias: la tabla de escuchas del sistema es
    /// una sola llamada y detecta lo que ya esta en uso, pero no ve los rangos
    /// que Hyper-V o WSL reservan sin escuchar en ellos. Para eso hace falta
    /// intentar la reserva de verdad.
    /// </summary>
    public static class PortAvailability
    {
        /// <summary>
        /// Puertos habituales en desarrollo. Se prefieren a los del rango
        /// efimero porque son los que uno recuerda y los que espera el resto
        /// de herramientas.
        /// </summary>
        private static readonly int[] CommonPorts =
            { 8080, 3000, 5173, 5000, 8000, 4200, 8081, 3001, 8888, 9000,
              8088, 4000, 5500, 8090, 9090, 7000, 8100, 8200, 8300, 8400 };

        private static readonly string[] CommonUses =
            { "HTTP alternativo", "Node / React / Express", "Vite", "Flask / ASP.NET",
              "Django / http.server", "Angular CLI", "HTTP alternativo", "Node alternativo",
              "Jupyter / pruebas", "PHP-FPM / SonarQube", "HTTP alternativo", "Phoenix / Elixir",
              "Live Server", "HTTP alternativo", "Prometheus / pruebas", "AirPlay en macOS",
              "libre", "libre", "libre", "libre" };

        // GetExtendedTcpTable da, ademas del puerto, el proceso que lo tiene.
        // IPGlobalProperties no lo expone, y saber que es tu propio node.exe de
        // ayer o un servicio del sistema cambia por completo que haces despues.
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order,
                                                       int addressFamily, int tableClass, uint reserved);

        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        // MIB_TCPROW_OWNER_PID mide 24 bytes; la variante IPv6 mide 56 y coloca
        // el puerto y el proceso en otros desplazamientos.
        private const int RowSizeV4 = 24;
        private const int PortOffsetV4 = 8;
        private const int PidOffsetV4 = 20;
        private const int RowSizeV6 = 56;
        private const int PortOffsetV6 = 20;
        private const int PidOffsetV6 = 52;

        /// <summary>
        /// Puerto a identificador de proceso. Se consultan las dos familias:
        /// un servidor que escucha solo en IPv6 aparecia como ocupado pero sin
        /// dueno, que es justo cuando mas falta hace saber quien es.
        /// </summary>
        public static Dictionary<int, int> GetListenerOwners()
        {
            Dictionary<int, int> owners = new Dictionary<int, int>();
            ReadOwnerTable(owners, AF_INET, RowSizeV4, PortOffsetV4, PidOffsetV4);
            ReadOwnerTable(owners, AF_INET6, RowSizeV6, PortOffsetV6, PidOffsetV6);
            return owners;
        }

        private static void ReadOwnerTable(Dictionary<int, int> owners, int family,
                                           int rowSize, int portOffset, int pidOffset)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, family,
                                    TCP_TABLE_OWNER_PID_LISTENER, 0);
                if (size <= 0) return;

                buffer = Marshal.AllocHGlobal(size);
                if (GetExtendedTcpTable(buffer, ref size, false, family,
                                        TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return;

                int count = Marshal.ReadInt32(buffer);
                long row = buffer.ToInt64() + 4;
                byte[] portBytes = new byte[4];

                for (int i = 0; i < count; i++)
                {
                    Marshal.Copy(new IntPtr(row + portOffset), portBytes, 0, 4);

                    // El puerto viaja en orden de red dentro de un DWORD.
                    int port = (portBytes[0] << 8) | portBytes[1];
                    int pid = Marshal.ReadInt32(new IntPtr(row + pidOffset));

                    if (!owners.ContainsKey(port)) owners[port] = pid;
                    row += rowSize;
                }
            }
            catch (Exception) { }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Nombre del proceso. Muchos servicios del sistema deniegan el acceso
        /// aunque se sea administrador, asi que el identificador es el respaldo.
        /// </summary>
        public static string DescribeOwner(int pid)
        {
            if (pid <= 0) return "";
            if (pid == 4) return "System (nucleo)";

            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return process.ProcessName + ".exe (PID " + pid + ")";
                }
            }
            catch (Exception) { return "PID " + pid; }
        }

        /// <summary>Todo lo que escucha ahora mismo, en una sola llamada al sistema.</summary>
        public static HashSet<int> GetListeningPorts()
        {
            HashSet<int> ports = new HashSet<int>();
            try
            {
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();

                foreach (IPEndPoint endpoint in properties.GetActiveTcpListeners())
                    ports.Add(endpoint.Port);

                // Una conexion en espera de cierre tambien impide reutilizar el puerto.
                foreach (TcpConnectionInformation connection in properties.GetActiveTcpConnections())
                {
                    if (connection.State == TcpState.Listen) ports.Add(connection.LocalEndPoint.Port);
                }
            }
            catch (Exception) { }

            return ports;
        }

        /// <summary>
        /// Prueba de reserva real. Es lo unico que detecta los rangos excluidos
        /// de Windows, donde nada escucha pero la reserva falla igualmente.
        /// </summary>
        public static bool CanBind(int port)
        {
            TcpListener listener = null;
            try
            {
                // IPAddress.Any y no Loopback: un puerto puede estar libre en
                // localhost y ocupado en la interfaz de red.
                listener = new TcpListener(IPAddress.Any, port);
                listener.ExclusiveAddressUse = true;
                listener.Start();
                return true;
            }
            catch (SocketException) { return false; }
            catch (Exception) { return false; }
            finally
            {
                if (listener != null)
                {
                    try { listener.Stop(); } catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// Prueba con el mismo mecanismo que usara el servidor. HttpListener no
        /// va por sockets sino por http.sys, que lleva su propio espacio de
        /// nombres: un puerto puede pasar la prueba de socket y fallar aqui.
        /// Con el prefijo comodin ademas hace falta ser administrador, asi que
        /// esto detecta tambien ese caso antes de pulsar Iniciar.
        /// </summary>
        public static bool CanReserveHttp(int port, bool lanMode)
        {
            if (port < 1 || port > 65535) return false;

            HttpListener listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(lanMode
                    ? "http://+:" + port + "/"
                    : "http://localhost:" + port + "/");
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (Exception) { return false; }
            finally
            {
                try { listener.Close(); } catch (Exception) { }
            }
        }

        public static PortStatus Check(int port, ICollection<int> portsUsedByOtherSites,
                                       HashSet<int> listening)
        {
            if (port < 1 || port > 65535) return PortStatus.Invalid;

            if (portsUsedByOtherSites != null && portsUsedByOtherSites.Contains(port))
                return PortStatus.UsedByAnotherSite;

            if (listening != null && listening.Contains(port)) return PortStatus.Listening;

            return CanBind(port) ? PortStatus.Free : PortStatus.Blocked;
        }

        private static List<int[]> _excludedCache;

        /// <summary>
        /// Rangos que Windows reserva sin escuchar en ellos (Hyper-V, WSL,
        /// Docker). Son la causa del desconcertante "no hay nada ahi pero no
        /// puedo usarlo". Se consultan una vez y se guardan.
        /// </summary>
        public static List<int[]> GetExcludedRanges()
        {
            if (_excludedCache != null) return _excludedCache;

            List<int[]> ranges = new List<int[]>();
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(
                    "netsh", "interface ipv4 show excludedportrange protocol=tcp");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;

                using (Process process = Process.Start(info))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(4000);

                        // Solo se leen pares de numeros: las cabeceras estan
                        // traducidas y no son fiables para analizar.
                        foreach (Match match in Regex.Matches(output, @"^\s*(\d+)\s+(\d+)\s*$",
                                                              RegexOptions.Multiline))
                        {
                            int from = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                            int to = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (from >= 1 && to >= from && to <= 65535) ranges.Add(new int[] { from, to });
                        }
                    }
                }
            }
            catch (Exception) { }

            _excludedCache = ranges;
            return ranges;
        }

        public static string DescribeExclusion(int port)
        {
            foreach (int[] range in GetExcludedRanges())
            {
                if (port >= range[0] && port <= range[1])
                    return "rango reservado " + range[0] + "-" + range[1];
            }
            return "";
        }

        /// <summary>Estado de los puertos habituales, en orden de preferencia.</summary>
        public static List<PortInfo> Survey(ICollection<int> portsUsedByOtherSites)
        {
            HashSet<int> listening = GetListeningPorts();
            Dictionary<int, int> owners = GetListenerOwners();
            List<PortInfo> result = new List<PortInfo>(CommonPorts.Length);

            for (int i = 0; i < CommonPorts.Length; i++)
            {
                result.Add(Describe(CommonPorts[i], CommonUses[i], portsUsedByOtherSites,
                                    listening, owners));
            }

            return result;
        }

        /// <summary>Estado completo de un puerto suelto, con su ocupante si lo hay.</summary>
        public static PortInfo Describe(int port, string commonUse,
                                        ICollection<int> portsUsedByOtherSites,
                                        HashSet<int> listening, Dictionary<int, int> owners)
        {
            PortStatus status = Check(port, portsUsedByOtherSites, listening);

            string owner = "";
            if (status == PortStatus.Listening && owners != null)
            {
                int pid;
                if (owners.TryGetValue(port, out pid)) owner = DescribeOwner(pid);
            }
            else if (status == PortStatus.Blocked)
            {
                owner = DescribeExclusion(port);
            }

            return new PortInfo(port, status, commonUse, owner);
        }

        /// <summary>
        /// Primer puerto usable. Recorre los habituales y, si todos estan
        /// tomados, barre el rango 8100-8999, que rara vez usa nada.
        /// </summary>
        public static int FindFirstFree(ICollection<int> portsUsedByOtherSites)
        {
            HashSet<int> listening = GetListeningPorts();

            foreach (int port in CommonPorts)
            {
                if (Check(port, portsUsedByOtherSites, listening) == PortStatus.Free) return port;
            }

            for (int port = 8100; port <= 8999; port++)
            {
                if (Check(port, portsUsedByOtherSites, listening) == PortStatus.Free) return port;
            }

            return -1;
        }
    }
}
