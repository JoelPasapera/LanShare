using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LanShare.Ui
{
    /// <summary>
    /// Recibe archivos arrastrados con WM_DROPFILES, el mecanismo clasico de
    /// Windows, sobre un control concreto.
    ///
    /// Solo hace falta cuando LanShare corre como administrador: Windows
    /// bloquea el arrastre OLE (AllowDrop) desde el Explorador, que no esta
    /// elevado, hacia una ventana elevada. WM_DROPFILES si llega, siempre que
    /// la ventana lo admita expresamente con ChangeWindowMessageFilterEx.
    ///
    /// Se engancha al control como subclase (NativeWindow) porque WinForms no
    /// expone WM_DROPFILES como evento, y sigue al control si su ventana se
    /// recrea.
    /// </summary>
    internal sealed class FileDropTarget : NativeWindow
    {
        public const int WM_DROPFILES = 0x0233;
        private const int WM_COPYDATA = 0x004A;

        /// <summary>Lo usa el origen del arrastre para pasar la lista de archivos.</summary>
        private const int WM_COPYGLOBALDATA = 0x0049;

        private const uint MSGFLT_ALLOW = 1;

        /// <summary>Tope de rutas por arrastre: soltar media unidad no debe abrir cien dialogos.</summary>
        private const int MaxPaths = 64;

        private readonly Action<string[]> _onDrop;

        public FileDropTarget(Control control, Action<string[]> onDrop)
        {
            if (control == null) throw new ArgumentNullException("control");
            if (onDrop == null) throw new ArgumentNullException("onDrop");

            _onDrop = onDrop;
            control.HandleCreated += OnHandleCreated;
            control.HandleDestroyed += OnHandleDestroyed;
            if (control.IsHandleCreated) Attach(control.Handle);
        }

        /// <summary>
        /// Admite WM_DROPFILES en una ventana elevada sin subclasificarla. Lo
        /// usa el formulario principal, que ya tiene su propio WndProc.
        /// </summary>
        public static void Accept(IntPtr window)
        {
            try
            {
                DragAcceptFiles(window, true);
                ChangeWindowMessageFilterEx(window, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(window, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(window, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch (Exception)
            {
                // Sin arrastre no se pierde nada: queda el boton de anadir.
            }
        }

        /// <summary>Rutas de un WM_DROPFILES. Libera siempre el HDROP.</summary>
        public static string[] ReadDrop(IntPtr drop)
        {
            List<string> paths = new List<string>();
            try
            {
                uint count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count && paths.Count < MaxPaths; i++)
                {
                    uint length = DragQueryFile(drop, i, null, 0);
                    if (length == 0) continue;

                    StringBuilder buffer = new StringBuilder((int)length + 1);
                    if (DragQueryFile(drop, i, buffer, (uint)buffer.Capacity) > 0) paths.Add(buffer.ToString());
                }
            }
            catch (Exception) { }
            finally
            {
                try { DragFinish(drop); }
                catch (Exception) { }
            }
            return paths.ToArray();
        }

        /// <summary>Deja de recibir arrastres y devuelve la ventana a su procedimiento original.</summary>
        public void Detach()
        {
            if (Handle != IntPtr.Zero) ReleaseHandle();
        }

        private void Attach(IntPtr handle)
        {
            Detach();
            AssignHandle(handle);
            Accept(handle);
        }

        private void OnHandleCreated(object sender, EventArgs e)
        {
            Attach(((Control)sender).Handle);
        }

        private void OnHandleDestroyed(object sender, EventArgs e)
        {
            Detach();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DROPFILES)
            {
                string[] paths = ReadDrop(m.WParam);
                if (paths.Length > 0) _onDrop(paths);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool accept);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr drop, uint file, StringBuilder buffer, uint size);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr drop);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action,
                                                               IntPtr changeInfo);
    }
}
