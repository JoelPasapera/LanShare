using System;
using System.Security;
using System.Text;

namespace LanShare.Platform
{
    /// <summary>
    /// Definicion XML de la tarea programada que arranca LanShare al iniciar
    /// sesion.
    ///
    /// Se registra con XML y no con los parametros de schtasks porque los
    /// valores por defecto de una tarea son malos para un servidor: no arranca
    /// si el portatil esta con bateria, se detiene al desenchufarlo y se mata a
    /// los tres dias de ejecucion. Ninguno de esos tres se puede cambiar desde
    /// la linea de comandos de schtasks.
    /// </summary>
    public static class ScheduledTaskXml
    {
        /// <summary>Retraso tras iniciar sesion, para dar tiempo a que suba la red.</summary>
        public const string LogonDelay = "PT10S";

        /// <param name="elevated">
        /// Verdadero registra la tarea con privilegios maximos: arranca como
        /// administrador sin pasar por el UAC, que es lo que necesitan los
        /// sitios en red local. Solo puede registrarla un administrador.
        /// </param>
        public static string Build(string command, string arguments, string workingDirectory,
                                   string userId, bool elevated)
        {
            if (string.IsNullOrEmpty(command)) throw new ArgumentException("Falta el ejecutable.", "command");
            if (string.IsNullOrEmpty(userId)) throw new ArgumentException("Falta el usuario.", "userId");

            StringBuilder xml = new StringBuilder(2048);
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n");
            xml.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n");

            xml.Append("  <RegistrationInfo>\r\n");
            Element(xml, 4, "Description", "Inicia LanShare al entrar en Windows y sirve los sitios marcados.");
            Element(xml, 4, "Author", "LanShare");
            xml.Append("  </RegistrationInfo>\r\n");

            xml.Append("  <Triggers>\r\n");
            xml.Append("    <LogonTrigger>\r\n");
            Element(xml, 6, "Enabled", "true");
            Element(xml, 6, "UserId", userId);
            Element(xml, 6, "Delay", LogonDelay);
            xml.Append("    </LogonTrigger>\r\n");
            xml.Append("  </Triggers>\r\n");

            xml.Append("  <Principals>\r\n");
            xml.Append("    <Principal id=\"Author\">\r\n");
            Element(xml, 6, "UserId", userId);
            Element(xml, 6, "LogonType", "InteractiveToken");
            Element(xml, 6, "RunLevel", elevated ? "HighestAvailable" : "LeastPrivilege");
            xml.Append("    </Principal>\r\n");
            xml.Append("  </Principals>\r\n");

            xml.Append("  <Settings>\r\n");
            Element(xml, 4, "MultipleInstancesPolicy", "IgnoreNew");
            Element(xml, 4, "DisallowStartIfOnBatteries", "false");
            Element(xml, 4, "StopIfGoingOnBatteries", "false");
            Element(xml, 4, "AllowHardTerminate", "true");
            Element(xml, 4, "StartWhenAvailable", "false");
            Element(xml, 4, "RunOnlyIfNetworkAvailable", "false");
            xml.Append("    <IdleSettings>\r\n");
            Element(xml, 6, "StopOnIdleEnd", "false");
            Element(xml, 6, "RestartOnIdle", "false");
            xml.Append("    </IdleSettings>\r\n");
            Element(xml, 4, "AllowStartOnDemand", "true");
            Element(xml, 4, "Enabled", "true");
            Element(xml, 4, "Hidden", "false");
            Element(xml, 4, "RunOnlyIfIdle", "false");
            Element(xml, 4, "WakeToRun", "false");
            // PT0S es "sin limite": el valor por defecto mata la tarea a los 3 dias.
            Element(xml, 4, "ExecutionTimeLimit", "PT0S");
            // 5 es prioridad normal; el valor por defecto de una tarea, 7, es inferior.
            Element(xml, 4, "Priority", "5");
            xml.Append("  </Settings>\r\n");

            xml.Append("  <Actions Context=\"Author\">\r\n");
            xml.Append("    <Exec>\r\n");
            Element(xml, 6, "Command", command);
            if (!string.IsNullOrEmpty(arguments)) Element(xml, 6, "Arguments", arguments);
            if (!string.IsNullOrEmpty(workingDirectory)) Element(xml, 6, "WorkingDirectory", workingDirectory);
            xml.Append("    </Exec>\r\n");
            xml.Append("  </Actions>\r\n");

            xml.Append("</Task>\r\n");
            return xml.ToString();
        }

        /// <summary>
        /// Rutas y nombres de usuario pueden llevar ampersand o apostrofes; sin
        /// escapar, el XML queda invalido y schtasks lo rechaza.
        /// </summary>
        private static void Element(StringBuilder xml, int indent, string name, string value)
        {
            xml.Append(' ', indent).Append('<').Append(name).Append('>')
               .Append(SecurityElement.Escape(value ?? ""))
               .Append("</").Append(name).Append(">\r\n");
        }
    }
}
