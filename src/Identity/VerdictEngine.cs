using System;
using System.Collections.Generic;
using System.Text;

namespace LanShare.Identity
{
    /// <summary>
    /// Fusiona todas las senales en un unico dictamen.
    ///
    /// Cada fuente vota por una familia de dispositivo con un peso segun lo
    /// dificil que sea falsearla: un puerto abierto pesa mas que un User-Agent,
    /// que el cliente controla. Si dos familias quedan a la par se reporta la
    /// discrepancia, porque una contradiccion (TTL de Windows con UA de
    /// Android) dice mas que un veredicto limpio pero equivocado.
    /// </summary>
    public static class VerdictEngine
    {
        private const string AppleMobile = "Apple movil";
        private const string Android = "Android";
        private const string Windows = "Windows";
        private const string Mac = "Mac";
        private const string Linux = "Linux";
        private const string Printer = "Impresora";
        private const string Tv = "TV / streaming";
        private const string Router = "Router / red";
        private const string Iot = "IoT";
        private const string VirtualMachine = "Maquina virtual";

        public static void Evaluate(ClientInfo client)
        {
            if (client == null) return;
            lock (client.SyncRoot) { EvaluateLocked(client); }
        }

        private static void EvaluateLocked(ClientInfo client)
        {
            Dictionary<string, int> votes = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> reasons = new List<string>();

            VoteFromUserAgent(client, votes, reasons);
            VoteFromPorts(client, votes, reasons);
            VoteFromTtl(client, votes, reasons);
            VoteFromGpu(client, votes, reasons);
            VoteFromVendor(client, votes, reasons);
            VoteFromSsdp(client, votes, reasons);

            if (client.Banners.Length > 0)
                reasons.Add("banner HTTP: " + Truncate(client.Banners));

            string best = "", second = "";
            int bestScore = 0, secondScore = 0;

            foreach (KeyValuePair<string, int> vote in votes)
            {
                if (vote.Value > bestScore)
                {
                    second = best; secondScore = bestScore;
                    best = vote.Key; bestScore = vote.Value;
                }
                else if (vote.Value > secondScore)
                {
                    second = vote.Key; secondScore = vote.Value;
                }
            }

            if (bestScore == 0)
            {
                client.Verdict = "Sin senales suficientes";
                client.VerdictDetail = "";
                client.VerdictScore = 0;
                return;
            }

            string confidence = bestScore >= 6 ? "alta" : (bestScore >= 3 ? "media" : "baja");
            client.Verdict = string.Format("{0} (confianza {1})", best, confidence);
            client.VerdictScore = bestScore;

            StringBuilder detail = new StringBuilder();
            detail.Append("Senales: ").Append(string.Join("; ", reasons.ToArray()));

            if (secondScore > 0 && secondScore >= bestScore - 1 && second != best)
            {
                detail.Append("  ||  DISCREPANCIA: tambien apunta a ").Append(second)
                      .Append(" (").Append(secondScore).Append(" vs ").Append(bestScore).Append(")");
            }

            client.VerdictDetail = detail.ToString();
        }

        // El User-Agent lo controla el cliente: peso moderado.
        private static void VoteFromUserAgent(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            string device = c.DeviceLabel;
            if (device.StartsWith("iPhone") || device.StartsWith("iPad") || device.StartsWith("iPod"))
                Vote(votes, reasons, AppleMobile, 2, "UA dice iOS");
            else if (device.StartsWith("Android"))
                Vote(votes, reasons, Android, 2, "UA dice Android");
            else if (device.StartsWith("Windows"))
                Vote(votes, reasons, Windows, 2, "UA dice Windows");
            else if (device.StartsWith("macOS"))
                Vote(votes, reasons, Mac, 2, "UA dice macOS");
            else if (device.StartsWith("Linux") || device.StartsWith("ChromeOS"))
                Vote(votes, reasons, Linux, 2, "UA dice Linux/ChromeOS");
            else if (device.StartsWith("Smart TV") || device.StartsWith("Android TV"))
                Vote(votes, reasons, Tv, 2, "UA dice Smart TV");
        }

        // Un puerto abierto es la senal mas dificil de falsear: peso alto.
        private static void VoteFromPorts(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            if (c.OpenPorts.Length == 0) return;

            if (Contains(c.OpenPorts, "62078")) Vote(votes, reasons, AppleMobile, 4, "puerto 62078 (lockdownd)");
            if (Contains(c.OpenPorts, "445") || Contains(c.OpenPorts, "3389")) Vote(votes, reasons, Windows, 3, "SMB/RDP abierto");
            if (Contains(c.OpenPorts, "548") || Contains(c.OpenPorts, "7000")) Vote(votes, reasons, Mac, 3, "AFP/AirPlay abierto");
            if (Contains(c.OpenPorts, "5555")) Vote(votes, reasons, Android, 3, "ADB abierto");
            if (Contains(c.OpenPorts, "9100") || Contains(c.OpenPorts, "631")) Vote(votes, reasons, Printer, 4, "puerto de impresion");
            if (Contains(c.OpenPorts, "8009")) Vote(votes, reasons, Tv, 3, "Chromecast");
            if (Contains(c.OpenPorts, "53")) Vote(votes, reasons, Router, 3, "sirve DNS");
            if (Contains(c.OpenPorts, "1883")) Vote(votes, reasons, Iot, 3, "broker MQTT");
        }

        // El TTL separa familias, no sistemas concretos: reparte voto bajo.
        private static void VoteFromTtl(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            if (c.Ttl <= 0) return;

            if (c.Ttl <= 64)
            {
                Vote(votes, null, Android, 1, null);
                Vote(votes, null, AppleMobile, 1, null);
                Vote(votes, null, Linux, 1, null);
                Vote(votes, null, Mac, 1, null);
                reasons.Add("TTL " + c.Ttl + " (familia Unix)");
            }
            else if (c.Ttl <= 128)
            {
                Vote(votes, reasons, Windows, 2, "TTL " + c.Ttl + " (Windows)");
            }
            else
            {
                Vote(votes, null, Router, 2, null);
                Vote(votes, null, Iot, 1, null);
                reasons.Add("TTL " + c.Ttl + " (embebido)");
            }
        }

        // La GPU identifica el SoC y no es trivial de falsificar.
        private static void VoteFromGpu(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            if (c.Gpu.Length == 0) return;

            string gpu = c.Gpu.ToUpperInvariant();
            if (gpu.Contains("ADRENO") || gpu.Contains("MALI") || gpu.Contains("POWERVR") || gpu.Contains("XCLIPSE"))
            {
                Vote(votes, reasons, Android, 3, "GPU movil " + Truncate(c.Gpu));
            }
            else if (gpu.Contains("APPLE"))
            {
                Vote(votes, null, AppleMobile, 2, null);
                Vote(votes, null, Mac, 2, null);
                reasons.Add("GPU Apple");
            }
            else if (gpu.Contains("NVIDIA") || gpu.Contains("RADEON") || gpu.Contains("GEFORCE") || gpu.Contains("INTEL"))
            {
                Vote(votes, null, Windows, 1, null);
                Vote(votes, null, Linux, 1, null);
                reasons.Add("GPU de escritorio");
            }
        }

        private static void VoteFromVendor(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            // Con MAC aleatoria el OUI no dice nada del fabricante real.
            if (c.MacRandomized || c.Vendor.Length == 0 || c.Vendor == "Desconocido") return;

            string vendor = c.Vendor.ToUpperInvariant();
            if (vendor.Contains("APPLE"))
            {
                Vote(votes, null, AppleMobile, 2, null);
                Vote(votes, null, Mac, 2, null);
                reasons.Add("OUI Apple");
            }
            else if (vendor.Contains("RASPBERRY")) Vote(votes, reasons, Linux, 3, "OUI Raspberry Pi");
            else if (vendor.Contains("ESPRESSIF")) Vote(votes, reasons, Iot, 4, "OUI Espressif");
            else if (vendor.Contains("VMWARE") || vendor.Contains("VIRTUALBOX") ||
                     vendor.Contains("QEMU") || vendor.Contains("HYPER-V"))
                Vote(votes, reasons, VirtualMachine, 4, "OUI de hipervisor");
        }

        private static void VoteFromSsdp(ClientInfo c, Dictionary<string, int> votes, List<string> reasons)
        {
            if (c.SsdpServer.Length == 0) return;

            string server = c.SsdpServer.ToUpperInvariant();
            if (server.Contains("WINDOWS")) Vote(votes, null, Windows, 3, null);
            else if (server.Contains("ANDROID") || server.Contains("TIZEN") || server.Contains("WEBOS"))
                Vote(votes, null, Tv, 3, null);
            else if (server.Contains("LINUX") || server.Contains("UNIX")) Vote(votes, null, Router, 2, null);

            reasons.Add("SSDP: " + Truncate(c.SsdpServer));
        }

        private static void Vote(Dictionary<string, int> votes, List<string> reasons,
                                 string family, int weight, string reason)
        {
            int current;
            votes[family] = votes.TryGetValue(family, out current) ? current + weight : weight;
            if (reasons != null && reason != null) reasons.Add(reason);
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        private static string Truncate(string value)
        {
            if (value == null) return "";
            value = value.Trim();
            return value.Length > 48 ? value.Substring(0, 48) + "..." : value;
        }
    }
}
