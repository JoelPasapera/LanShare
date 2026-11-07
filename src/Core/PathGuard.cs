using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace LanShare.Core
{
    public enum GuardResult
    {
        Ok,
        NotFound,
        Forbidden
    }

    /// <summary>
    /// Unica responsabilidad: decidir si una ruta cae dentro de la raiz servida.
    ///
    /// La defensa real no esta en inspeccionar la cadena de la URL, sino en
    /// canonizar el handle YA abierto. Comprobar la ruta y despues abrirla deja
    /// una ventana en la que un enlace simbolico puede cambiar entre ambos
    /// pasos (TOCTOU); validar el handle cierra esa ventana porque el inodo ya
    /// esta fijado.
    /// </summary>
    public static class PathGuard
    {
        /// <summary>Canoniza la raiz al arrancar. Cadena vacia si no se pudo resolver.</summary>
        public static string CanonicalizeRoot(string rootPath)
        {
            string real = NativePath.FromPath(rootPath);
            if (string.IsNullOrEmpty(real)) return string.Empty;

            string sep = Path.DirectorySeparatorChar.ToString();
            return real.EndsWith(sep, StringComparison.Ordinal) ? real : real + sep;
        }

        /// <summary>
        /// Comprobacion barata previa a tocar el disco. No sustituye a
        /// <see cref="TryOpenInsideRoot"/>, solo evita trabajo inutil.
        /// </summary>
        public static bool LooksInsideRoot(string fullPath, string rootPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return false;
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) return true;

            // GetFullPath quita el separador final, asi que una ruta que
            // normaliza a la raiz misma ("C:\www" frente a "C:\www\") no
            // coincidia por prefijo y se rechazaba por error.
            string trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(fullPath, trimmed, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Abre el archivo y valida en el mismo paso que el handle resultante
        /// cae dentro de la raiz. Devolver el handle ya validado evita que el
        /// llamante tenga que tocarlo despues.
        /// </summary>
        public static GuardResult TryOpenInsideRoot(string path, string realRootPath, out SafeFileHandle handle)
        {
            handle = NativePath.OpenRead(path);

            if (handle == null || handle.IsInvalid)
            {
                if (handle != null) handle.Dispose();
                handle = null;
                return GuardResult.NotFound;
            }

            string real = NativePath.FromHandle(handle);
            if (string.IsNullOrEmpty(real) ||
                !real.StartsWith(realRootPath, StringComparison.OrdinalIgnoreCase))
            {
                handle.Dispose();
                handle = null;
                return GuardResult.Forbidden;
            }

            return GuardResult.Ok;
        }

        /// <summary>
        /// Si la ruta real de una carpeta, con enlaces y uniones resueltos, cae
        /// dentro de la raiz real del sitio. Lo necesita lo que recorre carpetas
        /// enteras en lugar de abrir un archivo concreto, como el ZIP de una
        /// carpeta: una union dentro del sitio podria apuntar a cualquier parte
        /// del disco y el recorrido la seguiria.
        /// </summary>
        public static bool DirectoryInsideRoot(string directory, string realRootPath)
        {
            string real = RealDirectory(directory);
            return real != null && IsInsideRealRoot(real, realRootPath);
        }

        /// <summary>
        /// Ruta fisica de una carpeta, con separador final y con enlaces y
        /// uniones resueltos. null si no existe o no se pudo resolver.
        /// </summary>
        public static string RealDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;

            string real = NativePath.FromPath(directory);
            if (string.IsNullOrEmpty(real)) return null;

            string sep = Path.DirectorySeparatorChar.ToString();
            return real.EndsWith(sep, StringComparison.Ordinal) ? real : real + sep;
        }

        /// <summary>Si una ruta real (con separador final) cae dentro de la raiz real del sitio.</summary>
        public static bool IsInsideRealRoot(string realPath, string realRootPath)
        {
            if (string.IsNullOrEmpty(realPath) || string.IsNullOrEmpty(realRootPath)) return false;

            string sep = Path.DirectorySeparatorChar.ToString();
            string root = realRootPath.EndsWith(sep, StringComparison.Ordinal) ? realRootPath : realRootPath + sep;
            return realPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rechaza rutas relativas con caracteres que no deberian aparecer:
        /// el byte nulo y los dos puntos, que habilitarian flujos de datos
        /// alternativos de NTFS (archivo.txt:oculto).
        /// </summary>
        public static bool RelativePathIsSafe(string relativePath)
        {
            if (relativePath == null) return false;
            return relativePath.IndexOf('\0') < 0 && relativePath.IndexOf(':') < 0;
        }
    }
}
