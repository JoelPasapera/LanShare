using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;

namespace LanShare.Net
{
    /// <summary>
    /// Ordena las direcciones del equipo por la probabilidad de que un movil
    /// conectado a la misma Wi-Fi llegue a ellas.
    ///
    /// Un equipo de desarrollo suele tener adaptadores virtuales (WSL, Hyper-V,
    /// VirtualBox, VMware, Docker, VPN) con direcciones privadas que el movil no
    /// alcanza, y Windows no los enumera al final. Antes el codigo QR usaba la
    /// primera que salia; ahora gana la del adaptador con puerta de enlace, que
    /// es el que conecta con el router.
    ///
    /// Funcion pura: no consulta la red, solo puntua lo que recibe.
    /// </summary>
    public static class LanAddressRanking
    {
        private const int GatewayScore = 32;
        private const int VirtualScore = -16;
        private const int WirelessScore = 2;
        private const int WiredScore = 1;
        private const int PointToPointScore = -4;
        private const int PrivateRangeScore = 2;
        private const int SharedRangeScore = -2;

        /// <summary>
        /// Fragmentos del nombre o la descripcion de adaptadores que no dan a la
        /// red del movil. Se comparan en minusculas.
        /// </summary>
        private static readonly string[] VirtualMarkers =
        {
            "virtual", "hyper-v", "vethernet", "vmware", "vmnet", "virtualbox", "vbox",
            "wsl", "docker", "loopback", "pseudo", "npcap", "bluetooth",
            "vpn", "tap-windows", "wintun", "wireguard", "openvpn", "tailscale", "zerotier",
            "hamachi", "anyconnect", "fortinet", "globalprotect", "pangp", "nordlynx"
        };

        /// <summary>
        /// La zona Wi-Fi movil de Windows (y la antigua red hospedada) se anuncia
        /// como adaptador virtual, pero es justo a la que se conecta el movil
        /// cuando el PC comparte conexion.
        /// </summary>
        private static readonly string[] HotspotMarkers = { "wi-fi direct", "hosted network" };

        /// <summary>De la mas probable a la menos; a igual puntuacion, el orden de Windows.</summary>
        public static List<LanAddress> Order(IList<LanAddress> addresses)
        {
            List<LanAddress> ordered = new List<LanAddress>();
            if (addresses == null) return ordered;

            List<KeyValuePair<int, int>> keys = new List<KeyValuePair<int, int>>(addresses.Count);
            for (int i = 0; i < addresses.Count; i++)
            {
                if (addresses[i] == null) continue;
                keys.Add(new KeyValuePair<int, int>(Score(addresses[i]), i));
            }

            // List.Sort no es estable: el indice original deshace los empates.
            keys.Sort(delegate(KeyValuePair<int, int> a, KeyValuePair<int, int> b)
            {
                int byScore = b.Key.CompareTo(a.Key);
                return byScore != 0 ? byScore : a.Value.CompareTo(b.Value);
            });

            foreach (KeyValuePair<int, int> key in keys) ordered.Add(addresses[key.Value]);
            return ordered;
        }

        public static int Score(LanAddress address)
        {
            if (address == null) throw new ArgumentNullException("address");

            int score = 0;
            if (address.HasGateway) score += GatewayScore;
            if (LooksVirtual(address.AdapterName, address.Description)) score += VirtualScore;

            if (address.Type == NetworkInterfaceType.Wireless80211) score += WirelessScore;
            else if (IsWired(address.Type)) score += WiredScore;
            else if (address.Type == NetworkInterfaceType.Ppp) score += PointToPointScore;

            if (IsPrivateRange(address.Address)) score += PrivateRangeScore;
            else if (IsSharedRange(address.Address)) score += SharedRangeScore;

            return score;
        }

        public static bool LooksVirtual(string adapterName, string description)
        {
            string text = ((adapterName ?? "") + " " + (description ?? "")).ToLowerInvariant();
            foreach (string marker in HotspotMarkers)
            {
                if (text.Contains(marker)) return false;
            }

            foreach (string marker in VirtualMarkers)
            {
                if (text.Contains(marker)) return true;
            }
            return false;
        }

        private static bool IsWired(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Ethernet ||
                   type == NetworkInterfaceType.GigabitEthernet ||
                   type == NetworkInterfaceType.FastEthernetT ||
                   type == NetworkInterfaceType.FastEthernetFx ||
                   type == NetworkInterfaceType.Ethernet3Megabit;
        }

        /// <summary>10/8, 172.16/12 y 192.168/16: las redes de casa y de oficina.</summary>
        public static bool IsPrivateRange(string address)
        {
            int first, second;
            if (!TryLeadingOctets(address, out first, out second)) return false;

            return first == 10 ||
                   (first == 172 && second >= 16 && second <= 31) ||
                   (first == 192 && second == 168);
        }

        /// <summary>100.64/10: la usan Tailscale y los operadores con CGNAT, no la Wi-Fi de casa.</summary>
        public static bool IsSharedRange(string address)
        {
            int first, second;
            if (!TryLeadingOctets(address, out first, out second)) return false;
            return first == 100 && second >= 64 && second <= 127;
        }

        private static bool TryLeadingOctets(string address, out int first, out int second)
        {
            first = -1;
            second = -1;
            if (string.IsNullOrEmpty(address)) return false;

            string[] parts = address.Split('.');
            if (parts.Length != 4) return false;

            return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out first) &&
                   int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out second) &&
                   first <= 255 && second <= 255;
        }
    }
}
