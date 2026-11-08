using System;
using System.IO;
using LanShare.Platform;

namespace LanShare.Tests
{
    /// <summary>
    /// La marca que impide repetir una llamada nativa que tumbo el proceso.
    /// Todo en una carpeta temporal.
    /// </summary>
    public static class CrashGuardTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Platform / proteccion de llamadas nativas");

            runner.Test("una llamada que acaba bien no deja marca", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    CrashGuard guard = new CrashGuard(tree.Root, "prueba");
                    bool called = false;
                    guard.Run(delegate { called = true; });

                    Check.True(called, "se llamo");
                    Check.False(guard.Tripped, "sin marca");
                    Check.False(File.Exists(guard.MarkerPath), "sin archivo");
                }
            });

            runner.Test("durante la llamada la marca existe", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    CrashGuard guard = new CrashGuard(tree.Root, "prueba");
                    bool markedInside = false;
                    guard.Run(delegate { markedInside = File.Exists(guard.MarkerPath); });
                    Check.True(markedInside, "marcada mientras dura");
                }
            });

            runner.Test("una excepcion normal quita la marca y sigue su camino", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    CrashGuard guard = new CrashGuard(tree.Root, "prueba");
                    Check.Throws(delegate { guard.Run(delegate { throw new InvalidOperationException("fallo"); }); },
                                 "la excepcion sale");
                    Check.False(guard.Tripped, "solo una muerte del proceso deja la marca");
                }
            });

            runner.Test("CLAVE: una marca que quedo de antes desactiva la llamada", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    CrashGuard guard = new CrashGuard(tree.Root, "prueba");
                    File.WriteAllText(guard.MarkerPath, "proceso muerto a mitad");
                    Check.True(guard.Tripped, "detectada");
                    Check.True(new CrashGuard(tree.Root, "prueba").Tripped, "tambien en la sesion siguiente");
                    Check.False(new CrashGuard(tree.Root, "otra").Tripped, "cada llamada tiene su marca");
                }
            });

            runner.Test("la carpeta de la marca se crea si falta", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    CrashGuard guard = new CrashGuard(Path.Combine(tree.Root, "nueva"), "prueba");
                    bool markedInside = false;
                    guard.Run(delegate { markedInside = File.Exists(guard.MarkerPath); });
                    Check.True(markedInside, "marcada en una carpeta nueva");
                }
            });
        }
    }
}
