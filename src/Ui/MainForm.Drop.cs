using System;
using System.IO;
using System.Windows.Forms;
using LanShare.Core;
using LanShare.Net;

namespace LanShare.Ui
{
    /// <summary>
    /// Anadir un sitio arrastrando su carpeta, o una de sus paginas, desde el
    /// Explorador hasta la ventana. Se abre el dialogo de siempre con la
    /// carpeta, la pagina de inicio y un puerto libre ya puestos.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Dialogos que abre como mucho un mismo arrastre.</summary>
        private const int MaxDroppedSites = 8;

        private FileDropTarget _sitesDropTarget;
        private bool _dropViaMessages;

        /// <summary>
        /// Sin elevar, el arrastre OLE de WinForms, que cubre toda la ventana.
        /// Elevado, WM_DROPFILES en el formulario y en la tabla de sitios:
        /// Windows no deja que el Explorador, sin elevar, hable OLE con una
        /// ventana de administrador.
        /// </summary>
        private void InitializeFileDrop()
        {
            if (_isAdmin)
            {
                _dropViaMessages = true;
                _sitesDropTarget = new FileDropTarget(_sitesList, OnFilesDropped);
                if (IsHandleCreated) FileDropTarget.Accept(Handle);
                FormClosed += OnDropFormClosed;
                return;
            }

            AllowDrop = true;
            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;
        }

        private void OnDropFormClosed(object sender, FormClosedEventArgs e)
        {
            _sitesDropTarget.Detach();
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data != null ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (paths != null && paths.Length > 0) OnFilesDropped(paths);
        }

        /// <summary>
        /// Se difiere: mientras dura el evento de soltar, el Explorador espera
        /// y se quedaria congelado detras de un dialogo modal.
        /// </summary>
        private void OnFilesDropped(string[] paths)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke((MethodInvoker)delegate { AddDroppedPaths(paths); });
        }

        private void AddDroppedPaths(string[] paths)
        {
            if (IsDisposed) return;

            // Soltar no activa la ventana; el dialogo debe salir delante.
            Activate();

            int opened = 0;
            SiteDefinition lastAdded = null;
            foreach (string path in paths)
            {
                string root, startFile;
                if (!DroppedPath.TryResolve(path, out root, out startFile)) continue;

                SiteDefinition existing = FindSiteByRoot(root);
                if (existing != null)
                {
                    SelectSite(existing);
                    _log.Write("INFO", "La carpeta " + root + " ya se sirve en el puerto " + existing.Port + ".");
                    continue;
                }

                if (++opened > MaxDroppedSites) break;

                // Cancelar un dialogo es no querer los demas.
                SiteDefinition added = AddSite(root, startFile);
                if (added == null) break;
                if (added.Enabled) lastAdded = added;
            }

            // Un solo reinicio para todo lo soltado, al final: pedirlo tras cada
            // dialogo lo haria saltar mientras el siguiente sigue abierto.
            if (lastAdded != null) OnSiteAddedWhileRunning(lastAdded);
        }

        private SiteDefinition FindSiteByRoot(string root)
        {
            string wanted = NormalizeRoot(root);
            foreach (SiteDefinition site in _settings.Sites)
            {
                if (string.Equals(NormalizeRoot(site.RootPath), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return site;
                }
            }
            return null;
        }

        private static string NormalizeRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { path = Path.GetFullPath(path); }
            catch (Exception) { }
            return path.TrimEnd('\\', '/');
        }

        private void SelectSite(SiteDefinition site)
        {
            foreach (ListViewItem item in _sitesList.Items)
            {
                bool match = ReferenceEquals(item.Tag, site);
                item.Selected = match;
                if (match)
                {
                    item.Focused = true;
                    item.EnsureVisible();
                }
            }
            _sitesList.Focus();
        }
    }
}
