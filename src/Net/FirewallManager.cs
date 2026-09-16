using System;
using System.Diagnostics;

namespace LanShare.Net
{
    /// <summary>
    /// Alta y baja de la regla de firewall del puerto servido.
    ///
    /// El perfil se limita a privado y dominio a proposito: abrir el puerto en
    /// el perfil publico expondria el servidor en redes no confiables. Si el
    /// adaptador esta clasificado como publico, la regla no aplicara y el modo
    /// LAN no sera alcanzable; ese es el comportamiento deseado, y la interfaz
    /// avisa de ello.
    /// </summary>
    public static class FirewallManager
    {
        public static string BuildRuleName(int port)
        {
            return "LanShare (TCP " + port + ")";
        }

        public static bool TryAdd(int port, out string error)
        {
            error = null;
            string name = BuildRuleName(port);

            string arguments = string.Format(
                "advfirewall firewall add rule name=\"{0}\" dir=in action=allow " +
                "protocol=TCP localport={1} profile=private,domain enable=yes",
                name, port);

            return RunNetsh(arguments, out error);
        }

        public static void TryRemove(int port)
        {
            string error;
            RunNetsh(string.Format("advfirewall firewall delete rule name=\"{0}\"", BuildRuleName(port)), out error);
        }

        /// <summary>
        /// netsh en vez de los cmdlets de PowerShell: no hay que cargar un
        /// motor de scripting solo para dar de alta una regla.
        /// </summary>
        private static bool RunNetsh(string arguments, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("netsh", arguments);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                using (Process process = Process.Start(info))
                {
                    if (process == null) { error = "No se pudo lanzar netsh"; return false; }

                    string output = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);

                    if (process.ExitCode != 0)
                    {
                        error = string.IsNullOrEmpty(stderr) ? output.Trim() : stderr.Trim();
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
