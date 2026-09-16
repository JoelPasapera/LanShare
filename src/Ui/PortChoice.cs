using System;

namespace LanShare.Ui
{
    /// <summary>
    /// Entrada del desplegable de filtrado: el texto que se ve y el puerto que
    /// representa. Cero significa todos los sitios.
    /// </summary>
    public sealed class PortChoice
    {
        public int Port { get; private set; }
        public string Caption { get; private set; }

        public PortChoice(int port, string caption)
        {
            Port = port;
            Caption = caption;
        }

        public override string ToString()
        {
            return Caption;
        }
    }
}
