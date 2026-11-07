using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LanShare.Identity
{
    /// <summary>
    /// Escucha permanente en 224.0.0.251:5353.
    ///
    /// Los dispositivos anuncian sus servicios por su cuenta (AirPlay, AirDrop,
    /// Chromecast, impresoras), asi que basta con oir para construir una tabla
    /// IP a nombre .local sin enviar una sola sonda. Es bastante mas fiable que
    /// el PTR inverso, que casi nadie implementa.
    ///
    /// Abre un socket a la escucha, que es lo que dispara el aviso del firewall
    /// de Windows. Por eso solo se arranca en modo LAN, donde aporta algo.
    /// </summary>
    public static class MdnsListener
    {
        private static readonly ConcurrentDictionary<string, string> _names =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static int _started;
        private static volatile bool _stop;
        private static UdpClient _udp;

        public static int Count { get { return _names.Count; } }

        public static bool IsActive { get { return _udp != null && _started != 0; } }

        public static string Lookup(string ip)
        {
            string name;
            return _names.TryGetValue(ip, out name) ? name : "";
        }

        public static void Start()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;

            _stop = false;
            Thread thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "mdns-listener";
            thread.Start();
        }

        /// <summary>Debe poder reiniciarse: el usuario puede parar y arrancar varias veces.</summary>
        public static void Stop()
        {
            if (Interlocked.Exchange(ref _started, 0) == 0) return;

            _stop = true;
            try
            {
                if (_udp != null)
                {
                    _udp.Close();
                    _udp = null;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Pregunta por la lista de tipos de servicio: obliga a todo el mundo a
        /// responder, y las respuestas traen registros A en la seccion adicional.
        /// </summary>
        public static void ProbeAll()
        {
            try
            {
                List<byte> query = new List<byte>();
                query.AddRange(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
                DnsWire.WriteName(query, "_services._dns-sd._udp.local");
                query.Add(0); query.Add(12);   // PTR
                query.Add(0); query.Add(1);    // IN

                byte[] packet = query.ToArray();
                using (UdpClient udp = new UdpClient())
                {
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                }
            }
            catch (Exception) { }
        }

        private static void Loop()
        {
            try
            {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                _udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));
            }
            catch (Exception)
            {
                // El 5353 puede estar tomado por Bonjour o por el navegador.
                // Sin escucha pasiva el resto del sondeo sigue funcionando.
                _udp = null;
                Interlocked.Exchange(ref _started, 0);   // permite reintentar al reiniciar
                return;
            }

            while (!_stop)
            {
                try
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    Harvest(_udp.Receive(ref from));
                }
                catch (Exception)
                {
                    if (_stop) break;
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>Recorre todas las secciones: cada registro A asocia un nombre con una IPv4.</summary>
        private static void Harvest(byte[] data)
        {
            try
            {
                if (data.Length < 12) return;

                int questions = (data[4] << 8) | data[5];
                int answers = (data[6] << 8) | data[7];
                int authority = (data[8] << 8) | data[9];
                int additional = (data[10] << 8) | data[11];

                int pos = 12;
                for (int i = 0; i < questions; i++) { pos = DnsWire.SkipName(data, pos); pos += 4; }

                int total = answers + authority + additional;
                for (int i = 0; i < total; i++)
                {
                    if (pos + 10 > data.Length) return;

                    int nameStart = pos;
                    pos = DnsWire.SkipName(data, pos);
                    if (pos + 10 > data.Length) return;

                    int type = (data[pos] << 8) | data[pos + 1];
                    int rdLength = (data[pos + 8] << 8) | data[pos + 9];
                    int rdata = pos + 10;

                    if (type == 1 && rdLength == 4 && rdata + 4 <= data.Length)
                    {
                        string ip = string.Format("{0}.{1}.{2}.{3}",
                            data[rdata], data[rdata + 1], data[rdata + 2], data[rdata + 3]);

                        int unused;
                        string owner = DnsWire.StripLocalSuffix(DnsWire.ReadName(data, nameStart, out unused));
                        if (!string.IsNullOrEmpty(owner)) _names[ip] = owner;
                    }

                    pos = rdata + rdLength;
                }
            }
            catch (Exception) { }
        }
    }
}
