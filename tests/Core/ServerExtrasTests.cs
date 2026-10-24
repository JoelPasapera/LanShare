using System;
using System.Collections.Generic;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>Estadisticas de la cache, registro por sitio y enlace de subida.</summary>
    public static class ServerExtrasTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / CompressionCache - estadisticas");

            runner.Test("aciertos y fallos se cuentan por consulta", delegate
            {
                CompressionCache.Clear();
                CompressionCache.ResetStatistics();
                Check.Equal(-1, CompressionCache.HitRatePercent, "sin consultas no hay porcentaje");

                CompressionCache.Get("no-esta");
                CompressionCache.Store("esta", new byte[] { 1 });
                CompressionCache.Get("esta");
                CompressionCache.Get("esta");
                CompressionCache.Get("esta");

                Check.Equal(3L, CompressionCache.Hits, "aciertos");
                Check.Equal(1L, CompressionCache.Misses, "fallos");
                Check.Equal(75, CompressionCache.HitRatePercent, "porcentaje");

                CompressionCache.ResetStatistics();
                Check.Equal(0L, CompressionCache.Hits, "aciertos reiniciados");
                Check.Equal(1, CompressionCache.Count, "reiniciar no vacia la cache");
                CompressionCache.Clear();
            });

            runner.Test("una clave nula no cuenta como consulta", delegate
            {
                CompressionCache.ResetStatistics();
                CompressionCache.Get(null);
                Check.Equal(0L, CompressionCache.Misses, "sin fallo");
            });

            runner.Suite("Core / LogBus - una linea por entrada");

            runner.Test("cada entrada ocupa exactamente una linea", delegate
            {
                LogBus log = new LogBus();
                log.Write("INFO", "arranque");
                log.WriteRequest("INFO", "GET", "/a.js", 200, 3, 1200, " [gzip 60%]", "::1", 3000);

                foreach (LogEntry entry in log.Drain(10))
                {
                    int breaks = entry.Text.Split('\n').Length - 1;
                    Check.Equal(1, breaks, "un solo salto de linea en: " + entry.Text);
                    Check.True(entry.Text.EndsWith("\r\n", StringComparison.Ordinal), "termina en CRLF");
                }
            });

            runner.Test("un mensaje del servidor puede pertenecer a un sitio", delegate
            {
                LogBus log = new LogBus();
                log.Write("INFO", "cambio en disco", 5173);
                List<LogEntry> batch = log.Drain(10);
                Check.Equal(5173, batch[0].Port, "lleva el puerto para el filtro");
            });

            runner.Suite("Core / DirectoryListing - subida");

            runner.Test("con la subida activa, el listado enlaza a ella", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a.txt", "x");
                    string con = Encoding.UTF8.GetString(DirectoryListing.Build(tree.Root, "/", true));
                    string sin = Encoding.UTF8.GetString(DirectoryListing.Build(tree.Root, "/", false));
                    Check.Contains(con, UploadHandler.Endpoint, "enlace presente");
                    Check.DoesNotContain(sin, UploadHandler.Endpoint, "sin enlace si esta apagada");
                }
            });
        }
    }
}
