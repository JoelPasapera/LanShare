using System;
using System.IO;
using System.Text;
using LanShare.Core;

namespace LanShare.Tests
{
    /// <summary>Migas de la ruta, datos para ordenar y script del listado.</summary>
    public static class DirectoryNavigationTests
    {
        private static string Build(string directory, string url)
        {
            return Encoding.UTF8.GetString(DirectoryListing.Build(directory, url));
        }

        public static void Register(TestRunner runner)
        {
            runner.Suite("Core / DirectoryListing - navegacion y orden");

            runner.Test("cada tramo de la ruta enlaza a su carpeta", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string css = tree.Dir("proyecto/css");
                    string html = Build(css, "/proyecto/css/");
                    string rootName = Path.GetFileName(tree.Root.TrimEnd('\\', '/'));

                    Check.Contains(html, "<a href=\"/\">" + rootName + "</a>", "la raiz con el nombre de la carpeta del sitio");
                    Check.Contains(html, "<a href=\"/proyecto/\">proyecto</a>", "tramo intermedio");
                    Check.Contains(html, "<b>css</b>", "la carpeta actual, sin enlace");
                    Check.DoesNotContain(html, "href=\"/proyecto/css/\"", "la actual no se enlaza a si misma");
                }
            });

            runner.Test("en la raiz, solo el nombre de la carpeta", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string html = Build(tree.Root, "/");
                    Check.Contains(html, "<h1 class=\"ruta\"><b>", "nombre sin enlace");
                    Check.DoesNotContain(html, "<a href=\"/\">", "sin enlace a si misma");
                }
            });

            runner.Test("SEGURIDAD: los tramos se escapan como URL y como HTML", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string dir = tree.Dir("mis fotos & co/sub");
                    string html = Build(dir, "/mis fotos & co/sub/");
                    Check.Contains(html, "href=\"/mis%20fotos%20%26%20co/\"", "enlace codificado");
                    Check.Contains(html, ">mis fotos &amp; co</a>", "texto escapado");
                    Check.DoesNotContain(html, ">mis fotos & co<", "nunca en crudo");
                }
            });

            runner.Test("las filas llevan tamano exacto y fecha Unix para ordenar", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string file = tree.File("a.txt", "12345");
                    File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    tree.Dir("carpeta");

                    string html = Build(tree.Root, "/");
                    Check.Contains(html, "<tr data-s=\"5\" data-t=\"1577836800000\">", "archivo");
                    Check.Contains(html, "<tr data-s=\"-1\" data-t=\"", "carpeta marcada con -1");
                }
            });

            runner.Test("la tabla y su script de orden y filtro", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    string html = Build(tree.Root, "/");
                    Check.Contains(html, "<table id=\"lista\">", "tabla identificable");
                    Check.Contains(html, "data-k=\"s\"", "columna ordenable");
                    Check.Contains(html, "getElementById('lista')", "script incluido");
                    Check.Contains(html, "href=\"?zip\"", "descarga de la carpeta en ZIP");
                    Check.True(html.IndexOf("<script>", StringComparison.Ordinal) <
                               html.LastIndexOf("</body>", StringComparison.Ordinal), "dentro del documento");
                }
            });

            runner.Test("una URL vacia se trata como la raiz", delegate
            {
                using (TempTree tree = new TempTree())
                {
                    Check.DoesNotContain(Build(tree.Root, ""), "Subir un nivel", "sin nivel superior");
                    Check.DoesNotContain(Build(tree.Root, null), "Subir un nivel", "nula tampoco");
                }
            });
        }
    }
}
