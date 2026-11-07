using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LanShare.Ui
{
    /// <summary>
    /// Mensajes Win32 que WinForms no expone. Necesarios para repintar sin
    /// parpadeo, para conservar la barra de desplazamiento y para redimensionar
    /// la ventana desde el borde de una caja.
    /// </summary>
    internal static class NativeUi
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        public const int HTBOTTOM = 15;

        private const int WM_SETREDRAW = 0x000B;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_LINESCROLL = 0x00B6;
        private const int EM_GETLINECOUNT = 0x00BA;
        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
        private const int LVS_EX_DOUBLEBUFFER = 0x00010000;

        /// <summary>
        /// Linea superior visible. Es la posicion REAL de la barra, a
        /// diferencia de SelectionStart, que apunta al cursor: restaurar el
        /// cursor y llamar a ScrollToCaret es justo lo que provoca que el panel
        /// salte mientras se lee.
        /// </summary>
        public static int GetFirstVisibleLine(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return 0;
            return (int)SendMessage(handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        public static int GetLineCount(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return 0;
            return (int)SendMessage(handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ScrollToLine(IntPtr handle, int line)
        {
            if (handle == IntPtr.Zero || line < 0) return;
            int current = GetFirstVisibleLine(handle);
            SendMessage(handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(line - current));
        }

        public static void SetRedraw(IntPtr handle, bool enabled)
        {
            if (handle == IntPtr.Zero) return;
            SendMessage(handle, WM_SETREDRAW, (IntPtr)(enabled ? 1 : 0), IntPtr.Zero);
        }

        /// <summary>
        /// Empieza a redimensionar la ventana como si se hubiera pulsado en su
        /// borde: Windows se encarga del cursor y del arrastre hasta que se
        /// suelta el boton. Es la forma clasica de llevar un borde a otro sitio.
        /// </summary>
        public static void BeginWindowResize(IntPtr window, int hitTest, Point screen)
        {
            if (window == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(window, WM_NCLBUTTONDOWN, (IntPtr)hitTest, PackPoint(screen));
        }

        /// <summary>Doble clic en el borde: Windows estira la ventana a todo el alto de la pantalla.</summary>
        public static void DoubleClickWindowEdge(IntPtr window, int hitTest, Point screen)
        {
            if (window == IntPtr.Zero) return;
            SendMessage(window, WM_NCLBUTTONDBLCLK, (IntPtr)hitTest, PackPoint(screen));
        }

        /// <summary>Coordenadas en un lParam, cada una en 16 bits con signo.</summary>
        private static IntPtr PackPoint(Point screen)
        {
            return (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF));
        }

        /// <summary>ListView no expone DoubleBuffered en publico; el estilo extendido si.</summary>
        public static void EnableListViewDoubleBuffer(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            SendMessage(handle, LVM_SETEXTENDEDLISTVIEWSTYLE,
                        (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
        }
    }
}
