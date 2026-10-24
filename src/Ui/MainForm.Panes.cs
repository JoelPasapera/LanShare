using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Altura de las cajas de la ventana a gusto del usuario.
    ///
    /// Cada hueco entre cajas (la tabla de sitios, el estado, el registro, la
    /// tabla de clientes y su detalle) es un divisor que se arrastra con el
    /// cursor de redimensionar de Windows, y el propio borde de cada caja hace
    /// lo mismo (PaneEdges); doble clic devuelve las alturas de fabrica. El
    /// borde inferior del registro y del detalle es el de la propia ventana: la
    /// franja de debajo se comporta como tal. El reparto se guarda al soltar.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>Con la cabecera de la tabla, una fila y una barra horizontal si aparece.</summary>
        private const int SitesMinHeight = 135;
        private const int StatusMinHeight = 110;
        private const int TabsMinHeight = 265;
        private const int ClientListMinHeight = 90;
        private const int ClientListDefaultHeight = 170;
        private const int ClientDetailMinHeight = 88;

        /// <summary>Alto de la franja inferior que actua como borde de la ventana.</summary>
        private const int BottomResizeBand = 20;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;

        private PaneStack _mainPanes;
        private PaneStack _clientPanes;
        private ToolTip _gripTips;

        private void InitializeGripTips()
        {
            _gripTips = new ToolTip();
            FormClosed += OnGripTipsFormClosed;
        }

        private void OnGripTipsFormClosed(object sender, FormClosedEventArgs e)
        {
            _gripTips.Dispose();
        }

        private PaneGrip MakeGrip(int height)
        {
            PaneGrip grip = new PaneGrip(height);
            _gripTips.SetToolTip(grip, "Arrastra para cambiar la altura. Doble clic: alturas originales.");
            return grip;
        }

        /// <summary>
        /// Los bordes de la caja tambien sirven de asa, ademas del divisor que
        /// tienen al lado. El control mantiene vivo el enganche a traves de sus
        /// eventos, asi que no hace falta guardarlo.
        /// </summary>
        /// <param name="top">Divisor del borde de arriba, o null.</param>
        /// <param name="bottom">Divisor del borde de abajo, o null si la caja llega abajo del todo.</param>
        private void AttachEdges(Control box, PaneGrip top, PaneGrip bottom)
        {
            new PaneEdges(box, top, bottom, bottom == null ? this : null);
        }

        /// <summary>Se guarda al soltar el divisor, sin esperar a cerrar la ventana.</summary>
        private void OnPanesCommitted(object sender, EventArgs e)
        {
            CapturePaneHeights();
            _settings.Save();
        }

        private void CapturePaneHeights()
        {
            int[] main = _mainPanes.GetHeights();
            _settings.SitesPaneHeight = main[0];
            _settings.StatusPaneHeight = main[1];
            _settings.SplitterDistance = _clientPanes.GetHeights()[0];
        }

        private void RestorePaneHeights()
        {
            _mainPanes.SetHeights(_settings.SitesPaneHeight, _settings.StatusPaneHeight);
            _clientPanes.SetHeights(_settings.SplitterDistance);
        }

        /// <summary>
        /// La franja que queda bajo las pestanas responde como el borde inferior
        /// de la ventana: Windows pone su cursor y hace el redimensionado, igual
        /// que si se tirase del borde real, que esta unos pixeles mas abajo.
        /// </summary>
        private void AdjustBottomHitTest(ref Message m)
        {
            if (m.Result != (IntPtr)HTCLIENT || WindowState != FormWindowState.Normal) return;

            // Coordenadas de pantalla con signo: en un monitor a la izquierda
            // del principal son negativas.
            long packed = m.LParam.ToInt64();
            Point screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
            Point client = PointToClient(screen);

            if (client.Y >= ClientSize.Height - BottomResizeBand && client.Y < ClientSize.Height)
            {
                m.Result = (IntPtr)NativeUi.HTBOTTOM;
            }
        }
    }
}
