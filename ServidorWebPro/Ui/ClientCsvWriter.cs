using System;
using System.Globalization;
using System.IO;
using System.Text;
using ServidorWebPro.Identity;

namespace ServidorWebPro.Ui
{
    /// <summary>Exportacion del panel de clientes a CSV.</summary>
    public static class ClientCsvWriter
    {
        private static readonly string[] Headers =
        {
            "IP", "NombreRed", "rDNS", "mDNS", "NetBIOS", "MAC", "MacAleatoria", "Fabricante",
            "TTL", "RTTms", "SO_TTL", "Veredicto", "BaseVeredicto", "PuertosAbiertos",
            "TipoPorPuerto", "BannerHTTP", "SSDP", "Dispositivo", "Navegador", "GPU",
            "Pantalla", "Idioma", "Protocolo", "FirmaCabeceras", "Peticiones", "Bytes",
            "Primera", "Ultima", "UserAgent"
        };

        public static void Write(string path, ClientInfo[] clients)
        {
            // BOM UTF-8: sin el, Excel abre el archivo en la pagina de codigos
            // local y destroza cualquier acento.
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(",", Headers));

                foreach (ClientInfo c in clients)
                {
                    string[] fields =
                    {
                        c.Ip, c.BestName, c.HostName, c.MdnsName, c.NbName, c.Mac,
                        c.MacRandomized.ToString(), c.Vendor,
                        c.Ttl.ToString(CultureInfo.InvariantCulture),
                        c.RttMs.ToString(CultureInfo.InvariantCulture),
                        c.OsGuess, c.Verdict, c.VerdictDetail, c.OpenPorts,
                        c.PortGuess, c.Banners, c.SsdpServer, c.DeviceLabel, c.BrowserLabel,
                        c.Gpu, c.ScreenInfo, c.AcceptLanguage, c.Protocol, c.HeaderSignature,
                        c.RequestCount.ToString(CultureInfo.InvariantCulture),
                        c.BytesSent.ToString(CultureInfo.InvariantCulture),
                        ClientDetailFormatter.FormatTime(c.FirstSeenTicks),
                        ClientDetailFormatter.FormatTime(c.LastSeenTicks),
                        c.UserAgent
                    };

                    for (int i = 0; i < fields.Length; i++) fields[i] = Escape(fields[i]);
                    writer.WriteLine(string.Join(",", fields));
                }
            }
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuotes) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
