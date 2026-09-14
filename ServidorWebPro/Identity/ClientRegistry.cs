using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ServidorWebPro.Identity
{
    /// <summary>
    /// Almacen concurrente de clientes y orquestador del sondeo.
    ///
    /// La ruta de peticion solo hace escrituras atomicas; todo lo lento (DNS
    /// inverso, ARP, ICMP, mDNS, escaneo) corre en hilos dedicados.
    /// </summary>
    public static class ClientRegistry
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macAddrLen);

        private static readonly ConcurrentDictionary<string, ClientInfo> _clients =
            new ConcurrentDictionary<string, ClientInfo>(StringComparer.Ordinal);

        private static int _maintenanceStarted;

        // Trabajadores fijos en vez de un hilo por cliente. Cada sondeo abre
        // hasta 21 sockets, asi que sin tope una re-identificacion masiva
        // saturaria la pila de red.
        private const int ProbeWorkerCount = 6;
        private static readonly BlockingCollection<ClientInfo> _probeQueue =
            new BlockingCollection<ClientInfo>();
        private static int _probeWorkersStarted;

        // Alias puestos por el usuario, indexados por MAC o IP. Persisten entre
        // sesiones a traves de la configuracion.
        private static readonly ConcurrentDictionary<string, string> _aliases =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static bool EnableDeepName = true;    // mDNS + NetBIOS
        public static bool EnablePortScan = true;    // escaneo TCP activo

        // ------------------------------------------------------------------
        // Ruta caliente
        // ------------------------------------------------------------------

        public static void Track(string ip, string userAgent, NameValueCollection headers, long bytes)
        {
            if (string.IsNullOrEmpty(ip)) return;

            ClientInfo client = GetOrCreate(ip);

            Interlocked.Increment(ref client.RequestCount);
            if (bytes > 0) Interlocked.Add(ref client.BytesSent, bytes);
            Interlocked.Exchange(ref client.LastSeenTicks, DateTime.UtcNow.Ticks);

            // El parseo del UA solo corre cuando la cadena cambia.
            if (!string.IsNullOrEmpty(userAgent) &&
                !string.Equals(client.UserAgent, userAgent, StringComparison.Ordinal))
            {
                client.UserAgent = userAgent;
                client.DeviceLabel = UaParser.Device(userAgent);
                client.BrowserLabel = UaParser.Browser(userAgent);
            }

            if (headers != null)
            {
                Assign(ref client.AcceptLanguage, headers["Accept-Language"]);
                Assign(ref client.ChUa, headers["Sec-CH-UA"]);
                Assign(ref client.ChPlatform, headers["Sec-CH-UA-Platform"]);
                Assign(ref client.ChMobile, headers["Sec-CH-UA-Mobile"]);
                Assign(ref client.ChModel, headers["Sec-CH-UA-Model"]);
            }

            QueueResolve(client);
        }

        /// <summary>Metadatos crudos de la peticion: el orden de las cabeceras es huella de navegador.</summary>
        public static void SetHttpDetails(string ip, NameValueCollection headers,
                                          string protocol, bool keepAlive, int sourcePort)
        {
            if (string.IsNullOrEmpty(ip)) return;

            ClientInfo client;
            if (!_clients.TryGetValue(ip, out client)) return;

            client.Protocol = protocol;
            client.KeepAlive = keepAlive;
            client.SourcePort = sourcePort;

            if (headers == null) return;

            Assign(ref client.Referer, headers["Referer"]);
            Assign(ref client.AcceptEncoding, headers["Accept-Encoding"]);

            string[] keys = headers.AllKeys;
            client.HeaderSignature = string.Join(",", keys);

            StringBuilder raw = new StringBuilder();
            foreach (string key in keys)
            {
                raw.Append("  ").Append(key).Append(" : ").Append(headers[key]).Append("\r\n");
            }
            client.RawHeaders = raw.ToString();
        }

        /// <summary>Resultado de la sonda JS. Recalcula el veredicto: la GPU pesa.</summary>
        public static void SetProbe(string ip, string gpu, string screen, string summary)
        {
            if (string.IsNullOrEmpty(ip)) return;

            ClientInfo client = GetOrCreate(ip);
            client.Gpu = gpu ?? "";
            client.ScreenInfo = screen ?? "";
            client.ProbeSummary = summary ?? "";
            Interlocked.Exchange(ref client.ProbeTicks, DateTime.UtcNow.Ticks);

            VerdictEngine.Evaluate(client);
        }

        // ------------------------------------------------------------------
        // Consulta y control
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Alias
        // ------------------------------------------------------------------

        public static void LoadAliases(IDictionary<string, string> stored)
        {
            _aliases.Clear();
            if (stored == null) return;
            foreach (KeyValuePair<string, string> entry in stored)
            {
                if (!string.IsNullOrEmpty(entry.Key) && !string.IsNullOrEmpty(entry.Value))
                    _aliases[entry.Key] = entry.Value;
            }
            ApplyAliases();
        }

        public static Dictionary<string, string> ExportAliases()
        {
            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Un alias vacio borra el existente.</summary>
        public static void SetAlias(string key, string alias)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (string.IsNullOrEmpty(alias))
            {
                string removed;
                _aliases.TryRemove(key, out removed);
            }
            else
            {
                _aliases[key] = alias;
            }
            ApplyAliases();
        }

        /// <summary>
        /// Reaplica la tabla a los clientes vivos. Hace falta porque un cliente
        /// puede aparecer antes de resolver su MAC: hasta ese momento su clave
        /// es la IP, y al llegar la MAC cambia.
        /// </summary>
        private static void ApplyAliases()
        {
            foreach (ClientInfo client in _clients.Values)
            {
                string alias;
                if (_aliases.TryGetValue(client.AliasKey, out alias)) client.Alias = alias;
                else if (client.Mac.Length > 0 && _aliases.TryGetValue(client.Ip, out alias)) client.Alias = alias;
                else client.Alias = "";
            }
        }

        public static ClientInfo[] Snapshot()
        {
            return new List<ClientInfo>(_clients.Values).ToArray();
        }

        public static ClientInfo Find(string ip)
        {
            ClientInfo client;
            return _clients.TryGetValue(ip, out client) ? client : null;
        }

        public static int Count { get { return _clients.Count; } }

        public static void Clear() { _clients.Clear(); }

        public static void ResetResolution()
        {
            foreach (ClientInfo client in _clients.Values)
            {
                Interlocked.Exchange(ref client.ResolveState, 0);
                client.ProbeRound = 0;
                Interlocked.Exchange(ref client.NextProbeTicks, 0L);
                QueueResolve(client);
            }
        }

        private static ClientInfo GetOrCreate(string ip)
        {
            ClientInfo client;
            if (_clients.TryGetValue(ip, out client)) return client;

            ClientInfo fresh = new ClientInfo();
            fresh.Ip = ip;
            fresh.FirstSeenTicks = DateTime.UtcNow.Ticks;
            return _clients.GetOrAdd(ip, fresh);
        }

        private static void Assign(ref string field, string value)
        {
            if (!string.IsNullOrEmpty(value)) field = value;
        }

        // ------------------------------------------------------------------
        // Sondeo
        // ------------------------------------------------------------------

        private static void QueueResolve(ClientInfo client)
        {
            // Un unico intento por cliente: el CAS 0 -> 1 gana la carrera.
            if (Interlocked.CompareExchange(ref client.ResolveState, 1, 0) != 0) return;

            EnsureProbeWorkers();
            try { _probeQueue.Add(client); }
            catch (Exception) { Interlocked.Exchange(ref client.ResolveState, 0); }
        }

        /// <summary>
        /// Hilos dedicados, no el thread pool: un sondeo completo puede tardar
        /// segundos y no debe competir con las peticiones HTTP, que ahora viven
        /// precisamente en ese pool.
        /// </summary>
        private static void EnsureProbeWorkers()
        {
            if (Interlocked.CompareExchange(ref _probeWorkersStarted, 1, 0) != 0) return;

            for (int i = 0; i < ProbeWorkerCount; i++)
            {
                Thread worker = new Thread(ProbeWorkerLoop);
                worker.IsBackground = true;
                worker.Name = "probe-worker-" + i;
                worker.Start();
            }
        }

        private static void ProbeWorkerLoop()
        {
            foreach (ClientInfo client in _probeQueue.GetConsumingEnumerable())
            {
                try { ResolveWorker(client); }
                catch (Exception) { }
            }
        }

        private static void ResolveWorker(ClientInfo client)
        {
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(client.Ip, out address)) return;

                bool loopback = IPAddress.IsLoopback(address);
                bool ipv4 = address.AddressFamily == AddressFamily.InterNetwork;

                ResolveReverseDns(client);
                if (ipv4 && !loopback) ResolveMac(client, address);

                if (!loopback)
                {
                    int ttl;
                    long rtt;
                    NetProbe.PingInfo(client.Ip, 1200, out ttl, out rtt);
                    client.Ttl = ttl;                                 // int: escritura atomica
                    Interlocked.Exchange(ref client.RttMs, rtt);       // long: no lo es en 32 bits
                    client.OsGuess = NetProbe.OsFromTtl(ttl);
                }

                if (EnableDeepName && ipv4 && !loopback && client.HostName.Length == 0)
                    ResolveAlternateNames(client);

                if (ipv4 && !loopback && client.SsdpServer.Length == 0)
                    client.SsdpServer = NetProbe.Ssdp(client.Ip, 1200);

                if (EnablePortScan && ipv4 && !loopback)
                {
                    string guess, banners;
                    client.OpenPorts = NetProbe.ScanPorts(client.Ip, 800, out guess, out banners);
                    client.PortGuess = guess;
                    client.Banners = banners;
                }

                ApplyAliases();   // la MAC recien resuelta puede tener alias guardado
                VerdictEngine.Evaluate(client);
            }
            catch (Exception) { }
            finally
            {
                Interlocked.Exchange(ref client.ResolveState, 2);
            }
        }

        /// <summary>
        /// Con tope de tiempo: sin el, un resolutor lento dejaria el hilo
        /// colgado varios segundos.
        /// </summary>
        private static void ResolveReverseDns(ClientInfo client)
        {
            try
            {
                IAsyncResult ar = Dns.BeginGetHostEntry(client.Ip, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(2000, false)) return;

                IPHostEntry entry = Dns.EndGetHostEntry(ar);
                if (entry != null && !string.IsNullOrEmpty(entry.HostName))
                    client.HostName = entry.HostName;
            }
            catch (Exception) { }
        }

        private static void ResolveAlternateNames(ClientInfo client)
        {
            // Primero la tabla pasiva, que es gratis.
            string passive = MdnsListener.Lookup(client.Ip);
            if (passive.Length > 0)
            {
                client.MdnsName = passive;
                return;
            }

            MdnsListener.ProbeAll();
            client.MdnsName = NetProbe.MdnsReverse(client.Ip, 1200);

            if (client.MdnsName.Length == 0)
            {
                Thread.Sleep(400);   // margen para que llegue la respuesta multicast
                client.MdnsName = MdnsListener.Lookup(client.Ip);
            }

            if (client.MdnsName.Length == 0)
                client.NbName = NetProbe.NetbiosName(client.Ip, 900);
        }

        private static void ResolveMac(ClientInfo client, IPAddress address)
        {
            try
            {
                byte[] raw = address.GetAddressBytes();
                uint destination = (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
                byte[] mac = new byte[6];
                uint length = 6;

                if (SendARP(destination, 0, mac, ref length) != 0 || length < 6) return;

                client.Mac = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                                           mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

                // Bit 1 del primer octeto: direccion administrada localmente, es
                // decir MAC aleatoria de privacidad (iOS y Android modernos).
                client.MacRandomized = (mac[0] & 0x02) != 0;

                string oui = string.Format("{0:X2}{1:X2}{2:X2}", mac[0], mac[1], mac[2]);
                string vendor = OuiTable.Lookup(oui);

                if (vendor.Length > 0) client.Vendor = vendor;
                else if (client.MacRandomized) client.Vendor = "MAC aleatoria";
                else client.Vendor = "Desconocido";
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // Re-sondeo periodico
        // ------------------------------------------------------------------

        /// <summary>
        /// El CAS de QueueResolve garantiza un intento por cliente. Sin este
        /// bucle, un telefono dormido durante el primer ARP se quedaria sin MAC
        /// para siempre. Reintenta solo lo incompleto, con retroceso exponencial.
        /// </summary>
        public static void StartMaintenance()
        {
            if (Interlocked.CompareExchange(ref _maintenanceStarted, 1, 0) != 0) return;

            Thread thread = new Thread(MaintenanceLoop);
            thread.IsBackground = true;
            thread.Name = "probe-maintenance";
            thread.Start();
        }

        private static void MaintenanceLoop()
        {
            while (true)
            {
                Thread.Sleep(5000);
                try
                {
                    long now = DateTime.UtcNow.Ticks;
                    foreach (ClientInfo client in _clients.Values)
                    {
                        // La tabla pasiva de mDNS se consulta gratis cada vuelta.
                        if (client.MdnsName.Length == 0 && client.HostName.Length == 0)
                        {
                            string passive = MdnsListener.Lookup(client.Ip);
                            if (passive.Length > 0)
                            {
                                client.MdnsName = passive;
                                VerdictEngine.Evaluate(client);
                            }
                        }

                        if (client.ResolveState != 2) continue;
                        if (!client.IsIncomplete) continue;
                        if (now < client.NextProbeTicks) continue;
                        if (client.IdleTime.TotalSeconds > 300) continue;   // cliente ido

                        client.ProbeRound++;
                        int shift = client.ProbeRound < 5 ? client.ProbeRound : 5;
                        int waitSeconds = Math.Min(300, 15 * (1 << shift));
                        Interlocked.Exchange(ref client.NextProbeTicks,
                                             now + TimeSpan.FromSeconds(waitSeconds).Ticks);

                        Interlocked.Exchange(ref client.ResolveState, 0);
                        QueueResolve(client);
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
