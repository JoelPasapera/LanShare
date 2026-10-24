using System;
using System.IO;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>Que sitio se propone al arrastrar algo sobre la ventana.</summary>
    public static class DroppedPathTests
    {
        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / DroppedPath");

            runner.Test("una carpeta con index.html se sirve con index.html", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/index.html", "<p>");
                    tree.File("web/otra.html", "<p>");
                    string root, start;
                    Check.True(DroppedPath.TryResolve(Path.Combine(tree.Root, "web"), out root, out start), "resuelta");
                    Check.Equal(Path.Combine(tree.Root, "web"), root, "la propia carpeta");
                    Check.Equal("index.html", start, "index.html");
                }
            });

            runner.Test("sin index.html: la unica pagina, o el listado si hay varias", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("una/editor.html", "<p>");
                    tree.File("una/estilos.css", "x");
                    tree.File("varias/a.html", "<p>");
                    tree.File("varias/b.htm", "<p>");
                    tree.Dir("vacia");

                    Check.Equal("editor.html", DroppedPath.SuggestStartFile(Path.Combine(tree.Root, "una")), "la unica");
                    Check.Equal("", DroppedPath.SuggestStartFile(Path.Combine(tree.Root, "varias")), "varias: listado");
                    Check.Equal("", DroppedPath.SuggestStartFile(Path.Combine(tree.Root, "vacia")), "ninguna: listado");
                }
            });

            runner.Test("el patron *.htm* de Windows no cuela copias de seguridad", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/app.html", "<p>");
                    tree.File("web/app.html~", "<p>");
                    tree.File("web/app.htmlx", "<p>");
                    Check.Equal("app.html", DroppedPath.SuggestStartFile(Path.Combine(tree.Root, "web")), "solo la pagina");
                }
            });

            runner.Test("una pagina arrastrada: su carpeta, abriendo esa pagina", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/index.html", "<p>");
                    string page = tree.File("web/editor.html", "<p>");
                    string root, start;
                    Check.True(DroppedPath.TryResolve(page, out root, out start), "resuelta");
                    Check.Equal(Path.Combine(tree.Root, "web"), root, "carpeta padre");
                    Check.Equal("editor.html", start, "la pagina arrastrada, no index.html");
                }
            });

            runner.Test("otro archivo arrastrado: su carpeta, con la sugerencia normal", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("web/index.html", "<p>");
                    string css = tree.File("web/app.css", "x");
                    string root, start;
                    Check.True(DroppedPath.TryResolve(css, out root, out start), "resuelta");
                    Check.Equal("index.html", start, "index.html de la carpeta");
                }
            });

            runner.Test("lo que no existe no propone nada", delegate
            {
                string root, start;
                Check.False(DroppedPath.TryResolve(@"C:\no\existe\jamas\x", out root, out start), "inexistente");
                Check.Null(root, "sin carpeta");
                Check.Null(start, "sin pagina");
                Check.False(DroppedPath.TryResolve("", out root, out start), "vacia");
                Check.False(DroppedPath.TryResolve(null, out root, out start), "nula");
                Check.False(DroppedPath.TryResolve("C:\\a\0b", out root, out start), "caracter invalido");
            });
        }
    }
}
