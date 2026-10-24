using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace LanShare.Core
{
    /// <summary>
    /// Empaqueta una carpeta en un ZIP mientras se envia: recorre el arbol en
    /// profundidad y escribe cada archivo en cuanto lo abre, sin tener nunca
    /// mas de un archivo abierto ni nada entero en memoria.
    ///
    /// Sigue las reglas del listado (sin ocultos ni archivos de sistema) y la
    /// misma contencion que la descarga de un archivo suelto: cada archivo se
    /// abre y se valida por su ruta real, y cada carpeta se resuelve antes de
    /// entrar. Asi un enlace o una union que apunte fuera del sitio se queda
    /// fuera, una que apunte dentro no forma un ciclo, y los archivos de
    /// OneDrive, que Windows marca como puntos de reanalisis aunque esten en su
    /// sitio, entran con normalidad.
    /// </summary>
    public static class FolderArchive
    {
        /// <summary>Entradas como maximo: un tope de cordura, no de memoria.</summary>
        public const int MaxEntries = 200000;

        private const int FileBufferSize = 64 * 1024;

        public sealed class Result
        {
            public int Files;
            public int Directories;

            /// <summary>
            /// Archivos o carpetas que no se pudieron incluir enteros: en uso, sin
            /// permiso, que fallaron a mitad de lectura o que apuntan fuera del sitio.
            /// </summary>
            public int Skipped;

            public long Bytes;

            /// <summary>Tamano total del ZIP enviado, cabeceras y directorio incluidos.</summary>
            public long ArchiveBytes;

            /// <summary>Se alcanzo <see cref="MaxEntries"/> y el resto no se incluyo.</summary>
            public bool Truncated;
        }

        /// <summary>Si la consulta de la URL pide el ZIP: "?zip" o "?zip=1".</summary>
        public static bool IsRequested(string query)
        {
            if (string.IsNullOrEmpty(query)) return false;
            foreach (string part in query.TrimStart('?').Split('&'))
            {
                if (string.Equals(part, "zip", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("zip=", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <param name="rootName">
        /// Carpeta raiz dentro del ZIP: al descomprimir se crea una carpeta con
        /// este nombre en lugar de esparcir los archivos por el destino.
        /// </param>
        /// <param name="realRootPath">Raiz real del sitio, con separador final.</param>
        public static async Task<Result> WriteAsync(string directory, string rootName, string realRootPath,
                                                    Stream output)
        {
            if (directory == null) throw new ArgumentNullException("directory");
            if (output == null) throw new ArgumentNullException("output");

            // La comprobacion de cada archivo compara por prefijo: sin el
            // separador final, "C:\web\sitio2" pasaria por dentro de "C:\web\sitio".
            string sep = Path.DirectorySeparatorChar.ToString();
            if (!string.IsNullOrEmpty(realRootPath) && !realRootPath.EndsWith(sep, StringComparison.Ordinal))
            {
                realRootPath += sep;
            }

            string rootReal = PathGuard.RealDirectory(directory);
            if (rootReal == null || !PathGuard.IsInsideRealRoot(rootReal, realRootPath))
            {
                throw new UnauthorizedAccessException("La carpeta no esta dentro de la raiz del sitio.");
            }

            ZipWriter zip = new ZipWriter(output);
            Result result = new Result();

            // Cada carpeta real se recorre una sola vez: una union que apunte a
            // una carpeta anterior del propio sitio no se convierte en un bucle.
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            visited.Add(rootReal);

            DirectoryInfo root = new DirectoryInfo(directory);
            string prefix = SafeRootName(rootName) + "/";
            await zip.AddDirectoryAsync(prefix, root.LastWriteTimeUtc).ConfigureAwait(false);
            result.Directories++;

            Stack<KeyValuePair<DirectoryInfo, string>> pending = new Stack<KeyValuePair<DirectoryInfo, string>>();
            pending.Push(new KeyValuePair<DirectoryInfo, string>(root, prefix));

            while (pending.Count > 0 && !result.Truncated)
            {
                KeyValuePair<DirectoryInfo, string> current = pending.Pop();

                foreach (FileInfo file in SafeFiles(current.Key, result))
                {
                    if (result.Files + result.Directories >= MaxEntries)
                    {
                        result.Truncated = true;
                        break;
                    }

                    FileStream stream = OpenInsideRoot(file.FullName, realRootPath);
                    if (stream == null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    using (stream)
                    {
                        long length = stream.Length;
                        long written = await zip.AddFileAsync(current.Value + file.Name, stream, length,
                                                              file.LastWriteTimeUtc).ConfigureAwait(false);
                        result.Bytes += written;
                        result.Files++;
                        if (written < length) result.Skipped++;
                    }
                }

                if (result.Truncated) break;

                List<DirectoryInfo> subdirectories = ClaimSubdirectories(current.Key, realRootPath, visited, result);

                // Se apilan al reves para recorrerlas en el orden del disco.
                for (int i = subdirectories.Count - 1; i >= 0; i--)
                {
                    if (result.Files + result.Directories >= MaxEntries)
                    {
                        result.Truncated = true;
                        break;
                    }

                    DirectoryInfo sub = subdirectories[i];
                    string name = current.Value + sub.Name + "/";
                    await zip.AddDirectoryAsync(name, sub.LastWriteTimeUtc).ConfigureAwait(false);
                    result.Directories++;
                    pending.Push(new KeyValuePair<DirectoryInfo, string>(sub, name));
                }
            }

            await zip.FinishAsync().ConfigureAwait(false);
            result.ArchiveBytes = zip.Position;
            return result;
        }

        /// <summary>
        /// Subcarpetas por las que se puede entrar, en el orden del disco: las
        /// que caen dentro del sitio y no se han recorrido ya. Las carpetas de
        /// verdad se reclaman antes que los enlaces y las uniones, para que si
        /// una union apunta a una hermana, entre la hermana con su nombre real y
        /// no el alias.
        /// </summary>
        private static List<DirectoryInfo> ClaimSubdirectories(DirectoryInfo parent, string realRootPath,
                                                               HashSet<string> visited, Result result)
        {
            List<DirectoryInfo> candidates = SafeDirectories(parent, result);
            HashSet<DirectoryInfo> accepted = new HashSet<DirectoryInfo>();

            for (int pass = 0; pass < 2; pass++)
            {
                bool links = pass == 1;
                foreach (DirectoryInfo sub in candidates)
                {
                    bool isLink = (sub.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (isLink != links) continue;

                    string real = PathGuard.RealDirectory(sub.FullName);
                    if (real == null || !PathGuard.IsInsideRealRoot(real, realRootPath) || !visited.Add(real))
                    {
                        result.Skipped++;
                        continue;
                    }
                    accepted.Add(sub);
                }
            }

            List<DirectoryInfo> ordered = new List<DirectoryInfo>(accepted.Count);
            foreach (DirectoryInfo sub in candidates)
            {
                if (accepted.Contains(sub)) ordered.Add(sub);
            }
            return ordered;
        }

        /// <summary>
        /// Abre el archivo y comprueba en el mismo paso que su ruta real cae
        /// dentro del sitio, como al servir un archivo suelto. null si no se
        /// puede abrir o apunta fuera.
        /// </summary>
        private static FileStream OpenInsideRoot(string path, string realRootPath)
        {
            SafeFileHandle handle;
            if (PathGuard.TryOpenInsideRoot(path, realRootPath, out handle) != GuardResult.Ok) return null;

            try
            {
                return new FileStream(handle, FileAccess.Read, FileBufferSize, true);
            }
            catch (Exception)
            {
                handle.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Nombre de la carpeta raiz del ZIP. Una etiqueta como ".." no deja
        /// nada util tras sanearla; entonces se usa "carpeta".
        /// </summary>
        private static string SafeRootName(string rootName)
        {
            if (string.IsNullOrEmpty(rootName)) return "carpeta";
            try
            {
                string clean = ZipWriter.CleanName(rootName).TrimEnd('/');
                return clean.Length > 0 ? clean.Replace('/', '_') : "carpeta";
            }
            catch (ArgumentException)
            {
                return "carpeta";
            }
        }

        /// <summary>
        /// Archivos visibles de la carpeta. Una carpeta que deja de poder leerse
        /// a mitad de la enumeracion corta su lista, no el ZIP entero.
        /// </summary>
        private static List<FileInfo> SafeFiles(DirectoryInfo directory, Result result)
        {
            List<FileInfo> files = new List<FileInfo>();
            try
            {
                foreach (FileInfo file in directory.EnumerateFiles())
                {
                    if (Excluded(file.Attributes)) continue;
                    files.Add(file);
                }
            }
            catch (Exception)
            {
                result.Skipped++;
            }
            return files;
        }

        private static List<DirectoryInfo> SafeDirectories(DirectoryInfo directory, Result result)
        {
            List<DirectoryInfo> directories = new List<DirectoryInfo>();
            try
            {
                foreach (DirectoryInfo sub in directory.EnumerateDirectories())
                {
                    if (Excluded(sub.Attributes)) continue;
                    directories.Add(sub);
                }
            }
            catch (Exception)
            {
                result.Skipped++;
            }
            return directories;
        }

        /// <summary>Lo mismo que oculta el listado: archivos ocultos y de sistema.</summary>
        private static bool Excluded(FileAttributes attributes)
        {
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
    }
}
