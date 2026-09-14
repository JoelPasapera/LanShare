using System;
using System.Runtime.InteropServices;

namespace ServidorWebPro.Ui
{
    /// <summary>
    /// Mensajes Win32 que WinForms no expone. Necesarios para repintar sin
    /// parpadeo y para conservar la barra de desplazamiento.
    /// </summary>
    internal static class NativeUi
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

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

        /// <summary>ListView no expone DoubleBuffered en publico; el estilo extendido si.</summary>
        public static void EnableListViewDoubleBuffer(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            SendMessage(handle, LVM_SETEXTENDEDLISTVIEWSTYLE,
                        (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
        }
    }
}
