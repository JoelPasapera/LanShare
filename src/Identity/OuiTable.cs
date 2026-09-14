using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LanShare.Identity
{
    /// <summary>
    /// Prefijo OUI de una MAC a nombre de fabricante.
    ///
    /// La tabla integrada es deliberadamente corta: solo prefijos de confianza
    /// alta. La base completa del IEEE ronda las 35.000 entradas y no tiene
    /// sentido incrustarla ni inventarla. Para cobertura real, descarga
    /// https://standards-oui.ieee.org/oui/oui.txt y dejalo junto al ejecutable.
    /// </summary>
    public static class OuiTable
    {
        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex IeeeLine =
            new Regex(@"^\s*([0-9A-Fa-f]{2})-([0-9A-Fa-f]{2})-([0-9A-Fa-f]{2})\s+\(hex\)\s+(.+?)\s*$",
                      RegexOptions.Compiled);

        public static string Source { get; private set; }
        public static int Count { get { lock (_map) { return _map.Count; } } }

        private static readonly Dictionary<string, string> Builtin =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Virtualizacion y contenedores
            { "080027", "Oracle VirtualBox" },
            { "0A0027", "VirtualBox (Host-Only)" },
            { "005056", "VMware" },
            { "000C29", "VMware" },
            { "000569", "VMware" },
            { "001C14", "VMware" },
            { "00155D", "Microsoft Hyper-V" },
            { "525400", "QEMU / KVM" },
            { "0242AC", "Docker" },
            // Placas y IoT
            { "B827EB", "Raspberry Pi Foundation" },
            { "DCA632", "Raspberry Pi Trading" },
            { "E45F01", "Raspberry Pi Trading" },
            { "28CDC1", "Raspberry Pi Trading" },
            { "240AC4", "Espressif (ESP32)" },
            { "30AEA4", "Espressif (ESP32)" },
            { "84F3EB", "Espressif (ESP32)" },
            { "A4CF12", "Espressif (ESP32)" },
            { "7C9EBD", "Espressif (ESP32)" },
            { "ECFABC", "Espressif (ESP32)" },
            // Fabricantes comunes
            { "001B63", "Apple" },
            { "3C15C2", "Apple" },
            { "784F43", "Apple" },
            { "A483E7", "Apple" },
            { "ACBC32", "Apple" },
            { "DCA904", "Apple" },
            { "F01898", "Apple" },
            { "F45C89", "Apple" },
            { "00E04C", "Realtek" },
            { "50C7BF", "TP-Link" },
            { "24A43C", "Ubiquiti" },
            { "802AA8", "Ubiquiti" }
        };

        static OuiTable()
        {
            LoadBuiltin();
            Source = "tabla integrada";
        }

        private static void LoadBuiltin()
        {
            lock (_map)
            {
                _map.Clear();
                foreach (KeyValuePair<string, string> entry in Builtin) _map[entry.Key] = entry.Value;
            }
        }

        /// <summary>Carga el oui.txt del IEEE si esta junto al ejecutable. Devuelve entradas cargadas.</summary>
        public static int TryLoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return 0;

            Dictionary<string, string> loaded =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        Match m = IeeeLine.Match(line);
                        if (!m.Success) continue;
                        string prefix = (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value).ToUpperInvariant();
                        loaded[prefix] = m.Groups[4].Value;
                    }
                }
            }
            catch (Exception) { return 0; }

            if (loaded.Count == 0) return 0;

            lock (_map)
            {
                _map.Clear();
                foreach (KeyValuePair<string, string> entry in loaded) _map[entry.Key] = entry.Value;
                // La tabla integrada cubre lo que el archivo no traiga.
                foreach (KeyValuePair<string, string> entry in Builtin)
                {
                    if (!_map.ContainsKey(entry.Key)) _map[entry.Key] = entry.Value;
                }
            }

            Source = string.Format("oui.txt ({0} entradas)", loaded.Count);
            return loaded.Count;
        }

        public static string Lookup(string oui)
        {
            if (string.IsNullOrEmpty(oui)) return "";
            lock (_map)
            {
                string vendor;
                return _map.TryGetValue(oui, out vendor) ? vendor : "";
            }
        }
    }
}
