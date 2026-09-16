using System;
using System.IO;
using System.Text;

namespace LanShare.Tests
{
    /// <summary>
    /// Arbol de archivos temporal para las pruebas que tocan el disco. Se borra
    /// entero al liberarse, incluso si la prueba falla.
    /// </summary>
    public sealed class TempTree : IDisposable
    {
        public string Root { get; private set; }

        public TempTree()
        {
            Root = Path.Combine(Path.GetTempPath(),
                                "lanshare-tests-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(Root);
        }

        /// <summary>Raiz terminada en separador, como la espera el resolutor.</summary>
        public string RootWithSeparator
        {
            get
            {
                string separator = Path.DirectorySeparatorChar.ToString();
                return Root.EndsWith(separator, StringComparison.Ordinal) ? Root : Root + separator;
            }
        }

        public string File(string relativePath, string content)
        {
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            System.IO.File.WriteAllText(full, content, new UTF8Encoding(false));
            return full;
        }

        public string Binary(string relativePath, byte[] content)
        {
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            System.IO.File.WriteAllBytes(full, content);
            return full;
        }

        public string Dir(string relativePath)
        {
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
            catch (Exception) { }
        }
    }
}
