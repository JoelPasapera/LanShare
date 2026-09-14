using System;
using System.Collections.Generic;
using System.Text;
using LanShare.Identity;

namespace LanShare.Ui
{
    /// <summary>
    /// Unica responsabilidad: convertir un ClientInfo en el bloque de texto del
    /// panel de detalle. Separarlo del formulario permite probar el formato sin
    /// levantar una ventana.
    /// </summary>
    public static class ClientDetailFormatter
    {
        private const int LabelWidth = 19;

        public static string Build(ClientInfo c)
        {
            if (c == null) return "";

            StringBuilder sb = new StringBuilder();

            Section(sb, "VEREDICTO");
            Line(sb, "Dispositivo", Or(c.Verdict, "(sondeo en curso)"));
            if (c.VerdictDetail.Length > 0) Line(sb, "Base", c.VerdictDetail);

            Section(sb, "IDENTIDAD DE RED");
            Line(sb, "Alias", c.Alias.Length > 0
                              ? c.Alias + "   (guardado para " + c.AliasKey + ")"
                              : "(sin alias: doble clic en la fila para ponerle uno)");
            Line(sb, "IP / puerto origen", string.Format("{0}  (puerto efimero {1})", c.Ip, c.SourcePort));
            Line(sb, "DNS inverso", Or(c.HostName, "(sin respuesta)"));
            Line(sb, "Nombre mDNS .local", Or(c.MdnsName, "(sin respuesta)"));
            Line(sb, "Nombre NetBIOS", Or(c.NbName, "(sin respuesta)"));
            Line(sb, "MAC", FormatMac(c));

            Section(sb, "HUELLA DE RED");
            Line(sb, "SO por TTL", Or(c.OsGuess, "(sin respuesta ICMP: firewall del cliente o red que filtra)"));
            Line(sb, "Latencia ICMP", c.RttMs >= 0 ? c.RttMs + " ms" : "-");
            Line(sb, "Puertos abiertos", ClientRegistry.EnablePortScan
                                         ? Or(c.OpenPorts, "(ninguno de los 21 sondeados)")
                                         : "(escaneo desactivado)");
            Line(sb, "Tipo por puertos", Or(c.PortGuess, "-"));
            Line(sb, "Banner HTTP", Or(c.Banners, "-"));
            Line(sb, "SSDP / UPnP", Or(c.SsdpServer, "(no responde a M-SEARCH)"));

            Section(sb, "CAPA HTTP");
            Line(sb, "Dispositivo (UA)", c.DeviceLabel);
            Line(sb, "Navegador", c.BrowserLabel);
            Line(sb, "Protocolo", string.Format("{0}  |  keep-alive: {1}", c.Protocol, c.KeepAlive));
            Line(sb, "Idioma", Or(c.AcceptLanguage, "-"));
            Line(sb, "Compresion", Or(c.AcceptEncoding, "-"));
            Line(sb, "Referer", Or(c.Referer, "-"));
            Line(sb, "Firma de cabeceras", Or(c.HeaderSignature, "-"));
            Line(sb, "Client Hints", FormatHints(c));
            Line(sb, "User-Agent", Or(c.UserAgent, "(vacio)"));

            Section(sb, "SONDA JS");
            if (c.ProbeSummary.Length > 0)
            {
                sb.AppendLine(c.ProbeSummary);
                Line(sb, "Recibida", FormatTime(c.ProbeTicks));
            }
            else
            {
                sb.AppendLine("(sin datos: recarga una pagina HTML en el cliente)");
            }

            Section(sb, "TRAFICO");
            Line(sb, "Peticiones / bytes", string.Format("{0}  |  {1}", c.RequestCount, FormatBytes(c.BytesSent)));
            Line(sb, "Visto", string.Format("primera {0}  |  ultima {1}",
                                            FormatTime(c.FirstSeenTicks), FormatTime(c.LastSeenTicks)));

            if (c.RawHeaders.Length > 0)
            {
                Section(sb, "CABECERAS CRUDAS DE LA ULTIMA PETICION (en orden de llegada)");
                sb.Append(c.RawHeaders);
            }

            return sb.ToString();
        }

        public static string FormatMac(ClientInfo c)
        {
            if (c.Mac.Length == 0)
                return "(sin respuesta ARP: fuera del segmento local o cliente inactivo)";

            if (c.MacRandomized)
                return c.Mac + "  [ALEATORIA - privacidad del dispositivo, el OUI no identifica al fabricante]";

            return c.Mac + "  [" + c.Vendor + "]";
        }

        private static string FormatHints(ClientInfo c)
        {
            List<string> hints = new List<string>();
            if (c.ChUa.Length > 0) hints.Add("ua=" + c.ChUa);
            if (c.ChPlatform.Length > 0) hints.Add("plataforma=" + c.ChPlatform);
            if (c.ChMobile.Length > 0) hints.Add("movil=" + c.ChMobile);
            if (c.ChModel.Length > 0) hints.Add("modelo=" + c.ChModel);

            return hints.Count > 0
                ? string.Join("  |  ", hints.ToArray())
                : "(no enviados: exigen contexto seguro, solo https o localhost)";
        }

        public static string FormatTime(long ticks)
        {
            if (ticks <= 0) return "-";
            return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("N1") + " KB";
            if (bytes < 1073741824) return (bytes / 1048576.0).ToString("N1") + " MB";
            return (bytes / 1073741824.0).ToString("N2") + " GB";
        }

        public static string FormatElapsed(long ticks)
        {
            if (ticks <= 0) return "-";

            int seconds = (int)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            if (seconds < 2) return "ahora";
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600) return (seconds / 60) + "m";
            return (seconds / 3600) + "h";
        }

        public static string ShortName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "...";
            int dot = fullName.IndexOf('.');
            return dot > 0 ? fullName.Substring(0, dot) : fullName;
        }

        public static string TtlFamily(int ttl)
        {
            if (ttl <= 0) return "-";
            if (ttl <= 64) return "Unix/Android";
            if (ttl <= 128) return "Windows";
            return "Embebido";
        }

        private static void Section(StringBuilder sb, string title)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("=== " + title + " ===");
        }

        private static void Line(StringBuilder sb, string label, string value)
        {
            sb.Append(label.PadRight(LabelWidth)).Append(": ").AppendLine(value);
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
