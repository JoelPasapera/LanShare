using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LanShare.Net;

namespace LanShare.Tests
{
    /// <summary>
    /// La lista de redes de Windows, de verdad y solo leyendo: no cambia la
    /// categoria de ninguna red. Comprueba que las posiciones de la tabla de
    /// metodos COM son las buenas contrastando lo leido con lo que da .NET por
    /// su cuenta (los identificadores de los adaptadores).
    /// </summary>
    public static class NetworkListManagerTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Net / lista de redes de Windows (solo lectura)");

            runner.Test("CLAVE: cada conexion apunta a un adaptador que existe", delegate
            {
                List<NetworkConnectionInfo> connections = NetworkListManager.GetConnections();

                HashSet<Guid> adapters = new HashSet<Guid>();
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    Guid id;
                    if (Guid.TryParse(adapter.Id, out id)) adapters.Add(id);
                }

                foreach (NetworkConnectionInfo connection in connections)
                {
                    Check.True(adapters.Contains(connection.AdapterId),
                               "adaptador " + connection.AdapterId + " de la red " + connection.NetworkName);
                    Check.NotEqual(Guid.Empty, connection.NetworkId, "identificador de la red");
                }
            });

            runner.Test("con un adaptador conectado al router hay alguna conexion", delegate
            {
                if (!AnyAdapterWithGateway()) return;
                Check.True(NetworkListManager.GetConnections().Count > 0, "al menos una conexion");
            });

            runner.Test("una red que no existe no se puede cambiar, y no rompe nada", delegate
            {
                string error = NetworkListManager.MakePrivate(Guid.NewGuid());
                Check.NotNull(error, "explica el motivo");
            });
        }

        private static bool AnyAdapterWithGateway()
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (GatewayIPAddressInformation gateway in adapter.GetIPProperties().GatewayAddresses)
                {
                    if (gateway.Address != null && gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
