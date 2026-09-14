using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanShare.Net
{
    /// <summary>Direcciones IPv4 utilizables de los adaptadores activos.</summary>
    public static class LanAddresses
    {
        public static List<string> GetActive()
        {
            List<string> result = new List<string>();

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(info.Address)) continue;

                        string ip = info.Address.ToString();

                        // 169.254.x.x es autoasignada: el adaptador esta arriba
                        // pero no consiguio direccion del DHCP.
                        if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;

                        if (!result.Contains(ip)) result.Add(ip);
                    }
                }
            }
            catch (Exception) { }

            return result;
        }
    }
}
