using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    public static class InfrastructureTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / BufferPool");

            runner.Test("los buffers salen del tamano declarado", delegate
            {
                byte[] buffer = BufferPool.Rent();
                Check.Equal(BufferPool.BufferSize, buffer.Length, "tamano");
                BufferPool.Return(buffer);
            });

            runner.Test("un buffer devuelto se reutiliza", delegate
            {
                byte[] primero = BufferPool.Rent();
                BufferPool.Return(primero);
                byte[] segundo = BufferPool.Rent();

                Check.True(object.ReferenceEquals(primero, segundo),
                           "deberia ser la misma instancia, no una nueva");
                BufferPool.Return(segundo);
            });

            runner.Test("un buffer de otro tamano se rechaza", delegate
            {
                BufferPool.Return(new byte[123]);
                byte[] buffer = BufferPool.Rent();
                Check.Equal(BufferPool.BufferSize, buffer.Length, "no se colo el intruso");
                BufferPool.Return(buffer);
            });

            runner.Test("devolver nulo no revienta", delegate
            {
                Check.DoesNotThrow(delegate { BufferPool.Return(null); }, "nulo");
            });

            runner.Test("uso concurrente no corrompe nada", delegate
            {
                const int hilos = 8;
                const int vueltas = 200;
                Exception fallo = null;

                Thread[] equipo = new Thread[hilos];
                for (int i = 0; i < hilos; i++)
                {
                    equipo[i] = new Thread(delegate()
                    {
                        try
                        {
                            for (int v = 0; v < vueltas; v++)
                            {
                                byte[] buffer = BufferPool.Rent();
                                if (buffer.Length != BufferPool.BufferSize)
                                    throw new Exception("tamano incorrecto bajo concurrencia");
                                BufferPool.Return(buffer);
                            }
                        }
                        catch (Exception ex) { fallo = ex; }
                    });
                    equipo[i].Start();
                }
                foreach (Thread hilo in equipo) hilo.Join(10000);

                Check.Null(fallo, "sin fallos en los hilos");
            });

            runner.Suite("Core / LogBus");

            runner.Test("empieza vacia", delegate
            {
                LogBus log = new LogBus();
                Check.True(log.IsEmpty, "vacia");
                Check.Equal(0, log.Drain(100).Count, "drenaje vacio");
            });

            runner.Test("lo escrito se recupera", delegate
            {
                LogBus log = new LogBus();
                log.Write("INFO", "mensaje de prueba");

                List<LogEntry> lote = log.Drain(100);
                Check.Equal(1, lote.Count, "una entrada");
                Check.Contains(lote[0].Text, "mensaje de prueba", "contenido");
                Check.Contains(lote[0].Text, "[INFO]", "nivel");
                Check.Equal(0, lote[0].Port, "un mensaje del servidor no lleva puerto");
                Check.True(log.IsEmpty, "queda vacia tras drenar");
            });

            runner.Test("la linea de peticion lleva puerto, codigo y bytes", delegate
            {
                LogBus log = new LogBus();
                log.WriteRequest("INFO", "GET", "/css/app.css", 200, 15, 8471, " [gzip 76%]", "::1", 3000);

                List<LogEntry> lote = log.Drain(100);
                Check.Equal(1, lote.Count, "una entrada");
                Check.Equal(3000, lote[0].Port, "el puerto viaja aparte, para poder filtrar");

                string salida = lote[0].Text;
                Check.Contains(salida, ":3000", "puerto en el texto");
                Check.Contains(salida, "/css/app.css", "ruta");
                Check.Contains(salida, "200", "codigo");
                Check.Contains(salida, "8471B", "bytes");
                Check.Contains(salida, "[gzip 76%]", "nota");
                Check.Contains(salida, "::1", "cliente");
            });

            runner.Test("el drenaje respeta el tope por vuelta", delegate
            {
                LogBus log = new LogBus();
                for (int i = 0; i < 20; i++) log.Write("INFO", "linea " + i);

                Check.Equal(5, log.Drain(5).Count, "cinco por vuelta");
                Check.False(log.IsEmpty, "quedan mensajes pendientes");
            });

            runner.Test("cada entrada conserva el puerto de su peticion", delegate
            {
                LogBus log = new LogBus();
                log.WriteRequest("INFO", "GET", "/a", 200, 1, 10, "", "::1", 8080);
                log.WriteRequest("INFO", "GET", "/b", 200, 1, 10, "", "::1", 3000);
                log.Write("INFO", "mensaje del servidor");

                List<LogEntry> lote = log.Drain(100);
                Check.Equal(3, lote.Count, "tres entradas");
                Check.Equal(8080, lote[0].Port, "primera");
                Check.Equal(3000, lote[1].Port, "segunda");
                Check.Equal(0, lote[2].Port, "la del servidor sin puerto");
            });

            runner.Test("al saturarse descarta y lo dice", delegate
            {
                LogBus log = new LogBus();
                for (int i = 0; i < 21000; i++) log.Write("INFO", "x");

                List<LogEntry> lote = log.Drain(30000);
                bool aviso = false;
                foreach (LogEntry entry in lote)
                {
                    if (entry.Text.IndexOf("descartadas", StringComparison.Ordinal) >= 0)
                    {
                        aviso = true;
                        Check.Equal(0, entry.Port,
                                    "el aviso no lleva puerto: debe verse con cualquier filtro");
                    }
                }
                Check.True(aviso, "avisa del descarte");
            });

            runner.Test("escritura concurrente sin perdidas ni corrupcion", delegate
            {
                LogBus log = new LogBus();
                const int hilos = 6;
                const int porHilo = 100;

                Thread[] equipo = new Thread[hilos];
                for (int i = 0; i < hilos; i++)
                {
                    equipo[i] = new Thread(delegate()
                    {
                        for (int v = 0; v < porHilo; v++) log.Write("INFO", "concurrente");
                    });
                    equipo[i].Start();
                }
                foreach (Thread hilo in equipo) hilo.Join(10000);

                int contadas = 0;
                List<LogEntry> lote;
                while ((lote = log.Drain(1000)).Count > 0)
                {
                    foreach (LogEntry entry in lote)
                    {
                        if (entry.Text.IndexOf("concurrente", StringComparison.Ordinal) >= 0) contadas++;
                    }
                }

                Check.Equal(hilos * porHilo, contadas, "todos los mensajes");
            });
        }
    }
}
