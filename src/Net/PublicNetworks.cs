using System;
using System.Collections.Generic;

namespace LanShare.Net
{
    /// <summary>
    /// Que redes por las que llega el movil tiene Windows como publicas.
    ///
    /// La regla de firewall de LanShare solo abre las redes privadas y las de
    /// dominio: en una red publica el movil no conecta, y lo que se ve es
    /// que "el QR no funciona". Funcion pura: recibe las direcciones del
    /// equipo y las conexiones que da la lista de redes de Windows.
    /// </summary>
    public static class PublicNetworks
    {
        /// <summary>Las redes publicas que llevan alguna direccion que importa al movil, sin repetir.</summary>
        public static List<PublicNetwork> Find(IList<LanAddress> addresses, IList<NetworkConnectionInfo> connections)
        {
            List<PublicNetwork> found = new List<PublicNetwork>();
            if (addresses == null || connections == null) return found;

            foreach (LanAddress address in addresses)
            {
                if (address == null || !MattersToPhones(address)) continue;

                Guid adapterId;
                if (!Guid.TryParse(address.AdapterId, out adapterId)) continue;

                foreach (NetworkConnectionInfo connection in connections)
                {
                    if (connection == null || connection.AdapterId != adapterId) continue;
                    if (connection.Category != NetworkCategory.Public) continue;

                    PublicNetwork network = FindById(found, connection.NetworkId);
                    if (network == null)
                    {
                        network = new PublicNetwork(connection.NetworkId, connection.NetworkName);
                        found.Add(network);
                    }
                    network.AddAddress(address.Address);
                }
            }
            return found;
        }

        /// <summary>
        /// Las direcciones por las que puede entrar un movil. Las de una VPN no:
        /// Windows suele dejar su red como publica y eso no afecta al movil.
        /// </summary>
        private static bool MattersToPhones(LanAddress address)
        {
            return LanAddressRanking.IsReachableFromLan(address) &&
                   !LanAddressRanking.LooksLikeVpn(address.AdapterName, address.Description);
        }

        private static PublicNetwork FindById(List<PublicNetwork> networks, Guid networkId)
        {
            foreach (PublicNetwork network in networks)
            {
                if (network.NetworkId == networkId) return network;
            }
            return null;
        }
    }
}
