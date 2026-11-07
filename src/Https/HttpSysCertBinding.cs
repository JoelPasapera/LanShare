using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace LanShare.Https
{
    /// <summary>Una asociacion de certificado en http.sys: su puerto, de que aplicacion es y que certificado usa.</summary>
    public sealed class SslBinding
    {
        public SslBinding(int port, Guid appId, byte[] certificateHash)
        {
            Port = port;
            AppId = appId;
            CertificateHash = certificateHash ?? new byte[0];
        }

        /// <summary>Puerto de la asociacion; 0 si la direccion no es IPv4 ni IPv6.</summary>
        public int Port { get; private set; }
        public Guid AppId { get; private set; }
        public byte[] CertificateHash { get; private set; }
    }

    /// <summary>
    /// Asocia el certificado del servidor a un puerto en http.sys, que es
    /// quien hace el TLS para HttpListener. Es lo mismo que
    /// "netsh http add sslcert", pero con la API de Windows directamente: sin
    /// lanzar procesos ni leer una salida que cambia con el idioma, y con
    /// codigos de error de verdad.
    ///
    /// Cada puerto lleva dos asociaciones: 0.0.0.0 para IPv4 y [::] para IPv6
    /// (Chrome resuelve localhost primero a ::1). Todas llevan el
    /// identificador de aplicacion de LanShare: asi se reconocen las propias
    /// y nunca se toca la de otro programa (IIS Express, por ejemplo).
    /// Cambiarlas exige administrador; consultarlas no.
    /// </summary>
    public static class HttpSysCertBinding
    {
        /// <summary>Identificador de aplicacion de LanShare en http.sys. No debe cambiar nunca.</summary>
        public static readonly Guid AppId = new Guid("3f6c9a1e-52b7-4d08-9e4a-7c1d2b8f6a35");

        /// <summary>Almacen del certificado, en el ambito del equipo.</summary>
        private const string CertificateStoreName = "MY";

        private const uint HttpInitializeConfig = 0x00000002;
        private const int HttpServiceConfigSslCertInfo = 1;
        private const int HttpServiceConfigQueryExact = 0;
        private const int HttpServiceConfigQueryNext = 1;

        /// <summary>Tope del recorrido, por si http.sys nunca dijera que se acabo la lista.</summary>
        private const uint MaxListedBindings = 65536;

        /// <summary>
        /// Desactiva HTTP/2 en la asociacion. HttpListener esta escrito para
        /// HTTP/1.1 (fragmenta a mano las respuestas sin longitud, y los
        /// WebSocket van por Upgrade): con HTTP/1.1 por HTTPS todo funciona
        /// exactamente igual que por HTTP. Las versiones antiguas de Windows
        /// no conocen la marca; entonces se asocia sin ella.
        /// </summary>
        private const uint SslFlagDisableHttp2 = 0x00000010;

        private const uint NoError = 0;
        private const uint ErrorFileNotFound = 2;
        private const uint ErrorAccessDenied = 5;
        private const uint ErrorInvalidParameter = 87;
        private const uint ErrorInsufficientBuffer = 122;
        private const uint ErrorNoSuchLogonSession = 1312;

        private const short AddressFamilyIPv4 = 2;
        private const short AddressFamilyIPv6 = 23;

        // ------------------------------------------------------------------
        // Estructuras de http.h. Solo tipos de tamano fijo y punteros, para
        // que se copien tal cual: las cadenas y los bytes se reservan aparte.
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct HttpApiVersion
        {
            public ushort Major;
            public ushort Minor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HttpServiceConfigSslKey
        {
            public IntPtr IpPort;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HttpServiceConfigSslParam
        {
            public int SslHashLength;
            public IntPtr SslHash;
            public Guid AppId;
            public IntPtr SslCertStoreName;
            public uint DefaultCertCheckMode;
            public uint DefaultRevocationFreshnessTime;
            public uint DefaultRevocationUrlRetrievalTimeout;
            public IntPtr DefaultSslCtlIdentifier;
            public IntPtr DefaultSslCtlStoreName;
            public uint DefaultFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HttpServiceConfigSslSet
        {
            public HttpServiceConfigSslKey KeyDesc;
            public HttpServiceConfigSslParam ParamDesc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HttpServiceConfigSslQuery
        {
            public int QueryDesc;
            public HttpServiceConfigSslKey KeyDesc;
            public uint Token;
        }

        [DllImport("httpapi.dll")]
        private static extern uint HttpInitialize(HttpApiVersion version, uint flags, IntPtr reserved);

        [DllImport("httpapi.dll")]
        private static extern uint HttpTerminate(uint flags, IntPtr reserved);

        [DllImport("httpapi.dll")]
        private static extern uint HttpSetServiceConfiguration(IntPtr serviceHandle, int configId,
                                                               IntPtr configInformation, int configInformationLength,
                                                               IntPtr overlapped);

        [DllImport("httpapi.dll")]
        private static extern uint HttpDeleteServiceConfiguration(IntPtr serviceHandle, int configId,
                                                                  IntPtr configInformation, int configInformationLength,
                                                                  IntPtr overlapped);

        [DllImport("httpapi.dll")]
        private static extern uint HttpQueryServiceConfiguration(IntPtr serviceHandle, int configId,
                                                                 IntPtr inputConfigInformation, int inputConfigInformationLength,
                                                                 IntPtr outputConfigInformation, int outputConfigInformationLength,
                                                                 out int returnLength, IntPtr overlapped);

        /// <summary>
        /// Tamano de la estructura que se pasa a http.sys: 88 bytes en un
        /// proceso de 64 bits y 56 en uno de 32. Lo comprueban las pruebas,
        /// porque un desajuste aqui da ERROR_INVALID_PARAMETER sin mas pista.
        /// </summary>
        public static int SslSetSize
        {
            get { return Marshal.SizeOf(typeof(HttpServiceConfigSslSet)); }
        }

        public static int SslQuerySize
        {
            get { return Marshal.SizeOf(typeof(HttpServiceConfigSslQuery)); }
        }

        // ------------------------------------------------------------------
        // Operaciones
        // ------------------------------------------------------------------

        /// <summary>
        /// Si el puerto ya tiene asociado este certificado, por LanShare, en
        /// IPv4. Es lo que se consulta sin administrador para saber si HTTPS
        /// puede funcionar sin cambiar nada.
        /// </summary>
        public static bool IsBound(int port, byte[] certificateHash)
        {
            SslBinding current;
            if (Query(new IPEndPoint(IPAddress.Any, port), out current) != NoError) return false;
            return current.AppId == AppId &&
                   CertificateExtensionData.BytesEqual(current.CertificateHash, certificateHash);
        }

        /// <summary>
        /// Deja el puerto asociado al certificado. Si ya tenia otro de
        /// LanShare (uno anterior a una renovacion), se sustituye; si es de
        /// otro programa, no se toca y se explica. Lo que ya esta bien no se
        /// rehace, asi que se puede llamar en cada arranque.
        ///
        /// IPv4 es imprescindible: es por donde llega el movil. IPv6 se
        /// intenta tambien (https://localhost en Chrome va primero a ::1), pero
        /// si falla el puerto sirve igual y el motivo vuelve en
        /// <paramref name="warning"/>.
        /// </summary>
        /// <returns>null si quedo asociado; si no, el motivo en castellano.</returns>
        public static string Ensure(int port, byte[] certificateHash, out string warning)
        {
            if (certificateHash == null || certificateHash.Length == 0) throw new ArgumentException("certificateHash");
            warning = null;

            string error = EnsureEndpoint(new IPEndPoint(IPAddress.Any, port), certificateHash);
            if (error != null) return error;

            string ipv6 = EnsureEndpoint(new IPEndPoint(IPAddress.IPv6Any, port), certificateHash);
            if (ipv6 != null) warning = "sin HTTPS por IPv6 en el puerto " + port + ": " + ipv6;
            return null;
        }

        private static string EnsureEndpoint(IPEndPoint endpoint, byte[] certificateHash)
        {
            SslBinding current;
            uint queried = Query(endpoint, out current);

            if (queried == NoError)
            {
                if (current.AppId != AppId)
                {
                    return "otro programa ya tiene un certificado asociado a ese puerto (" + Describe(endpoint) +
                           "). Elige otro puerto HTTPS para el sitio.";
                }
                if (CertificateExtensionData.BytesEqual(current.CertificateHash, certificateHash)) return null;

                uint deleted = Delete(endpoint);
                if (deleted != NoError && deleted != ErrorFileNotFound) return Explain(deleted);
            }
            else if (queried != ErrorFileNotFound)
            {
                return Explain(queried);
            }

            uint set = Set(endpoint, certificateHash, SslFlagDisableHttp2);
            if (set == ErrorInvalidParameter) set = Set(endpoint, certificateHash, 0);
            return set == NoError ? null : Explain(set);
        }

        /// <summary>Quita las asociaciones de LanShare de un puerto. Las de otros programas no se tocan.</summary>
        /// <returns>null si quedo libre; si no, el motivo.</returns>
        public static string Remove(int port)
        {
            foreach (IPEndPoint endpoint in Endpoints(port))
            {
                SslBinding current;
                uint queried = Query(endpoint, out current);
                if (queried == ErrorFileNotFound) continue;
                if (queried != NoError) return Explain(queried);
                if (current.AppId != AppId) continue;

                uint deleted = Delete(endpoint);
                if (deleted != NoError && deleted != ErrorFileNotFound) return Explain(deleted);
            }
            return null;
        }

        /// <summary>
        /// Puertos que tienen alguna asociacion de LanShare, recorriendo toda
        /// la configuracion de http.sys. Asi se limpian tambien los que dejo
        /// un sitio borrado o una version anterior.
        /// </summary>
        public static List<int> FindOurPorts()
        {
            List<int> ports = new List<int>();
            foreach (SslBinding binding in ListAll())
            {
                if (binding.AppId == AppId && binding.Port > 0 && !ports.Contains(binding.Port)) ports.Add(binding.Port);
            }
            return ports;
        }

        /// <summary>
        /// Todas las asociaciones por direccion de http.sys, sean de quien
        /// sean. Solo lectura: no necesita administrador. Si la API no
        /// responde, la lista sale vacia.
        /// </summary>
        public static List<SslBinding> ListAll()
        {
            List<SslBinding> all = new List<SslBinding>();

            WithHttpApi(delegate
            {
                for (uint token = 0; token < MaxListedBindings; token++)
                {
                    HttpServiceConfigSslQuery query = new HttpServiceConfigSslQuery();
                    query.QueryDesc = HttpServiceConfigQueryNext;
                    query.Token = token;

                    SslBinding binding;
                    uint result = QueryRaw(query, out binding);
                    if (result != NoError) break;   // ERROR_NO_MORE_ITEMS al final de la lista
                    all.Add(binding);
                }
                return NoError;
            });

            return all;
        }

        // ------------------------------------------------------------------
        // Llamadas a la API
        // ------------------------------------------------------------------

        private static IPEndPoint[] Endpoints(int port)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            return new IPEndPoint[] { new IPEndPoint(IPAddress.Any, port), new IPEndPoint(IPAddress.IPv6Any, port) };
        }

        private static uint Query(IPEndPoint endpoint, out SslBinding binding)
        {
            SslBinding found = null;
            uint result = WithSockaddr(endpoint, delegate(IntPtr sockaddr)
            {
                return WithHttpApi(delegate
                {
                    HttpServiceConfigSslQuery query = new HttpServiceConfigSslQuery();
                    query.QueryDesc = HttpServiceConfigQueryExact;
                    query.KeyDesc.IpPort = sockaddr;

                    return QueryRaw(query, out found);
                });
            });

            binding = found;
            return result;
        }

        /// <summary>
        /// Consulta en dos pasos: la primera llamada dice cuanto ocupa la
        /// respuesta (que incluye el certificado y las cadenas a continuacion
        /// de la estructura) y la segunda la rellena.
        /// </summary>
        private static uint QueryRaw(HttpServiceConfigSslQuery query, out SslBinding binding)
        {
            binding = null;

            int inputSize = Marshal.SizeOf(typeof(HttpServiceConfigSslQuery));
            IntPtr input = Marshal.AllocHGlobal(inputSize);
            IntPtr output = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(query, input, false);

                // Sin hueco para la respuesta, una consulta que encuentra algo
                // contesta ERROR_INSUFFICIENT_BUFFER con el tamano necesario.
                // Cualquier otra cosa es "no hay" (ERROR_FILE_NOT_FOUND, o
                // ERROR_NO_MORE_ITEMS al final de un recorrido) o un fallo.
                int needed;
                uint result = HttpQueryServiceConfiguration(IntPtr.Zero, HttpServiceConfigSslCertInfo, input, inputSize,
                                                            IntPtr.Zero, 0, out needed, IntPtr.Zero);
                if (result != ErrorInsufficientBuffer) return result == NoError ? ErrorFileNotFound : result;

                output = Marshal.AllocHGlobal(needed);
                result = HttpQueryServiceConfiguration(IntPtr.Zero, HttpServiceConfigSslCertInfo, input, inputSize,
                                                       output, needed, out needed, IntPtr.Zero);
                if (result != NoError) return result;

                HttpServiceConfigSslSet set =
                    (HttpServiceConfigSslSet)Marshal.PtrToStructure(output, typeof(HttpServiceConfigSslSet));

                byte[] hash = new byte[Math.Max(0, set.ParamDesc.SslHashLength)];
                if (hash.Length > 0 && set.ParamDesc.SslHash != IntPtr.Zero)
                {
                    Marshal.Copy(set.ParamDesc.SslHash, hash, 0, hash.Length);
                }

                binding = new SslBinding(ReadPort(set.KeyDesc.IpPort), set.ParamDesc.AppId, hash);
                return NoError;
            }
            finally
            {
                Marshal.FreeHGlobal(input);
                if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            }
        }

        private static uint Set(IPEndPoint endpoint, byte[] certificateHash, uint flags)
        {
            return WithSockaddr(endpoint, delegate(IntPtr sockaddr)
            {
                GCHandle hash = GCHandle.Alloc(certificateHash, GCHandleType.Pinned);
                IntPtr storeName = Marshal.StringToHGlobalUni(CertificateStoreName);
                int size = Marshal.SizeOf(typeof(HttpServiceConfigSslSet));
                IntPtr config = Marshal.AllocHGlobal(size);
                try
                {
                    HttpServiceConfigSslSet set = new HttpServiceConfigSslSet();
                    set.KeyDesc.IpPort = sockaddr;
                    set.ParamDesc.SslHashLength = certificateHash.Length;
                    set.ParamDesc.SslHash = hash.AddrOfPinnedObject();
                    set.ParamDesc.AppId = AppId;
                    set.ParamDesc.SslCertStoreName = storeName;
                    set.ParamDesc.DefaultFlags = flags;
                    Marshal.StructureToPtr(set, config, false);

                    return WithHttpApi(delegate
                    {
                        return HttpSetServiceConfiguration(IntPtr.Zero, HttpServiceConfigSslCertInfo,
                                                           config, size, IntPtr.Zero);
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(config);
                    Marshal.FreeHGlobal(storeName);
                    hash.Free();
                }
            });
        }

        private static uint Delete(IPEndPoint endpoint)
        {
            return WithSockaddr(endpoint, delegate(IntPtr sockaddr)
            {
                int size = Marshal.SizeOf(typeof(HttpServiceConfigSslSet));
                IntPtr config = Marshal.AllocHGlobal(size);
                try
                {
                    HttpServiceConfigSslSet set = new HttpServiceConfigSslSet();
                    set.KeyDesc.IpPort = sockaddr;
                    Marshal.StructureToPtr(set, config, false);

                    return WithHttpApi(delegate
                    {
                        return HttpDeleteServiceConfiguration(IntPtr.Zero, HttpServiceConfigSslCertInfo,
                                                              config, size, IntPtr.Zero);
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(config);
                }
            });
        }

        // ------------------------------------------------------------------
        // Utilidades
        // ------------------------------------------------------------------

        private delegate uint ApiCall();
        private delegate uint SockaddrCall(IntPtr sockaddr);

        /// <summary>Inicializa la API en modo configuracion durante la llamada y la cierra despues.</summary>
        private static uint WithHttpApi(ApiCall call)
        {
            HttpApiVersion version = new HttpApiVersion();
            version.Major = 1;
            version.Minor = 0;

            uint initialized = HttpInitialize(version, HttpInitializeConfig, IntPtr.Zero);
            if (initialized != NoError) return initialized;
            try
            {
                return call();
            }
            finally
            {
                HttpTerminate(HttpInitializeConfig, IntPtr.Zero);
            }
        }

        /// <summary>
        /// La direccion en el formato sockaddr de Windows. SocketAddress de
        /// .NET ya la serializa asi (familia, puerto en orden de red, direccion):
        /// solo hay que copiarla a memoria no gestionada.
        /// </summary>
        private static uint WithSockaddr(IPEndPoint endpoint, SockaddrCall call)
        {
            SocketAddress serialized = endpoint.Serialize();
            IntPtr sockaddr = Marshal.AllocHGlobal(serialized.Size);
            try
            {
                for (int i = 0; i < serialized.Size; i++) Marshal.WriteByte(sockaddr, i, serialized[i]);
                return call(sockaddr);
            }
            finally
            {
                Marshal.FreeHGlobal(sockaddr);
            }
        }

        /// <summary>Puerto de un sockaddr IPv4 o IPv6: dos bytes en orden de red tras la familia.</summary>
        private static int ReadPort(IntPtr sockaddr)
        {
            if (sockaddr == IntPtr.Zero) return 0;
            short family = Marshal.ReadInt16(sockaddr, 0);
            if (family != AddressFamilyIPv4 && family != AddressFamilyIPv6) return 0;
            return (Marshal.ReadByte(sockaddr, 2) << 8) | Marshal.ReadByte(sockaddr, 3);
        }

        private static string Describe(IPEndPoint endpoint)
        {
            return endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? "[::]:" + endpoint.Port
                : "0.0.0.0:" + endpoint.Port;
        }

        private static string Explain(uint error)
        {
            switch (error)
            {
                case ErrorAccessDenied:
                    return "hace falta administrador para asociar el certificado en http.sys.";
                case ErrorNoSuchLogonSession:
                    return "Windows no encuentra la clave privada del certificado del servidor.";
                default:
                    return new Win32Exception((int)error).Message + " (error " + error + " de http.sys).";
            }
        }
    }
}
