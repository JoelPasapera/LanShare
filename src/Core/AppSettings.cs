using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        public string RootPath = "";
        public int Port = 8080;
        public bool LanMode = false;
        public bool Cors = true;
        public bool Compression = true;
        public bool DirectoryListing = true;
        public bool DistributionMode = false;
        public int SplitterDistance = 170;
        public int WindowWidth = 0;
        public int WindowHeight = 0;
        public List<string> RecentRoots = new List<string>();

        /// <summary>Clave (MAC o IP) al nombre que el usuario le puso al dispositivo.</summary>
        public Dictionary<string, string> Aliases = new Dictionary<string, string>();

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

        public static AppSettings Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return new AppSettings();

                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppSettings loaded = serializer.Deserialize<AppSettings>(json);
                if (loaded == null) return new AppSettings();

                // Un archivo editado a mano puede traer nulos donde se esperan
                // colecciones; normalizarlo aqui evita fallos mas adelante.
                if (loaded.RecentRoots == null) loaded.RecentRoots = new List<string>();
                if (loaded.Aliases == null) loaded.Aliases = new Dictionary<string, string>();
                if (loaded.Port < 1 || loaded.Port > 65535) loaded.Port = 8080;

                return loaded;
            }
            catch (Exception)
            {
                // Una configuracion corrupta no debe impedir arrancar.
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                string path = FilePath;
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                JavaScriptSerializer serializer = new JavaScriptSerializer();
                File.WriteAllText(path, serializer.Serialize(this), Encoding.UTF8);
            }
            catch (Exception) { }
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
