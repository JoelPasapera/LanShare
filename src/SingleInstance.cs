using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LanShare
{
    /// <summary>
    /// Garantiza una sola instancia por sesion de usuario.
    ///
    /// Dos instancias a la vez se pisaban en silencio: solo una puede quedarse
    /// con el puerto 5353 de mDNS, y ambas escriben el mismo settings.json, asi
    /// que gana la ultima que cierra. Ahora la segunda despierta a la primera y
    /// termina.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "Local\\LanShare.SingleInstance";

        /// <summary>
        /// Mensaje registrado con el que la segunda instancia pide a la primera
        /// que se muestre. Cero si el registro fallo.
        /// </summary>
        public static readonly int ShowMessage = RegisterWindowMessage("LanShare.ShowWindow");

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const uint MsgFilterAllow = 1;

        private Mutex _mutex;
        private bool _owned;

        /// <param name="waitMs">
        /// Cuanto esperar a que la instancia anterior suelte el mutex. Cero para
        /// no esperar. Al escalar privilegios la nueva instancia arranca antes
        /// de que la vieja termine de cerrar y guardar la configuracion.
        /// </param>
        public SingleInstance(int waitMs)
        {
            try
            {
                bool createdNew;
                _mutex = new Mutex(true, MutexName, out createdNew);
                _owned = createdNew;

                if (!_owned && waitMs > 0)
                {
                    try { _owned = _mutex.WaitOne(waitMs); }
                    catch (AbandonedMutexException) { _owned = true; }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // La creo una instancia elevada y esta no lo es: existe, aunque
                // no se pueda abrir. Es justo lo que hay que detectar.
                _owned = false;
            }
            catch (Exception)
            {
                // Si el sistema no da mutex con nombre, mejor arrancar que no.
                _owned = true;
            }
        }

        public bool IsFirst
        {
            get { return _owned; }
        }

        /// <summary>Pide a la instancia que ya corre que muestre su ventana.</summary>
        public static void SignalExisting()
        {
            if (ShowMessage == 0) return;
            try { PostMessage(HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception) { }
        }

        /// <summary>
        /// Deja pasar el mensaje aunque llegue de un proceso sin elevar. Sin
        /// esto, el aislamiento de privilegios de Windows lo descarta cuando la
        /// instancia que corre es de administrador y la nueva no.
        /// </summary>
        public static void AllowShowMessage(IntPtr windowHandle)
        {
            if (ShowMessage == 0 || windowHandle == IntPtr.Zero) return;
            try { ChangeWindowMessageFilterEx(windowHandle, (uint)ShowMessage, MsgFilterAllow, IntPtr.Zero); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (_mutex == null) return;

            if (_owned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (Exception) { }
            }

            _mutex.Dispose();
            _mutex = null;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action,
                                                               IntPtr changeInfo);
    }
}
