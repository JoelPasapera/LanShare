using System;

namespace LanShare.Ui
{
    /// <summary>
    /// Un aviso sobre la red que se muestra junto al codigo QR, con lo que lo
    /// arregla: el dialogo del QR no sabe de redes ni de firewall, solo
    /// ensena el texto y llama al arreglo cuando se pulsa el boton.
    /// </summary>
    public sealed class NetworkNotice
    {
        public NetworkNotice(string text, string fixLabel, Func<string> fix)
        {
            if (fix == null) throw new ArgumentNullException("fix");
            Text = text ?? "";
            FixLabel = string.IsNullOrEmpty(fixLabel) ? "Arreglar" : fixLabel;
            Fix = fix;
        }

        public string Text { get; private set; }

        /// <summary>Texto del boton ("Pasar a privada").</summary>
        public string FixLabel { get; private set; }

        /// <summary>Intenta arreglarlo: null si se pudo; si no, el motivo para el usuario.</summary>
        public Func<string> Fix { get; private set; }
    }
}
