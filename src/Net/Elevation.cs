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

        /// <summary>Relanza el propio ejecutable con el verbo runas. Devuelve false si el usuario cancela el UAC.</summary>
        public static bool Relaunch(out string error)
        {
            error = null;
            try
            {
                string path = Assembly.GetEntryAssembly().Location;

                ProcessStartInfo info = new ProcessStartInfo(path);
                info.UseShellExecute = true;   // obligatorio para que runas funcione
                info.Verb = "runas";

                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
