using System;
using System.Collections.Generic;

namespace LanShare.Core
{
    /// <summary>
    /// Configuracion de una sesion: los sitios que se sirven y el comportamiento
    /// comun a todos ellos.
    ///
    /// La lista de sitios se fija al arrancar; los cuatro interruptores se
    /// pueden cambiar con el servidor en marcha porque los hilos de peticion
    /// los leen en cada llamada.
    /// </summary>
    public sealed class ServerOptions
    {
        // Campos volatiles explicitos: una propiedad automatica no admite el
        // modificador, y estos los escribe la interfaz mientras el pool lee.
        private volatile bool _corsEnabled;
        private volatile bool _enableCompression;
        private volatile bool _enableDirectoryListing;
        private volatile bool _distributionMode;

        private readonly Dictionary<int, SiteRuntime> _byPort;
        private readonly List<SiteRuntime> _sites;

        public ServerOptions(IList<SiteRuntime> sites, bool corsEnabled)
        {
            if (sites == null || sites.Count == 0)
                throw new ArgumentException("Hace falta al menos un sitio.", "sites");

            _sites = new List<SiteRuntime>(sites);
            _byPort = new Dictionary<int, SiteRuntime>();

            foreach (SiteRuntime site in _sites)
            {
                if (_byPort.ContainsKey(site.Port))
                    throw new ArgumentException("Dos sitios comparten el puerto " + site.Port + ".");
                _byPort[site.Port] = site;
            }

            _corsEnabled = corsEnabled;
            _enableCompression = true;
            _enableDirectoryListing = true;
            _distributionMode = false;
        }

        public IList<SiteRuntime> Sites { get { return _sites; } }

        /// <summary>Resuelve que sitio atiende una peticion segun el puerto de entrada.</summary>
        public SiteRuntime FindByPort(int port)
        {
            SiteRuntime site;
            return _byPort.TryGetValue(port, out site) ? site : null;
        }

        public bool CorsEnabled
        {
            get { return _corsEnabled; }
            set { _corsEnabled = value; }
        }

        public bool EnableCompression
        {
            get { return _enableCompression; }
            set { _enableCompression = value; }
        }

        public bool EnableDirectoryListing
        {
            get { return _enableDirectoryListing; }
            set { _enableDirectoryListing = value; }
        }

        /// <summary>
        /// En desarrollo se envia no-store para que el navegador nunca sirva
        /// una version vieja. En distribucion se emiten validadores y se
        /// responde 304, que ahorra reenviar lo que el cliente ya tiene.
        /// </summary>
        public bool DistributionMode
        {
            get { return _distributionMode; }
            set { _distributionMode = value; }
        }
    }
}
