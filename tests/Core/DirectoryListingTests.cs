using System;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>
    /// Listado de carpetas. Genera HTML a partir de nombres de archivo que el
    /// usuario controla, asi que el escapado es una cuestion de seguridad.
    /// </summary>
    public static class DirectoryListingTests
    {
        private static string Build(TempTree tree, string urlPath)
        {
            return Encoding.UTF8.GetString(DirectoryListing.Build(tree.Root, urlPath));
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / DirectoryListing");

            runner.Test("aparecen archivos y carpetas", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("nota.txt", "hola");
                    tree.Dir("documentos");

                    string html = Build(tree, "/");
                    Check.Contains(html, "nota.txt", "archivo");
                    Check.Contains(html, "documentos", "carpeta");
                }
            });

            runner.Test("es un documento HTML completo", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a.txt", "x");
                    string html = Build(tree, "/");
                    Check.Contains(html, "<!DOCTYPE html>", "doctype");
                    Check.Contains(html, "</body>", "cierre de body");
                    Check.Contains(html, "charset=\"utf-8\"", "codificacion");
                }
            });

            runner.Test("SEGURIDAD: los metacaracteres del nombre se escapan", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    // Windows prohibe < y > en los nombres, asi que el escapado
                    // se verifica con los que si acepta: comillas y ampersand.
                    tree.File("it's & co.txt", "x");
                    string html = Build(tree, "/");

                    Check.Contains(html, "&amp;", "ampersand escapado");
                    Check.Contains(html, "&#39;", "comilla simple escapada");
                    Check.DoesNotContain(html, ">it's & co.txt<", "nunca en crudo");
                }
            });

            runner.Test("SEGURIDAD: el ampersand del nombre se escapa", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a & b.txt", "x");
                    string html = Build(tree, "/");
                    Check.Contains(html, "&amp;", "ampersand escapado en el texto");
                    Check.Contains(html, "%20", "espacio codificado en el enlace");
                }
            });

            runner.Test("el enlace y el texto usan escapados distintos", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("mi archivo.txt", "x");
                    string html = Build(tree, "/");
                    Check.Contains(html, "href=\"mi%20archivo.txt\"", "href codificado como URL");
                    Check.Contains(html, "mi archivo.txt</a>", "texto visible sin codificar");
                }
            });

            runner.Test("las carpetas llevan barra final en el enlace", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Dir("docs");
                    Check.Contains(Build(tree, "/"), "href=\"docs/\"", "barra final");
                }
            });

            runner.Test("hay enlace al nivel superior salvo en la raiz", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a.txt", "x");
                    Check.DoesNotContain(Build(tree, "/"), "Subir un nivel", "en la raiz no");
                    Check.Contains(Build(tree, "/docs/"), "Subir un nivel", "en subcarpeta si");
                }
            });

            runner.Test("el pie cuenta archivos y carpetas", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a.txt", "12345");
                    tree.File("b.txt", "12345");
                    tree.Dir("c");

                    string html = Build(tree, "/");
                    Check.Contains(html, "2 archivos", "recuento de archivos");
                    Check.Contains(html, "1 carpeta", "recuento de carpetas");
                }
            });

            runner.Test("el singular y el plural concuerdan", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("solo.txt", "x");
                    string html = Build(tree, "/");
                    Check.Contains(html, "1 archivo", "singular");
                    Check.DoesNotContain(html, "1 archivos", "sin plural incorrecto");
                }
            });

            runner.Test("carpeta vacia no rompe nada", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string html = Build(tree, "/");
                    Check.Contains(html, "0 archivos", "cero archivos");
                    Check.Contains(html, "</table>", "tabla cerrada");
                }
            });

            runner.Test("las carpetas van antes que los archivos", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("aaa.txt", "x");
                    tree.Dir("zzz");

                    string html = Build(tree, "/");
                    int carpeta = html.IndexOf("zzz", StringComparison.Ordinal);
                    int archivo = html.IndexOf("aaa.txt", StringComparison.Ordinal);
                    Check.True(carpeta < archivo, "la carpeta aparece primero pese al orden alfabetico");
                }
            });

            runner.Test("el titulo refleja la ruta pedida", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.File("a.txt", "x");
                    Check.Contains(Build(tree, "/proyecto/css/"), "/proyecto/css/", "ruta en el titulo");
                }
            });

            runner.Test("los tamanos salen con unidad", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    tree.Binary("grande.bin", new byte[4096]);
                    Check.Contains(Build(tree, "/"), "KB", "unidad");
                }
            });
        }
    }
}
