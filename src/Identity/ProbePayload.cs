using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace LanShare.Identity
{
    /// <summary>
    /// Interpreta el JSON que devuelve la sonda y lo vuelca en el registro.
    /// Usa JavaScriptSerializer, que forma parte de .NET Framework: nada que
    /// instalar ni ningun paquete externo.
    /// </summary>
    public static class ProbePayload
    {
        /// <summary>Devuelve false si el cuerpo no era interpretable.</summary>
        public static bool ApplyTo(string clientIp, string json)
        {
            if (string.IsNullOrEmpty(json)) return false;

            Dictionary<string, object> data;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 64 * 1024;
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception) { return false; }

            if (data == null) return false;

            string gpu = Text(data, "gpu");
            string screen = Text(data, "screen");

            StringBuilder summary = new StringBuilder();
            summary.AppendLine(Line("Pantalla", screen + "   |  viewport " + Text(data, "viewport")));
            summary.AppendLine(Line("GPU / SoC", gpu.Length > 0 ? gpu : "(WebGL no disponible)"));
            summary.AppendLine(Line("CPU / RAM", Text(data, "cores") + " nucleos  |  " +
                                                 Text(data, "ram") + " GB (redondeado por el navegador)"));
            summary.AppendLine(Line("Puntos tactiles", Text(data, "touch") + "  |  navigator.platform: " +
                                                       Text(data, "platform")));
            summary.AppendLine(Line("Idiomas (JS)", Text(data, "langs")));
            summary.AppendLine(Line("Zona horaria", Text(data, "tz")));
            summary.AppendLine(Line("Red segun cliente", Fallback(Text(data, "net"), "-")));

            object hintsRaw;
            if (data.TryGetValue("hints", out hintsRaw))
            {
                Dictionary<string, object> hints = hintsRaw as Dictionary<string, object>;
                if (hints != null)
                {
                    summary.AppendLine(Line("High entropy hints",
                        "modelo=" + Text(hints, "model") +
                        "  plataforma=" + Text(hints, "platformVersion") +
                        "  arq=" + Text(hints, "architecture") + " " + Text(hints, "bitness")));
                }
            }

            ClientRegistry.SetProbe(clientIp, gpu, screen, summary.ToString().TrimEnd());
            return true;
        }

        private static string Line(string label, string value)
        {
            return label.PadRight(19) + ": " + value;
        }

        private static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null) return "";
            return Convert.ToString(value);
        }

        private static string Fallback(string value, string ifEmpty)
        {
            return string.IsNullOrEmpty(value) ? ifEmpty : value;
        }
    }
}
