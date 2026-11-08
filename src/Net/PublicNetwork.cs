using System;
using System.Collections.Generic;

namespace LanShare.Net
{
    /// <summary>
    /// Una red que Windows tiene como publica y por la que deberia llegar el
    /// movil: su nombre, su identificador (para cambiarla) y las direcciones
    /// del equipo en ella.
    /// </summary>
    public sealed class PublicNetwork
    {
        private readonly List<string> _addresses = new List<string>();

        public PublicNetwork(Guid networkId, string name)
        {
            NetworkId = networkId;
            Name = name ?? "";
        }

        public Guid NetworkId { get; private set; }

        /// <summary>Nombre que le da Windows; suele ser el de la Wi-Fi.</summary>
        public string Name { get; private set; }

        /// <summary>Direcciones IPv4 del equipo en esta red, sin repetir.</summary>
        public IList<string> Addresses
        {
            get { return _addresses.AsReadOnly(); }
        }

        internal void AddAddress(string address)
        {
            if (!string.IsNullOrEmpty(address) && !_addresses.Contains(address)) _addresses.Add(address);
        }

        /// <summary>"Casa" (192.168.1.5), para los avisos.</summary>
        public string Describe()
        {
            string name = Name.Length > 0 ? "\"" + Name + "\"" : "sin nombre";
            return _addresses.Count > 0
                ? name + " (" + string.Join(", ", _addresses.ToArray()) + ")"
                : name;
        }
    }
}
