using System;
using System.IO;

namespace LanShare.Core
{
    /// <summary>
    /// Un sitio servido: una carpeta en un puerto. Se guarda tal cual en la
    /// configuracion, por eso son campos publicos.
    /// </summary>
    public sealed class SiteDefinition
    {
        public bool Enabled = true;
        public int Port = 8080;
        public string RootPath = "";

        /// <summary>Falso sirve solo en localhost; verdadero abre a la red local.</summary>
        public bool LanMode = false;

        /// <summary>Etiqueta opcional; si esta vacia se muestra el nombre de la carpeta.</summary>
        public string Label = "";

        /// <summary>
        /// Archivo que se sirve cuando se pide una carpeta. Cadena vacia
        /// significa servir como carpeta: listar siempre, sin buscar indice.
        /// </summary>
        public string StartFile = StaticFileResolver.DefaultIndexFile;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Label)) return Label;
                if (string.IsNullOrEmpty(RootPath)) return "(sin carpeta)";

                try
                {
                    string trimmed = RootPath.TrimEnd(Path.DirectorySeparatorChar);
                    string name = Path.GetFileName(trimmed);
                    return string.IsNullOrEmpty(name) ? trimmed : name;
                }
                catch (Exception) { return RootPath; }
            }
        }

        /// <summary>
        /// Como se muestra el archivo de inicio en la tabla. Servir la carpeta
        /// entera es una decision deliberada, no un dato ausente, asi que lleva
        /// etiqueta propia en lugar de dejar la celda en blanco.
        /// </summary>
        public string StartFileDisplay
        {
            get { return string.IsNullOrEmpty(StartFile) ? "(carpeta)" : StartFile; }
        }

        public SiteDefinition Clone()
        {
            SiteDefinition copy = new SiteDefinition();
            copy.Enabled = Enabled;
            copy.Port = Port;
            copy.RootPath = RootPath;
            copy.LanMode = LanMode;
            copy.Label = Label;
            copy.StartFile = StartFile;
            return copy;
        }
    }
}
