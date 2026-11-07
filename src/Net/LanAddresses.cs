using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanShare.Net
{
    /// <summary>
    /// Direcciones IPv4 utilizables de los adaptadores activos, de la que mas
    /// probablemente alcance el movil a la que menos (ver LanAddressRanking).
    /// </summary>
    public static class LanAddresses
    {
        /// <summary>Solo las direcciones, en orden de preferencia.</summary>
        public static List<string> GetActive()
        {
            List<string> result = new List<string>();
            foreach (LanAddress address in GetActiveDetailed()) result.Add(address.Address);
            return result;
        }

        /// <summary>Las direcciones con su adaptador, en orden de preferencia.</summary>
        public static List<LanAddress> GetActiveDetailed()
        {
            List<LanAddress> found = new List<LanAddress>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            NetworkInterface[] adapters;
            try { adapters = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception) { return found; }

            foreach (NetworkInterface adapter in adapters)
            {
                // Un adaptador que falla al consultarlo no oculta a los demas.
                try { Collect(adapter, found, seen); }
                catch (Exception) { }
            }

            return LanAddressRanking.Order(found);
        }

        private static void Collect(NetworkInterface adapter, List<LanAddress> found, HashSet<string> seen)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) return;
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) return;
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return;

            IPInterfaceProperties properties = adapter.GetIPProperties();
            bool gateway = HasIPv4Gateway(properties);

            foreach (UnicastIPAddressInformation info in properties.UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(info.Address)) continue;

                string ip = info.Address.ToString();

                // 169.254.x.x es autoasignada: el adaptador esta arriba
                // pero no consiguio direccion del DHCP.
                if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;

                if (!seen.Add(ip)) continue;
                found.Add(new LanAddress(ip, adapter.Name, adapter.Description,
                                         adapter.NetworkInterfaceType, gateway));
            }
        }

        private static bool HasIPv4Gateway(IPInterfaceProperties properties)
        {
            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
            {
                IPAddress address = gateway.Address;
                if (address != null && address.AddressFamily == AddressFamily.InterNetwork &&
                    !address.Equals(IPAddress.Any))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
