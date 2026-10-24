using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LanShare.Identity
{
    /// <summary>
    /// Identificacion a nivel de red, independiente de HTTP. Funciona aunque
    /// el dispositivo use MAC aleatoria y User-Agent reducido.
    /// </summary>
    public static class NetProbe
    {
        // ---------- ICMP: el TTL delata la familia de sistema operativo ----------

        /// <summary>
        /// Tres intentos: un solo ping se pierde con facilidad sobre Wi-Fi, y
        /// un fallo transitorio dejaria al cliente sin huella de SO.
        /// </summary>
        public static void PingInfo(string ip, int timeoutMs, out int ttl, out long rtt)
        {
            ttl = -1;
            rtt = -1;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(ip, timeoutMs);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            rtt = reply.RoundtripTime;
                            if (reply.Options != null) ttl = reply.Options.Ttl;
                            if (ttl > 0) return;
                        }
                    }
                }
                catch (Exception) { }
                Thread.Sleep(150);
            }
        }

        /// <summary>
        /// Los sistemas parten de un TTL inicial fijo y cada salto lo
        /// decrementa. En LAN sin routers intermedios llega intacto.
        /// </summary>
        public static string OsFromTtl(int ttl)
        {
            if (ttl <= 0) return "";

            int initial;
            string family;
            if (ttl <= 64) { initial = 64; family = "Linux / Android / iOS / macOS"; }
            else if (ttl <= 128) { initial = 128; family = "Windows"; }
            else { initial = 255; family = "Equipo de red / embebido"; }

            int hops = initial - ttl;
            return string.Format("{0}  (TTL {1}, {2} salto{3})",
                                 family, ttl, hops, hops == 1 ? "" : "s");
        }

        // ---------- mDNS: PTR inverso multicast ----------

        public static string MdnsReverse(string ip, int timeoutMs)
        {
            try
            {
                string[] octets = ip.Split('.');
                if (octets.Length != 4) return "";

                string qname = octets[3] + "." + octets[2] + "." + octets[1] + "." + octets[0] + ".in-addr.arpa";

                List<byte> query = new List<byte>();
                query.AddRange(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
                DnsWire.WriteName(query, qname);
                query.Add(0); query.Add(12);        // QTYPE = PTR
                query.Add(0x80); query.Add(0x01);   // QCLASS = IN con bit QU: respuesta unicast

                byte[] packet = query.ToArray();

                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] response;
                        try { response = udp.Receive(ref from); }
                        catch (Exception) { break; }

                        // La red esta llena de trafico mDNS ajeno.
                        if (from.Address.ToString() != ip) continue;

                        string name = ParsePtrAnswer(response);
                        if (name.Length > 0) return name;
                    }
                }
            }
            catch (Exception) { }
            return "";
        }

        private static string ParsePtrAnswer(byte[] data)
        {
            try
            {
                if (data.Length < 12) return "";
                int questions = (data[4] << 8) | data[5];
                int answers = (data[6] << 8) | data[7];
                if (answers < 1) return "";

                int pos = 12;
                for (int i = 0; i < questions; i++) { pos = DnsWire.SkipName(data, pos); pos += 4; }

                for (int i = 0; i < answers; i++)
                {
                    pos = DnsWire.SkipName(data, pos);
                    if (pos + 10 > data.Length) return "";

                    int type = (data[pos] << 8) | data[pos + 1];
                    int rdLength = (data[pos + 8] << 8) | data[pos + 9];
                    int rdata = pos + 10;

                    if (type == 12 && rdata < data.Length)
                    {
                        int unused;
                        return DnsWire.StripLocalSuffix(DnsWire.ReadName(data, rdata, out unused));
                    }
                    pos = rdata + rdLength;
                }
            }
            catch (Exception) { }
            return "";
        }

        // ---------- NetBIOS NBSTAT: nombre de maquina en clientes Windows ----------

        public static string NetbiosName(string ip, int timeoutMs)
        {
            try
            {
                List<byte> query = new List<byte>();
                query.AddRange(new byte[] { 0x4E, 0x42, 0x00, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });

                // Nombre comodin NBSTAT: '*' mas 15 nulos, en codificacion de
                // primer nivel (cada byte se parte en dos nibbles sumados a 'A').
                query.Add(0x20);
                byte[] raw = new byte[16];
                raw[0] = (byte)'*';
                for (int i = 0; i < 16; i++)
                {
                    query.Add((byte)('A' + ((raw[i] >> 4) & 0x0F)));
                    query.Add((byte)('A' + (raw[i] & 0x0F)));
                }
                query.Add(0x00);
                query.Add(0x00); query.Add(0x21);   // QTYPE = NBSTAT
                query.Add(0x00); query.Add(0x01);   // QCLASS = IN

                byte[] packet = query.ToArray();
                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork))
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(ip), 137));
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    return ParseNbstat(udp.Receive(ref from));
                }
            }
            catch (Exception) { }
            return "";
        }

        private static string ParseNbstat(byte[] data)
        {
            try
            {
                if (data.Length < 12) return "";
                int questions = (data[4] << 8) | data[5];
                int answers = (data[6] << 8) | data[7];
                if (answers < 1) return "";

                int pos = 12;
                for (int i = 0; i < questions; i++) { pos = DnsWire.SkipName(data, pos); pos += 4; }
                pos = DnsWire.SkipName(data, pos);
                if (pos + 10 > data.Length) return "";

                int rdata = pos + 10;
                if (rdata >= data.Length) return "";

                int count = data[rdata];
                int p = rdata + 1;
                for (int i = 0; i < count && p + 17 < data.Length; i++, p += 18)
                {
                    string name = Encoding.ASCII.GetString(data, p, 15).TrimEnd(' ', '\0');
                    byte suffix = data[p + 15];
                    int flags = (data[p + 16] << 8) | data[p + 17];
                    bool isGroup = (flags & 0x8000) != 0;

                    // Sufijo 0x00 y no-grupo identifica a la estacion de trabajo.
                    if (suffix == 0x00 && !isGroup && name.Length > 0) return name;
                }
            }
            catch (Exception) { }
            return "";
        }

        // ---------- SSDP: el producto se nombra a si mismo ----------

        public static string Ssdp(string ip, int timeoutMs)
        {
            try
            {
                string message = "M-SEARCH * HTTP/1.1\r\n" +
                                 "HOST: " + ip + ":1900\r\n" +
                                 "MAN: \"ssdp:discover\"\r\n" +
                                 "MX: 1\r\nST: ssdp:all\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(message);

                using (UdpClient udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(ip), 1900));
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    return HeaderValue(Encoding.ASCII.GetString(udp.Receive(ref from)), "SERVER:");
                }
            }
            catch (Exception) { }
            return "";
        }

        // ---------- Escaneo TCP de puertos con firma ----------

        private static readonly int[] SignaturePorts =
            { 22, 53, 80, 139, 443, 445, 548, 554, 631, 1883, 1900, 3389,
              5000, 5555, 5900, 7000, 8009, 8080, 9100, 32400, 62078 };

        private static readonly string[] SignatureNames =
            { "SSH", "DNS", "HTTP", "NetBIOS", "HTTPS", "SMB", "AFP", "RTSP", "IPP",
              "MQTT", "UPnP", "RDP", "UPnP-alt", "ADB", "VNC", "AirPlay", "Chromecast",
              "HTTP-alt", "JetDirect", "Plex", "lockdownd" };

        // Puertos cuyo banner HTTP suele nombrar el firmware del producto.
        private static readonly int[] BannerPorts = { 80, 8080, 5000, 631, 9100, 32400 };

        /// <summary>
        /// Todas las sondas se lanzan a la vez y comparten una unica ventana de
        /// espera. En serie eran 21 conexiones de 300 ms cada una; en paralelo
        /// es una sola espera, lo que ademas permite un timeout mas generoso
        /// con menos falsos negativos.
        /// </summary>
        public static string ScanPorts(string ip, int timeoutMs, out string deviceGuess, out string banners)
        {
            deviceGuess = "";
            banners = "";

            int count = SignaturePorts.Length;
            TcpClient[] clients = new TcpClient[count];
            IAsyncResult[] pending = new IAsyncResult[count];

            for (int i = 0; i < count; i++)
            {
                try
                {
                    clients[i] = new TcpClient();
                    pending[i] = clients[i].BeginConnect(ip, SignaturePorts[i], null, null);
                }
                catch (Exception) { pending[i] = null; }
            }

            Thread.Sleep(timeoutMs);

            List<string> open = new List<string>();
            HashSet<int> found = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                bool isOpen = false;
                try
                {
                    if (pending[i] != null && pending[i].IsCompleted)
                    {
                        clients[i].EndConnect(pending[i]);   // lanza si el puerto rechazo
                        isOpen = clients[i].Connected;
                    }
                }
                catch (Exception) { isOpen = false; }

                if (isOpen)
                {
                    open.Add(SignaturePorts[i] + "/" + SignatureNames[i]);
                    found.Add(SignaturePorts[i]);
                }
                try { if (clients[i] != null) clients[i].Close(); } catch (Exception) { }
            }

            deviceGuess = GuessFromPorts(found);

            List<string> bannerList = new List<string>();
            foreach (int port in BannerPorts)
            {
                if (!found.Contains(port)) continue;
                string banner = HttpBanner(ip, port, 700);
                if (banner.Length > 0) bannerList.Add(port + ": " + banner);
            }
            banners = string.Join(" | ", bannerList.ToArray());

            return string.Join(", ", open.ToArray());
        }

        private static string GuessFromPorts(HashSet<int> found)
        {
            if (found.Contains(62078)) return "iPhone o iPad (lockdownd)";
            if (found.Contains(445) || found.Contains(3389)) return "Windows";
            if (found.Contains(548) || found.Contains(7000)) return "Apple (macOS / AirPlay)";
            if (found.Contains(5555)) return "Android con ADB expuesto";
            if (found.Contains(9100) || found.Contains(631)) return "Impresora de red";
            if (found.Contains(8009)) return "Chromecast / Google TV";
            if (found.Contains(32400)) return "Servidor Plex / NAS";
            if (found.Contains(554)) return "Camara IP / NVR";
            if (found.Contains(53)) return "Router / servidor DNS";
            if (found.Contains(1883)) return "Broker MQTT / IoT";
            if (found.Contains(22)) return "Linux / NAS / router";
            return "";
        }

        private static string HttpBanner(string ip, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, false)) return "";
                    client.EndConnect(ar);

                    client.ReceiveTimeout = timeoutMs;
                    client.SendTimeout = timeoutMs;

                    NetworkStream stream = client.GetStream();
                    byte[] request = Encoding.ASCII.GetBytes(
                        "HEAD / HTTP/1.0\r\nHost: " + ip + "\r\nConnection: close\r\n\r\n");
                    stream.Write(request, 0, request.Length);

                    byte[] buffer = new byte[1024];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0) return "";

                    return HeaderValue(Encoding.ASCII.GetString(buffer, 0, read), "Server:");
                }
            }
            catch (Exception) { }
            return "";
        }

        private static string HeaderValue(string response, string headerName)
        {
            foreach (string line in response.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(headerName, StringComparison.OrdinalIgnoreCase))
                    return trimmed.Substring(headerName.Length).Trim();
            }
            return "";
        }
    }
}
