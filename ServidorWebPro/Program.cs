using System;
using System.Threading;
using System.Windows.Forms;
using ServidorWebPro.Ui;

namespace ServidorWebPro
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Al compilarse como winexe no hay consola donde leer un fallo, asi
            // que cualquier excepcion no controlada se muestra en un dialogo.
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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
