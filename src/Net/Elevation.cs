using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace LanShare.Net
{
    /// <summary>Comprobacion y escalada de privilegios.</summary>
    public static class Elevation
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Relanza el propio ejecutable con el verbo runas. Devuelve false si el
        /// usuario cancela el UAC.
        /// </summary>
        /// <param name="arguments">
        /// Opciones para la nueva instancia. Debe incluir la espera a la anterior:
        /// la nueva arranca mientras esta todavia cierra y guarda la configuracion.
        /// </param>
        public static bool Relaunch(string arguments, out string error)
        {
            error = null;
            try
            {
                string path = Assembly.GetEntryAssembly().Location;

                ProcessStartInfo info = new ProcessStartInfo(path, arguments ?? "");
                info.UseShellExecute = true;   // obligatorio para que runas funcione
                info.Verb = "runas";

                Process.Start(info);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // 1223 (ERROR_CANCELLED): el usuario dijo que no al UAC. No es
                // un error que haya que contarle.
                error = ex.NativeErrorCode == 1223 ? null : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
