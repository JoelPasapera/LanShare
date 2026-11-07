using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LanShare.Platform
{
    /// <summary>
    /// Inicio automatico al entrar en Windows.
    ///
    /// Hay dos mecanismos y se elige segun los privilegios del momento:
    ///   - Con administrador, una tarea programada con privilegios maximos.
    ///     Arranca elevado sin mostrar el UAC, que es lo que necesitan los
    ///     sitios en red local.
    ///   - Sin administrador, la clave Run del usuario. Funciona siempre, pero
    ///     arranca sin elevar: los sitios en red local no podran iniciarse.
    /// </summary>
    public static class AutoStart
    {
        public const string TaskName = "LanShare";
        public const string Arguments = "--start --tray";

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string TaskCacheTree = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree";
        private const string RunValueName = "LanShare";

        /// <summary>Por que via esta registrado, si lo esta.</summary>
        public static AutoStartMode Current()
        {
            if (TaskExists()) return AutoStartMode.ScheduledTask;
            if (RunValue() != null) return AutoStartMode.RunKey;
            return AutoStartMode.None;
        }

        public static bool Enable(string executablePath, bool elevated, out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            {
                message = "No se encontro el ejecutable de LanShare.";
                return false;
            }

            if (elevated)
            {
                string error;
                if (CreateTask(executablePath, out error))
                {
                    // Una sola via: con las dos, la segunda instancia solo
                    // despertaria a la primera, pero es trabajo inutil al iniciar.
                    DeleteRunValue();
                    message = "LanShare se iniciara al entrar en Windows, como administrador y en la " +
                              "bandeja, sirviendo los sitios marcados.";
                    return true;
                }
                message = "No se pudo crear la tarea programada: " + error;
                return false;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) throw new IOException("No se pudo abrir la clave Run.");
                    key.SetValue(RunValueName, Quote(executablePath) + " " + Arguments, RegistryValueKind.String);
                }
                message = "LanShare se iniciara al entrar en Windows, en la bandeja.\r\n\r\n" +
                          "Arrancara SIN privilegios de administrador, asi que los sitios en red local " +
                          "no podran iniciarse solos. Para que arranque elevado, abre LanShare como " +
                          "administrador y vuelve a activar esta opcion.";
                return true;
            }
            catch (Exception ex)
            {
                message = "No se pudo registrar el inicio automatico: " + ex.Message;
                return false;
            }
        }

        public static bool Disable(out string message)
        {
            message = "";
            bool ok = true;

            if (TaskExists())
            {
                int code;
                string output = RunSchtasks("/Delete /TN " + Quote(TaskName) + " /F", out code);
                if (code != 0)
                {
                    ok = false;
                    message = "No se pudo borrar la tarea programada (" + output.Trim() + "). Si la creaste " +
                              "como administrador, abre LanShare como administrador para quitarla.";
                }
            }

            if (!DeleteRunValue())
            {
                ok = false;
                if (message.Length == 0) message = "No se pudo borrar la entrada de inicio del registro.";
            }

            return ok;
        }

        /// <summary>
        /// Si el ejecutable cambio de carpeta, la entrada de la clave Run apunta
        /// a una ruta que ya no existe. Se corrige en silencio al arrancar.
        /// </summary>
        public static void RefreshRunKeyPath(string executablePath)
        {
            try
            {
                string current = RunValue();
                if (current == null || string.IsNullOrEmpty(executablePath)) return;

                string expected = Quote(executablePath) + " " + Arguments;
                if (string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)) return;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null) key.SetValue(RunValueName, expected, RegistryValueKind.String);
                }
            }
            catch (Exception) { }
        }

        private static bool CreateTask(string executablePath, out string error)
        {
            error = "";
            string xmlPath = Path.Combine(Path.GetTempPath(), "lanshare-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                string user = WindowsIdentity.GetCurrent().Name;
                string xml = ScheduledTaskXml.Build(executablePath, Arguments,
                                                    Path.GetDirectoryName(executablePath), user, true);

                // schtasks exige UTF-16 con BOM para importar XML; en UTF-8
                // responde que el XML esta mal formado.
                File.WriteAllText(xmlPath, xml, Encoding.Unicode);

                int code;
                string output = RunSchtasks("/Create /TN " + Quote(TaskName) + " /XML " + Quote(xmlPath) + " /F", out code);
                if (code != 0) error = output.Trim();
                return code == 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(xmlPath)) File.Delete(xmlPath); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Primero se mira la cache de tareas del registro, que es instantaneo;
        /// solo si no se puede leer se pregunta a schtasks.exe, que tarda lo que
        /// tarda arrancar un proceso y se notaba al abrir Configuracion.
        /// </summary>
        private static bool TaskExists()
        {
            bool exists;
            if (TryTaskCache(out exists)) return exists;

            int code;
            RunSchtasks("/Query /TN " + Quote(TaskName), out code);
            return code == 0;
        }

        /// <summary>
        /// El Programador de tareas guarda cada tarea registrada como una clave
        /// en TaskCache\Tree. Devuelve false si no se pudo consultar.
        /// </summary>
        private static bool TryTaskCache(out bool exists)
        {
            exists = false;
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey tree = machine.OpenSubKey(TaskCacheTree, false))
                {
                    if (tree == null) return false;
                    using (RegistryKey task = tree.OpenSubKey(TaskName, false))
                    {
                        exists = task != null;
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string RunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key == null ? null : key.GetValue(RunValueName) as string;
                }
            }
            catch (Exception) { return null; }
        }

        private static bool DeleteRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(RunValueName) != null) key.DeleteValue(RunValueName, false);
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string RunSchtasks(string arguments, out int exitCode)
        {
            exitCode = -1;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("schtasks.exe", arguments);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                using (Process process = Process.Start(info))
                {
                    if (process == null) return "";

                    // Las dos salidas se leen a la vez: leer una entera antes de
                    // la otra se bloquea si el programa llena primero la segunda.
                    // Y asi la espera tiene tope de verdad: un ReadToEnd sincrono
                    // no volvia hasta que el proceso terminase, colgado o no.
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> errors = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); }
                        catch (Exception) { }
                        return "schtasks no respondio";
                    }

                    if (!Task.WaitAll(new Task[] { output, errors }, 5000)) return "schtasks no cerro su salida";

                    exitCode = process.ExitCode;
                    return output.Result + errors.Result;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }
    }

    public enum AutoStartMode
    {
        None,
        RunKey,
        ScheduledTask
    }
}
