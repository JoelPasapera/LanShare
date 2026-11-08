using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using LanShare.Core;
using LanShare.Net;

namespace LanShare.Ui
{
    /// <summary>
    /// Menu contextual de la tabla de sitios: las acciones que se hacen sobre
    /// un proyecto concreto, sobre todo mientras se esta sirviendo.
    /// </summary>
    public partial class MainForm
    {
        private ToolStripMenuItem _menuOpenBrowser;
        private ToolStripMenuItem _menuOpenFolder;
        private ToolStripMenuItem _menuCopyLocal;
        private ToolStripMenuItem _menuCopyLan;
        private ToolStripMenuItem _menuSiteQr;
        private ToolStripMenuItem _menuUploadQr;
        private ToolStripMenuItem _menuCertificateQr;
        private ToolStripMenuItem _menuEdit;
        private ToolStripMenuItem _menuRemove;

        private ContextMenuStrip BuildSitesMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            _menuOpenBrowser = AddMenuItem(menu, "Abrir en el navegador", OnMenuOpenBrowser);
            _menuOpenFolder = AddMenuItem(menu, "Abrir carpeta", OnMenuOpenFolder);
            menu.Items.Add(new ToolStripSeparator());
            _menuCopyLocal = AddMenuItem(menu, "Copiar URL local", OnMenuCopyLocal);
            _menuCopyLan = AddMenuItem(menu, "Copiar URL de red local", OnMenuCopyLan);
            _menuSiteQr = AddMenuItem(menu, "Codigo QR de este sitio...", OnMenuSiteQr);
            _menuUploadQr = AddMenuItem(menu, "Codigo QR para enviarte archivos...", OnMenuUploadQr);
            _menuCertificateQr = AddMenuItem(menu, "Codigo QR para instalar el certificado HTTPS en el movil...",
                                             OnMenuCertificateQr);
            menu.Items.Add(new ToolStripSeparator());
            _menuEdit = AddMenuItem(menu, "Editar...", OnEditSiteClick);
            _menuRemove = AddMenuItem(menu, "Quitar", OnRemoveSiteClick);

            // Solo se muestran: los atajos los atiende ProcessCmdKey con el foco
            // en la tabla, para no robarle Supr o Ctrl+C al registro.
            _menuOpenBrowser.ShortcutKeyDisplayString = "Intro";
            _menuCopyLocal.ShortcutKeyDisplayString = "Ctrl+C";
            _menuEdit.ShortcutKeyDisplayString = "F2";
            _menuRemove.ShortcutKeyDisplayString = "Supr";

            _menuOpenBrowser.Font = new System.Drawing.Font(_menuOpenBrowser.Font, System.Drawing.FontStyle.Bold);
            menu.Opening += OnSitesMenuOpening;
            return menu;
        }

        private static ToolStripMenuItem AddMenuItem(ContextMenuStrip menu, string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            menu.Items.Add(item);
            return item;
        }

        /// <summary>Si este sitio concreto forma parte del servidor en marcha.</summary>
        private bool IsSiteRunning(SiteDefinition site)
        {
            return site != null && _server != null && _server.Options.FindByPort(site.Port) != null;
        }

        /// <summary>
        /// Cada accion se habilita segun lo que tenga sentido en ese momento:
        /// abrir o compartir exige que el sitio se este sirviendo. Editar y
        /// quitar valen siempre: con el servidor en marcha se aplican
        /// reiniciandolo.
        /// </summary>
        private void OnSitesMenuOpening(object sender, CancelEventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null)
            {
                e.Cancel = true;
                return;
            }

            bool running = IsSiteRunning(site);

            _menuOpenBrowser.Enabled = running;
            _menuOpenFolder.Enabled = Directory.Exists(site.RootPath);
            _menuCopyLocal.Enabled = true;
            _menuCopyLan.Enabled = running && site.LanMode;
            _menuSiteQr.Enabled = running && site.LanMode;
            _menuUploadQr.Enabled = running && site.LanMode && _settings.UploadEnabled;
            _menuUploadQr.Visible = _settings.UploadEnabled;
            _menuCertificateQr.Visible = site.HttpsPort > 0;
            _menuCertificateQr.Enabled = running && site.LanMode && IsSiteServedOverHttps(site);
            _menuEdit.Enabled = true;
            _menuRemove.Enabled = true;
        }

        private void OnMenuOpenBrowser(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null) return;
            OpenWithShell(LocalUrlOf(site));
        }

        private void OnMenuOpenFolder(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null || !Directory.Exists(site.RootPath)) return;
            OpenWithShell(site.RootPath);
        }

        private void OnMenuCopyLocal(object sender, EventArgs e)
        {
            SiteDefinition site = SelectedSite;
            if (site == null) return;
            CopyToClipboard(LocalUrlOf(site));
        }

        private void OnMenuCopyLan(object sender, EventArgs e)
        {
            List<UrlChoice> urls = LanUrlsFor(SelectedSite, "");
            if (urls.Count == 0)
            {
                Warn("No se detectaron adaptadores de red activos.", "Sin red local");
                return;
            }

            List<string> plain = new List<string>();
            foreach (UrlChoice choice in urls) plain.Add(choice.Url);
            CopyToClipboard(string.Join("\r\n", plain.ToArray()));
        }

        /// <summary>
        /// QR de un solo sitio: con varios proyectos en marcha, el boton general
        /// obliga a buscar en el desplegable; desde aqui se va directo.
        /// </summary>
        private void OnMenuSiteQr(object sender, EventArgs e)
        {
            ShowQr(LanUrlsFor(SelectedSite, ""));
        }

        /// <summary>
        /// QR que lleva el movil directo a la pagina de subida: el camino mas
        /// corto para pasar una foto al PC.
        /// </summary>
        private void OnMenuUploadQr(object sender, EventArgs e)
        {
            ShowQr(LanUrlsFor(SelectedSite, UploadHandler.Endpoint.TrimStart('/')));
        }

        /// <summary>
        /// QR de la pagina que instala la autoridad local en el movil: se
        /// escanea una vez por movil, antes de usar las direcciones HTTPS.
        /// </summary>
        private void OnMenuCertificateQr(object sender, EventArgs e)
        {
            ShowQr(CertificateUrlsFor(SelectedSite));
        }

        /// <summary>
        /// El codigo QR, con aviso si Windows tiene la red como publica: es
        /// justo cuando el movil no conectaria y pareceria culpa del codigo.
        /// </summary>
        private void ShowQr(List<UrlChoice> urls)
        {
            if (urls.Count == 0)
            {
                Warn("No se detectaron adaptadores de red activos.", "Sin red local");
                return;
            }

            RefreshPublicNetworks();
            using (QrDialog dialog = new QrDialog(urls, PublicNetworkNotice()))
            {
                dialog.ShowDialog(this);
            }
        }

        private static string LocalUrlOf(SiteDefinition site)
        {
            return "http://localhost:" + site.Port.ToString(CultureInfo.InvariantCulture) + "/";
        }

        /// <summary>
        /// Direcciones de red local del sitio, una por adaptador. Si se esta
        /// sirviendo tambien por HTTPS, esa va delante en cada adaptador.
        /// </summary>
        /// <param name="path">Ruta tras la barra inicial, sin ella; vacia para la raiz.</param>
        private List<UrlChoice> LanUrlsFor(SiteDefinition site, string path)
        {
            List<UrlChoice> urls = new List<UrlChoice>();
            if (site == null) return urls;

            string nombre = site.DisplayName;
            int httpsPort = RunningHttpsPort(site);
            foreach (LanAddress address in LanAddresses.GetActiveDetailed())
            {
                if (httpsPort > 0)
                {
                    string segura = "https://" + address.Address + ":" +
                                    httpsPort.ToString(CultureInfo.InvariantCulture) + "/" + path;
                    urls.Add(UrlChoice.ForSite(nombre, segura, address.AdapterName));
                }

                string url = "http://" + address.Address + ":" +
                             site.Port.ToString(CultureInfo.InvariantCulture) + "/" + path;
                urls.Add(UrlChoice.ForSite(nombre, url, address.AdapterName));
            }
            return urls;
        }

        private void OpenWithShell(string target)
        {
            try { Process.Start(target); }
            catch (Exception ex) { _log.Write("WARN", "No se pudo abrir " + target + ": " + ex.Message); }
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                _log.Write("INFO", "Copiado al portapapeles: " + text.Replace("\r\n", "  "));
            }
            catch (Exception ex)
            {
                Warn("No se pudo acceder al portapapeles: " + ex.Message, "Error");
            }
        }

        /// <summary>Momento del ultimo doble clic sobre la tabla de sitios.</summary>
        private DateTime _lastSiteDoubleClick = DateTime.MinValue;

        /// <summary>
        /// WinForms emite MouseDown antes de que el control nativo procese el
        /// clic, asi que aqui se puede anotar el doble clic antes de que llegue
        /// el cambio de casilla que provoca.
        /// </summary>
        private void OnSiteListMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Clicks > 1) _lastSiteDoubleClick = DateTime.UtcNow;
        }

        /// <summary>
        /// El ListView con casillas marca o desmarca la fila al hacer doble clic
        /// en ella, y el doble clic es la forma de editar un sitio: sin esto,
        /// editar cambiaba de paso si se sirve y, con el servidor en marcha,
        /// lo reiniciaba detras del dialogo.
        /// </summary>
        private void OnSiteItemCheck(object sender, ItemCheckEventArgs e)
        {
            // Lo que repinta el propio programa desde el modelo no es un clic:
            // cancelarlo dejaba sitios marcados dibujados como desmarcados.
            if (_suspendSiteEvents) return;

            if ((DateTime.UtcNow - _lastSiteDoubleClick).TotalMilliseconds <= SystemInformation.DoubleClickTime)
            {
                e.NewValue = e.CurrentValue;

                // Un doble clic produce un solo cambio de casilla; los siguientes
                // ya no son suyos.
                _lastSiteDoubleClick = DateTime.MinValue;
            }
        }
    }
}
