#requires -version 5.1
<#
    Servidor Web Pro - Hardened & Multi-Threaded
    Version corregida (archivo unico).
#>

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# --- GUARDA DE APARTAMENTO STA ---
# WinForms y Clipboard exigen STA. En MTA (pwsh 7 por defecto) el formulario
# se comporta de forma erratica y Clipboard::SetText lanza excepcion.
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    Write-Host "ERROR: este script requiere apartamento STA." -ForegroundColor Red
    Write-Host "Ejecutalo con:  powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File `"<ruta>.ps1`"" -ForegroundColor Yellow
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# --- DICCIONARIO GLOBAL DE MIME TYPES ---
# Solo lectura concurrente desde los runspaces worker: Hashtable es segura
# para multiples lectores mientras ningun hilo escriba.
$global:MimeTypes = @{
    ".html"        = "text/html; charset=utf-8"
    ".htm"         = "text/html; charset=utf-8"
    ".css"         = "text/css; charset=utf-8"
    ".js"          = "text/javascript; charset=utf-8"
    ".mjs"         = "text/javascript; charset=utf-8"
    ".cjs"         = "text/javascript; charset=utf-8"
    ".json"        = "application/json; charset=utf-8"
    ".map"         = "application/json"
    ".png"         = "image/png"
    ".jpg"         = "image/jpeg"
    ".jpeg"        = "image/jpeg"
    ".gif"         = "image/gif"
    ".svg"         = "image/svg+xml"
    ".webp"        = "image/webp"
    ".avif"        = "image/avif"
    ".ico"         = "image/x-icon"
    ".bmp"         = "image/bmp"
    ".mp3"         = "audio/mpeg"
    ".ogg"         = "audio/ogg"
    ".oga"         = "audio/ogg"
    ".wav"         = "audio/wav"
    ".flac"        = "audio/flac"
    ".m4a"         = "audio/mp4"
    ".mp4"         = "video/mp4"
    ".m4v"         = "video/mp4"
    ".webm"        = "video/webm"
    ".ogv"         = "video/ogg"
    ".wasm"        = "application/wasm"
    ".pdf"         = "application/pdf"
    ".txt"         = "text/plain; charset=utf-8"
    ".md"          = "text/markdown; charset=utf-8"
    ".csv"         = "text/csv; charset=utf-8"
    ".xml"         = "application/xml; charset=utf-8"
    ".webmanifest" = "application/manifest+json"
    ".zip"         = "application/zip"
    ".ttf"         = "font/ttf"
    ".otf"         = "font/otf"
    ".woff"        = "font/woff"
    ".woff2"       = "font/woff2"
    ".eot"         = "application/vnd.ms-fontobject"
}

# --- RESOLUCION NATIVA WIN32 ANTI-JUNCTION / SYMLINK / TOCTOU + PICKER MODERNO ---
# La guarda comprueba el ULTIMO tipo definido: asi, si una version anterior del
# script ya cargo NativePath en esta sesion, el error se detecta y se avisa en
# vez de correr con clases desactualizadas (los tipos no se pueden recargar).
if (-not ([System.Management.Automation.PSTypeName]'ClientRegistry').Type) {
    try {
    Add-Type -TypeDefinition @"
    using System;
    using System.Text;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Text.RegularExpressions;

    public static class NativePath {
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_SHARE_ALL = 7; // READ | WRITE | DELETE

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        // Sobrecarga de sondeo: se llama con buffer nulo para obtener el tamano requerido.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandleProbe(
            SafeFileHandle hFile, IntPtr lpszFilePath, uint cchFilePath, uint dwFlags);

        private static string StripPrefix(string path) {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                return path.Substring(4);
            return path;
        }

        // Devuelve string.Empty ante cualquier fallo. Nunca devuelve la ruta de
        // entrada sin canonizar: un fallback silencioso invalidaria el control.
        public static string GetRealPathFromHandle(SafeFileHandle hFile) {
            if (hFile == null || hFile.IsInvalid || hFile.IsClosed) return string.Empty;

            uint needed = GetFinalPathNameByHandleProbe(hFile, IntPtr.Zero, 0, 0);
            if (needed == 0) return string.Empty;
            if (needed > 65536) return string.Empty;

            StringBuilder sb = new StringBuilder((int)needed);
            uint written = GetFinalPathNameByHandle(hFile, sb, needed, 0);
            if (written == 0 || written >= needed) return string.Empty;

            return StripPrefix(sb.ToString());
        }

        public static string GetRealPath(string path) {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            using (SafeFileHandle hFile = CreateFile(
                       path, 0, FILE_SHARE_ALL, IntPtr.Zero,
                       OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero)) {
                if (hFile.IsInvalid) return string.Empty;
                return GetRealPathFromHandle(hFile);
            }
        }
    }

    public class ModernFolderPicker {
        public string InitialFolder { get; set; }
        public string SelectedPath { get; private set; }

        public bool ShowDialog(IntPtr owner) {
            IFileOpenDialog dialog = null;
            try {
                dialog = (IFileOpenDialog)new FileOpenDialog();
            } catch {
                return false;
            }

            try {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

                if (!string.IsNullOrEmpty(InitialFolder) && System.IO.Directory.Exists(InitialFolder)) {
                    IShellItem item;
                    if (SHCreateItemFromParsingName(InitialFolder, IntPtr.Zero, typeof(IShellItem).GUID, out item) == 0 && item != null) {
                        dialog.SetFolder(item);
                    }
                }

                if (dialog.Show(owner) == 0) {
                    IShellItem item;
                    dialog.GetResult(out item);
                    if (item != null) {
                        IntPtr pathPtr;
                        item.GetDisplayName(SIGDN_FILESYSPATH, out pathPtr);
                        SelectedPath = Marshal.PtrToStringUni(pathPtr);
                        Marshal.FreeCoTaskMem(pathPtr);
                        return true;
                    }
                }
            } catch {
            } finally {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
            return false;
        }

        private const uint FOS_PICKFOLDERS = 0x0020;
        private const uint FOS_FORCEFILESYSTEM = 0x0040;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint dwOptions);
            void GetOptions(out uint pdwOptions);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName();
            void GetFileName();
            void SetTitle();
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes();
            void Compare();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);
    }

    // Campos publicos (no propiedades) para poder pasarlos por ref a Interlocked.
    public class ClientInfo {
        public string Ip = "";
        public string UserAgent = "";
        public string AcceptLanguage = "";
        public string ChUa = "";
        public string ChPlatform = "";
        public string ChMobile = "";
        public string ChModel = "";
        public string HostName = "";
        public string Mac = "";
        public string Vendor = "";
        public bool   MacRandomized = false;
        public string DeviceLabel = "Desconocido";
        public string BrowserLabel = "Desconocido";
        public long   RequestCount = 0;
        public long   BytesSent = 0;
        public long   FirstSeenTicks = 0;
        public long   LastSeenTicks = 0;
        public int    ResolveState = 0; // 0 pendiente, 1 en curso, 2 resuelto
    }

    // Estado compartido entre todos los runspaces worker. Al ser un tipo estatico
    // del AppDomain, no hace falta inyectarlo como argumento en cada peticion.
    public static class ClientRegistry {

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macAddrLen);

        private static readonly ConcurrentDictionary<string, ClientInfo> _map =
            new ConcurrentDictionary<string, ClientInfo>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> _oui =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ---------- API publica ----------

        public static void SetVendorTable(System.Collections.IDictionary table) {
            lock (_oui) {
                _oui.Clear();
                if (table == null) return;
                foreach (System.Collections.DictionaryEntry e in table) {
                    string k = Convert.ToString(e.Key);
                    if (k == null) continue;
                    k = k.Replace(":", "").Replace("-", "").Replace(".", "").Trim().ToUpperInvariant();
                    if (k.Length >= 6) _oui[k.Substring(0, 6)] = Convert.ToString(e.Value);
                }
            }
        }

        public static int VendorCount { get { lock (_oui) { return _oui.Count; } } }

        public static void Track(string ip, string ua, string lang,
                                 string chUa, string chPlatform, string chMobile,
                                 string chModel, long bytes) {
            if (string.IsNullOrEmpty(ip)) return;

            ClientInfo ci;
            if (!_map.TryGetValue(ip, out ci)) {
                ClientInfo fresh = new ClientInfo();
                fresh.Ip = ip;
                fresh.FirstSeenTicks = DateTime.UtcNow.Ticks;
                ci = _map.GetOrAdd(ip, fresh);
            }

            Interlocked.Increment(ref ci.RequestCount);
            if (bytes > 0) Interlocked.Add(ref ci.BytesSent, bytes);
            Interlocked.Exchange(ref ci.LastSeenTicks, DateTime.UtcNow.Ticks);

            // El parseo de UA solo corre cuando cambia la cadena, no en cada peticion.
            if (!string.IsNullOrEmpty(ua) && !string.Equals(ci.UserAgent, ua, StringComparison.Ordinal)) {
                ci.UserAgent   = ua;
                ci.DeviceLabel = ParseDevice(ua);
                ci.BrowserLabel= ParseBrowser(ua);
            }
            if (!string.IsNullOrEmpty(lang))       ci.AcceptLanguage = lang;
            if (!string.IsNullOrEmpty(chUa))       ci.ChUa           = chUa;
            if (!string.IsNullOrEmpty(chPlatform)) ci.ChPlatform     = chPlatform;
            if (!string.IsNullOrEmpty(chMobile))   ci.ChMobile       = chMobile;
            if (!string.IsNullOrEmpty(chModel))    ci.ChModel        = chModel;

            QueueResolve(ci);
        }

        public static ClientInfo[] Snapshot() {
            List<ClientInfo> list = new List<ClientInfo>(_map.Values);
            return list.ToArray();
        }

        public static void Clear() { _map.Clear(); }

        public static void ResetResolution() {
            foreach (ClientInfo ci in _map.Values) {
                Interlocked.Exchange(ref ci.ResolveState, 0);
                QueueResolve(ci);
            }
        }

        // ---------- Enriquecimiento fuera de la ruta de peticion ----------

        private static void QueueResolve(ClientInfo ci) {
            // Un unico intento por cliente: CAS 0 -> 1 gana la carrera.
            if (Interlocked.CompareExchange(ref ci.ResolveState, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(ResolveWorker, ci);
        }

        private static void ResolveWorker(object state) {
            ClientInfo ci = (ClientInfo)state;
            try {
                IPAddress addr;
                if (!IPAddress.TryParse(ci.Ip, out addr)) return;

                // DNS inverso con tope de tiempo: sin el, un resolutor lento
                // dejaria el hilo colgado varios segundos.
                try {
                    IAsyncResult ar = Dns.BeginGetHostEntry(ci.Ip, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(2000, false)) {
                        IPHostEntry he = Dns.EndGetHostEntry(ar);
                        if (he != null && !string.IsNullOrEmpty(he.HostName)) ci.HostName = he.HostName;
                    }
                } catch { }

                // ARP: solo IPv4 y solo dentro del mismo segmento L2 (la LAN).
                if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr)) {
                    try {
                        byte[] raw = addr.GetAddressBytes();
                        uint dest = (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
                        byte[] mac = new byte[6];
                        uint len = 6;
                        if (SendARP(dest, 0, mac, ref len) == 0 && len >= 6) {
                            ci.Mac = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                                                   mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
                            // Bit 1 del primer octeto = direccion administrada localmente,
                            // es decir MAC aleatoria de privacidad (iOS/Android modernos).
                            ci.MacRandomized = (mac[0] & 0x02) != 0;

                            string oui = string.Format("{0:X2}{1:X2}{2:X2}", mac[0], mac[1], mac[2]);
                            string vendor = null;
                            lock (_oui) { _oui.TryGetValue(oui, out vendor); }

                            if (!string.IsNullOrEmpty(vendor))   ci.Vendor = vendor;
                            else if (ci.MacRandomized)           ci.Vendor = "MAC aleatoria";
                            else                                 ci.Vendor = "Desconocido";
                        }
                    } catch { }
                }
            } catch { }
            finally {
                Interlocked.Exchange(ref ci.ResolveState, 2);
            }
        }

        // ---------- Parseo de User-Agent ----------

        // Devuelve la version mayor que sigue al token, sin regex.
        private static string VerAfter(string ua, string token) {
            int i = ua.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return "";
            i += token.Length;
            int j = i;
            while (j < ua.Length && (char.IsDigit(ua[j]) || ua[j] == '.')) j++;
            string v = ua.Substring(i, j - i);
            int dot = v.IndexOf('.');
            return dot > 0 ? v.Substring(0, dot) : v;
        }

        private static bool Has(string ua, string token) {
            return ua.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private static string Join(string name, string ver) {
            return ver.Length > 0 ? name + " " + ver : name;
        }

        public static string ParseBrowser(string ua) {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";
            // El orden importa: Edge y Opera incluyen "Chrome/" en su UA.
            if (Has(ua, "Edg/"))            return Join("Edge",              VerAfter(ua, "Edg/"));
            if (Has(ua, "EdgA/"))           return Join("Edge Android",      VerAfter(ua, "EdgA/"));
            if (Has(ua, "EdgiOS/"))         return Join("Edge iOS",          VerAfter(ua, "EdgiOS/"));
            if (Has(ua, "OPR/"))            return Join("Opera",             VerAfter(ua, "OPR/"));
            if (Has(ua, "SamsungBrowser/")) return Join("Samsung Internet",  VerAfter(ua, "SamsungBrowser/"));
            if (Has(ua, "YaBrowser/"))      return Join("Yandex",            VerAfter(ua, "YaBrowser/"));
            if (Has(ua, "Vivaldi/"))        return Join("Vivaldi",           VerAfter(ua, "Vivaldi/"));
            if (Has(ua, "FxiOS/"))          return Join("Firefox iOS",       VerAfter(ua, "FxiOS/"));
            if (Has(ua, "Firefox/"))        return Join("Firefox",           VerAfter(ua, "Firefox/"));
            if (Has(ua, "CriOS/"))          return Join("Chrome iOS",        VerAfter(ua, "CriOS/"));
            if (Has(ua, "Chrome/"))         return Join("Chrome",            VerAfter(ua, "Chrome/"));
            if (Has(ua, "Version/") && Has(ua, "Safari/"))
                                            return Join("Safari",            VerAfter(ua, "Version/"));
            if (Has(ua, "curl/"))           return Join("curl",              VerAfter(ua, "curl/"));
            if (Has(ua, "Wget"))            return "wget";
            if (Has(ua, "PostmanRuntime"))  return "Postman";
            if (Has(ua, "python-requests")) return "python-requests";
            if (Has(ua, "PowerShell"))      return "PowerShell";
            if (Has(ua, "Dart/"))           return "Dart/Flutter";
            if (Has(ua, "okhttp"))          return "OkHttp (app nativa)";
            return "Otro";
        }

        public static string ParseDevice(string ua) {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";

            if (Has(ua, "Android")) {
                Match mv = Regex.Match(ua, @"Android\s+([\d.]+)");
                string ver = mv.Success ? mv.Groups[1].Value : "";
                string model = "";
                Match mm = Regex.Match(ua, @"Android[^;)]*;\s*(?:[a-z]{2}(?:-[a-zA-Z]{2})?;\s*)?([^;)]+?)\s*(?:Build/|\))");
                if (mm.Success) model = mm.Groups[1].Value.Trim();
                // "K" es el marcador congelado de la UA reducida de Chrome: no hay modelo.
                if (model == "K" || model == "Android" || model.Length == 0)
                    return Join("Android", ver);
                return Join("Android", ver) + " \u00B7 " + model;
            }

            if (Has(ua, "Windows Phone")) return "Windows Phone";

            if (Has(ua, "iPhone")) {
                Match m = Regex.Match(ua, @"CPU iPhone OS (\d+)[_.](\d+)");
                return m.Success ? "iPhone \u00B7 iOS " + m.Groups[1].Value + "." + m.Groups[2].Value : "iPhone";
            }
            if (Has(ua, "iPad")) {
                Match m = Regex.Match(ua, @"CPU OS (\d+)[_.](\d+)");
                return m.Success ? "iPad \u00B7 iPadOS " + m.Groups[1].Value + "." + m.Groups[2].Value : "iPad";
            }
            if (Has(ua, "iPod")) return "iPod touch";

            if (Has(ua, "CrOS")) return "ChromeOS";

            if (Has(ua, "Windows NT 10.0")) return "Windows 10/11";
            if (Has(ua, "Windows NT 6.3"))  return "Windows 8.1";
            if (Has(ua, "Windows NT 6.2"))  return "Windows 8";
            if (Has(ua, "Windows NT 6.1"))  return "Windows 7";
            if (Has(ua, "Windows NT"))      return "Windows (antiguo)";

            if (Has(ua, "Mac OS X")) {
                Match m = Regex.Match(ua, @"Mac OS X (\d+)[_.](\d+)");
                return m.Success ? "macOS " + m.Groups[1].Value + "." + m.Groups[2].Value : "macOS";
            }

            if (Has(ua, "Android TV"))  return "Android TV";
            if (Has(ua, "SMART-TV") || Has(ua, "Tizen")) return "Smart TV";
            if (Has(ua, "PlayStation")) return "PlayStation";
            if (Has(ua, "Nintendo"))    return "Nintendo";

            if (Has(ua, "Linux") || Has(ua, "X11")) return "Linux";
            return "Desconocido";
        }
    }
"@
    } catch {
        Write-Host "ERROR al compilar los tipos nativos:" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        Write-Host "Si ya ejecutaste una version anterior en esta misma consola, abre una nueva ventana de PowerShell: los tipos cargados no se pueden reemplazar." -ForegroundColor Yellow
        return
    }
}

# --- TABLA OUI (MAC -> FABRICANTE) ---
# Conjunto minimo y deliberadamente conservador: solo prefijos de alta confianza.
# La base completa de IEEE son ~35.000 entradas y no tiene sentido embeberla.
# Para cobertura real, descarga https://standards-oui.ieee.org/oui/oui.txt y
# dejalo como "oui.txt" junto a este script: se carga solo al arrancar.
$global:OuiTable = @{
    # Virtualizacion y contenedores
    "080027" = "Oracle VirtualBox"
    "0A0027" = "VirtualBox (Host-Only)"
    "005056" = "VMware"
    "000C29" = "VMware"
    "000569" = "VMware"
    "001C14" = "VMware"
    "00155D" = "Microsoft Hyper-V"
    "525400" = "QEMU / KVM"
    "0242AC" = "Docker"
    # SBC e IoT
    "B827EB" = "Raspberry Pi Foundation"
    "DCA632" = "Raspberry Pi Trading"
    "E45F01" = "Raspberry Pi Trading"
    "28CDC1" = "Raspberry Pi Trading"
    "240AC4" = "Espressif (ESP32)"
    "30AEA4" = "Espressif (ESP32)"
    "84F3EB" = "Espressif (ESP32)"
    "A4CF12" = "Espressif (ESP32)"
    "7C9EBD" = "Espressif (ESP32)"
    "ECFABC" = "Espressif (ESP32)"
    # Fabricantes comunes
    "001B63" = "Apple"
    "3C15C2" = "Apple"
    "784F43" = "Apple"
    "A483E7" = "Apple"
    "ACBC32" = "Apple"
    "DCA904" = "Apple"
    "F01898" = "Apple"
    "F45C89" = "Apple"
    "00E04C" = "Realtek"
    "50C7BF" = "TP-Link"
    "24A43C" = "Ubiquiti"
    "802AA8" = "Ubiquiti"
}

function Import-OuiFile {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not [System.IO.File]::Exists($Path)) { return 0 }

    $table = @{}
    $rx = [regex]'^\s*([0-9A-Fa-f]{2})-([0-9A-Fa-f]{2})-([0-9A-Fa-f]{2})\s+\(hex\)\s+(.+?)\s*$'
    $reader = $null
    try {
        $reader = New-Object System.IO.StreamReader($Path)
        while ($null -ne ($linea = $reader.ReadLine())) {
            $m = $rx.Match($linea)
            if ($m.Success) {
                $table[($m.Groups[1].Value + $m.Groups[2].Value + $m.Groups[3].Value).ToUpperInvariant()] = $m.Groups[4].Value
            }
        }
    } catch {
        return 0
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    # La tabla integrada queda como respaldo para lo que el archivo no cubra.
    foreach ($k in $global:OuiTable.Keys) {
        if (-not $table.ContainsKey($k)) { $table[$k] = $global:OuiTable[$k] }
    }
    [ClientRegistry]::SetVendorTable($table)
    return $table.Count
}

[ClientRegistry]::SetVendorTable($global:OuiTable)
$script:ouiSource = "tabla integrada"
if ($PSScriptRoot) {
    $ouiPath = [System.IO.Path]::Combine($PSScriptRoot, "oui.txt")
    $loaded = Import-OuiFile -Path $ouiPath
    if ($loaded -gt 0) { $script:ouiSource = "oui.txt ($loaded entradas)" }
}

# --- FUNCIONES AUXILIARES Y ESTADO COMPARTIDO ---
function Test-IsAdmin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ActiveLANIPs {
    [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        Where-Object {
            $_.OperationalStatus -eq 'Up' -and
            $_.NetworkInterfaceType -ne 'Loopback' -and
            $_.NetworkInterfaceType -ne 'Tunnel'
        } |
        ForEach-Object {
            $_.GetIPProperties().UnicastAddresses |
                Where-Object {
                    $_.Address.AddressFamily -eq 'InterNetwork' -and
                    -not [System.Net.IPAddress]::IsLoopback($_.Address)
                } |
                Select-Object -ExpandProperty Address |
                ForEach-Object { $_.IPAddressToString }
        } |
        Where-Object { $_ -notlike "169.254.*" } |
        Select-Object -Unique
}

function Add-ServerFirewallRule {
    param([int]$Port)
    $ruleName = "Servidor Web Pro (TCP $Port)"
    try {
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName `
                                -Direction Inbound `
                                -Protocol TCP `
                                -LocalPort $Port `
                                -Action Allow `
                                -Profile @('Private', 'Domain') `
                                -Enabled True -ErrorAction Stop | Out-Null
        }
        return $ruleName
    } catch {
        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN]`r`nNo se pudo crear la regla de firewall: $($_.Exception.Message)`r`n")
        return $null
    }
}

function Remove-ServerFirewallRule {
    if ([string]::IsNullOrEmpty($script:firewallRuleName)) { return }
    try {
        Remove-NetFirewallRule -DisplayName $script:firewallRuleName -ErrorAction SilentlyContinue
        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nRegla de firewall eliminada: $($script:firewallRuleName)`r`n")
    } catch { }
    $script:firewallRuleName = $null
}

$esAdmin = Test-IsAdmin
$script:logQueue         = New-Object System.Collections.Concurrent.ConcurrentQueue[string]
$script:psInstance       = $null
$script:currentLanUrls   = @()
$script:firewallRuleName = $null
$script:maxLogChars      = 400000

$script:serverState = [hashtable]::Synchronized(@{
    IsRunning = $false
    Listener  = $null
})

# --- INTERFAZ GRAFICA ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Servidor Web Pro - Hardened & Multi-Threaded"
$form.Size = New-Object System.Drawing.Size(900, 680)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$labelPermiso = New-Object System.Windows.Forms.Label
$labelPermiso.Location = New-Object System.Drawing.Point(20, 15)
$labelPermiso.Size = New-Object System.Drawing.Size(400, 20)
$labelPermiso.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)

if ($esAdmin) {
    $labelPermiso.Text = "Permisos: Administrador (Elevado)"
    $labelPermiso.ForeColor = [System.Drawing.Color]::DarkGreen
} else {
    $labelPermiso.Text = "Permisos: Usuario Estandar (Sin Administrador)"
    $labelPermiso.ForeColor = [System.Drawing.Color]::DarkRed
}
$form.Controls.Add($labelPermiso)

$btnEscalar = New-Object System.Windows.Forms.Button
$btnEscalar.Location = New-Object System.Drawing.Point(710, 10)
$btnEscalar.Size = New-Object System.Drawing.Size(150, 25)
$btnEscalar.Text = "Escalar Privilegios"
$btnEscalar.Enabled = -not $esAdmin
$btnEscalar.Add_Click({
    if ($PSCommandPath) {
        try {
            Start-Process powershell.exe -ArgumentList @(
                "-STA", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`""
            ) -Verb RunAs -ErrorAction Stop
            $form.Close()
        } catch {
            [System.Windows.Forms.MessageBox]::Show(
                "No se pudo elevar: $($_.Exception.Message)",
                "Error", [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
    } else {
        [System.Windows.Forms.MessageBox]::Show(
            "Guarda el codigo en un archivo .ps1 para ejecutarlo como Administrador.",
            "Aviso", [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
    }
})
$form.Controls.Add($btnEscalar)

$line = New-Object System.Windows.Forms.Label
$line.Location = New-Object System.Drawing.Point(20, 40)
$line.Size = New-Object System.Drawing.Size(840, 2)
$line.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$form.Controls.Add($line)

$gbModo = New-Object System.Windows.Forms.GroupBox
$gbModo.Location = New-Object System.Drawing.Point(20, 50)
$gbModo.Size = New-Object System.Drawing.Size(840, 80)
$gbModo.Text = "Alcance del Servidor"

$rbLocal = New-Object System.Windows.Forms.RadioButton
$rbLocal.Location = New-Object System.Drawing.Point(15, 20)
$rbLocal.Size = New-Object System.Drawing.Size(220, 22)
$rbLocal.Text = "Solo este equipo (localhost)"
$rbLocal.Checked = $true
$gbModo.Controls.Add($rbLocal)

$rbLAN = New-Object System.Windows.Forms.RadioButton
$rbLAN.Location = New-Object System.Drawing.Point(245, 20)
$rbLAN.Size = New-Object System.Drawing.Size(350, 22)
$rbLAN.Text = "Red local (accesible desde otros dispositivos)"
$gbModo.Controls.Add($rbLAN)

$chkCors = New-Object System.Windows.Forms.CheckBox
$chkCors.Location = New-Object System.Drawing.Point(15, 48)
$chkCors.Size = New-Object System.Drawing.Size(600, 22)
$chkCors.Text = "Habilitar CORS abierto (Access-Control-Allow-Origin: *)"
$chkCors.Checked = $true
$gbModo.Controls.Add($chkCors)

$form.Controls.Add($gbModo)

$labelRuta = New-Object System.Windows.Forms.Label
$labelRuta.Location = New-Object System.Drawing.Point(20, 137)
$labelRuta.Size = New-Object System.Drawing.Size(400, 18)
$labelRuta.Text = "Carpeta raiz de la aplicacion web:"
$form.Controls.Add($labelRuta)

$txtRuta = New-Object System.Windows.Forms.TextBox
$txtRuta.Location = New-Object System.Drawing.Point(20, 157)
$txtRuta.Size = New-Object System.Drawing.Size(590, 23)
if ($PSScriptRoot) { $txtRuta.Text = $PSScriptRoot } else { $txtRuta.Text = "" }
$form.Controls.Add($txtRuta)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(620, 155)
$btnBrowse.Size = New-Object System.Drawing.Size(90, 27)
$btnBrowse.Text = "Examinar..."
$btnBrowse.Add_Click({
    $picker = New-Object ModernFolderPicker
    $picker.InitialFolder = $txtRuta.Text
    if ($picker.ShowDialog($form.Handle)) {
        $txtRuta.Text = $picker.SelectedPath
    }
})
$form.Controls.Add($btnBrowse)

$labelPuerto = New-Object System.Windows.Forms.Label
$labelPuerto.Location = New-Object System.Drawing.Point(725, 137)
$labelPuerto.Size = New-Object System.Drawing.Size(80, 18)
$labelPuerto.Text = "Puerto TCP:"
$form.Controls.Add($labelPuerto)

$txtPuerto = New-Object System.Windows.Forms.TextBox
$txtPuerto.Location = New-Object System.Drawing.Point(725, 157)
$txtPuerto.Size = New-Object System.Drawing.Size(135, 23)
$txtPuerto.Text = "8080"
$form.Controls.Add($txtPuerto)

$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 193)
$btnStart.Size = New-Object System.Drawing.Size(840, 38)
$btnStart.Text = "Iniciar Servidor"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnStart)

$gbEstado = New-Object System.Windows.Forms.GroupBox
$gbEstado.Location = New-Object System.Drawing.Point(20, 240)
$gbEstado.Size = New-Object System.Drawing.Size(840, 100)
$gbEstado.Text = "Estado y Direcciones de Acceso"

$txtEstadoInfo = New-Object System.Windows.Forms.TextBox
$txtEstadoInfo.Location = New-Object System.Drawing.Point(15, 22)
$txtEstadoInfo.Size = New-Object System.Drawing.Size(650, 68)
$txtEstadoInfo.Multiline = $true
$txtEstadoInfo.ReadOnly = $true
$txtEstadoInfo.ScrollBars = "Vertical"
$txtEstadoInfo.Text = "Estado: Detenido"
$txtEstadoInfo.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$gbEstado.Controls.Add($txtEstadoInfo)

$btnCopyLAN = New-Object System.Windows.Forms.Button
$btnCopyLAN.Location = New-Object System.Drawing.Point(675, 30)
$btnCopyLAN.Size = New-Object System.Drawing.Size(150, 45)
$btnCopyLAN.Text = "Copiar URL LAN"
$btnCopyLAN.Enabled = $false
$btnCopyLAN.Add_Click({
    if ($script:currentLanUrls.Count -gt 0) {
        $textToCopy = $script:currentLanUrls -join "`r`n"
        try {
            [System.Windows.Forms.Clipboard]::SetText($textToCopy)
            [System.Windows.Forms.MessageBox]::Show(
                "URL(s) LAN copiada(s) al portapapeles:`r`n`r`n$textToCopy",
                "Copiado", [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        } catch {
            [System.Windows.Forms.MessageBox]::Show(
                "No se pudo acceder al portapapeles: $($_.Exception.Message)",
                "Error", [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        }
    }
})
$gbEstado.Controls.Add($btnCopyLAN)
$form.Controls.Add($gbEstado)

# =====================================================================
#  PESTANAS: REGISTRO + CLIENTES
# =====================================================================
$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Location = New-Object System.Drawing.Point(20, 348)
$tabs.Size = New-Object System.Drawing.Size(840, 275)

$tabLog = New-Object System.Windows.Forms.TabPage
$tabLog.Text = "Registro de Telemetria"
$tabLog.UseVisualStyleBackColor = $true

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(6, 6)
$txtLog.Size = New-Object System.Drawing.Size(820, 235)
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
$txtLog.ForeColor = [System.Drawing.Color]::FromArgb(220, 220, 220)
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$tabLog.Controls.Add($txtLog)
$tabs.TabPages.Add($tabLog)

$tabClientes = New-Object System.Windows.Forms.TabPage
$tabClientes.Text = "Clientes conectados"
$tabClientes.UseVisualStyleBackColor = $true

$lvClientes = New-Object System.Windows.Forms.ListView
$lvClientes.Location = New-Object System.Drawing.Point(6, 6)
$lvClientes.Size = New-Object System.Drawing.Size(820, 128)
$lvClientes.View = [System.Windows.Forms.View]::Details
$lvClientes.FullRowSelect = $true
$lvClientes.GridLines = $true
$lvClientes.MultiSelect = $false
$lvClientes.HideSelection = $false
$lvClientes.Font = New-Object System.Drawing.Font("Consolas", 8.5)
[void]$lvClientes.Columns.Add("IP", 105)
[void]$lvClientes.Columns.Add("Host (rDNS)", 125)
[void]$lvClientes.Columns.Add("MAC", 125)
[void]$lvClientes.Columns.Add("Fabricante", 110)
[void]$lvClientes.Columns.Add("Dispositivo", 115)
[void]$lvClientes.Columns.Add("Navegador", 100)
[void]$lvClientes.Columns.Add("Pet.", 45)
[void]$lvClientes.Columns.Add("Ultima", 70)
$tabClientes.Controls.Add($lvClientes)

$btnReId = New-Object System.Windows.Forms.Button
$btnReId.Location = New-Object System.Drawing.Point(6, 140)
$btnReId.Size = New-Object System.Drawing.Size(115, 26)
$btnReId.Text = "Re-identificar"
$btnReId.Add_Click({
    [ClientRegistry]::ResetResolution()
    $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nRe-resolviendo rDNS y ARP de todos los clientes.`r`n")
})
$tabClientes.Controls.Add($btnReId)

$btnLimpiarClientes = New-Object System.Windows.Forms.Button
$btnLimpiarClientes.Location = New-Object System.Drawing.Point(127, 140)
$btnLimpiarClientes.Size = New-Object System.Drawing.Size(115, 26)
$btnLimpiarClientes.Text = "Limpiar lista"
$btnLimpiarClientes.Add_Click({
    [ClientRegistry]::Clear()
    $lvClientes.Items.Clear()
    $script:clientRows.Clear()
    $txtClienteDetalle.Text = ""
})
$tabClientes.Controls.Add($btnLimpiarClientes)

$btnExportClientes = New-Object System.Windows.Forms.Button
$btnExportClientes.Location = New-Object System.Drawing.Point(248, 140)
$btnExportClientes.Size = New-Object System.Drawing.Size(115, 26)
$btnExportClientes.Text = "Exportar CSV"
$btnExportClientes.Add_Click({
    $snapshot = [ClientRegistry]::Snapshot()
    if ($snapshot.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show(
            "No hay clientes registrados.", "Exportar",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        return
    }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter = "CSV (*.csv)|*.csv"
    $dlg.FileName = "clientes_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }

    try {
        $filas = foreach ($c in $snapshot) {
            [PSCustomObject]@{
                IP           = $c.Ip
                Host         = $c.HostName
                MAC          = $c.Mac
                MacAleatoria = $c.MacRandomized
                Fabricante   = $c.Vendor
                Dispositivo  = $c.DeviceLabel
                Navegador    = $c.BrowserLabel
                Idioma       = $c.AcceptLanguage
                Peticiones   = $c.RequestCount
                Bytes        = $c.BytesSent
                Primera      = (Get-ClientLocalTime $c.FirstSeenTicks)
                Ultima       = (Get-ClientLocalTime $c.LastSeenTicks)
                UserAgent    = $c.UserAgent
            }
        }
        $filas | Export-Csv -Path $dlg.FileName -NoTypeInformation -Encoding UTF8
    } catch {
        [System.Windows.Forms.MessageBox]::Show(
            "No se pudo exportar: $($_.Exception.Message)", "Error",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
})
$tabClientes.Controls.Add($btnExportClientes)

$lblOui = New-Object System.Windows.Forms.Label
$lblOui.Location = New-Object System.Drawing.Point(372, 145)
$lblOui.Size = New-Object System.Drawing.Size(454, 18)
$lblOui.ForeColor = [System.Drawing.Color]::DimGray
$lblOui.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$lblOui.Text = "Fabricante: $($script:ouiSource). Coloca oui.txt (IEEE) junto al script para cobertura completa."
$tabClientes.Controls.Add($lblOui)

$txtClienteDetalle = New-Object System.Windows.Forms.TextBox
$txtClienteDetalle.Location = New-Object System.Drawing.Point(6, 172)
$txtClienteDetalle.Size = New-Object System.Drawing.Size(820, 69)
$txtClienteDetalle.Multiline = $true
$txtClienteDetalle.ReadOnly = $true
$txtClienteDetalle.ScrollBars = "Vertical"
$txtClienteDetalle.BackColor = [System.Drawing.Color]::FromArgb(248, 248, 248)
$txtClienteDetalle.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$txtClienteDetalle.Text = "Selecciona un cliente para ver su detalle completo."
$tabClientes.Controls.Add($txtClienteDetalle)

$tabs.TabPages.Add($tabClientes)
$form.Controls.Add($tabs)

# --- FORMATEO Y REFRESCO DEL PANEL DE CLIENTES ---
$script:clientRows = @{}

function Get-ClientLocalTime {
    param([long]$Ticks)
    if ($Ticks -le 0) { return "-" }
    return ([DateTime]::new($Ticks, [System.DateTimeKind]::Utc)).ToLocalTime().ToString("HH:mm:ss")
}

function Format-ByteSize {
    param([long]$Bytes)
    if ($Bytes -lt 1024)          { return "$Bytes B" }
    if ($Bytes -lt 1048576)       { return "{0:N1} KB" -f ($Bytes / 1024) }
    if ($Bytes -lt 1073741824)    { return "{0:N1} MB" -f ($Bytes / 1048576) }
    return "{0:N2} GB" -f ($Bytes / 1073741824)
}

function Format-Elapsed {
    param([long]$Ticks)
    if ($Ticks -le 0) { return "-" }
    $secs = [int]([DateTime]::UtcNow - [DateTime]::new($Ticks, [System.DateTimeKind]::Utc)).TotalSeconds
    if ($secs -lt 2)    { return "ahora" }
    if ($secs -lt 60)   { return "${secs}s" }
    if ($secs -lt 3600) { return "$([int]($secs / 60))m" }
    return "$([int]($secs / 3600))h"
}

function Show-ClientDetail {
    if ($lvClientes.SelectedItems.Count -eq 0) { return }
    $ip = $lvClientes.SelectedItems[0].Tag
    $c = $null
    foreach ($x in [ClientRegistry]::Snapshot()) { if ($x.Ip -eq $ip) { $c = $x; break } }
    if ($null -eq $c) { return }

    $macLinea = if ([string]::IsNullOrEmpty($c.Mac)) {
        "(sin respuesta ARP: fuera del segmento local o cliente inactivo)"
    } elseif ($c.MacRandomized) {
        "$($c.Mac)  [ALEATORIA - privacidad del dispositivo, el OUI no identifica al fabricante]"
    } else {
        "$($c.Mac)  [$($c.Vendor)]"
    }

    $hints = @()
    if ($c.ChUa)       { $hints += "ua=$($c.ChUa)" }
    if ($c.ChPlatform) { $hints += "plataforma=$($c.ChPlatform)" }
    if ($c.ChMobile)   { $hints += "movil=$($c.ChMobile)" }
    if ($c.ChModel)    { $hints += "modelo=$($c.ChModel)" }
    $hintsLinea = if ($hints.Count -gt 0) { $hints -join "  |  " } else { "(no enviados: requieren contexto seguro, solo llegan por https o localhost)" }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("IP             : $($c.Ip)")
    [void]$sb.AppendLine("Host (rDNS)    : $(if ($c.HostName) { $c.HostName } else { '(sin resolucion inversa)' })")
    [void]$sb.AppendLine("MAC            : $macLinea")
    [void]$sb.AppendLine("Dispositivo    : $($c.DeviceLabel)")
    [void]$sb.AppendLine("Navegador      : $($c.BrowserLabel)")
    [void]$sb.AppendLine("Idioma         : $(if ($c.AcceptLanguage) { $c.AcceptLanguage } else { '-' })")
    [void]$sb.AppendLine("Client Hints   : $hintsLinea")
    [void]$sb.AppendLine("Trafico        : $($c.RequestCount) peticiones  |  $(Format-ByteSize $c.BytesSent)")
    [void]$sb.AppendLine("Visto          : primera $(Get-ClientLocalTime $c.FirstSeenTicks)  |  ultima $(Get-ClientLocalTime $c.LastSeenTicks)")
    [void]$sb.AppendLine("User-Agent     : $(if ($c.UserAgent) { $c.UserAgent } else { '(vacio)' })")
    $txtClienteDetalle.Text = $sb.ToString()
}

function Update-ClientList {
    $snapshot = [ClientRegistry]::Snapshot()
    $tabClientes.Text = if ($snapshot.Count -gt 0) { "Clientes conectados ($($snapshot.Count))" } else { "Clientes conectados" }
    if ($snapshot.Count -eq 0) { return }

    $lvClientes.BeginUpdate()
    try {
        foreach ($c in $snapshot) {
            $item = $script:clientRows[$c.Ip]
            if ($null -eq $item) {
                $item = New-Object System.Windows.Forms.ListViewItem($c.Ip)
                for ($i = 0; $i -lt 7; $i++) { [void]$item.SubItems.Add("") }
                $item.Tag = $c.Ip
                [void]$lvClientes.Items.Add($item)
                $script:clientRows[$c.Ip] = $item
            }

            $host_ = if ($c.HostName) { ($c.HostName -split '\.')[0] } else { "..." }
            $mac_  = if ($c.Mac) { $c.Mac } else { "..." }
            $vend_ = if ($c.MacRandomized) { "MAC aleatoria" } elseif ($c.Vendor) { $c.Vendor } else { "..." }

            $item.SubItems[1].Text = $host_
            $item.SubItems[2].Text = $mac_
            $item.SubItems[3].Text = $vend_
            $item.SubItems[4].Text = $c.DeviceLabel
            $item.SubItems[5].Text = $c.BrowserLabel
            $item.SubItems[6].Text = [string]$c.RequestCount
            $item.SubItems[7].Text = Format-Elapsed $c.LastSeenTicks

            # Inactivo mas de 60 s: se atenua en vez de desaparecer, para no
            # perder el historial de quien se conecto durante la sesion.
            $idle = ([DateTime]::UtcNow - [DateTime]::new($c.LastSeenTicks, [System.DateTimeKind]::Utc)).TotalSeconds
            $item.ForeColor = if ($idle -gt 60) { [System.Drawing.Color]::Gray } else { [System.Drawing.Color]::Black }
        }
    } finally {
        $lvClientes.EndUpdate()
    }

    if ($lvClientes.SelectedItems.Count -gt 0) { Show-ClientDetail }
}

$lvClientes.Add_SelectedIndexChanged({ Show-ClientDetail })

$clientsTimer = New-Object System.Windows.Forms.Timer
$clientsTimer.Interval = 1500
$clientsTimer.Add_Tick({
    # Solo se repinta si la pestana esta a la vista.
    if ($tabs.SelectedTab -eq $tabClientes) { Update-ClientList }
    else { $tabClientes.Text = "Clientes conectados ($([ClientRegistry]::Snapshot().Count))" }
})
$clientsTimer.Start()

# Drenaje por lotes: un solo AppendText por tick en vez de uno por mensaje,
# con recorte del buffer para que la UI no se degrade en sesiones largas.
$logTimer = New-Object System.Windows.Forms.Timer
$logTimer.Interval = 100
$logTimer.Add_Tick({
    if ($script:logQueue.IsEmpty) { return }

    $sb = New-Object System.Text.StringBuilder
    $logMsg = ""
    $drained = 0
    while ($drained -lt 500 -and $script:logQueue.TryDequeue([ref]$logMsg)) {
        [void]$sb.Append($logMsg)
        $drained++
    }
    if ($sb.Length -eq 0) { return }

    $txtLog.AppendText($sb.ToString())

    if ($txtLog.TextLength -gt $script:maxLogChars) {
        $keepChars = [int]($script:maxLogChars * 0.7)
        $trimmed = $txtLog.Text.Substring($txtLog.TextLength - $keepChars)
        $nl = $trimmed.IndexOf("`n")
        if ($nl -ge 0) { $trimmed = $trimmed.Substring($nl + 1) }
        $txtLog.Text = "[... registro truncado ...]`r`n" + $trimmed
    }

    $txtLog.SelectionStart = $txtLog.TextLength
    $txtLog.ScrollToCaret()
})
$logTimer.Start()

# --- HANDLER CONCURRENTE ---
$requestHandlerScript = {
    param($context, $rootPath, $realRootPath, $logQueue, $serverState, $mimeDict, $corsEnabled)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $request  = $context.Request
    $response = $context.Response

    $clientIP = "?"
    try { if ($request.RemoteEndPoint) { $clientIP = $request.RemoteEndPoint.Address.ToString() } } catch { }

    $httpMethod = $request.HttpMethod
    $rawUrl     = "?"
    try { if ($request.Url) { $rawUrl = $request.Url.PathAndQuery } } catch { }

    [long]$bytesSent = 0

    $writeLog = {
        param($level, $note)
        if ($sw.IsRunning) { $sw.Stop() }
        $code = 0
        try { $code = $response.StatusCode } catch { }
        $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        $logQueue.Enqueue("$ts [$level]`r`n$httpMethod $rawUrl $code $($sw.ElapsedMilliseconds)ms ${bytesSent}B$note - Client: $clientIP`r`n")
    }

    try {
        if ($corsEnabled) {
            $response.AddHeader("Access-Control-Allow-Origin", "*")
            $response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS")
            $response.AddHeader("Access-Control-Allow-Headers", "*")
        }
        $response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        $response.AddHeader("X-Content-Type-Options", "nosniff")

        if ($httpMethod -eq "OPTIONS") {
            $response.StatusCode = 204
            & $writeLog 'INFO' ''
            return
        }

        if ($httpMethod -ne "GET" -and $httpMethod -ne "HEAD") {
            $response.StatusCode = 405
            $response.AddHeader("Allow", "GET, HEAD, OPTIONS")
            & $writeLog 'WARN' ' - Metodo no permitido'
            return
        }

        if ($null -eq $request.Url) {
            $response.StatusCode = 400
            & $writeLog 'WARN' ' - URL invalida'
            return
        }

        # BUG 1: Url.LocalPath YA viene decodificado. Un segundo UnescapeDataString
        # convertia %2520 en espacio y %252e%252e%252f en ../, anulando una capa
        # de defensa y corrompiendo nombres de archivo legitimos.
        $localPath   = $request.Url.LocalPath
        $isDirectory = $localPath.EndsWith('/')
        $relPath     = $localPath.TrimStart('/', '\')

        # Rechazo de NUL y de ':' (Alternate Data Streams y rutas con unidad).
        if ($relPath.IndexOf([char]0) -ge 0 -or $relPath.Contains(":")) {
            $response.StatusCode = 400
            & $writeLog 'SECURITY WARN' ' - Ruta con caracteres prohibidos'
            return
        }

        if ([string]::IsNullOrEmpty($relPath)) { $relPath = "index.html" }

        $fullPath = $null
        try {
            $fullPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($rootPath, $relPath))
        } catch {
            $response.StatusCode = 400
            & $writeLog 'WARN' ' - Ruta malformada'
            return
        }

        # Chequeo barato previo a tocar el disco. El control autoritativo sigue
        # siendo el del handle real, mas abajo.
        if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $response.StatusCode = 403
            & $writeLog 'SECURITY WARN' ' - Path traversal bloqueado'
            return
        }

        $fileToServe = $null

        if ([System.IO.File]::Exists($fullPath)) {
            $fileToServe = $fullPath
        }
        elseif ([System.IO.Directory]::Exists($fullPath)) {
            # BUG 3: los subdirectorios con index.html propio nunca se servian.
            if (-not $isDirectory) {
                # Sin barra final las rutas relativas del HTML se resuelven mal.
                $location = $request.Url.AbsolutePath + '/'
                if (-not [string]::IsNullOrEmpty($request.Url.Query)) { $location += $request.Url.Query }
                $response.StatusCode = 301
                $response.AddHeader("Location", $location)
                & $writeLog 'INFO' ' - Redirigido a directorio'
                return
            }
            $dirIndex = [System.IO.Path]::Combine($fullPath, "index.html")
            if ([System.IO.File]::Exists($dirIndex)) { $fileToServe = $dirIndex }
        }

        # Fallback SPA: solo para rutas sin extension.
        if ($null -eq $fileToServe) {
            if ([string]::IsNullOrEmpty([System.IO.Path]::GetExtension($relPath))) {
                $spaIndex = [System.IO.Path]::Combine($rootPath, "index.html")
                if ([System.IO.File]::Exists($spaIndex)) { $fileToServe = $spaIndex }
            }
        }

        if ($null -eq $fileToServe) {
            $response.StatusCode = 404
            & $writeLog 'WARN' ' - Archivo no encontrado'
            return
        }

        $fileStream = $null
        try {
            $fileStream = [System.IO.File]::Open(
                $fileToServe,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
        } catch {
            $response.StatusCode = 404
            & $writeLog 'WARN' ' - No se pudo abrir el handle'
            return
        }

        try {
            # Control autoritativo: se canoniza el handle YA abierto, no una ruta
            # que pueda cambiar entre la comprobacion y el uso.
            $realHandlePath = [NativePath]::GetRealPathFromHandle($fileStream.SafeFileHandle)

            if ([string]::IsNullOrEmpty($realHandlePath) -or
                -not $realHandlePath.StartsWith($realRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                $response.StatusCode = 403
                & $writeLog 'SECURITY WARN' ' - Symlink/TOCTOU bloqueado'
                return
            }

            # BUG 5: MimeMapping nunca devuelve vacio, asi que la tercera capa era
            # codigo muerto y ademas ataba el script a .NET Framework via System.Web.
            $ext = [System.IO.Path]::GetExtension($fileToServe).ToLowerInvariant()
            if ($mimeDict.ContainsKey($ext)) {
                $response.ContentType = $mimeDict[$ext]
            } else {
                $response.ContentType = "application/octet-stream"
            }

            [long]$fileLength = $fileStream.Length
            $response.AddHeader("Accept-Ranges", "bytes")

            [long]$start = 0
            [long]$end   = $fileLength - 1
            $useRange     = $false
            $rangeInvalid = $false

            $rangeHeader = $request.Headers["Range"]
            if (-not [string]::IsNullOrWhiteSpace($rangeHeader) -and
                $rangeHeader -match '^\s*bytes\s*=\s*(\d*)\s*-\s*(\d*)\s*$') {

                $rawStart = $matches[1]
                $rawEnd   = $matches[2]

                if ([string]::IsNullOrEmpty($rawStart) -and [string]::IsNullOrEmpty($rawEnd)) {
                    # BUG 8: "bytes=-" matcheaba y devolvia 206 con el archivo completo.
                    $rangeInvalid = $true
                }
                elseif ([string]::IsNullOrEmpty($rawStart)) {
                    [long]$suffix = 0
                    if (-not [long]::TryParse($rawEnd, [ref]$suffix) -or $suffix -le 0) {
                        $rangeInvalid = $true
                    } else {
                        $start = [Math]::Max([long]0, $fileLength - $suffix)
                        $end   = $fileLength - 1
                        $useRange = $true
                    }
                }
                else {
                    [long]$parsedStart = 0
                    if (-not [long]::TryParse($rawStart, [ref]$parsedStart)) {
                        $rangeInvalid = $true
                    } else {
                        $start = $parsedStart
                        if ([string]::IsNullOrEmpty($rawEnd)) {
                            $end = $fileLength - 1
                            $useRange = $true
                        } else {
                            [long]$parsedEnd = 0
                            if (-not [long]::TryParse($rawEnd, [ref]$parsedEnd)) {
                                $rangeInvalid = $true
                            } else {
                                $end = $parsedEnd
                                $useRange = $true
                            }
                        }
                    }
                }

                if ($useRange) {
                    if ($end -ge $fileLength) { $end = $fileLength - 1 }
                    if ($fileLength -eq 0 -or $start -ge $fileLength -or $start -gt $end) {
                        $rangeInvalid = $true
                        $useRange = $false
                    }
                }
            }

            if ($rangeInvalid) {
                $response.StatusCode = 416
                $response.AddHeader("Content-Range", "bytes */$fileLength")
                & $writeLog 'WARN' ' - Range no satisfacible'
                return
            }

            [long]$contentLength = 0
            if ($useRange) {
                $response.StatusCode = 206
                $contentLength = $end - $start + 1
                $response.AddHeader("Content-Range", "bytes $start-$end/$fileLength")
            } else {
                $response.StatusCode = 200
                $start = 0
                $contentLength = $fileLength
            }
            $response.ContentLength64 = $contentLength

            $aborted = $false

            if ($httpMethod -eq "GET" -and $contentLength -gt 0) {
                try {
                    if ($start -gt 0) {
                        [void]$fileStream.Seek($start, [System.IO.SeekOrigin]::Begin)
                    }
                    $buffer = New-Object byte[] 65536
                    [long]$remaining = $contentLength
                    $outStream = $response.OutputStream

                    while ($remaining -gt 0) {
                        if (-not $serverState.IsRunning) { $aborted = $true; break }
                        $toRead = [int][Math]::Min([long]$buffer.Length, $remaining)
                        $read = $fileStream.Read($buffer, 0, $toRead)
                        if ($read -le 0) { break }
                        $outStream.Write($buffer, 0, $read)
                        $remaining -= $read
                        $bytesSent += $read
                    }
                }
                # BUG 7: la desconexion del cliente (seek de video, imagen cancelada)
                # caia al catch generico y ensuciaba el log con ERROR 500 falsos.
                catch [System.Net.HttpListenerException] { $aborted = $true }
                catch [System.IO.IOException]            { $aborted = $true }
                catch [System.ObjectDisposedException]   { $aborted = $true }
            }

            if ($aborted) {
                & $writeLog 'INFO' ' - Transferencia interrumpida'
            } else {
                & $writeLog 'INFO' ''
            }
        }
        finally {
            if ($null -ne $fileStream) { $fileStream.Dispose() }
        }
    }
    catch {
        $ex = $_.Exception
        try { $response.StatusCode = 500 } catch { }
        if ($sw.IsRunning) { $sw.Stop() }
        $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        $logQueue.Enqueue("$ts [ERROR]`r`n$httpMethod $rawUrl - Client: $clientIP`r`nException [$($ex.GetType().FullName)]:`r`n$($ex.Message)`r`n")
    }
    finally {
        # Registro del cliente: una sola escritura atomica por peticion. El
        # enriquecimiento lento (rDNS, ARP) lo dispara ClientRegistry en el
        # ThreadPool, nunca en este hilo.
        try {
            $ua    = ""
            $lang  = ""
            $chUa  = ""
            $chPlt = ""
            $chMob = ""
            $chMod = ""
            try {
                $h = $request.Headers
                $ua    = [string]$request.UserAgent
                $lang  = [string]$h["Accept-Language"]
                $chUa  = [string]$h["Sec-CH-UA"]
                $chPlt = [string]$h["Sec-CH-UA-Platform"]
                $chMob = [string]$h["Sec-CH-UA-Mobile"]
                $chMod = [string]$h["Sec-CH-UA-Model"]
            } catch { }
            [ClientRegistry]::Track($clientIP, $ua, $lang, $chUa, $chPlt, $chMob, $chMod, $bytesSent)
        } catch { }

        try { $context.Response.Close() } catch { }
    }
}

# --- MASTER LISTENER LOOP ---
# BUG 6: el listener ahora se crea e inicia en el hilo de UI y se inyecta ya
# arrancado. Antes serverState.Listener se poblaba desde este runspace, y un
# Detener inmediato veia $null, se saltaba el Stop() y dejaba el puerto tomado.
$serverMasterScript = {
    param($listener, $rootPath, $realRootPath, $logQueue, $handlerScriptBlock, $serverState, $mimeDict, $corsEnabled)

    function Clear-CompletedTasks {
        param($list)
        for ($i = $list.Count - 1; $i -ge 0; $i--) {
            if ($list[$i].Status.IsCompleted) {
                try { $list[$i].Pipe.EndInvoke($list[$i].Status) } catch { }
                try { $list[$i].Pipe.Dispose() } catch { }
                $list.RemoveAt($i)
            }
        }
    }

    $pool = $null
    $tasks = New-Object System.Collections.Generic.List[PSObject]

    try {
        $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2()
        $pool = [runspacefactory]::CreateRunspacePool(2, 25, $iss, $Host)
        $pool.Open()

        while ($serverState.IsRunning -and $listener.IsListening) {
            $ctxTask = $null
            try { $ctxTask = $listener.GetContextAsync() } catch { break }

            # Espera con timeout: permite cosechar pipelines terminados y
            # reaccionar al Detener aunque no llegue ninguna peticion nueva.
            $gotContext = $false
            $faulted = $false
            while ($true) {
                $completed = $false
                try { $completed = $ctxTask.Wait(250) } catch { $faulted = $true; break }
                if ($completed) { $gotContext = $true; break }
                if (-not $serverState.IsRunning) { break }
                Clear-CompletedTasks $tasks
            }
            if ($faulted -or -not $gotContext) { break }

            $context = $null
            try { $context = $ctxTask.Result } catch { break }
            if ($null -eq $context) { break }

            $psWorker = [powershell]::Create()
            $psWorker.RunspacePool = $pool
            [void]$psWorker.AddScript($handlerScriptBlock)
            [void]$psWorker.AddArgument($context)
            [void]$psWorker.AddArgument($rootPath)
            [void]$psWorker.AddArgument($realRootPath)
            [void]$psWorker.AddArgument($logQueue)
            [void]$psWorker.AddArgument($serverState)
            [void]$psWorker.AddArgument($mimeDict)
            [void]$psWorker.AddArgument($corsEnabled)

            $asyncResult = $psWorker.BeginInvoke()
            [void]$tasks.Add([PSCustomObject]@{ Pipe = $psWorker; Status = $asyncResult })

            Clear-CompletedTasks $tasks
        }
    }
    catch {
        if ($serverState.IsRunning) {
            $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR]`r`nMaster Loop Error: $($_.Exception.Message)`r`n")
        }
    }
    finally {
        foreach ($t in $tasks) {
            try { $t.Pipe.Stop() }    catch { }
            try { $t.Pipe.Dispose() } catch { }
        }
        if ($null -ne $pool) {
            try { $pool.Close(); $pool.Dispose() } catch { }
        }
    }
}

# --- CONTROL DE INICIO Y DETENCION ---
function Stop-WebServer {
    $script:serverState.IsRunning = $false

    if ($null -ne $script:serverState.Listener) {
        try { $script:serverState.Listener.Stop() }  catch { }
        try { $script:serverState.Listener.Close() } catch { }
        $script:serverState.Listener = $null
    }

    if ($null -ne $script:psInstance) {
        try { $script:psInstance.Stop() }    catch { }
        try { $script:psInstance.Dispose() } catch { }
        $script:psInstance = $null
    }

    Remove-ServerFirewallRule
}

$btnStart.Add_Click({
    if ($script:serverState.IsRunning) {
        Stop-WebServer

        $gbModo.Enabled     = $true
        $txtPuerto.Enabled  = $true
        $txtRuta.Enabled    = $true
        $btnBrowse.Enabled  = $true
        $btnCopyLAN.Enabled = $false
        $script:currentLanUrls = @()

        $txtEstadoInfo.Text  = "Estado: Detenido"
        $btnStart.Text       = "Iniciar Servidor"
        $btnStart.BackColor  = [System.Drawing.Color]::FromArgb(40, 167, 69)

        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nServidor detenido.`r`n")
        return
    }

    # --- Validacion de puerto ---
    $puertoRaw = $txtPuerto.Text.Trim()
    [int]$puerto = 0
    if (-not [int]::TryParse($puertoRaw, [ref]$puerto) -or $puerto -lt 1 -or $puerto -gt 65535) {
        [System.Windows.Forms.MessageBox]::Show(
            "Ingrese un puerto valido (1-65535).", "Error de Validacion",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }

    # --- Validacion de raiz ---
    $rutaInput = $txtRuta.Text.Trim().Trim('"').Trim("'")
    if ([string]::IsNullOrWhiteSpace($rutaInput) -or -not [System.IO.Directory]::Exists($rutaInput)) {
        [System.Windows.Forms.MessageBox]::Show(
            "Selecciona una carpeta raiz valida.", "Error",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }

    $basePath = [System.IO.Path]::GetFullPath($rutaInput)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }

    # La raiz por defecto era C:\ : un clic accidental en modo LAN publicaba el
    # disco entero. Ahora se exige confirmacion explicita.
    if ([System.IO.Path]::GetPathRoot($basePath) -eq $basePath) {
        $confirm = [System.Windows.Forms.MessageBox]::Show(
            "Vas a servir la RAIZ COMPLETA de la unidad ($basePath).`r`n`r`nTodo el contenido de ese disco quedara accesible. Continuar?",
            "Advertencia de exposicion",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    $realRootPath = [NativePath]::GetRealPath($basePath)
    if ([string]::IsNullOrEmpty($realRootPath)) {
        [System.Windows.Forms.MessageBox]::Show(
            "No se pudo canonizar la carpeta raiz. Sin esa resolucion el control anti-symlink no puede aplicarse y el servidor no arrancara.",
            "Error de resolucion de ruta",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }
    if (-not $realRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $realRootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    # --- Binding ---
    $bindingPrefix = ""
    if ($rbLocal.Checked) {
        $bindingPrefix = "http://localhost:$puerto/"
    } else {
        if (-not (Test-IsAdmin)) {
            [System.Windows.Forms.MessageBox]::Show(
                "El modo Red Local requiere ejecutar la aplicacion como Administrador.",
                "Permisos Insuficientes",
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
            return
        }
        $bindingPrefix = "http://+:$puerto/"
    }

    # El listener se crea y arranca AQUI para que el error de binding (puerto
    # ocupado, ACL faltante) se vea de inmediato y no como una linea de log.
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add($bindingPrefix)
    try {
        $listener.Start()
    } catch {
        try { $listener.Close() } catch { }
        [System.Windows.Forms.MessageBox]::Show(
            "No se pudo iniciar el listener en $bindingPrefix`r`n`r`n$($_.Exception.Message)",
            "Error al iniciar",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
        return
    }

    $script:serverState.Listener  = $listener
    $script:serverState.IsRunning = $true

    # Cada sesion de servidor arranca con la lista de clientes en blanco.
    [ClientRegistry]::Clear()
    $lvClientes.Items.Clear()
    $script:clientRows.Clear()
    $txtClienteDetalle.Text = "Selecciona un cliente para ver su detalle completo."

    # --- Estado y firewall (solo despues de un Start() exitoso) ---
    $statusText = "Estado: Corriendo`r`n`r`nAcceso local:`r`nhttp://localhost:$puerto/"

    if ($rbLocal.Checked) {
        $btnCopyLAN.Enabled = $false
        $script:currentLanUrls = @()
    } else {
        $detectedIPs = @(Get-ActiveLANIPs)
        if ($detectedIPs.Count -gt 0) {
            $script:currentLanUrls = @($detectedIPs | ForEach-Object { "http://${_}:$puerto/" })
            $statusText += "`r`n`r`nAcceso LAN:`r`n" + ($script:currentLanUrls -join "`r`n")
            $btnCopyLAN.Enabled = $true
        } else {
            $statusText += "`r`n`r`nAcceso LAN:`r`nNo se detectaron adaptadores de red activos."
            $btnCopyLAN.Enabled = $false
        }
        $script:firewallRuleName = Add-ServerFirewallRule -Port $puerto
    }

    # --- Arranque del loop maestro ---
    $script:psInstance = [powershell]::Create()
    [void]$script:psInstance.AddScript($serverMasterScript)
    [void]$script:psInstance.AddArgument($listener)
    [void]$script:psInstance.AddArgument($basePath)
    [void]$script:psInstance.AddArgument($realRootPath)
    [void]$script:psInstance.AddArgument($script:logQueue)
    [void]$script:psInstance.AddArgument($requestHandlerScript)
    [void]$script:psInstance.AddArgument($script:serverState)
    [void]$script:psInstance.AddArgument($global:MimeTypes)
    [void]$script:psInstance.AddArgument([bool]$chkCors.Checked)
    [void]$script:psInstance.BeginInvoke()

    $gbModo.Enabled    = $false
    $txtPuerto.Enabled = $false
    $txtRuta.Enabled   = $false
    $btnBrowse.Enabled = $false

    $txtEstadoInfo.Text = $statusText
    $btnStart.Text      = "Detener Servidor"
    $btnStart.BackColor = [System.Drawing.Color]::FromArgb(220, 53, 69)

    $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nServidor iniciado en $bindingPrefix [Raiz real: $realRootPath]`r`n")

    try { Start-Process "http://localhost:$puerto/" -ErrorAction Stop } catch {
        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN]`r`nNo se pudo abrir el navegador: $($_.Exception.Message)`r`n")
    }
})

$form.Add_FormClosing({
    $logTimer.Stop()
    $clientsTimer.Stop()
    Stop-WebServer
})

[void]$form.ShowDialog()

# ShowDialog no libera el formulario: la limpieza va aqui, no dentro de FormClosed.
try { $logTimer.Dispose() }     catch { }
try { $clientsTimer.Dispose() } catch { }
try { $form.Dispose() }         catch { }
