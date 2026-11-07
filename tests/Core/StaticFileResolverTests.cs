using System;
using System.IO;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Traduccion de URL a decision de servicio. Aqui vivio el fallo de que la
    /// raiz sin index.html devolvia 404 en lugar del listado.
    /// </summary>
    public static class StaticFileResolverTests
    {
        private static Resolution Resolve(TempTree tree, string localPath, bool listing)
        {
            return Resolve(tree, localPath, StaticFileResolver.DefaultIndexFile, listing);
        }

        private static Resolution Resolve(TempTree tree, string localPath, string indexFile, bool listing)
        {
            string query = "";
            int mark = localPath.IndexOf('?');
            if (mark >= 0)
            {
                query = localPath.Substring(mark);
                localPath = localPath.Substring(0, mark);
            }

            return StaticFileResolver.Resolve(localPath, localPath, query,
                                              tree.RootWithSeparator, indexFile, listing);
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / StaticFileResolver");

            runner.Test("la raiz con index.html sirve el indice", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "raiz");
                    Resolution r = Resolve(tree, "/", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "index.html", "archivo");
                }
            });

            runner.Test("REGRESION: la raiz sin index.html se lista", delegate
            {
                // Antes devolvia 404: al sustituir la ruta vacia por index.html
                // nunca se llegaba a la rama de directorio.
                using (TempTree tree = new TempTree())
                {
                    tree.File("datos.txt", "hola");
                    Resolution r = Resolve(tree, "/", true);
                    Check.Equal(ResolutionKind.DirectoryListing, r.Kind, "tipo");
                    Check.NotEmpty(r.DirectoryPath, "carpeta");
                }
            });

            runner.Test("la raiz sin index y sin listado da 404", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("datos.txt", "hola");
                    Check.Equal(ResolutionKind.NotFound, Resolve(tree, "/", false).Kind, "tipo");
                }
            });

            runner.Test("un archivo normal se sirve", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("css/app.css", "body{}");
                    Resolution r = Resolve(tree, "/css/app.css", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "app.css", "archivo");
                }
            });

            runner.Test("una carpeta sin barra final se redirige", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Dir("docs");
                    Resolution r = Resolve(tree, "/docs", true);
                    Check.Equal(ResolutionKind.RedirectToDirectory, r.Kind, "tipo");
                    Check.Equal("/docs/", r.RedirectLocation, "destino");
                }
            });

            runner.Test("la redireccion conserva la cadena de consulta", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Dir("docs");
                    Resolution r = Resolve(tree, "/docs?a=1&b=2", true);
                    Check.Equal(ResolutionKind.RedirectToDirectory, r.Kind, "tipo");
                    Check.Equal("/docs/?a=1&b=2", r.RedirectLocation, "destino");
                }
            });

            runner.Test("subcarpeta con su propio indice", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("docs/index.html", "docs");
                    Resolution r = Resolve(tree, "/docs/", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "index.html", "archivo");
                }
            });

            runner.Test("subcarpeta sin indice se lista", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("docs/nota.txt", "x");
                    Check.Equal(ResolutionKind.DirectoryListing, Resolve(tree, "/docs/", true).Kind, "tipo");
                }
            });

            runner.Test("carpeta vacia tambien se lista", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Dir("vacia");
                    Check.Equal(ResolutionKind.DirectoryListing, Resolve(tree, "/vacia/", true).Kind, "tipo");
                }
            });

            runner.Test("repliegue de pagina unica solo sin extension", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "spa");
                    Resolution ruta = Resolve(tree, "/perfil/ajustes", true);
                    Check.Equal(ResolutionKind.File, ruta.Kind, "ruta sin extension");
                    Check.Contains(ruta.FilePath, "index.html", "sirve el indice");
                }
            });

            runner.Test("un recurso con extension que falta NO secuestra el indice", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "spa");
                    Check.Equal(ResolutionKind.NotFound, Resolve(tree, "/falta.js", true).Kind, "js ausente");
                    Check.Equal(ResolutionKind.NotFound, Resolve(tree, "/falta.png", true).Kind, "png ausente");
                }
            });

            runner.Test("sin indice en la raiz no hay repliegue", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("datos.txt", "x");
                    Check.Equal(ResolutionKind.NotFound, Resolve(tree, "/perfil/ajustes", false).Kind, "sin indice");
                }
            });

            runner.Test("SEGURIDAD: salir de la raiz se bloquea", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    Check.Equal(ResolutionKind.Forbidden, Resolve(tree, "/../secreto.txt", true).Kind, "..");
                    Check.Equal(ResolutionKind.Forbidden, Resolve(tree, "/../../Windows/win.ini", true).Kind, "dos niveles");
                    Check.Equal(ResolutionKind.Forbidden, Resolve(tree, "/docs/../../fuera.txt", true).Kind, "por subcarpeta");
                }
            });

            runner.Test("SEGURIDAD: caracteres prohibidos dan 400", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    Check.Equal(ResolutionKind.BadRequest, Resolve(tree, "/a.txt:oculto", true).Kind, "flujo NTFS");
                    Check.Equal(ResolutionKind.BadRequest, Resolve(tree, "/C:/Windows", true).Kind, "unidad");
                }
            });

            runner.Test("volver a la raiz por ruta relativa sigue dentro", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "raiz");
                    tree.Dir("docs");
                    Resolution r = Resolve(tree, "/docs/../index.html", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                }
            });

            runner.Suite("Core / StaticFileResolver - archivo de inicio");

            runner.Test("un nombre propio sustituye a index.html", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("inicio.html", "portada");
                    Resolution r = Resolve(tree, "/", "inicio.html", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "inicio.html", "archivo");
                }
            });

            runner.Test("con nombre propio, index.html deja de mandar", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "el que no toca");
                    tree.File("inicio.html", "la portada buena");

                    Resolution r = Resolve(tree, "/", "inicio.html", true);
                    Check.Contains(r.FilePath, "inicio.html", "gana el configurado");
                    Check.DoesNotContain(r.FilePath, "index.html", "no el de siempre");
                }
            });

            runner.Test("el nombre propio tambien aplica en subcarpetas", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("docs/inicio.html", "portada de docs");
                    Resolution r = Resolve(tree, "/docs/", "inicio.html", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "inicio.html", "archivo");
                }
            });

            runner.Test("si el archivo configurado no existe, se lista", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "existe pero no es el configurado");
                    Check.Equal(ResolutionKind.DirectoryListing,
                                Resolve(tree, "/", "no-existe.html", true).Kind, "tipo");
                }
            });

            runner.Test("el repliegue de pagina unica usa el archivo configurado", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("app.html", "spa");
                    Resolution r = Resolve(tree, "/perfil/ajustes", "app.html", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "tipo");
                    Check.Contains(r.FilePath, "app.html", "archivo");
                }
            });

            runner.Suite("Core / StaticFileResolver - servir como carpeta");

            runner.Test("sin archivo de inicio se lista aunque exista index.html", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "aqui no manda");
                    tree.File("datos.csv", "a,b,c");

                    Check.Equal(ResolutionKind.DirectoryListing,
                                Resolve(tree, "/", "", true).Kind, "la raiz se lista");
                }
            });

            runner.Test("servido como carpeta, las subcarpetas tambien se listan", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("docs/index.html", "tampoco manda aqui");
                    Check.Equal(ResolutionKind.DirectoryListing,
                                Resolve(tree, "/docs/", "", true).Kind, "subcarpeta");
                }
            });

            runner.Test("servido como carpeta no hay repliegue de pagina unica", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "existe");
                    Check.Equal(ResolutionKind.NotFound,
                                Resolve(tree, "/ruta/inventada", "", true).Kind,
                                "una ruta inventada no devuelve el indice");
                }
            });

            runner.Test("servido como carpeta, los archivos siguen sirviendose", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "se puede abrir a mano");
                    Resolution r = Resolve(tree, "/index.html", "", true);
                    Check.Equal(ResolutionKind.File, r.Kind, "pedido por su nombre si");
                }
            });

            runner.Test("servido como carpeta y sin listado no queda nada", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("index.html", "x");
                    Check.Equal(ResolutionKind.NotFound, Resolve(tree, "/", "", false).Kind, "404");
                }
            });
        }
    }
}
