using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace LanShare.Core
{
    /// <summary>
    /// Configuracion persistente entre sesiones.
    ///
    /// Se guarda en %APPDATA%, no junto al ejecutable: asi funciona igual si el
    /// .exe vive en Archivos de programa o en una unidad de solo lectura.
    /// </summary>
    public sealed class AppSettings
    {
        private const int MaxRecentRoots = 8;

        // Campos publicos: JavaScriptSerializer los serializa directamente y
        // evita tener que arrastrar una dependencia externa solo para esto.
        /// <summary>Sitios servidos. Cada uno con su puerto y su carpeta.</summary>
        public List<SiteDefinition> Sites = new List<SiteDefinition>();

        // Campos de la version de un solo sitio. Se conservan solo para poder
        // migrar una configuracion antigua; ya no se escriben.
        public string RootPath = "";
        public int Port = 8080;
        public bool LanMode = false;

        public bool Cors = true;
        public bool Compression = true;
        public bool DirectoryListing = true;
        public bool DistributionMode = false;
        /// <summary>Alto de la tabla de clientes, sobre su detalle (antes, la posicion del divisor).</summary>
        public int SplitterDistance = 170;

        /// <summary>Alto elegido para la tabla de sitios; 0 deja el de fabrica.</summary>
        public int SitesPaneHeight = 0;

        /// <summary>Alto elegido para el cuadro de estado; 0 deja el de fabrica.</summary>
        public int StatusPaneHeight = 0;

        /// <summary>
        /// Si LanShare llego a preparar HTTPS alguna vez en este equipo. Solo
        /// entonces se revisan, al arrancar, las asociaciones de http.sys que
        /// hayan quedado sin uso: quien nunca usa HTTPS no toca nada de eso.
        /// </summary>
        public bool HttpsPrepared = false;

        public int WindowWidth = 0;
        public int WindowHeight = 0;
        public List<string> RecentRoots = new List<string>();

        /// <summary>Clave (MAC o IP) al nombre que el usuario le puso al dispositivo.</summary>
        public Dictionary<string, string> Aliases = new Dictionary<string, string>();

        /// <summary>
        /// Puertos con regla de firewall que quedaron creadas al cerrar Windows
        /// la sesion con el servidor en marcha. El siguiente arranque borra las
        /// que ya no correspondan a ningun sitio.
        /// </summary>
        public List<int> FirewallPorts = new List<int>();

        // --- Desarrollo ---

        /// <summary>Recarga las paginas abiertas al guardar un archivo del sitio.</summary>
        public bool LiveReload = false;

        /// <summary>
        /// Muestra en el registro los errores y mensajes de consola de las
        /// paginas abiertas en otros dispositivos.
        /// </summary>
        public bool RemoteConsole = false;

        /// <summary>
        /// Opcion global de aislamiento entre origenes de una version anterior.
        /// Ahora es de cada sitio (SiteDefinition.CrossOriginIsolation): solo
        /// se conserva para migrarla al leer y ya no se usa.
        /// </summary>
        public bool CrossOriginIsolation = false;

        // --- Subida de archivos ---
        // Apagada de serie: es la unica funcion que escribe en disco desde la red.

        public bool UploadEnabled = false;

        /// <summary>Vacio significa la carpeta por defecto (Descargas\LanShare).</summary>
        public string UploadFolder = "";

        public int UploadMaxMegabytes = 2048;

        // --- Identificacion de clientes ---

        /// <summary>Inyecta el script que mide GPU y pantalla en las paginas HTML.</summary>
        public bool InjectProbe = true;

        /// <summary>Abre conexiones TCP a 21 puertos del cliente para reconocer el sistema.</summary>
        public bool PortScan = true;

        /// <summary>Pregunta el nombre del equipo por mDNS y NetBIOS.</summary>
        public bool DeepName = true;

        // --- Ventana ---

        public bool MinimizeToTray = false;

        public const int MinUploadMegabytes = 1;
        public const int MaxUploadMegabytes = 102400;

        /// <summary>
        /// Carpeta donde se guardan las subidas. Es un metodo y no una
        /// propiedad para que el serializador no la escriba en el JSON.
        /// </summary>
        public string GetUploadFolder()
        {
            if (!string.IsNullOrEmpty(UploadFolder)) return UploadFolder;
            return DefaultUploadFolder();
        }

        public static string DefaultUploadFolder()
        {
            return Path.Combine(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                "LanShare");
        }

        public static string FilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LanShare");
                return Path.Combine(folder, "settings.json");
            }
        }

        /// <summary>
        /// Lee la configuracion. Si el archivo principal esta danado se recurre
        /// a la copia de la sesion anterior antes que a los valores de fabrica:
        /// perder la lista de sitios por un archivo corrupto era lo peor posible.
        /// </summary>
        public static AppSettings Load()
        {
            return LoadFrom(FilePath);
        }

        /// <summary>Como Load, pero desde una ruta concreta. Lo usan las pruebas.</summary>
        public static AppSettings LoadFrom(string path)
        {
            AppSettings loaded = TryLoad(path);
            if (loaded == null) loaded = TryLoad(path + ".bak");
            return loaded ?? new AppSettings();
        }

        private static AppSettings TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppSettings loaded = serializer.Deserialize<AppSettings>(json);
                if (loaded == null) return null;

                // Un archivo editado a mano puede traer nulos donde se esperan
                // colecciones; normalizarlo aqui evita fallos mas adelante.
                if (loaded.RecentRoots == null) loaded.RecentRoots = new List<string>();
                if (loaded.Aliases == null) loaded.Aliases = new Dictionary<string, string>();
                if (loaded.Sites == null) loaded.Sites = new List<SiteDefinition>();

                if (loaded.UploadFolder == null) loaded.UploadFolder = "";
                if (loaded.UploadMaxMegabytes < MinUploadMegabytes || loaded.UploadMaxMegabytes > MaxUploadMegabytes)
                    loaded.UploadMaxMegabytes = 2048;

                loaded.MigrateSingleSite();
                loaded.MigrateGlobalIsolation();
                return loaded;
            }
            catch (Exception)
            {
                // Una configuracion corrupta no debe impedir arrancar.
                return null;
            }
        }

        public void Save()
        {
            SaveTo(FilePath);
        }

        /// <summary>Como Save, pero a una ruta concreta. Lo usan las pruebas.</summary>
        /// <returns>false si no se pudo guardar; la version anterior queda intacta.</returns>
        public bool SaveTo(string path)
        {
            string temp = path + ".tmp";
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);

                // Escritura atomica: primero a un temporal y despues se sustituye.
                // Escribir encima directamente dejaba un archivo truncado si el
                // equipo se apagaba a mitad, y la siguiente sesion arrancaba
                // con la configuracion de fabrica. File.Replace ademas conserva
                // la version anterior como .bak, que Load usa de respaldo.
                File.WriteAllText(temp, json, Encoding.UTF8);

                // Un antivirus o el indizador de Windows pueden tener el archivo
                // abierto justo en ese instante: se reintenta antes de rendirse.
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                        else File.Move(temp, path);
                        return true;
                    }
                    catch (IOException)
                    {
                        if (attempt >= 3) throw;
                        Thread.Sleep(60 * attempt);
                    }
                }
            }
            catch (Exception)
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception) { }
                return false;
            }
        }

        /// <summary>
        /// Una configuracion de la version de un solo sitio no trae lista de
        /// sitios. En vez de perderla, se convierte en el primer sitio.
        /// </summary>
        private void MigrateSingleSite()
        {
            if (Sites.Count > 0) return;
            if (string.IsNullOrEmpty(RootPath)) return;

            SiteDefinition site = new SiteDefinition();
            site.Enabled = true;
            site.RootPath = RootPath;
            site.Port = (Port >= 1 && Port <= 65535) ? Port : 8080;
            site.LanMode = LanMode;
            Sites.Add(site);

            RootPath = "";
        }

        /// <summary>
        /// Quien activo el aislamiento cuando era global lo tenia en todos sus
        /// sitios: se le conserva asi, ahora sitio a sitio.
        /// </summary>
        private void MigrateGlobalIsolation()
        {
            if (!CrossOriginIsolation) return;

            foreach (SiteDefinition site in Sites)
            {
                if (site != null) site.CrossOriginIsolation = true;
            }
            CrossOriginIsolation = false;
        }

        /// <summary>Sube la carpeta al principio del historial, sin duplicados.</summary>
        public void RememberRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return;

            for (int i = RecentRoots.Count - 1; i >= 0; i--)
            {
                if (string.Equals(RecentRoots[i], root, StringComparison.OrdinalIgnoreCase))
                    RecentRoots.RemoveAt(i);
            }

            RecentRoots.Insert(0, root);
            while (RecentRoots.Count > MaxRecentRoots) RecentRoots.RemoveAt(RecentRoots.Count - 1);
        }
    }
}
