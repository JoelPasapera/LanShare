using System;

namespace LanShare.Net
{
    /// <summary>
    /// Una conexion de red activa segun Windows: por que adaptador va, a que
    /// red pertenece y como esta clasificada esa red.
    /// </summary>
    public sealed class NetworkConnectionInfo
    {
        public NetworkConnectionInfo(Guid adapterId, Guid networkId, string networkName, NetworkCategory category)
        {
            AdapterId = adapterId;
            NetworkId = networkId;
            NetworkName = networkName ?? "";
            Category = category;
        }

        /// <summary>El adaptador, con el mismo identificador que NetworkInterface.Id.</summary>
        public Guid AdapterId { get; private set; }

        /// <summary>La red: varias conexiones pueden compartirla.</summary>
        public Guid NetworkId { get; private set; }

        /// <summary>Nombre de la red tal como lo muestra Windows ("Casa", "Red 3").</summary>
        public string NetworkName { get; private set; }

        public NetworkCategory Category { get; private set; }
    }
}
