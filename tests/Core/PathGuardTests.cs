using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Contencion de rutas. Es el control de seguridad del servidor: si algo
    /// de aqui cede, se puede leer cualquier archivo del disco.
    /// </summary>
    public static class PathGuardTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / PathGuard");

            runner.Test("caracteres prohibidos en la ruta relativa", delegate
            {
                Check.True(PathGuard.RelativePathIsSafe("css/app.css"), "ruta normal");
                Check.True(PathGuard.RelativePathIsSafe(""), "vacia");
                Check.False(PathGuard.RelativePathIsSafe("archivo.txt:oculto"), "flujo alternativo NTFS");
                Check.False(PathGuard.RelativePathIsSafe("C:/algo"), "unidad");
                Check.False(PathGuard.RelativePathIsSafe("a\0b"), "byte nulo");
                Check.False(PathGuard.RelativePathIsSafe(null), "nula");
            });

            runner.Test("prefijo: dentro, fuera y la raiz misma", delegate
            {
                const string root = @"C:\www\";
                Check.True(PathGuard.LooksInsideRoot(@"C:\www\index.html", root), "dentro");
                Check.True(PathGuard.LooksInsideRoot(@"C:\www\docs\a.js", root), "subcarpeta");
                Check.True(PathGuard.LooksInsideRoot(@"C:\www", root), "la raiz sin separador final");
                Check.False(PathGuard.LooksInsideRoot(@"C:\otro\x.txt", root), "fuera");
                Check.False(PathGuard.LooksInsideRoot("", root), "vacia");
            });

            runner.Test("SEGURIDAD: una carpeta hermana con el mismo prefijo no cuela", delegate
            {
                const string root = @"C:\www\";
                Check.False(PathGuard.LooksInsideRoot(@"C:\wwwEVIL\robo.txt", root), "wwwEVIL");
                Check.False(PathGuard.LooksInsideRoot(@"C:\www-copia\robo.txt", root), "www-copia");
                Check.False(PathGuard.LooksInsideRoot(@"C:\wwwx", root), "wwwx");
            });

            runner.Test("el prefijo no distingue mayusculas, como NTFS", delegate
            {
                Check.True(PathGuard.LooksInsideRoot(@"C:\WWW\Index.HTML", @"C:\www\"), "mayusculas");
            });

            runner.Test("canonizar una carpeta real devuelve ruta con separador", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string real = PathGuard.CanonicalizeRoot(tree.Root);
                    Check.NotEmpty(real, "ruta canonizada");
                    Check.True(real.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal),
                               "termina en separador");
                }
            });

            runner.Test("canonizar algo inexistente devuelve vacio", delegate
            {
                string real = PathGuard.CanonicalizeRoot(@"C:\no-existe-" + Guid.NewGuid().ToString("N"));
                Check.Empty(real, "sin canonizar");
            });

            runner.Test("abrir y validar un archivo dentro de la raiz", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string file = tree.File("index.html", "<html></html>");
                    string root = PathGuard.CanonicalizeRoot(tree.Root);

                    SafeFileHandle handle;
                    GuardResult result = PathGuard.TryOpenInsideRoot(file, root, out handle);

                    Check.Equal(GuardResult.Ok, result, "resultado");
                    Check.NotNull(handle, "handle");
                    if (handle != null) handle.Dispose();
                }
            });

            runner.Test("un archivo inexistente da NotFound y no deja handle", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string root = PathGuard.CanonicalizeRoot(tree.Root);
                    SafeFileHandle handle;
                    GuardResult result = PathGuard.TryOpenInsideRoot(
                        Path.Combine(tree.Root, "no-existe.txt"), root, out handle);

                    Check.Equal(GuardResult.NotFound, result, "resultado");
                    Check.Null(handle, "no debe devolver handle");
                }
            });

            runner.Test("SEGURIDAD: un archivo fuera de la raiz da Forbidden", delegate
            {
                using (TempTree inside = new TempTree())
                using (TempTree outside = new TempTree())
                {
                    string secret = outside.File("secreto.txt", "no deberia leerse");
                    string root = PathGuard.CanonicalizeRoot(inside.Root);

                    SafeFileHandle handle;
                    GuardResult result = PathGuard.TryOpenInsideRoot(secret, root, out handle);

                    Check.Equal(GuardResult.Forbidden, result, "resultado");
                    Check.Null(handle, "no debe devolver handle");
                }
            });
        }
    }
}
