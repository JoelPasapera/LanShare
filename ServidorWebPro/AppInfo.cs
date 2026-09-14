namespace ServidorWebPro
{
    /// <summary>
    /// Identidad de la aplicacion en un solo sitio, para que el titulo de la
    /// ventana, el pie de la interfaz y los metadatos no se desincronicen.
    /// </summary>
    public static class AppInfo
    {
        public const string Name = "Servidor Web Pro";
        public const string Version = "2.0";
        public const string Author = "Joel Pasapera";

        /// <summary>Texto del titulo de la ventana.</summary>
        public static string WindowTitle
        {
            get { return Name + " " + Version + "  -  " + Author; }
        }

        /// <summary>Pie discreto de la cabecera, con punto medio como separador.</summary>
        public static string Credit
        {
            get { return Name + " v" + Version + "   \u00B7   " + Author; }
        }
    }
}
