using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LanShare.Ui
{
    /// <summary>
    /// Selector de carpeta con el dialogo moderno de Windows (IFileOpenDialog
    /// en modo carpeta), en vez del arbol anticuado de FolderBrowserDialog.
    /// </summary>
    public sealed class ModernFolderPicker
    {
        public string InitialFolder { get; set; }
        public string SelectedPath { get; private set; }

        private const uint FOS_PICKFOLDERS = 0x0020;
        private const uint FOS_FORCEFILESYSTEM = 0x0040;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        public bool ShowDialog(IntPtr owner)
        {
            IFileOpenDialog dialog = null;
            try { dialog = (IFileOpenDialog)new FileOpenDialogRcw(); }
            catch (Exception) { return false; }

            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

                if (!string.IsNullOrEmpty(InitialFolder) && Directory.Exists(InitialFolder))
                {
                    IShellItem start;
                    if (SHCreateItemFromParsingName(InitialFolder, IntPtr.Zero,
                                                    typeof(IShellItem).GUID, out start) == 0 && start != null)
                    {
                        dialog.SetFolder(start);
                    }
                }

                if (dialog.Show(owner) != 0) return false;

                IShellItem item;
                dialog.GetResult(out item);
                if (item == null) return false;

                IntPtr pathPtr;
                item.GetDisplayName(SIGDN_FILESYSPATH, out pathPtr);
                SelectedPath = Marshal.PtrToStringUni(pathPtr);
                Marshal.FreeCoTaskMem(pathPtr);
                return true;
            }
            catch (Exception) { return false; }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        // El orden de los metodos define la tabla virtual: las entradas que no
        // se usan se declaran sin parametros a proposito, pero NO se pueden
        // reordenar ni eliminar sin romper las llamadas de abajo.
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName();
            void GetFileName();
            void SetTitle();
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem item);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(uint sigdnName, out IntPtr name);
            void GetAttributes();
            void Compare();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem item);
    }
}
