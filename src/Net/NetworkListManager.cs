using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LanShare.Net
{
    /// <summary>
    /// La lista de redes de Windows (Network List Manager, netprofm.dll), por
    /// COM: a que red va cada adaptador, si Windows la tiene como publica o
    /// privada y, con administrador, pasarla a privada. Es lo mismo que
    /// Configuracion, Red e Internet, tipo de perfil de red, y lo que mira el
    /// firewall para decidir que reglas aplica.
    ///
    /// El gestor se crea con su interfaz declarada (INetworkListManager). Lo
    /// que devuelve (enumerador, conexiones, redes) se usa tal cual llega,
    /// llamando por su tabla de metodos: es el mismo puntero del tipo que
    /// promete la firma, asi que no hace falta pedirle nada mas. Las
    /// posiciones salen de netlistmgr.h: todas estas interfaces son duales, y
    /// delante de sus metodos van los tres de IUnknown y los cuatro de IDispatch.
    /// </summary>
    public static class NetworkListManager
    {
        private static readonly Guid ManagerClassId = new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

        private const int Ok = 0;
        private const int AccessDenied = unchecked((int)0x80070005);

        /// <summary>Tope de conexiones que se leen, por si el enumerador nunca dijera que acabo.</summary>
        private const int MaxConnections = 256;

        // Posiciones en la tabla de metodos (0-2 IUnknown, 3-6 IDispatch).
        private const int EnumNextSlot = 8;                 // IEnumNetworkConnections: _NewEnum, Next
        private const int ConnectionGetNetworkSlot = 7;     // INetworkConnection: GetNetwork...
        private const int ConnectionGetAdapterIdSlot = 12;  // ...IsConnectedToInternet, IsConnected,
                                                            //    GetConnectivity, GetConnectionId, GetAdapterId
        private const int NetworkGetNameSlot = 7;           // INetwork: GetName, SetName, GetDescription,
        private const int NetworkGetNetworkIdSlot = 11;     //    SetDescription, GetNetworkId, GetDomainType,
        private const int NetworkGetCategorySlot = 18;      //    GetNetworkConnections, GetTimeCreatedAndConnected,
        private const int NetworkSetCategorySlot = 19;      //    IsConnectedToInternet, IsConnected,
                                                            //    GetConnectivity, GetCategory, SetCategory

        /// <summary>
        /// Las conexiones activas, cada una con su adaptador, su red y la
        /// categoria de esa red. Lanza si la lista de redes no responde; una
        /// conexion que no se puede leer se salta.
        /// </summary>
        public static List<NetworkConnectionInfo> GetConnections()
        {
            List<NetworkConnectionInfo> result = new List<NetworkConnectionInfo>();
            INetworkListManager manager = CreateManager();
            IntPtr enumerator = IntPtr.Zero;
            Exception firstError = null;

            try
            {
                int hr = manager.GetNetworkConnections(out enumerator);
                if (hr != Ok) Marshal.ThrowExceptionForHR(hr);
                if (enumerator == IntPtr.Zero)
                {
                    throw new InvalidOperationException("La lista de redes de Windows no devolvio sus conexiones.");
                }

                NextMethod next = Method<NextMethod>(enumerator, EnumNextSlot);
                for (int read = 0; read < MaxConnections; read++)
                {
                    IntPtr connection;
                    uint fetched = 0;
                    if (next(enumerator, 1, out connection, ref fetched) != Ok || fetched == 0) break;
                    if (connection == IntPtr.Zero) break;

                    try
                    {
                        NetworkConnectionInfo info = DescribeConnection(connection);
                        if (info != null) result.Add(info);
                    }
                    catch (Exception ex)
                    {
                        if (firstError == null) firstError = ex;
                    }
                    finally
                    {
                        Marshal.Release(connection);
                    }
                }
            }
            finally
            {
                if (enumerator != IntPtr.Zero) Marshal.Release(enumerator);
                Marshal.ReleaseComObject(manager);
            }

            // Si no se pudo leer ninguna, el fallo es del metodo, no de una
            // conexion rara: mejor decirlo que devolver una lista vacia.
            if (result.Count == 0 && firstError != null)
            {
                throw new InvalidOperationException(
                    "No se pudo leer ninguna conexion de la lista de redes de Windows: " + firstError.Message, firstError);
            }
            return result;
        }

        /// <summary>Pasa una red a privada. Hace falta administrador.</summary>
        /// <returns>null si se cambio; si no, el motivo, para el usuario.</returns>
        public static string MakePrivate(Guid networkId)
        {
            INetworkListManager manager;
            try
            {
                manager = CreateManager();
            }
            catch (Exception ex)
            {
                return "Windows no deja consultar sus redes (" + ex.Message + ").";
            }

            IntPtr network = IntPtr.Zero;
            try
            {
                int hr = manager.GetNetwork(networkId, out network);
                if (hr != Ok || network == IntPtr.Zero) return "Windows ya no tiene esa red (" + Explain(hr) + ").";

                hr = Method<SetInt32Method>(network, NetworkSetCategorySlot)(network, (int)NetworkCategory.Private);
                if (hr == Ok) return null;
                if (hr == AccessDenied) return "hace falta ejecutar LanShare como administrador.";
                return Explain(hr);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                if (network != IntPtr.Zero) Marshal.Release(network);
                Marshal.ReleaseComObject(manager);
            }
        }

        // ------------------------------------------------------------------

        private static INetworkListManager CreateManager()
        {
            Type type = Type.GetTypeFromCLSID(ManagerClassId, true);
            return (INetworkListManager)Activator.CreateInstance(type);
        }

        /// <summary>Adaptador, red y categoria de una conexion; null si algo no se pudo leer.</summary>
        private static NetworkConnectionInfo DescribeConnection(IntPtr connection)
        {
            Guid adapterId;
            if (Method<GetGuidMethod>(connection, ConnectionGetAdapterIdSlot)(connection, out adapterId) != Ok) return null;

            IntPtr network;
            if (Method<GetPointerMethod>(connection, ConnectionGetNetworkSlot)(connection, out network) != Ok) return null;
            if (network == IntPtr.Zero) return null;

            try
            {
                Guid networkId;
                if (Method<GetGuidMethod>(network, NetworkGetNetworkIdSlot)(network, out networkId) != Ok) return null;

                int rawCategory;
                if (Method<GetInt32Method>(network, NetworkGetCategorySlot)(network, out rawCategory) != Ok) return null;

                NetworkCategory category;
                if (!TryCategory(rawCategory, out category)) return null;

                return new NetworkConnectionInfo(adapterId, networkId, ReadName(network), category);
            }
            finally
            {
                Marshal.Release(network);
            }
        }

        /// <summary>Nombre de la red; vacio si no se puede leer.</summary>
        private static string ReadName(IntPtr network)
        {
            IntPtr text;
            if (Method<GetPointerMethod>(network, NetworkGetNameSlot)(network, out text) != Ok || text == IntPtr.Zero)
            {
                return "";
            }

            try
            {
                return Marshal.PtrToStringBSTR(text) ?? "";
            }
            finally
            {
                Marshal.FreeBSTR(text);
            }
        }

        /// <summary>Solo los tres valores de NLM_NETWORK_CATEGORY: otro numero seria un fallo de lectura.</summary>
        private static bool TryCategory(int value, out NetworkCategory category)
        {
            category = (NetworkCategory)value;
            return value == (int)NetworkCategory.Public ||
                   value == (int)NetworkCategory.Private ||
                   value == (int)NetworkCategory.Domain;
        }

        private static string Explain(int hr)
        {
            Exception error = hr != Ok ? Marshal.GetExceptionForHR(hr) : null;
            string text = error != null ? error.Message : "sin detalle";
            return text + " (0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>El metodo que ocupa una posicion de la tabla de un puntero COM, listo para llamarlo.</summary>
        private static T Method<T>(IntPtr comObject, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(comObject);
            IntPtr function = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(function, typeof(T)) as T;
        }

        // ------------------------------------------------------------------
        // Firmas de los metodos que se llaman. El primer argumento es siempre
        // el propio puntero, como en cualquier llamada COM.
        // ------------------------------------------------------------------

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NextMethod(IntPtr self, uint count, out IntPtr item, ref uint fetched);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPointerMethod(IntPtr self, out IntPtr value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetGuidMethod(IntPtr self, out Guid value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInt32Method(IntPtr self, out int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetInt32Method(IntPtr self, int value);

        /// <summary>
        /// Solo los tres primeros metodos: los demas no se usan y no hace falta
        /// declararlos. Devuelven punteros sin envolver, que se sueltan a mano.
        /// </summary>
        [ComImport]
        [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface INetworkListManager
        {
            [PreserveSig]
            int GetNetworks(int flags, out IntPtr enumNetworks);

            [PreserveSig]
            int GetNetwork(Guid networkId, out IntPtr network);

            [PreserveSig]
            int GetNetworkConnections(out IntPtr enumConnections);
        }
    }
}
