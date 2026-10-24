using System.Net.NetworkInformation;

namespace LanShare.Net
{
    /// <summary>
    /// Una direccion IPv4 del equipo y lo que se sabe del adaptador que la
    /// tiene: lo justo para decidir cual ofrecer primero al movil.
    /// </summary>
    public sealed class LanAddress
    {
        public string Address { get; private set; }

        /// <summary>Nombre de la conexion en Windows: "Wi-Fi", "vEthernet (WSL)".</summary>
        public string AdapterName { get; private set; }

        /// <summary>Descripcion del controlador: "Hyper-V Virtual Ethernet Adapter".</summary>
        public string Description { get; private set; }

        public NetworkInterfaceType Type { get; private set; }

        /// <summary>Si el adaptador tiene puerta de enlace IPv4: el que llega al router.</summary>
        public bool HasGateway { get; private set; }

        public LanAddress(string address, string adapterName, string description,
                          NetworkInterfaceType type, bool hasGateway)
        {
            Address = address ?? "";
            AdapterName = adapterName ?? "";
            Description = description ?? "";
            Type = type;
            HasGateway = hasGateway;
        }

        public override string ToString()
        {
            return AdapterName.Length > 0 ? Address + " (" + AdapterName + ")" : Address;
        }
    }
}
