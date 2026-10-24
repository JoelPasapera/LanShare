using System;
using System.Collections.Generic;

namespace LanShare
{
    /// <summary>
    /// Opciones de arranque leidas de la linea de comandos.
    ///
    ///   --start          inicia los sitios marcados nada mas abrir
    ///   --tray           arranca oculto en la bandeja del sistema
    ///   --wait-instance  espera a que la instancia anterior termine (relanzado
    ///                    al escalar privilegios)
    ///
    /// Se aceptan tambien con barra (/start) y sin distinguir mayusculas, que
    /// es como suelen escribirse en accesos directos de Windows.
    /// </summary>
    public sealed class StartupOptions
    {
        public bool StartServer { get; private set; }
        public bool StartInTray { get; private set; }
        public bool WaitForPreviousInstance { get; private set; }

        /// <summary>Argumentos no reconocidos; se informan en el registro.</summary>
        public IList<string> Unknown { get; private set; }

        private StartupOptions()
        {
            Unknown = new List<string>();
        }

        public static StartupOptions Empty
        {
            get { return new StartupOptions(); }
        }

        public static StartupOptions Parse(string[] args)
        {
            StartupOptions options = new StartupOptions();
            if (args == null) return options;

            foreach (string raw in args)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                string arg = raw.Trim();
                if (arg.Length == 0) continue;

                string name = Normalize(arg);
                switch (name)
                {
                    case "start":
                    case "iniciar":
                        options.StartServer = true;
                        break;
                    case "tray":
                    case "bandeja":
                    case "minimized":
                        options.StartInTray = true;
                        break;
                    case "wait-instance":
                        options.WaitForPreviousInstance = true;
                        break;
                    default:
                        options.Unknown.Add(raw);
                        break;
                }
            }

            return options;
        }

        /// <summary>Quita el prefijo (--, - o /) y pasa a minusculas.</summary>
        private static string Normalize(string arg)
        {
            int start = 0;
            while (start < arg.Length && (arg[start] == '-' || arg[start] == '/')) start++;

            // Sin prefijo no es una opcion, aunque coincida el texto: evita que
            // una ruta llamada "start" active algo por accidente.
            if (start == 0) return string.Empty;

            return arg.Substring(start).ToLowerInvariant();
        }

        /// <summary>Linea de comandos equivalente, para relanzar o programar.</summary>
        public static string Build(bool startServer, bool startInTray, bool waitForPrevious)
        {
            List<string> parts = new List<string>();
            if (startServer) parts.Add("--start");
            if (startInTray) parts.Add("--tray");
            if (waitForPrevious) parts.Add("--wait-instance");
            return string.Join(" ", parts.ToArray());
        }
    }
}
