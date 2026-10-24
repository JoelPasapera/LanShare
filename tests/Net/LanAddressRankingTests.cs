using System.Collections.Generic;
using System.Net.NetworkInformation;
using LanShare.Net;

namespace LanShare.Tests
{
    /// <summary>
    /// Que direccion se ofrece primero al movil. Los adaptadores imitan los
    /// nombres y descripciones reales que Windows da a cada uno.
    /// </summary>
    public static class LanAddressRankingTests
    {
        private static LanAddress WiFi(string ip)
        {
            return new LanAddress(ip, "Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz",
                                  NetworkInterfaceType.Wireless80211, true);
        }

        private static LanAddress Wsl()
        {
            return new LanAddress("172.29.144.1", "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter",
                                  NetworkInterfaceType.Ethernet, false);
        }

        private static LanAddress VirtualBox()
        {
            return new LanAddress("192.168.56.1", "Ethernet 2", "VirtualBox Host-Only Ethernet Adapter",
                                  NetworkInterfaceType.Ethernet, false);
        }

        private static string First(params LanAddress[] enumerated)
        {
            return LanAddressRanking.Order(enumerated)[0].Address;
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Net / LanAddressRanking");

            runner.Test("la Wi-Fi va antes que WSL y VirtualBox aunque Windows la enumere al final", delegate
            {
                Check.Equal("192.168.1.5", First(Wsl(), VirtualBox(), WiFi("192.168.1.5")), "primera direccion");
            });

            runner.Test("el cable con puerta de enlace va antes que una Wi-Fi sin ella", delegate
            {
                LanAddress cable = new LanAddress("192.168.0.20", "Ethernet", "Realtek PCIe GbE Family Controller",
                                                  NetworkInterfaceType.Ethernet, true);
                LanAddress wifiSuelta = new LanAddress("10.0.0.7", "Wi-Fi", "Intel(R) Wi-Fi 6 AX201",
                                                       NetworkInterfaceType.Wireless80211, false);
                Check.Equal("192.168.0.20", First(wifiSuelta, cable), "primera direccion");
            });

            runner.Test("un conmutador externo de Hyper-V con puerta de enlace gana a WSL", delegate
            {
                // Con un conmutador externo, la tarjeta fisica pierde su IP y la
                // direccion buena la tiene un adaptador vEthernet.
                LanAddress externo = new LanAddress("192.168.1.40", "vEthernet (Externo)",
                                                    "Hyper-V Virtual Ethernet Adapter #2",
                                                    NetworkInterfaceType.Ethernet, true);
                Check.Equal("192.168.1.40", First(Wsl(), externo), "primera direccion");
            });

            runner.Test("la Wi-Fi gana a una VPN que tambien tiene puerta de enlace", delegate
            {
                LanAddress vpn = new LanAddress("10.8.0.2", "Ethernet 3", "TAP-Windows Adapter V9",
                                                NetworkInterfaceType.Ethernet, true);
                Check.Equal("192.168.1.5", First(vpn, WiFi("192.168.1.5")), "primera direccion");
            });

            runner.Test("la zona Wi-Fi movil del PC no cuenta como virtual", delegate
            {
                // Cuando el PC comparte conexion, el movil se conecta justo ahi.
                LanAddress zona = new LanAddress("192.168.137.1", "Conexion de area local* 10",
                                                 "Microsoft Wi-Fi Direct Virtual Adapter #2",
                                                 NetworkInterfaceType.Wireless80211, false);
                Check.False(LanAddressRanking.LooksVirtual(zona.AdapterName, zona.Description), "no es virtual");
                Check.False(LanAddressRanking.LooksVirtual("Conexion de area local* 2",
                                                           "Microsoft Hosted Network Virtual Adapter"),
                            "la red hospedada antigua tampoco");
                Check.Equal("192.168.137.1", First(Wsl(), zona), "va antes que WSL");
            });

            runner.Test("Tailscale (100.64/10) va despues de la red de casa", delegate
            {
                LanAddress tailscale = new LanAddress("100.101.7.3", "Tailscale", "Tailscale Tunnel",
                                                      NetworkInterfaceType.Ethernet, false);
                LanAddress casa = new LanAddress("192.168.1.9", "Ethernet", "Intel(R) Ethernet Connection",
                                                 NetworkInterfaceType.Ethernet, false);
                Check.Equal("192.168.1.9", First(tailscale, casa), "primera direccion");
            });

            runner.Test("a igual puntuacion se respeta el orden de Windows", delegate
            {
                List<LanAddress> ordenadas = LanAddressRanking.Order(new LanAddress[] {
                    WiFi("192.168.1.5"), WiFi("192.168.1.6"), WiFi("192.168.1.7")
                });
                Check.Equal("192.168.1.5", ordenadas[0].Address, "primera");
                Check.Equal("192.168.1.6", ordenadas[1].Address, "segunda");
                Check.Equal("192.168.1.7", ordenadas[2].Address, "tercera");
            });

            runner.Test("no pierde ni inventa direcciones", delegate
            {
                List<LanAddress> ordenadas = LanAddressRanking.Order(new LanAddress[] {
                    Wsl(), null, VirtualBox(), WiFi("192.168.1.5")
                });
                Check.Equal(3, ordenadas.Count, "las tres direcciones reales");
                Check.Equal(0, LanAddressRanking.Order(null).Count, "lista nula");
            });

            runner.Test("reconoce los adaptadores virtuales habituales", delegate
            {
                string[][] virtuales = {
                    new string[] { "vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter" },
                    new string[] { "VMware Network Adapter VMnet8", "VMware Virtual Ethernet Adapter for VMnet8" },
                    new string[] { "vEthernet (DockerNAT)", "" },
                    new string[] { "Conexion de red Bluetooth", "Bluetooth Device (Personal Area Network)" },
                    new string[] { "WireGuard", "WireGuard Tunnel" },
                    new string[] { "Ethernet 4", "Npcap Loopback Adapter" },
                };
                foreach (string[] adaptador in virtuales)
                {
                    Check.True(LanAddressRanking.LooksVirtual(adaptador[0], adaptador[1]), "virtual: " + adaptador[0]);
                }

                Check.False(LanAddressRanking.LooksVirtual("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz"), "Wi-Fi real");
                Check.False(LanAddressRanking.LooksVirtual("Ethernet", "Realtek PCIe GbE Family Controller"), "cable real");
                Check.False(LanAddressRanking.LooksVirtual(null, null), "sin datos");
            });

            runner.Test("rangos privados y compartidos", delegate
            {
                Check.True(LanAddressRanking.IsPrivateRange("10.0.0.1"), "10/8");
                Check.True(LanAddressRanking.IsPrivateRange("172.16.0.1"), "172.16");
                Check.True(LanAddressRanking.IsPrivateRange("172.31.255.1"), "172.31");
                Check.False(LanAddressRanking.IsPrivateRange("172.32.0.1"), "172.32 no es privada");
                Check.True(LanAddressRanking.IsPrivateRange("192.168.1.5"), "192.168");
                Check.False(LanAddressRanking.IsPrivateRange("192.169.1.5"), "192.169 no es privada");
                Check.False(LanAddressRanking.IsPrivateRange("8.8.8.8"), "publica");
                Check.True(LanAddressRanking.IsSharedRange("100.64.0.1"), "100.64");
                Check.True(LanAddressRanking.IsSharedRange("100.127.255.1"), "100.127");
                Check.False(LanAddressRanking.IsSharedRange("100.128.0.1"), "100.128 fuera");
                Check.False(LanAddressRanking.IsPrivateRange("no.es.una.ip"), "texto");
                Check.False(LanAddressRanking.IsPrivateRange(""), "vacia");
                Check.False(LanAddressRanking.IsPrivateRange("-10.0.0.1"), "signo");
            });
        }
    }
}
