using System;

namespace LanShare.Identity
{
    /// <summary>
    /// Todo lo que se sabe de un cliente. Los contadores son campos publicos
    /// y no propiedades porque Interlocked exige poder tomarlos por referencia.
    /// </summary>
    public sealed class ClientInfo
    {
        /// <summary>
        /// Cerrojo del propio registro. El sondeo, la sonda JS y el bucle de
        /// mantenimiento pueden reevaluar el veredicto a la vez; sin esto el
        /// dictamen podria quedar emparejado con el detalle de otra evaluacion.
        /// </summary>
        public readonly object SyncRoot = new object();

        // --- Identidad de red ---
        public string Ip = "";
        public string HostName = "";        // DNS inverso
        public string MdnsName = "";        // nombre .local
        public string NbName = "";          // nombre NetBIOS
        public string Mac = "";
        public string Vendor = "";
        public bool MacRandomized;

        /// <summary>Nombre puesto a mano por el usuario. Manda sobre cualquier deduccion.</summary>
        public string Alias = "";

        // --- Huella de red ---
        public int Ttl = -1;
        public long RttMs = -1;
        public string OsGuess = "";
        public string OpenPorts = "";
        public string PortGuess = "";
        public string SsdpServer = "";
        public string Banners = "";

        // --- Capa HTTP ---
        public string UserAgent = "";
        public string AcceptLanguage = "";
        public string AcceptEncoding = "";
        public string Referer = "";
        public string Protocol = "";
        public bool KeepAlive;
        public int SourcePort;
        public string RawHeaders = "";
        public string HeaderSignature = "";
        public string ChUa = "";
        public string ChPlatform = "";
        public string ChMobile = "";
        public string ChModel = "";
        public string DeviceLabel = "Desconocido";
        public string BrowserLabel = "Desconocido";

        // --- Sonda JS ---
        public string Gpu = "";
        public string ScreenInfo = "";
        public string ProbeSummary = "";
        public long ProbeTicks;

        // --- Veredicto fusionado ---
        public string Verdict = "";
        public string VerdictDetail = "";
        public int VerdictScore;

        // --- Contadores (solo via Interlocked) ---
        public long RequestCount;
        public long BytesSent;
        public long FirstSeenTicks;
        public long LastSeenTicks;

        // --- Control del sondeo ---
        public int ResolveState;    // 0 pendiente, 1 en curso, 2 resuelto
        public long NextProbeTicks;
        public int ProbeRound;

        /// <summary>Nombre mas fiable disponible, en orden de confianza.</summary>
        /// <summary>
        /// Clave con la que se recuerda el alias. La MAC sobrevive a los
        /// cambios de IP por DHCP, asi que se prefiere cuando esta disponible.
        /// </summary>
        public string AliasKey
        {
            get { return Mac.Length > 0 ? Mac : Ip; }
        }

        public string BestName
        {
            get
            {
                if (Alias.Length > 0) return Alias;
                if (HostName.Length > 0) return HostName;
                if (MdnsName.Length > 0) return MdnsName;
                if (NbName.Length > 0) return NbName;
                return "";
            }
        }

        /// <summary>Le falta algun dato que justifique reintentar el sondeo.</summary>
        public bool IsIncomplete
        {
            get { return Mac.Length == 0 || BestName.Length == 0 || Ttl <= 0; }
        }

        public TimeSpan IdleTime
        {
            get { return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - LastSeenTicks); }
        }
    }
}
