using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using LanShare.Net;

namespace LanShare.Tests
{
    /// <summary>
    /// Cuando avisar de que Windows tiene como publica la red por la que llega
    /// el movil. Los adaptadores y las conexiones imitan lo que dan Windows y
    /// su lista de redes; nada de esto consulta la red de verdad.
    /// </summary>
    public static class PublicNetworksTests
    {
        private static readonly Guid WiFiAdapter = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid CableAdapter = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid WslAdapter = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly Guid VpnAdapter = new Guid("44444444-4444-4444-4444-444444444444");

        private static readonly Guid HomeNetwork = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid OtherNetwork = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        private static LanAddress WiFi(string ip)
        {
            return new LanAddress(ip, "Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", NetworkInterfaceType.Wireless80211,
                                  true, WiFiAdapter.ToString("B"));
        }

        private static LanAddress Wsl()
        {
            return new LanAddress("172.29.144.1", "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter",
                                  NetworkInterfaceType.Ethernet, false, WslAdapter.ToString("B"));
        }

        private static LanAddress Vpn()
        {
            return new LanAddress("10.8.0.2", "Ethernet 3", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet,
                                  true, VpnAdapter.ToString("B"));
        }

        private static NetworkConnectionInfo Connection(Guid adapter, Guid network, string name, NetworkCategory category)
        {
            return new NetworkConnectionInfo(adapter, network, name, category);
        }

        private static List<PublicNetwork> Find(LanAddress[] addresses, params NetworkConnectionInfo[] connections)
        {
            return PublicNetworks.Find(addresses, connections);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Net / red publica en Windows");

            runner.Test("CLAVE: la Wi-Fi de casa como publica se avisa", delegate
            {
                List<PublicNetwork> found = Find(new LanAddress[] { WiFi("192.168.1.5") },
                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Public));

                Check.Equal(1, found.Count, "una red");
                Check.Equal(HomeNetwork, found[0].NetworkId, "la de la Wi-Fi");
                Check.Equal("Casa", found[0].Name, "nombre");
                Check.Equal("\"Casa\" (192.168.1.5)", found[0].Describe(), "descripcion");
            });

            runner.Test("privada o de dominio no se avisa", delegate
            {
                LanAddress[] wifi = { WiFi("192.168.1.5") };
                Check.Equal(0, Find(wifi, Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Private)).Count,
                            "privada");
                Check.Equal(0, Find(wifi, Connection(WiFiAdapter, HomeNetwork, "Empresa", NetworkCategory.Domain)).Count,
                            "dominio");
            });

            runner.Test("WSL y las VPN no cuentan aunque su red sea publica", delegate
            {
                // Windows deja como publica la red de WSL y la de casi todas las
                // VPN, y ningun movil entra por ahi: avisar seria ruido.
                List<PublicNetwork> found = Find(new LanAddress[] { WiFi("192.168.1.5"), Wsl(), Vpn() },
                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Private),
                    Connection(WslAdapter, OtherNetwork, "Red no identificada", NetworkCategory.Public),
                    Connection(VpnAdapter, OtherNetwork, "Red no identificada", NetworkCategory.Public));

                Check.Equal(0, found.Count, "nada que avisar");
            });

            runner.Test("dos adaptadores en la misma red publica dan un solo aviso", delegate
            {
                LanAddress cable = new LanAddress("192.168.1.20", "Ethernet", "Realtek PCIe GbE Family Controller",
                                                  NetworkInterfaceType.Ethernet, true, CableAdapter.ToString("B"));
                List<PublicNetwork> found = Find(new LanAddress[] { WiFi("192.168.1.5"), cable },
                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Public),
                    Connection(CableAdapter, HomeNetwork, "Casa", NetworkCategory.Public));

                Check.Equal(1, found.Count, "una red");
                Check.Equal(2, found[0].Addresses.Count, "con sus dos direcciones");
                Check.Equal("\"Casa\" (192.168.1.5, 192.168.1.20)", found[0].Describe(), "descripcion");
            });

            runner.Test("cada red publica distinta se avisa por separado", delegate
            {
                LanAddress cable = new LanAddress("10.0.0.7", "Ethernet", "Realtek PCIe GbE Family Controller",
                                                  NetworkInterfaceType.Ethernet, true, CableAdapter.ToString("B"));
                List<PublicNetwork> found = Find(new LanAddress[] { WiFi("192.168.1.5"), cable },
                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Public),
                    Connection(CableAdapter, OtherNetwork, "Oficina", NetworkCategory.Public));

                Check.Equal(2, found.Count, "dos redes");
                Check.Equal("Casa", found[0].Name, "en el orden de las direcciones");
                Check.Equal("Oficina", found[1].Name, "la segunda");
            });

            runner.Test("un conmutador externo de Hyper-V si cuenta", delegate
            {
                // Con el conmutador externo, la direccion de casa la tiene el
                // adaptador virtual: es por donde llega el movil.
                Guid externalAdapter = new Guid("55555555-5555-5555-5555-555555555555");
                LanAddress external = new LanAddress("192.168.1.40", "vEthernet (Externo)",
                                                     "Hyper-V Virtual Ethernet Adapter #2",
                                                     NetworkInterfaceType.Ethernet, true, externalAdapter.ToString("B"));
                List<PublicNetwork> found = Find(new LanAddress[] { external },
                    Connection(externalAdapter, HomeNetwork, "Casa", NetworkCategory.Public));

                Check.Equal(1, found.Count, "avisada");
            });

            runner.Test("sin identificador de adaptador o sin datos no se avisa nada", delegate
            {
                LanAddress unknown = new LanAddress("192.168.1.5", "Wi-Fi", "Intel(R) Wi-Fi 6 AX201",
                                                    NetworkInterfaceType.Wireless80211, true);
                Check.Equal("", unknown.AdapterId, "el constructor antiguo no inventa identificador");
                Check.Equal(0, Find(new LanAddress[] { unknown },
                                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Public)).Count,
                            "sin identificador");
                Check.Equal(0, PublicNetworks.Find(null, null).Count, "sin datos");
                Check.Equal(0, Find(new LanAddress[] { WiFi("192.168.1.5") }).Count, "sin conexiones");
            });

            runner.Test("el identificador del adaptador se lee en cualquier formato de GUID", delegate
            {
                LanAddress upper = new LanAddress("192.168.1.5", "Wi-Fi", "Intel(R) Wi-Fi 6 AX201",
                                                  NetworkInterfaceType.Wireless80211, true,
                                                  "{11111111-1111-1111-1111-111111111111}".ToUpperInvariant());
                Check.Equal(1, Find(new LanAddress[] { upper },
                                    Connection(WiFiAdapter, HomeNetwork, "Casa", NetworkCategory.Public)).Count,
                            "con llaves y en mayusculas, como lo da Windows");
            });

            runner.Test("una red sin nombre tambien se describe", delegate
            {
                PublicNetwork network = new PublicNetwork(HomeNetwork, null);
                Check.Equal("sin nombre", network.Describe(), "sin direcciones");
            });

            runner.Suite("Net / adaptadores de VPN");

            runner.Test("reconoce las VPN habituales", delegate
            {
                Check.True(LanAddressRanking.LooksLikeVpn("Ethernet 3", "TAP-Windows Adapter V9"), "OpenVPN");
                Check.True(LanAddressRanking.LooksLikeVpn("WireGuard Tunnel", "WireGuard Tunnel"), "WireGuard");
                Check.True(LanAddressRanking.LooksLikeVpn("Tailscale", "Tailscale Tunnel"), "Tailscale");
                Check.False(LanAddressRanking.LooksLikeVpn("Wi-Fi", "Intel(R) Wi-Fi 6 AX201"), "Wi-Fi");
                Check.False(LanAddressRanking.LooksLikeVpn("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter"),
                            "WSL es virtual, pero no VPN");
            });

            runner.Test("las VPN siguen contando como virtuales", delegate
            {
                Check.True(LanAddressRanking.LooksVirtual("Ethernet 3", "TAP-Windows Adapter V9"), "OpenVPN");
                Check.True(LanAddressRanking.LooksVirtual("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter"), "WSL");
            });
        }
    }
}
