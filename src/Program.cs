using System;
using System.Threading;
using System.Windows.Forms;
using LanShare.Ui;

namespace LanShare
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Al compilarse como winexe no hay consola donde leer un fallo, asi
            // que cualquier excepcion no controlada se muestra en un dialogo.
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            StartupOptions options = StartupOptions.Parse(args);

            // El mutex vive mientras dure el using, es decir, toda la sesion de
            // la aplicacion: si el recolector lo liberase antes, una segunda
            // instancia lo encontraria libre.
            using (SingleInstance instance = new SingleInstance(options.WaitForPreviousInstance ? 60000 : 0))
            {
                if (!instance.IsFirst)
                {
                    SingleInstance.SignalExisting();

                    // Relanzada para escalar privilegios, la anterior no se cerro
                    // a tiempo (un dialogo abierto, por ejemplo). Sin este aviso
                    // no quedaria ninguna instancia nueva y no se sabria por que.
                    if (options.WaitForPreviousInstance)
                    {
                        MessageBox.Show("La ventana anterior de LanShare no se cerro a tiempo, asi que esta, " +
                                        "como administrador, no puede abrirse.\r\n\r\nCierra LanShare y vuelve " +
                                        "a abrirlo como administrador.", AppInfo.Name,
                                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(options));
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Report(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void Report(Exception ex)
        {
            string message = ex != null
                ? ex.GetType().FullName + "\r\n\r\n" + ex.Message + "\r\n\r\n" + ex.StackTrace
                : "Error desconocido.";

            MessageBox.Show(message, "Error no controlado",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
