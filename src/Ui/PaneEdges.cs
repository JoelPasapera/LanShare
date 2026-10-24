using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Convierte el borde de arriba y el de abajo de una caja (un TextBox, una
    /// ListView) en asas para cambiar su altura: es donde se pone el raton de
    /// forma natural. Cada borde hace lo mismo que el divisor que tiene al
    /// lado (a traves de el), o estira la ventana si la caja llega abajo del
    /// todo. El borde muestra el cursor de redimensionar y sombrea su divisor.
    ///
    /// Se engancha a la ventana nativa del control, como FileDropTarget, para
    /// ver el raton antes que el: si el TextBox recibiera el clic, empezaria a
    /// seleccionar texto mientras la caja cambia de tamano.
    /// </summary>
    internal sealed class PaneEdges : NativeWindow
    {
        /// <summary>Pixeles de cada borde que responden, contando el marco del control.</summary>
        private const int EdgeSize = 5;

        private const int WM_SETCURSOR = 0x0020;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_CAPTURECHANGED = 0x0215;
        private const int WM_MOUSELEAVE = 0x02A3;
        private const int HTCLIENT = 1;
        private const int HTBORDER = 18;

        /// <summary>Bit de wParam en los mensajes de raton: el boton izquierdo esta pulsado.</summary>
        private const int MK_LBUTTON = 0x0001;

        private enum Edge { None, Top, Bottom }

        private readonly Control _control;
        private readonly PaneGrip _top;
        private readonly PaneGrip _bottom;
        private readonly Form _window;

        private PaneGrip _dragging;
        private PaneGrip _highlighted;

        /// <param name="control">La caja.</param>
        /// <param name="top">Divisor cuyo limite mueve el borde de arriba; null si ese borde no hace nada.</param>
        /// <param name="bottom">Divisor cuyo limite mueve el borde de abajo; null si no lo hay.</param>
        /// <param name="window">Si no hay divisor abajo, ventana que estira el borde de abajo; null si ninguna.</param>
        public PaneEdges(Control control, PaneGrip top, PaneGrip bottom, Form window)
        {
            if (control == null) throw new ArgumentNullException("control");
            _control = control;
            _top = top;
            _bottom = bottom;
            _window = window;

            // Sigue al control si su ventana se recrea, como FileDropTarget.
            control.HandleCreated += OnHandleCreated;
            control.HandleDestroyed += OnHandleDestroyed;
            if (control.IsHandleCreated) AssignHandle(control.Handle);
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
            AssignHandle(_control.Handle);
        }

        private void OnHandleDestroyed(object sender, EventArgs e)
        {
            // Si desaparece a mitad de arrastre, el divisor y su pila no deben
            // quedarse esperando un final que ya no llegara.
            PaneGrip grip = _dragging;
            _dragging = null;
            if (grip != null) grip.EndDragFrom();

            Highlight(null);
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);

                // El marco del control (2 px) cuenta como borde de la caja: se
                // trata como area de cliente para recibir el clic aqui. Las
                // barras de desplazamiento no se tocan.
                bool frameOrClient = m.Result == (IntPtr)HTBORDER || m.Result == (IntPtr)HTCLIENT;
                if (frameOrClient && EdgeAt(ScreenPoint(m.LParam)) != Edge.None) m.Result = (IntPtr)HTCLIENT;
                return;
            }

            if (m.Msg == WM_SETCURSOR && LowWord(m.LParam) == HTCLIENT &&
                (_dragging != null || EdgeAt(Control.MousePosition) != Edge.None))
            {
                Cursor.Current = Cursors.SizeNS;
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_MOUSEMOVE)
            {
                bool leftDown = (LowWord(m.WParam) & MK_LBUTTON) != 0;
                if (_dragging != null)
                {
                    // Si el boton se solto sin que llegara el aviso, se termina
                    // aqui: si no, el limite seguiria al raton sin pulsar nada.
                    if (leftDown) _dragging.DragFrom(Control.MousePosition.Y);
                    else Finish();
                    return;
                }

                // Mientras se selecciona texto, pasar por el borde no es apuntarlo.
                Highlight(leftDown ? null : GripFor(EdgeAt(Control.MousePosition)));
            }
            else if (m.Msg == WM_MOUSELEAVE)
            {
                Highlight(null);
            }
            else if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                // Donde fue el clic, no donde esta ahora el raton: con la ventana
                // ocupada, el mensaje puede llegar cuando ya se ha movido.
                Point screen = _control.PointToScreen(ClientPoint(m.LParam));
                Edge edge = EdgeAt(screen);
                if (edge != Edge.None)
                {
                    Press(edge, m.Msg == WM_LBUTTONDBLCLK, screen);
                    return;
                }
            }
            else if (m.Msg == WM_LBUTTONUP)
            {
                if (_dragging != null)
                {
                    Finish();
                    return;
                }
            }
            else if (m.Msg == WM_CAPTURECHANGED)
            {
                if (_dragging != null) Finish();
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// Pulsacion en un borde: con divisor, se arrastra su limite (o, con
        /// doble clic, se vuelve a las alturas originales); sin el, se estira la
        /// ventana como desde su borde inferior.
        /// </summary>
        private void Press(Edge edge, bool doubleClick, Point screen)
        {
            PaneGrip grip = GripFor(edge);
            if (grip != null)
            {
                if (doubleClick)
                {
                    grip.RequestReset();
                    return;
                }
                _dragging = grip;
                _control.Capture = true;
                grip.BeginDragFrom(screen.Y);
                return;
            }

            if (_window == null) return;
            Highlight(null);
            if (doubleClick) NativeUi.DoubleClickWindowEdge(_window.Handle, NativeUi.HTBOTTOM, screen);
            else NativeUi.BeginWindowResize(_window.Handle, NativeUi.HTBOTTOM, screen);
        }

        private void Finish()
        {
            PaneGrip grip = _dragging;
            _dragging = null;
            if (_control.Capture) _control.Capture = false;
            grip.EndDragFrom();
            Highlight(GripFor(EdgeAt(Control.MousePosition)));
        }

        /// <summary>Sombrea el divisor que se moveria; null para ninguno.</summary>
        private void Highlight(PaneGrip grip)
        {
            if (_highlighted == grip) return;
            if (_highlighted != null) _highlighted.SetEdgeHighlight(false);
            _highlighted = grip;
            if (grip != null) grip.SetEdgeHighlight(true);
        }

        private PaneGrip GripFor(Edge edge)
        {
            if (edge == Edge.Top) return _top;
            if (edge == Edge.Bottom) return _bottom;
            return null;
        }

        /// <summary>
        /// En que borde esta un punto de la pantalla. El de abajo sin divisor
        /// solo cuenta si la ventana se puede estirar: maximizada, no.
        /// </summary>
        private Edge EdgeAt(Point screen)
        {
            Control parent = _control.Parent;
            if (parent == null) return Edge.None;

            Rectangle bounds = parent.RectangleToScreen(_control.Bounds);
            if (!bounds.Contains(screen)) return Edge.None;

            if (_top != null && screen.Y < bounds.Top + EdgeSize) return Edge.Top;

            bool bottomActive = _bottom != null ||
                                (_window != null && _window.WindowState == FormWindowState.Normal);
            if (bottomActive && screen.Y >= bounds.Bottom - EdgeSize) return Edge.Bottom;

            return Edge.None;
        }

        private static int LowWord(IntPtr value)
        {
            return (int)(value.ToInt64() & 0xFFFF);
        }

        /// <summary>Punto de pantalla de un lParam, con signo: a la izquierda del monitor principal es negativo.</summary>
        private static Point ScreenPoint(IntPtr lParam)
        {
            return SignedPoint(lParam);
        }

        /// <summary>Punto de cliente de un mensaje de raton; sobre el marco del control es negativo.</summary>
        private static Point ClientPoint(IntPtr lParam)
        {
            return SignedPoint(lParam);
        }

        private static Point SignedPoint(IntPtr lParam)
        {
            long packed = lParam.ToInt64();
            return new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
        }
    }
}
