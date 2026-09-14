using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LanShare.Core
{
    /// <summary>
    /// Canonizacion de rutas por API de Windows. Resuelve junctions, enlaces
    /// simbolicos y nombres cortes 8.3 hasta la ruta fisica definitiva.
    /// </summary>
    internal static class NativePath
    {
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;
        private const uint GENERIC_READ = 0x80000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_ALL = 7; // READ | WRITE | DELETE

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        // Sobrecarga de sondeo: se invoca con buffer nulo para obtener el tamano necesario.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandleProbe(
            SafeFileHandle hFile, IntPtr lpszFilePath, uint cchFilePath, uint dwFlags);

        /// <summary>
        /// Ruta fisica del handle YA abierto. Devuelve cadena vacia ante
        /// cualquier fallo: un repliegue silencioso a la ruta de entrada
        /// invalidaria el control de contencion que depende de este metodo.
        /// </summary>
        public static string FromHandle(SafeFileHandle handle)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed) return string.Empty;

            uint needed = GetFinalPathNameByHandleProbe(handle, IntPtr.Zero, 0, 0);
            if (needed == 0 || needed > 65536) return string.Empty;

            StringBuilder sb = new StringBuilder((int)needed);
            uint written = GetFinalPathNameByHandle(handle, sb, needed, 0);
            if (written == 0 || written >= needed) return string.Empty;

            return StripPrefix(sb.ToString());
        }

        /// <summary>Canoniza una ruta abriendo un handle temporal sin pedir acceso de lectura.</summary>
        public static string FromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            using (SafeFileHandle handle = CreateFile(
                       path, 0, FILE_SHARE_ALL, IntPtr.Zero,
                       OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (handle.IsInvalid) return string.Empty;
                return FromHandle(handle);
            }
        }

        /// <summary>
        /// Abre el archivo para lectura asincrona. Se abre aqui, y no dejando
        /// que FileStream lo haga, porque leer FileStream.SafeFileHandle
        /// despues de construirlo descuadra su buffer interno: teniendo el
        /// handle desde el principio podemos validarlo y luego envolverlo.
        /// </summary>
        public static SafeFileHandle OpenRead(string path)
        {
            return CreateFile(path, GENERIC_READ, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                              FILE_FLAG_OVERLAPPED | FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);
        }

        private static string StripPrefix(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path.Substring(4);
            return path;
        }
    }
}
