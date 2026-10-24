using System;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Atajos de teclado de la ventana principal.
    ///
    /// ProcessCmdKey los ve antes que el control con el foco. Los globales
    /// usan teclas que ningun control de la ventana necesita; los de la tabla
    /// de sitios solo actuan con el foco en ella, para que Supr o Ctrl+C sigan
    /// haciendo lo de siempre en el registro.
    ///
    /// Los botones se pulsan con PerformClick y no llamando a su manejador:
    /// asi un boton deshabilitado sigue sin hacer nada, tambien desde el teclado.
    /// </summary>
    public partial class MainForm
    {
        private ToolTip _shortcutTips;

        private void InitializeShortcuts()
        {
            _shortcutTips = new ToolTip();
            _shortcutTips.SetToolTip(_startButton, "Iniciar o detener el servidor (F5)");
            _shortcutTips.SetToolTip(_addSiteButton,
                "Anadir un sitio (Ctrl+N). Tambien puedes arrastrar su carpeta a la ventana.");
            _shortcutTips.SetToolTip(_editSiteButton, "Editar el sitio seleccionado (F2 o doble clic)");
            _shortcutTips.SetToolTip(_removeSiteButton, "Quitar el sitio seleccionado (Supr)");
            _shortcutTips.SetToolTip(_settingsButton, "Configuracion (Ctrl+,)");
            FormClosed += OnShortcutsFormClosed;
        }

        private void OnShortcutsFormClosed(object sender, FormClosedEventArgs e)
        {
            _shortcutTips.Dispose();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Bit 30 de lParam: la tecla ya estaba pulsada, es la repeticion
            // automatica de mantenerla. F5 mantenido alternaba iniciar y detener
            // y abria pestanas en cada arranque.
            bool repeat = (msg.LParam.ToInt64() & 0x40000000L) != 0;
            if (HandleShortcut(keyData, repeat)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool HandleShortcut(Keys keyData, bool repeat)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (!repeat) _startButton.PerformClick();
                    return true;
                case Keys.Control | Keys.N:
                    _addSiteButton.PerformClick();
                    return true;
                case Keys.Control | Keys.Oemcomma:
                    _settingsButton.PerformClick();
                    return true;
                case Keys.Control | Keys.S:
                    OnSaveLogClick(this, EventArgs.Empty);
                    return true;
            }

            if (!_sitesList.Focused || SelectedSite == null) return false;

            switch (keyData)
            {
                case Keys.Enter:
                    if (repeat) return true;

                    // En marcha, abrir es lo util; parado, lo unico posible es editar.
                    if (IsSiteRunning(SelectedSite)) OnMenuOpenBrowser(this, EventArgs.Empty);
                    else _editSiteButton.PerformClick();
                    return true;
                case Keys.F2:
                    _editSiteButton.PerformClick();
                    return true;
                case Keys.Delete:
                    _removeSiteButton.PerformClick();
                    return true;
                case Keys.Control | Keys.C:
                    OnMenuCopyLocal(this, EventArgs.Empty);
                    return true;
            }
            return false;
        }
    }
}
