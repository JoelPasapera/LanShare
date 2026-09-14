using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServidorWebPro.Ui
{
    /// <summary>
    /// Aplica el icono de la ventana leyendolo del recurso Win32 que ya lleva
    /// el ejecutable.
    ///
    /// Antes el mismo .ico se incrustaba dos veces: una para que lo mostrara el
    /// explorador y otra como recurso gestionado para la ventana. Cargarlo por
    /// API evita la copia duplicada, y ademas permite pedir cada tamano por
    /// separado en lugar de escalar uno solo.
    /// </summary>
    internal static class AppIcon
    {
        /// <summary>Identificador que el compilador de C# asigna a /win32icon.</summary>
        private const int ApplicationIconId = 32512;

        private const uint IMAGE_ICON = 1;
        private const uint LR_SHARED = 0x8000;
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type,
                                               int width, int height, uint options);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message,
                                                 IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string name);

        /// <summary>Debe llamarse cuando la ventana ya tiene handle, no en el constructor.</summary>
        public static void Apply(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;

            try
            {
                IntPtr module = GetModuleHandle(null);
                if (module != IntPtr.Zero)
                {
                    // LR_SHARED: el sistema gestiona el ciclo de vida del icono,
                    // asi que no hay que destruirlo al cerrar.
                    IntPtr small = LoadImage(module, (IntPtr)ApplicationIconId, IMAGE_ICON,
                                             SystemInformation.SmallIconSize.Width,
                                             SystemInformation.SmallIconSize.Height, LR_SHARED);
                    IntPtr big = LoadImage(module, (IntPtr)ApplicationIconId, IMAGE_ICON,
                                           SystemInformation.IconSize.Width,
                                           SystemInformation.IconSize.Height, LR_SHARED);

                    if (small != IntPtr.Zero || big != IntPtr.Zero)
                    {
                        if (small != IntPtr.Zero)
                            SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_SMALL, small);
                        if (big != IntPtr.Zero)
                            SendMessage(form.Handle, WM_SETICON, (IntPtr)ICON_BIG, big);
                        return;
                    }
                }
            }
            catch (Exception) { }

            ApplyFallback(form);
        }

        /// <summary>
        /// Si el recurso no estuviera donde se espera, se extrae del archivo.
        /// Da un solo tamano y la barra de titulo lo reduce, pero es mejor que
        /// quedarse sin icono.
        /// </summary>
        private static void ApplyFallback(Form form)
        {
            try
            {
                string path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path)) form.Icon = Icon.ExtractAssociatedIcon(path);
            }
            catch (Exception) { }
        }
    }
}
