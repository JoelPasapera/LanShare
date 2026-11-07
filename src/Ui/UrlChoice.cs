namespace LanShare.Ui
{
    /// <summary>
    /// Una direccion de acceso con su etiqueta legible.
    ///
    /// Con varios sitios sirviendo a la vez, una lista de URL desnudas obliga a
    /// deducir el proyecto por el numero de puerto. La etiqueta lo dice.
    /// </summary>
    public sealed class UrlChoice
    {
        public string Url { get; private set; }
        public string Caption { get; private set; }

        public UrlChoice(string url, string caption)
        {
            Url = url;
            Caption = caption;
        }

        /// <summary>
        /// URL de un sitio en una direccion de red: "proyecto - url (Wi-Fi)".
        /// El nombre del adaptador ayuda a elegir cuando el equipo tiene varios.
        /// </summary>
        public static UrlChoice ForSite(string siteName, string url, string adapterName)
        {
            string caption = siteName + "   -   " + url;
            if (!string.IsNullOrEmpty(adapterName)) caption += "   (" + adapterName + ")";
            return new UrlChoice(url, caption);
        }

        /// <summary>ComboBox muestra esto; el codigo lee <see cref="Url"/>.</summary>
        public override string ToString()
        {
            return Caption;
        }
    }
}
