namespace LanShare.Core
{
    /// <summary>
    /// Lo que se inyecta en una respuesta concreta. Se decide una vez por
    /// peticion y de aqui salen a la vez el HTML, el validador y la clave de
    /// la cache, que tienen que coincidir siempre.
    ///
    /// Solo hay ocho combinaciones y se crean al cargar la clase: pedir una
    /// en cada peticion no reserva memoria.
    /// </summary>
    public sealed class HtmlInjection
    {
        private static readonly HtmlInjection[] All = BuildAll();

        public static readonly HtmlInjection None = All[0];

        /// <summary>Al principio de &lt;head&gt;: la consola remota, que debe escuchar antes que nadie.</summary>
        public readonly string HeadSnippet;

        /// <summary>Antes de &lt;/body&gt;: la sonda y la recarga, que no tienen prisa.</summary>
        public readonly string BodySnippet;

        /// <summary>Sufijo del ETag y de la clave de la cache de compresion.</summary>
        public readonly string Variant;

        private HtmlInjection(bool probe, bool liveReload, bool remoteConsole)
        {
            HeadSnippet = remoteConsole ? RemoteConsoleScript.Html : "";
            BodySnippet = HtmlInjector.Compose(probe, liveReload);
            Variant = HtmlInjector.Variant(probe, liveReload, remoteConsole);
        }

        public bool IsEmpty
        {
            get { return Variant.Length == 0; }
        }

        public static HtmlInjection For(bool probe, bool liveReload, bool remoteConsole)
        {
            return All[(probe ? 1 : 0) | (liveReload ? 2 : 0) | (remoteConsole ? 4 : 0)];
        }

        public string Apply(string html)
        {
            return HtmlInjector.Inject(HtmlInjector.InjectHead(html, HeadSnippet), BodySnippet);
        }

        private static HtmlInjection[] BuildAll()
        {
            HtmlInjection[] all = new HtmlInjection[8];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = new HtmlInjection((i & 1) != 0, (i & 2) != 0, (i & 4) != 0);
            }
            return all;
        }
    }
}
