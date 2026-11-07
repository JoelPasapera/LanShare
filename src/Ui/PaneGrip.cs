using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Divisor horizontal entre dos cajas: al pasar por encima aparece el cursor
    /// de redimensionar de Windows y, al arrastrarlo, cambian las alturas en
    /// vivo. Doble clic pide volver al reparto original.
    ///
    /// Solo sabe de raton y de pintarse: informa de cuanto se ha movido desde
    /// que empezo el arrastre y PaneStack decide las alturas. Que informe del
    /// total y no de cada paso hace que el limite siga al raton con exactitud,
    /// tambien al volver de haberse topado con un minimo.
    /// </summary>
    internal sealed class PaneGrip : Control
    {
        private bool _mouseOver;
        private bool _edgeOver;
        private bool _dragging;
        private int _startScreenY;

        public event EventHandler DragStarted;
        public event EventHandler<GripDragEventArgs> Dragged;
        public event EventHandler DragCompleted;
        public event EventHandler ResetRequested;

        public PaneGrip(int height)
        {
            // Fondo transparente: dentro de una pestana, el fondo es el del tema
            // de Windows y un color liso se notaria como un parche.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);

            TabStop = false;
            Dock = DockStyle.Top;
            Height = height;
            Cursor = Cursors.SizeNS;
            AccessibleRole = AccessibleRole.Grip;
            AccessibleName = "Divisor de altura";
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (e.Clicks > 1)
            {
                Raise(ResetRequested);
                return;
            }

            // En coordenadas de pantalla: el propio divisor se desplaza mientras
            // se arrastra, y medir respecto a el haria temblar el limite.
            Capture = true;
            BeginDragFrom(Control.MousePosition.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Si el boton se solto sin que llegara el aviso, el arrastre acaba
            // aqui: si no, el limite seguiria al raton sin pulsar nada.
            if (_dragging && (e.Button & MouseButtons.Left) == 0)
            {
                EndDrag();
                return;
            }
            DragFrom(Control.MousePosition.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) EndDrag();
        }

        /// <summary>Si se pierde el raton a mitad (Alt+Tab, otra ventana), el reparto se queda donde estaba.</summary>
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            EndDrag();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouseOver = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseOver = false;
            Invalidate();
        }

        // ------------------------------------------------------------------
        // Arrastres que empiezan en otro sitio: el borde de una caja (PaneEdges)
        // mueve el mismo limite que este divisor, y lo hace a traves de el para
        // que el reparto se calcule y se guarde en un solo lugar.
        // ------------------------------------------------------------------

        public void BeginDragFrom(int screenY)
        {
            if (_dragging) return;
            _startScreenY = screenY;
            _dragging = true;
            Invalidate();
            Raise(DragStarted);
        }

        public void DragFrom(int screenY)
        {
            if (!_dragging) return;
            EventHandler<GripDragEventArgs> handler = Dragged;
            if (handler != null) handler(this, new GripDragEventArgs(screenY - _startScreenY));
        }

        public void EndDragFrom()
        {
            EndDrag();
        }

        public void RequestReset()
        {
            Raise(ResetRequested);
        }

        /// <summary>Se sombrea mientras el raton esta en el borde de la caja que mueve.</summary>
        public void SetEdgeHighlight(bool highlighted)
        {
            if (_edgeOver == highlighted) return;
            _edgeOver = highlighted;
            Invalidate();
        }

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            // Solo si el raton lo tiene el: en un arrastre desde el borde de una
            // caja, la captura es de la caja y la suelta ella.
            if (Capture) Capture = false;
            Invalidate();
            Raise(DragCompleted);
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// En reposo, el hueco de siempre con un asa de tres puntos en el centro,
        /// como los divisores de Windows. Al pasar por encima se sombrea, para
        /// que se vea que ese borde se puede arrastrar.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;

            // El fondo ya lo pinto OnPaintBackground (el del contenedor); el
            // sombreado va encima, semitransparente.
            if (_dragging || _mouseOver || _edgeOver)
            {
                Color tone = _dragging ? SystemColors.Highlight : SystemColors.ControlDark;
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(_dragging ? 90 : 60, tone)))
                {
                    graphics.FillRectangle(shade, ClientRectangle);
                }
            }

            int centerX = Width / 2;
            int centerY = Height / 2;
            using (SolidBrush dot = new SolidBrush(_dragging ? SystemColors.Highlight : SystemColors.ControlDark))
            {
                for (int i = -1; i <= 1; i++) graphics.FillRectangle(dot, centerX + i * 6 - 1, centerY - 1, 3, 3);
            }
        }
    }

    /// <summary>Cuanto se ha movido un divisor desde que empezo a arrastrarse.</summary>
    internal sealed class GripDragEventArgs : EventArgs
    {
        public GripDragEventArgs(int offset)
        {
            Offset = offset;
        }

        /// <summary>Pixeles, positivo hacia abajo.</summary>
        public int Offset { get; private set; }
    }
}
