#requires -version 5.1
<#
    Servidor Web Pro - Hardened & Multi-Threaded
    Version corregida (archivo unico).
#>

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# --- GUARDA DE APARTAMENTO STA ---
# WinForms y Clipboard exigen STA. En MTA (pwsh 7 por defecto) el formulario
# se comporta de forma erratica y Clipboard::SetText lanza excepcion.
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    Write-Host "ERROR: este script requiere apartamento STA." -ForegroundColor Red
    Write-Host "Ejecutalo con:  powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"<ruta>.ps1`"" -ForegroundColor Yellow
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# --- OCULTAR LA CONSOLA ANFITRIONA ---
# El proceso sigue siendo powershell.exe, solo se esconde su ventana. Esta
# clase va aparte y es diminuta para que compile en milisegundos: el bloque
# grande de tipos tarda bastante mas y hasta entonces la consola seria visible.
if (-not ([System.Management.Automation.PSTypeName]'ConsolaWin').Type) {
    Add-Type -TypeDefinition @"
    using System;
    using System.Runtime.InteropServices;

    public static class ConsolaWin {
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]   private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        public static void Ocultar() {
            IntPtr h = GetConsoleWindow();
            if (h != IntPtr.Zero) ShowWindow(h, SW_HIDE);
        }

        public static void Mostrar() {
            IntPtr h = GetConsoleWindow();
            if (h != IntPtr.Zero) ShowWindow(h, SW_SHOW);
        }
    }
"@
}

# Comenta esta linea si necesitas ver la consola para depurar.
[ConsolaWin]::Ocultar()

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
    using System.Net.NetworkInformation;
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

    // =================================================================
    //  NativeUi - mensajes Win32 que WinForms no expone. Necesario para
    //  repintar sin parpadeo y para conservar la barra de desplazamiento.
    // =================================================================
    public static class NativeUi {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW               = 0x000B;
        private const int EM_GETFIRSTVISIBLELINE     = 0x00CE;
        private const int EM_LINESCROLL              = 0x00B6;
        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
        private const int LVS_EX_DOUBLEBUFFER        = 0x00010000;

        // Linea superior visible: es la posicion REAL del scroll, no la del cursor.
        public static int GetFirstVisibleLine(IntPtr h) {
            if (h == IntPtr.Zero) return 0;
            return (int)SendMessage(h, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ScrollToLine(IntPtr h, int line) {
            if (h == IntPtr.Zero || line < 0) return;
            int current = GetFirstVisibleLine(h);
            SendMessage(h, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(line - current));
        }

        public static void SetRedraw(IntPtr h, bool on) {
            if (h == IntPtr.Zero) return;
            SendMessage(h, WM_SETREDRAW, (IntPtr)(on ? 1 : 0), IntPtr.Zero);
        }

        // ListView no expone DoubleBuffered en publico; el estilo extendido si.
        public static void EnableListViewDoubleBuffer(IntPtr h) {
            if (h == IntPtr.Zero) return;
            SendMessage(h, LVM_SETEXTENDEDLISTVIEWSTYLE,
                        (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
        }
    }

    // =================================================================
    //  DnsWire - serializacion y lectura de nombres en formato DNS.
    //  Compartido por mDNS (5353) y NetBIOS (137), que usan el mismo
    //  encabezado de 12 bytes.
    // =================================================================
    internal static class DnsWire {

        public static void WriteName(List<byte> buf, string name) {
            foreach (string label in name.Split('.')) {
                if (label.Length == 0) continue;
                buf.Add((byte)label.Length);
                foreach (char c in label) buf.Add((byte)c);
            }
            buf.Add(0);
        }

        // Lee un nombre con soporte de compresion por punteros (0xC0).
        // 'next' queda apuntando al byte siguiente al nombre en el flujo original.
        public static string ReadName(byte[] data, int offset, out int next) {
            StringBuilder sb = new StringBuilder();
            int pos = offset;
            int jumps = 0;
            next = -1;

            while (pos >= 0 && pos < data.Length) {
                int len = data[pos];
                if (len == 0) { pos++; if (next < 0) next = pos; break; }

                if ((len & 0xC0) == 0xC0) {
                    if (pos + 1 >= data.Length) break;
                    int ptr = ((len & 0x3F) << 8) | data[pos + 1];
                    if (next < 0) next = pos + 2;
                    pos = ptr;
                    if (++jumps > 16) break;   // corta cadenas de punteros circulares
                    continue;
                }

                pos++;
                if (pos + len > data.Length) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(data, pos, len));
                pos += len;
            }

            if (next < 0) next = pos;
            return sb.ToString();
        }

        public static int SkipName(byte[] data, int offset) {
            int next;
            ReadName(data, offset, out next);
            return next;
        }
    }

    // =================================================================
    //  NetProbe - identificacion a nivel de red, independiente de HTTP.
    //  Funciona aunque el dispositivo use MAC aleatoria y UA reducida.
    // =================================================================
    public static class NetProbe {

        // ---------- ICMP: el TTL delata la familia de sistema operativo ----------

        // Tres intentos: un solo ping se pierde con facilidad en Wi-Fi, y un
        // fallo transitorio dejaria al cliente sin huella de SO para siempre.
        public static void PingInfo(string ip, int timeoutMs, out int ttl, out long rtt) {
            ttl = -1; rtt = -1;
            for (int intento = 0; intento < 3; intento++) {
                try {
                    using (Ping p = new Ping()) {
                        PingReply r = p.Send(ip, timeoutMs);
                        if (r != null && r.Status == IPStatus.Success) {
                            rtt = r.RoundtripTime;
                            if (r.Options != null) ttl = r.Options.Ttl;
                            if (ttl > 0) return;
                        }
                    }
                } catch { }
                Thread.Sleep(150);
            }
        }

        // Los SO parten de un TTL inicial fijo y cada salto lo decrementa.
        // En LAN sin routers intermedios el valor llega intacto.
        public static string OsFromTtl(int ttl) {
            if (ttl <= 0) return "";
            int initial;
            if (ttl <= 64)       initial = 64;
            else if (ttl <= 128) initial = 128;
            else                 initial = 255;

            int hops = initial - ttl;
            string family;
            if (initial == 64)        family = "Linux / Android / iOS / macOS";
            else if (initial == 128)  family = "Windows";
            else                      family = "Equipo de red / embebido";

            return family + "  (TTL " + ttl + ", " + hops + " salto" + (hops == 1 ? "" : "s") + ")";
        }

        // ---------- mDNS: PTR inverso multicast. La via para moviles. ----------

        public static string MdnsReverse(string ip, int timeoutMs) {
            try {
                string[] o = ip.Split('.');
                if (o.Length != 4) return "";
                string qname = o[3] + "." + o[2] + "." + o[1] + "." + o[0] + ".in-addr.arpa";

                List<byte> q = new List<byte>();
                q.Add(0); q.Add(0);          // ID: 0 en mDNS
                q.Add(0); q.Add(0);          // flags
                q.Add(0); q.Add(1);          // QDCOUNT = 1
                q.Add(0); q.Add(0);          // ANCOUNT
                q.Add(0); q.Add(0);          // NSCOUNT
                q.Add(0); q.Add(0);          // ARCOUNT
                DnsWire.WriteName(q, qname);
                q.Add(0); q.Add(12);         // QTYPE = PTR
                q.Add(0x80); q.Add(0x01);    // QCLASS = IN con bit QU: pide respuesta unicast

                byte[] packet = q.ToArray();

                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork)) {
                    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));

                    DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline) {
                        IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] resp;
                        try { resp = udp.Receive(ref from); } catch { break; }
                        // La red esta llena de trafico mDNS ajeno: solo interesa el objetivo.
                        if (from.Address.ToString() != ip) continue;
                        string name = ParsePtr(resp);
                        if (name.Length > 0) return name;
                    }
                }
            } catch { }
            return "";
        }

        private static string ParsePtr(byte[] data) {
            try {
                if (data.Length < 12) return "";
                int qd = (data[4] << 8) | data[5];
                int an = (data[6] << 8) | data[7];
                if (an < 1) return "";

                int pos = 12;
                for (int i = 0; i < qd; i++) { pos = DnsWire.SkipName(data, pos); pos += 4; }

                for (int i = 0; i < an; i++) {
                    pos = DnsWire.SkipName(data, pos);
                    if (pos + 10 > data.Length) return "";
                    int type  = (data[pos] << 8) | data[pos + 1];
                    int rdlen = (data[pos + 8] << 8) | data[pos + 9];
                    int rdata = pos + 10;
                    if (type == 12 && rdata < data.Length) {
                        int dummy;
                        string n = DnsWire.ReadName(data, rdata, out dummy);
                        if (n.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                            n = n.Substring(0, n.Length - 6);
                        return n;
                    }
                    pos = rdata + rdlen;
                }
            } catch { }
            return "";
        }

        // ---------- NetBIOS NBSTAT: nombre de maquina en clientes Windows ----------

        public static string NetbiosName(string ip, int timeoutMs) {
            try {
                List<byte> q = new List<byte>();
                q.Add(0x4E); q.Add(0x42);    // ID
                q.Add(0x00); q.Add(0x00);    // flags
                q.Add(0x00); q.Add(0x01);    // QDCOUNT = 1
                q.Add(0x00); q.Add(0x00);
                q.Add(0x00); q.Add(0x00);
                q.Add(0x00); q.Add(0x00);

                // Nombre comodin NBSTAT: '*' + 15 nulos, en codificacion de primer
                // nivel (cada byte -> dos nibbles sumados a 'A') = 32 caracteres.
                q.Add(0x20);
                byte[] raw = new byte[16];
                raw[0] = (byte)'*';
                for (int i = 0; i < 16; i++) {
                    q.Add((byte)('A' + ((raw[i] >> 4) & 0x0F)));
                    q.Add((byte)('A' + (raw[i] & 0x0F)));
                }
                q.Add(0x00);
                q.Add(0x00); q.Add(0x21);    // QTYPE = NBSTAT
                q.Add(0x00); q.Add(0x01);    // QCLASS = IN

                byte[] packet = q.ToArray();
                using (UdpClient udp = new UdpClient(AddressFamily.InterNetwork)) {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse(ip), 137));
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref from);
                    return ParseNbstat(resp);
                }
            } catch { }
            return "";
        }

        private static string ParseNbstat(byte[] data) {
            try {
                if (data.Length < 12) return "";
                int qd = (data[4] << 8) | data[5];
                int an = (data[6] << 8) | data[7];
                if (an < 1) return "";

                int pos = 12;
                for (int i = 0; i < qd; i++) { pos = DnsWire.SkipName(data, pos); pos += 4; }
                pos = DnsWire.SkipName(data, pos);
                if (pos + 10 > data.Length) return "";

                int rdata = pos + 10;
                if (rdata >= data.Length) return "";

                int count = data[rdata];
                int p = rdata + 1;
                for (int i = 0; i < count && p + 17 < data.Length; i++, p += 18) {
                    string name = Encoding.ASCII.GetString(data, p, 15).TrimEnd(' ', '\0');
                    byte suffix = data[p + 15];
                    int flags = (data[p + 16] << 8) | data[p + 17];
                    bool isGroup = (flags & 0x8000) != 0;
                    // Sufijo 0x00 y no-grupo = nombre de la estacion de trabajo.
                    if (suffix == 0x00 && !isGroup && name.Length > 0) return name;
                }
            } catch { }
            return "";
        }

        // ---------- Escaneo TCP de puertos con firma ----------

        private static readonly int[] SigPorts =
            { 22, 53, 80, 139, 443, 445, 548, 554, 631, 1883, 1900, 3389,
              5000, 5555, 5900, 7000, 8009, 8080, 9100, 32400, 62078 };

        private static readonly string[] SigNames =
            { "SSH", "DNS", "HTTP", "NetBIOS", "HTTPS", "SMB", "AFP", "RTSP", "IPP",
              "MQTT", "UPnP", "RDP", "UPnP-alt", "ADB", "VNC", "AirPlay", "Chromecast",
              "HTTP-alt", "JetDirect", "Plex", "lockdownd" };

        // Puertos cuyo banner HTTP suele nombrar el producto (router, NAS, impresora).
        private static readonly int[] BannerPorts = { 80, 8080, 5000, 631, 9100, 32400 };

        // Todas las sondas se lanzan a la vez y comparten una sola ventana de
        // espera. Secuencialmente eran 21 x 300 ms; ahora es una espera unica,
        // lo que ademas permite un timeout mas generoso con menos falsos
        // negativos sobre Wi-Fi.
        public static string ScanPorts(string ip, int timeoutMs, out string deviceGuess, out string banners) {
            deviceGuess = "";
            banners = "";

            int n = SigPorts.Length;
            TcpClient[] cli = new TcpClient[n];
            IAsyncResult[] ar = new IAsyncResult[n];

            for (int i = 0; i < n; i++) {
                try {
                    cli[i] = new TcpClient();
                    ar[i] = cli[i].BeginConnect(ip, SigPorts[i], null, null);
                } catch { ar[i] = null; }
            }

            Thread.Sleep(timeoutMs);

            List<string> open = new List<string>();
            Dictionary<int, bool> found = new Dictionary<int, bool>();
            for (int i = 0; i < n; i++) {
                bool ok = false;
                try {
                    if (ar[i] != null && ar[i].IsCompleted) {
                        cli[i].EndConnect(ar[i]);   // lanza si el puerto rechazo
                        ok = cli[i].Connected;
                    }
                } catch { ok = false; }
                if (ok) { open.Add(SigPorts[i] + "/" + SigNames[i]); found[SigPorts[i]] = true; }
                try { if (cli[i] != null) cli[i].Close(); } catch { }
            }

            if (found.ContainsKey(62078))                                deviceGuess = "iPhone o iPad (lockdownd)";
            else if (found.ContainsKey(445) || found.ContainsKey(3389))   deviceGuess = "Windows";
            else if (found.ContainsKey(548) || found.ContainsKey(7000))   deviceGuess = "Apple (macOS / AirPlay)";
            else if (found.ContainsKey(5555))                             deviceGuess = "Android con ADB expuesto";
            else if (found.ContainsKey(9100) || found.ContainsKey(631))   deviceGuess = "Impresora de red";
            else if (found.ContainsKey(8009))                             deviceGuess = "Chromecast / Google TV";
            else if (found.ContainsKey(32400))                            deviceGuess = "Servidor Plex / NAS";
            else if (found.ContainsKey(554))                              deviceGuess = "Camara IP / NVR";
            else if (found.ContainsKey(53))                               deviceGuess = "Router / servidor DNS";
            else if (found.ContainsKey(1883))                             deviceGuess = "Broker MQTT / IoT";
            else if (found.ContainsKey(22))                               deviceGuess = "Linux / NAS / router";

            // Banner HTTP de los puertos abiertos: la cabecera Server suele
            // nombrar el firmware exacto de impresoras, routers y NAS.
            List<string> bl = new List<string>();
            foreach (int bp in BannerPorts) {
                if (!found.ContainsKey(bp)) continue;
                string b = HttpBanner(ip, bp, 700);
                if (b.Length > 0) bl.Add(bp + ": " + b);
            }
            banners = string.Join(" | ", bl.ToArray());

            return string.Join(", ", open.ToArray());
        }

        private static string HttpBanner(string ip, int port, int timeoutMs) {
            try {
                using (TcpClient c = new TcpClient()) {
                    IAsyncResult a = c.BeginConnect(ip, port, null, null);
                    if (!a.AsyncWaitHandle.WaitOne(timeoutMs, false)) return "";
                    c.EndConnect(a);
                    c.ReceiveTimeout = timeoutMs;
                    c.SendTimeout = timeoutMs;

                    NetworkStream ns = c.GetStream();
                    byte[] req = Encoding.ASCII.GetBytes(
                        "HEAD / HTTP/1.0\r\nHost: " + ip + "\r\nConnection: close\r\n\r\n");
                    ns.Write(req, 0, req.Length);

                    byte[] buf = new byte[1024];
                    int got = ns.Read(buf, 0, buf.Length);
                    if (got <= 0) return "";
                    string txt = Encoding.ASCII.GetString(buf, 0, got);
                    foreach (string linea in txt.Split('\n')) {
                        string l = linea.Trim();
                        if (l.StartsWith("Server:", StringComparison.OrdinalIgnoreCase))
                            return l.Substring(7).Trim();
                    }
                }
            } catch { }
            return "";
        }

        // M-SEARCH unicast: televisores, consolas y routers responden con una
        // cabecera SERVER que nombra su sistema operativo y su producto.
        public static string Ssdp(string ip, int timeoutMs) {
            try {
                string msg = "M-SEARCH * HTTP/1.1\r\n" +
                             "HOST: " + ip + ":1900\r\n" +
                             "MAN: \"ssdp:discover\"\r\n" +
                             "MX: 1\r\nST: ssdp:all\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(msg);
                using (UdpClient u = new UdpClient()) {
                    u.Client.ReceiveTimeout = timeoutMs;
                    u.Send(data, data.Length, new IPEndPoint(IPAddress.Parse(ip), 1900));
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = u.Receive(ref from);
                    string txt = Encoding.ASCII.GetString(resp);
                    foreach (string linea in txt.Split('\n')) {
                        string l = linea.Trim();
                        if (l.StartsWith("SERVER:", StringComparison.OrdinalIgnoreCase))
                            return l.Substring(7).Trim();
                    }
                }
            } catch { }
            return "";
        }
    }

    // =================================================================
    //  MdnsListener - escucha permanente en 224.0.0.251:5353.
    //  Los dispositivos anuncian sus servicios solos (AirPlay, AirDrop,
    //  Chromecast, impresoras), asi que basta con oir para construir una
    //  tabla IP -> nombre .local sin enviar una sola sonda. Es mucho mas
    //  fiable que el PTR inverso, que casi nadie implementa.
    // =================================================================
    public static class MdnsListener {
        private static readonly ConcurrentDictionary<string, string> _names =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private static int _started = 0;
        private static volatile bool _stop = false;
        private static UdpClient _udp;

        public static int Count { get { return _names.Count; } }

        public static string Lookup(string ip) {
            string n;
            return _names.TryGetValue(ip, out n) ? n : "";
        }

        public static bool Activo { get { return _udp != null && _started != 0; } }

        public static void Start() {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            _stop = false;
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        // Debe poder reiniciarse: el usuario puede parar y volver a iniciar el
        // servidor varias veces en la misma sesion.
        public static void Stop() {
            if (Interlocked.Exchange(ref _started, 0) == 0) return;
            _stop = true;
            try { if (_udp != null) { _udp.Close(); _udp = null; } } catch { }
        }

        // Pregunta por la lista de tipos de servicio: obliga a todo el mundo a
        // responder, y las respuestas traen registros A en la seccion adicional.
        public static void ProbeAll() {
            try {
                List<byte> q = new List<byte>();
                q.Add(0); q.Add(0); q.Add(0); q.Add(0);
                q.Add(0); q.Add(1);
                q.Add(0); q.Add(0); q.Add(0); q.Add(0); q.Add(0); q.Add(0);
                DnsWire.WriteName(q, "_services._dns-sd._udp.local");
                q.Add(0); q.Add(12);     // PTR
                q.Add(0); q.Add(1);      // IN
                byte[] packet = q.ToArray();
                using (UdpClient u = new UdpClient()) {
                    u.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                }
            } catch { }
        }

        private static void Loop() {
            try {
                _udp = new UdpClient();
                _udp.ExclusiveAddressUse = false;
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                _udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));
            } catch {
                // El puerto 5353 puede estar tomado por Bonjour o por el propio
                // navegador. Sin escucha pasiva el resto sigue funcionando.
                _udp = null;
                Interlocked.Exchange(ref _started, 0);   // permite reintentar al reiniciar
                return;
            }

            while (!_stop) {
                try {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref from);
                    Harvest(data);
                } catch {
                    if (_stop) break;
                    Thread.Sleep(250);
                }
            }
        }

        // Recorre todas las secciones buscando registros A: cada uno asocia
        // un nombre .local con una IPv4.
        private static void Harvest(byte[] d) {
            try {
                if (d.Length < 12) return;
                int qd = (d[4] << 8) | d[5];
                int an = (d[6] << 8) | d[7];
                int ns = (d[8] << 8) | d[9];
                int adc = (d[10] << 8) | d[11];

                int pos = 12;
                for (int i = 0; i < qd; i++) { pos = DnsWire.SkipName(d, pos); pos += 4; }

                int total = an + ns + adc;
                for (int i = 0; i < total; i++) {
                    if (pos + 10 > d.Length) return;
                    int nameStart = pos;
                    pos = DnsWire.SkipName(d, pos);
                    if (pos + 10 > d.Length) return;

                    int type  = (d[pos] << 8) | d[pos + 1];
                    int rdlen = (d[pos + 8] << 8) | d[pos + 9];
                    int rdata = pos + 10;

                    if (type == 1 && rdlen == 4 && rdata + 4 <= d.Length) {
                        string ip = d[rdata] + "." + d[rdata + 1] + "." + d[rdata + 2] + "." + d[rdata + 3];
                        int dummy;
                        string owner = DnsWire.ReadName(d, nameStart, out dummy);
                        if (owner.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                            owner = owner.Substring(0, owner.Length - 6);
                        if (owner.Length > 0) _names[ip] = owner;
                    }
                    pos = rdata + rdlen;
                }
            } catch { }
        }
    }

    // =================================================================
    //  UaParser - unica responsabilidad: interpretar el User-Agent.
    // =================================================================
    public static class UaParser {

        private static bool Has(string ua, string token) {
            return ua.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        // Version mayor que sigue a un token, sin regex.
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

        private static string Join(string name, string ver) {
            return ver.Length > 0 ? name + " " + ver : name;
        }

        public static string Browser(string ua) {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";
            // El orden importa: Edge, Opera y Samsung incluyen "Chrome/" en su UA.
            if (Has(ua, "Edg/"))            return Join("Edge",             VerAfter(ua, "Edg/"));
            if (Has(ua, "EdgA/"))           return Join("Edge Android",     VerAfter(ua, "EdgA/"));
            if (Has(ua, "EdgiOS/"))         return Join("Edge iOS",         VerAfter(ua, "EdgiOS/"));
            if (Has(ua, "OPR/"))            return Join("Opera",            VerAfter(ua, "OPR/"));
            if (Has(ua, "SamsungBrowser/")) return Join("Samsung Internet", VerAfter(ua, "SamsungBrowser/"));
            if (Has(ua, "YaBrowser/"))      return Join("Yandex",           VerAfter(ua, "YaBrowser/"));
            if (Has(ua, "Vivaldi/"))        return Join("Vivaldi",          VerAfter(ua, "Vivaldi/"));
            if (Has(ua, "FxiOS/"))          return Join("Firefox iOS",      VerAfter(ua, "FxiOS/"));
            if (Has(ua, "Firefox/"))        return Join("Firefox",          VerAfter(ua, "Firefox/"));
            if (Has(ua, "CriOS/"))          return Join("Chrome iOS",       VerAfter(ua, "CriOS/"));
            if (Has(ua, "Chrome/"))         return Join("Chrome",           VerAfter(ua, "Chrome/"));
            if (Has(ua, "Version/") && Has(ua, "Safari/"))
                                            return Join("Safari",           VerAfter(ua, "Version/"));
            if (Has(ua, "curl/"))           return Join("curl",             VerAfter(ua, "curl/"));
            if (Has(ua, "Wget"))            return "wget";
            if (Has(ua, "PostmanRuntime"))  return "Postman";
            if (Has(ua, "python-requests")) return "python-requests";
            if (Has(ua, "PowerShell"))      return "PowerShell";
            if (Has(ua, "Dart/"))           return "Dart / Flutter";
            if (Has(ua, "okhttp"))          return "OkHttp (app nativa)";
            return "Otro";
        }

        public static string Device(string ua) {
            if (string.IsNullOrEmpty(ua)) return "Desconocido";

            if (Has(ua, "Android TV")) return "Android TV";

            if (Has(ua, "Android")) {
                // Chrome 110+ congela la UA de Android al literal "Android 10; K".
                // Ni la version ni el modelo son reales: etiquetarlos seria mentir.
                if (Has(ua, "Android 10; K")) return "Android (version y modelo ocultos)";

                Match mv = Regex.Match(ua, @"Android\s+([\d.]+)");
                string ver = mv.Success ? mv.Groups[1].Value : "";

                string model = "";
                Match mm = Regex.Match(ua, @"Android[^;)]*;\s*(?:[a-z]{2}(?:-[a-zA-Z]{2})?;\s*)?([^;)]+?)\s*(?:Build/|\))");
                if (mm.Success) model = mm.Groups[1].Value.Trim();

                if (model == "K" || model == "Android" || model.Length == 0)
                    return Join("Android", ver) + " (modelo oculto)";
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
                // Safari congela macOS en 10.15.7 desde Big Sur.
                return m.Success ? "macOS " + m.Groups[1].Value + "." + m.Groups[2].Value : "macOS";
            }

            if (Has(ua, "SMART-TV") || Has(ua, "Tizen") || Has(ua, "Web0S")) return "Smart TV";
            if (Has(ua, "PlayStation")) return "PlayStation";
            if (Has(ua, "Nintendo"))    return "Nintendo";

            if (Has(ua, "Linux") || Has(ua, "X11")) return "Linux";
            return "Desconocido";
        }
    }

    // =================================================================
    //  ClientInfo - datos por cliente. Campos publicos (no propiedades)
    //  para poder pasarlos por ref a Interlocked.
    // =================================================================
    public class ClientInfo {
        // Identidad de red
        public string Ip = "";
        public string HostName = "";        // DNS inverso
        public string MdnsName = "";        // nombre .local por multicast
        public string NbName = "";          // nombre NetBIOS
        public string Mac = "";
        public string Vendor = "";
        public bool   MacRandomized = false;

        // Huella de red
        public int    Ttl = -1;
        public long   RttMs = -1;
        public string OsGuess = "";
        public string OpenPorts = "";
        public string PortGuess = "";
        public string SsdpServer = "";
        public string Banners = "";

        // Veredicto fusionado a partir de todas las senales disponibles.
        public string Verdict = "";
        public string VerdictDetail = "";
        public int    VerdictScore = 0;

        // Control de re-sondeo periodico.
        public long NextProbeTicks = 0;
        public int  ProbeRound = 0;

        // HTTP
        public string UserAgent = "";
        public string AcceptLanguage = "";
        public string AcceptEncoding = "";
        public string Referer = "";
        public string Protocol = "";
        public bool   KeepAlive = false;
        public int    LastPort = 0;
        public string RawHeaders = "";
        public string HeaderSignature = "";
        public string ChUa = "";
        public string ChPlatform = "";
        public string ChMobile = "";
        public string ChModel = "";
        public string DeviceLabel = "Desconocido";
        public string BrowserLabel = "Desconocido";

        // Sonda JS
        public string Gpu = "";
        public string ScreenInfo = "";
        public string ProbeSummary = "";
        public long   ProbeTicks = 0;

        // Contadores
        public long RequestCount = 0;
        public long BytesSent = 0;
        public long FirstSeenTicks = 0;
        public long LastSeenTicks = 0;
        public int  ResolveState = 0;   // 0 pendiente, 1 en curso, 2 resuelto

        // Mejor nombre disponible, en orden de fiabilidad.
        public string BestName {
            get {
                if (HostName.Length > 0) return HostName;
                if (MdnsName.Length > 0) return MdnsName;
                if (NbName.Length > 0)   return NbName;
                return "";
            }
        }

        public bool Incompleto {
            get { return Mac.Length == 0 || BestName.Length == 0 || Ttl <= 0; }
        }
    }

    // =================================================================
    //  ClientRegistry - almacen concurrente y orquestador del
    //  enriquecimiento. Estado estatico del AppDomain: visible desde
    //  todos los runspaces worker sin inyectarlo en cada peticion.
    // =================================================================
    public static class ClientRegistry {

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macAddrLen);

        private static readonly ConcurrentDictionary<string, ClientInfo> _map =
            new ConcurrentDictionary<string, ClientInfo>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> _oui =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Interruptores de nivel. Son estaticos y no parametros del handler
        // para que las casillas surtan efecto sin reiniciar el servidor.
        public static bool EnableDeepName = true;    // mDNS + NetBIOS
        public static bool EnablePortScan = true;    // escaneo TCP activo
        public static bool EnableJsProbe  = true;    // inyeccion de la sonda JS
        public static string ProbeScript  = "";      // cuerpo del <script> a inyectar

        // ---------- Tabla de fabricantes ----------

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

        // ---------- Ruta caliente: una escritura atomica por peticion ----------

        public static void Track(string ip, string ua, string lang,
                                 string chUa, string chPlatform, string chMobile,
                                 string chModel, long bytes) {
            if (string.IsNullOrEmpty(ip)) return;

            ClientInfo ci = GetOrCreate(ip);

            Interlocked.Increment(ref ci.RequestCount);
            if (bytes > 0) Interlocked.Add(ref ci.BytesSent, bytes);
            Interlocked.Exchange(ref ci.LastSeenTicks, DateTime.UtcNow.Ticks);

            // El parseo de UA solo corre cuando la cadena cambia, no en cada peticion.
            if (!string.IsNullOrEmpty(ua) && !string.Equals(ci.UserAgent, ua, StringComparison.Ordinal)) {
                ci.UserAgent    = ua;
                ci.DeviceLabel  = UaParser.Device(ua);
                ci.BrowserLabel = UaParser.Browser(ua);
            }
            if (!string.IsNullOrEmpty(lang))       ci.AcceptLanguage = lang;
            if (!string.IsNullOrEmpty(chUa))       ci.ChUa           = chUa;
            if (!string.IsNullOrEmpty(chPlatform)) ci.ChPlatform     = chPlatform;
            if (!string.IsNullOrEmpty(chMobile))   ci.ChMobile       = chMobile;
            if (!string.IsNullOrEmpty(chModel))    ci.ChModel        = chModel;

            QueueResolve(ci);
        }

        // Nivel 0: metadatos crudos de la peticion.
        public static void SetHttpDetails(string ip, string rawHeaders, string headerSig,
                                          string protocol, bool keepAlive, int srcPort,
                                          string referer, string acceptEncoding) {
            if (string.IsNullOrEmpty(ip)) return;
            ClientInfo ci;
            if (!_map.TryGetValue(ip, out ci)) return;
            ci.RawHeaders      = rawHeaders;
            ci.HeaderSignature = headerSig;
            ci.Protocol        = protocol;
            ci.KeepAlive       = keepAlive;
            ci.LastPort        = srcPort;
            if (!string.IsNullOrEmpty(referer))        ci.Referer        = referer;
            if (!string.IsNullOrEmpty(acceptEncoding)) ci.AcceptEncoding = acceptEncoding;
        }

        // Nivel 3: resultado de la sonda JS.
        public static void SetProbe(string ip, string gpu, string screen, string summary) {
            if (string.IsNullOrEmpty(ip)) return;
            ClientInfo ci = GetOrCreate(ip);
            ci.Gpu          = gpu   == null ? "" : gpu;
            ci.ScreenInfo   = screen== null ? "" : screen;
            ci.ProbeSummary = summary == null ? "" : summary;
            Interlocked.Exchange(ref ci.ProbeTicks, DateTime.UtcNow.Ticks);
            // La GPU es una senal fuerte y llega despues del sondeo de red.
            BuildVerdict(ci);
        }

        private static ClientInfo GetOrCreate(string ip) {
            ClientInfo ci;
            if (!_map.TryGetValue(ip, out ci)) {
                ClientInfo fresh = new ClientInfo();
                fresh.Ip = ip;
                fresh.FirstSeenTicks = DateTime.UtcNow.Ticks;
                ci = _map.GetOrAdd(ip, fresh);
            }
            return ci;
        }

        public static ClientInfo[] Snapshot() {
            return new List<ClientInfo>(_map.Values).ToArray();
        }

        public static void Clear() { _map.Clear(); }

        public static void ResetResolution() {
            foreach (ClientInfo ci in _map.Values) {
                Interlocked.Exchange(ref ci.ResolveState, 0);
                QueueResolve(ci);
            }
        }

        // ---------- Enriquecimiento, siempre fuera de la ruta de peticion ----------

        private static void QueueResolve(ClientInfo ci) {
            // Un unico intento por cliente: el CAS 0 -> 1 gana la carrera.
            if (Interlocked.CompareExchange(ref ci.ResolveState, 1, 0) != 0) return;

            // Hilo dedicado en vez de ThreadPool: el sondeo completo puede
            // tardar segundos y no debe competir con el pool de peticiones.
            Thread t = new Thread(ResolveWorker);
            t.IsBackground = true;
            t.Start(ci);
        }

        private static void ResolveWorker(object state) {
            ClientInfo ci = (ClientInfo)state;
            try {
                IPAddress addr;
                if (!IPAddress.TryParse(ci.Ip, out addr)) return;
                bool loopback = IPAddress.IsLoopback(addr);
                bool ipv4 = addr.AddressFamily == AddressFamily.InterNetwork;

                // 1. DNS inverso, con tope de tiempo: sin el, un resolutor lento
                //    dejaria el hilo colgado varios segundos.
                try {
                    IAsyncResult ar = Dns.BeginGetHostEntry(ci.Ip, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(2000, false)) {
                        IPHostEntry he = Dns.EndGetHostEntry(ar);
                        if (he != null && !string.IsNullOrEmpty(he.HostName)) ci.HostName = he.HostName;
                    }
                } catch { }

                // 2. ARP: solo IPv4 dentro del mismo segmento L2.
                if (ipv4 && !loopback) ResolveMac(ci, addr);

                // 3. TTL por ICMP: huella de SO que sobrevive a la MAC aleatoria.
                if (!loopback) {
                    int ttl; long rtt;
                    NetProbe.PingInfo(ci.Ip, 1200, out ttl, out rtt);
                    ci.Ttl = ttl;
                    ci.RttMs = rtt;
                    ci.OsGuess = NetProbe.OsFromTtl(ttl);
                }

                // 4. Nombres alternativos, solo si el DNS inverso no dio nada.
                //    Primero la tabla pasiva (gratis), luego sondas activas.
                if (EnableDeepName && ipv4 && !loopback && ci.HostName.Length == 0) {
                    string pasivo = MdnsListener.Lookup(ci.Ip);
                    if (pasivo.Length > 0) {
                        ci.MdnsName = pasivo;
                    } else {
                        MdnsListener.ProbeAll();
                        ci.MdnsName = NetProbe.MdnsReverse(ci.Ip, 1200);
                        if (ci.MdnsName.Length == 0) {
                            Thread.Sleep(400);                       // margen para la respuesta multicast
                            ci.MdnsName = MdnsListener.Lookup(ci.Ip);
                        }
                        if (ci.MdnsName.Length == 0) ci.NbName = NetProbe.NetbiosName(ci.Ip, 900);
                    }
                }

                // 5. SSDP: televisores, consolas y routers se identifican solos.
                if (ipv4 && !loopback && ci.SsdpServer.Length == 0) {
                    ci.SsdpServer = NetProbe.Ssdp(ci.Ip, 1200);
                }

                // 6. Escaneo TCP en paralelo mas banner de los puertos HTTP.
                if (EnablePortScan && ipv4 && !loopback) {
                    string guess, banners;
                    ci.OpenPorts = NetProbe.ScanPorts(ci.Ip, 800, out guess, out banners);
                    ci.PortGuess = guess;
                    ci.Banners   = banners;
                }

                // 7. Fusion de todas las senales en un unico veredicto.
                BuildVerdict(ci);
            } catch { }
            finally {
                Interlocked.Exchange(ref ci.ResolveState, 2);
            }
        }

        // Cada fuente vota por una familia de dispositivo. El veredicto es la
        // mas votada; si hay empate o discrepancia se reporta, porque una
        // contradiccion (TTL de Windows con UA de Android) es informacion util.
        public static void BuildVerdict(ClientInfo ci) {
            Dictionary<string, int> votos = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> razones = new List<string>();

            // --- User-Agent ---
            string dev = ci.DeviceLabel;
            if (dev.StartsWith("iPhone") || dev.StartsWith("iPad") || dev.StartsWith("iPod"))
                { Votar(votos, "Apple movil", 2); razones.Add("UA dice iOS"); }
            else if (dev.StartsWith("Android"))
                { Votar(votos, "Android", 2); razones.Add("UA dice Android"); }
            else if (dev.StartsWith("Windows"))
                { Votar(votos, "Windows", 2); razones.Add("UA dice Windows"); }
            else if (dev.StartsWith("macOS"))
                { Votar(votos, "Mac", 2); razones.Add("UA dice macOS"); }
            else if (dev.StartsWith("Linux") || dev.StartsWith("ChromeOS"))
                { Votar(votos, "Linux", 2); razones.Add("UA dice Linux/ChromeOS"); }
            else if (dev.StartsWith("Smart TV") || dev.StartsWith("Android TV"))
                { Votar(votos, "TV / streaming", 2); razones.Add("UA dice Smart TV"); }

            // --- Puertos abiertos: la senal mas dura ---
            if (ci.OpenPorts.Length > 0) {
                if (ci.OpenPorts.Contains("62078")) { Votar(votos, "Apple movil", 4); razones.Add("puerto 62078 (lockdownd)"); }
                if (ci.OpenPorts.Contains("445") || ci.OpenPorts.Contains("3389")) { Votar(votos, "Windows", 3); razones.Add("SMB/RDP abierto"); }
                if (ci.OpenPorts.Contains("548") || ci.OpenPorts.Contains("7000")) { Votar(votos, "Mac", 3); razones.Add("AFP/AirPlay abierto"); }
                if (ci.OpenPorts.Contains("5555")) { Votar(votos, "Android", 3); razones.Add("ADB abierto"); }
                if (ci.OpenPorts.Contains("9100") || ci.OpenPorts.Contains("631")) { Votar(votos, "Impresora", 4); razones.Add("puerto de impresion"); }
                if (ci.OpenPorts.Contains("8009")) { Votar(votos, "TV / streaming", 3); razones.Add("Chromecast"); }
                if (ci.OpenPorts.Contains("53")) { Votar(votos, "Router / red", 3); razones.Add("sirve DNS"); }
                if (ci.OpenPorts.Contains("1883")) { Votar(votos, "IoT", 3); razones.Add("broker MQTT"); }
            }

            // --- TTL: separa familias, no sistemas concretos ---
            if (ci.Ttl > 0) {
                if (ci.Ttl <= 64) {
                    Votar(votos, "Android", 1); Votar(votos, "Apple movil", 1);
                    Votar(votos, "Linux", 1);   Votar(votos, "Mac", 1);
                    razones.Add("TTL " + ci.Ttl + " (familia Unix)");
                } else if (ci.Ttl <= 128) {
                    Votar(votos, "Windows", 2);
                    razones.Add("TTL " + ci.Ttl + " (Windows)");
                } else {
                    Votar(votos, "Router / red", 2); Votar(votos, "IoT", 1);
                    razones.Add("TTL " + ci.Ttl + " (embebido)");
                }
            }

            // --- GPU reportada por la sonda JS: identifica el SoC ---
            if (ci.Gpu.Length > 0) {
                string g = ci.Gpu.ToUpperInvariant();
                if (g.Contains("ADRENO") || g.Contains("MALI") || g.Contains("POWERVR") || g.Contains("XCLIPSE"))
                    { Votar(votos, "Android", 3); razones.Add("GPU movil " + Primera(ci.Gpu)); }
                else if (g.Contains("APPLE"))
                    { Votar(votos, "Apple movil", 2); Votar(votos, "Mac", 2); razones.Add("GPU Apple"); }
                else if (g.Contains("NVIDIA") || g.Contains("RADEON") || g.Contains("GEFORCE") || g.Contains("INTEL"))
                    { Votar(votos, "Windows", 1); Votar(votos, "Linux", 1); razones.Add("GPU de escritorio"); }
            }

            // --- Fabricante por OUI (inutil si la MAC es aleatoria) ---
            if (!ci.MacRandomized && ci.Vendor.Length > 0 && ci.Vendor != "Desconocido") {
                string v = ci.Vendor.ToUpperInvariant();
                if (v.Contains("APPLE"))          { Votar(votos, "Apple movil", 2); Votar(votos, "Mac", 2); razones.Add("OUI Apple"); }
                else if (v.Contains("RASPBERRY")) { Votar(votos, "Linux", 3); razones.Add("OUI Raspberry Pi"); }
                else if (v.Contains("ESPRESSIF")) { Votar(votos, "IoT", 4); razones.Add("OUI Espressif"); }
                else if (v.Contains("VMWARE") || v.Contains("VIRTUALBOX") || v.Contains("QEMU") || v.Contains("HYPER-V"))
                                                  { Votar(votos, "Maquina virtual", 4); razones.Add("OUI de hipervisor"); }
            }

            // --- SSDP: el producto se nombra a si mismo ---
            if (ci.SsdpServer.Length > 0) {
                string sv = ci.SsdpServer.ToUpperInvariant();
                if (sv.Contains("WINDOWS"))                          { Votar(votos, "Windows", 3); }
                else if (sv.Contains("ANDROID") || sv.Contains("TIZEN") || sv.Contains("WEBOS"))
                                                                     { Votar(votos, "TV / streaming", 3); }
                else if (sv.Contains("LINUX") || sv.Contains("UNIX")) { Votar(votos, "Router / red", 2); }
                razones.Add("SSDP: " + Primera(ci.SsdpServer));
            }

            if (ci.Banners.Length > 0) razones.Add("banner HTTP: " + Primera(ci.Banners));

            // --- Escrutinio ---
            string mejor = ""; int mejorV = 0; string segundo = ""; int segundoV = 0;
            foreach (KeyValuePair<string, int> kv in votos) {
                if (kv.Value > mejorV) { segundo = mejor; segundoV = mejorV; mejor = kv.Key; mejorV = kv.Value; }
                else if (kv.Value > segundoV) { segundo = kv.Key; segundoV = kv.Value; }
            }

            if (mejorV == 0) {
                ci.Verdict = "Sin senales suficientes";
                ci.VerdictScore = 0;
                ci.VerdictDetail = "";
                return;
            }

            string conf = mejorV >= 6 ? "alta" : (mejorV >= 3 ? "media" : "baja");
            ci.Verdict = mejor + " (confianza " + conf + ")";
            ci.VerdictScore = mejorV;

            StringBuilder sb = new StringBuilder();
            sb.Append("Senales: ").Append(string.Join("; ", razones.ToArray()));
            if (segundoV > 0 && segundoV >= mejorV - 1 && segundo != mejor)
                sb.Append("  ||  DISCREPANCIA: tambien apunta a ").Append(segundo)
                  .Append(" (").Append(segundoV).Append(" vs ").Append(mejorV).Append(")");
            ci.VerdictDetail = sb.ToString();
        }

        private static void Votar(Dictionary<string, int> d, string clave, int peso) {
            int v;
            d[clave] = d.TryGetValue(clave, out v) ? v + peso : peso;
        }

        private static string Primera(string s) {
            if (s == null) return "";
            s = s.Trim();
            return s.Length > 48 ? s.Substring(0, 48) + "..." : s;
        }

        // ---------- Re-sondeo periodico ----------
        // El CAS de QueueResolve garantiza un intento por cliente. Sin esto, un
        // telefono dormido durante el primer ARP se quedaria sin MAC para
        // siempre. Se reintenta solo lo incompleto, con retroceso exponencial.
        private static int _maintStarted = 0;

        public static void StartMaintenance() {
            if (Interlocked.CompareExchange(ref _maintStarted, 1, 0) != 0) return;
            Thread t = new Thread(MaintLoop);
            t.IsBackground = true;
            t.Start();
        }

        private static void MaintLoop() {
            while (true) {
                Thread.Sleep(5000);
                try {
                    long now = DateTime.UtcNow.Ticks;
                    foreach (ClientInfo ci in _map.Values) {

                        // La tabla pasiva de mDNS se consulta gratis en cada vuelta.
                        if (ci.MdnsName.Length == 0 && ci.HostName.Length == 0) {
                            string pasivo = MdnsListener.Lookup(ci.Ip);
                            if (pasivo.Length > 0) { ci.MdnsName = pasivo; BuildVerdict(ci); }
                        }

                        if (ci.ResolveState != 2) continue;
                        if (!ci.Incompleto) continue;
                        if (now < ci.NextProbeTicks) continue;

                        double idle = TimeSpan.FromTicks(now - ci.LastSeenTicks).TotalSeconds;
                        if (idle > 300) continue;   // cliente ido: no insistir

                        ci.ProbeRound++;
                        int espera = 15 * (1 << (ci.ProbeRound < 5 ? ci.ProbeRound : 5));
                        if (espera > 300) espera = 300;
                        ci.NextProbeTicks = now + TimeSpan.FromSeconds(espera).Ticks;

                        Interlocked.Exchange(ref ci.ResolveState, 0);
                        QueueResolve(ci);
                    }
                } catch { }
            }
        }

        private static void ResolveMac(ClientInfo ci, IPAddress addr) {
            try {
                byte[] raw = addr.GetAddressBytes();
                uint dest = (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
                byte[] mac = new byte[6];
                uint len = 6;
                if (SendARP(dest, 0, mac, ref len) != 0 || len < 6) return;

                ci.Mac = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                                       mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);

                // Bit 1 del primer octeto = direccion administrada localmente, es
                // decir MAC aleatoria de privacidad (iOS y Android modernos).
                ci.MacRandomized = (mac[0] & 0x02) != 0;

                string oui = string.Format("{0:X2}{1:X2}{2:X2}", mac[0], mac[1], mac[2]);
                string vendor = null;
                lock (_oui) { _oui.TryGetValue(oui, out vendor); }

                if (!string.IsNullOrEmpty(vendor))  ci.Vendor = vendor;
                else if (ci.MacRandomized)          ci.Vendor = "MAC aleatoria";
                else                                ci.Vendor = "Desconocido";
            } catch { }
        }
    }
"@
    } catch {
        # Con la consola oculta un Write-Host no lo veria nadie: se recupera la
        # ventana y ademas se avisa por dialogo.
        [ConsolaWin]::Mostrar()
        $msg = "No se pudieron compilar los tipos nativos:`r`n`r`n$($_.Exception.Message)`r`n`r`n" +
               "Si ya ejecutaste una version anterior en esta misma consola, abre una ventana de PowerShell nueva: los tipos ya cargados no se pueden reemplazar."
        Write-Host $msg -ForegroundColor Red
        [System.Windows.Forms.MessageBox]::Show($msg, "Error de inicializacion",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
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
$AT  = [System.Windows.Forms.AnchorStyles]::Top
$AB  = [System.Windows.Forms.AnchorStyles]::Bottom
$AL  = [System.Windows.Forms.AnchorStyles]::Left
$AR  = [System.Windows.Forms.AnchorStyles]::Right

$form = New-Object System.Windows.Forms.Form
$form.Text = "Servidor Web Pro - Hardened & Multi-Threaded"
$form.Size = New-Object System.Drawing.Size(1080, 800)
$form.MinimumSize = New-Object System.Drawing.Size(940, 660)
$form.StartPosition = "CenterScreen"
# Sizable habilita el arrastre de bordes; MaximizeBox activa el boton central
# de maximizar/restaurar. Todo el contenido se reajusta por anclajes.
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::Sizable
$form.MaximizeBox = $true
$form.MinimizeBox = $true

$labelPermiso = New-Object System.Windows.Forms.Label
$labelPermiso.Location = New-Object System.Drawing.Point(20, 15)
$labelPermiso.Size = New-Object System.Drawing.Size(400, 20)
$labelPermiso.Anchor = $AT -bor $AL
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
$btnEscalar.Location = New-Object System.Drawing.Point(894, 10)
$btnEscalar.Size = New-Object System.Drawing.Size(150, 25)
$btnEscalar.Anchor = $AT -bor $AR
$btnEscalar.Text = "Escalar Privilegios"
$btnEscalar.Enabled = -not $esAdmin
$btnEscalar.Add_Click({
    if ($PSCommandPath) {
        try {
            Start-Process powershell.exe -ArgumentList @(
                "-STA", "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-WindowStyle", "Hidden", "-File", "`"$PSCommandPath`""
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
$line.Size = New-Object System.Drawing.Size(1024, 2)
$line.Anchor = $AT -bor $AL -bor $AR
$line.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$form.Controls.Add($line)

$gbModo = New-Object System.Windows.Forms.GroupBox
$gbModo.Location = New-Object System.Drawing.Point(20, 50)
$gbModo.Size = New-Object System.Drawing.Size(1024, 50)
$gbModo.Anchor = $AT -bor $AL -bor $AR
$gbModo.Text = "Alcance del Servidor"

$rbLocal = New-Object System.Windows.Forms.RadioButton
$rbLocal.Location = New-Object System.Drawing.Point(15, 20)
$rbLocal.Size = New-Object System.Drawing.Size(220, 22)
$rbLocal.Text = "Solo este equipo (localhost)"
$rbLocal.Checked = $true
$gbModo.Controls.Add($rbLocal)

$rbLAN = New-Object System.Windows.Forms.RadioButton
$rbLAN.Location = New-Object System.Drawing.Point(245, 20)
$rbLAN.Size = New-Object System.Drawing.Size(330, 22)
$rbLAN.Text = "Red local (accesible desde otros dispositivos)"
$gbModo.Controls.Add($rbLAN)

$chkCors = New-Object System.Windows.Forms.CheckBox
$chkCors.Location = New-Object System.Drawing.Point(590, 20)
$chkCors.Size = New-Object System.Drawing.Size(280, 22)
$chkCors.Text = "CORS abierto (Allow-Origin: *)"
$chkCors.Checked = $true
$gbModo.Controls.Add($chkCors)

$form.Controls.Add($gbModo)

$labelRuta = New-Object System.Windows.Forms.Label
$labelRuta.Location = New-Object System.Drawing.Point(20, 107)
$labelRuta.Size = New-Object System.Drawing.Size(400, 18)
$labelRuta.Anchor = $AT -bor $AL
$labelRuta.Text = "Carpeta raiz de la aplicacion web:"
$form.Controls.Add($labelRuta)

$txtRuta = New-Object System.Windows.Forms.TextBox
$txtRuta.Location = New-Object System.Drawing.Point(20, 127)
$txtRuta.Size = New-Object System.Drawing.Size(774, 23)
$txtRuta.Anchor = $AT -bor $AL -bor $AR
if ($PSScriptRoot) { $txtRuta.Text = $PSScriptRoot } else { $txtRuta.Text = "" }
$form.Controls.Add($txtRuta)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(804, 125)
$btnBrowse.Size = New-Object System.Drawing.Size(90, 27)
$btnBrowse.Anchor = $AT -bor $AR
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
$labelPuerto.Location = New-Object System.Drawing.Point(909, 107)
$labelPuerto.Size = New-Object System.Drawing.Size(80, 18)
$labelPuerto.Anchor = $AT -bor $AR
$labelPuerto.Text = "Puerto TCP:"
$form.Controls.Add($labelPuerto)

$txtPuerto = New-Object System.Windows.Forms.TextBox
$txtPuerto.Location = New-Object System.Drawing.Point(909, 127)
$txtPuerto.Size = New-Object System.Drawing.Size(135, 23)
$txtPuerto.Anchor = $AT -bor $AR
$txtPuerto.Text = "8080"
$form.Controls.Add($txtPuerto)

$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 163)
$btnStart.Size = New-Object System.Drawing.Size(1024, 38)
$btnStart.Anchor = $AT -bor $AL -bor $AR
$btnStart.Text = "Iniciar Servidor"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnStart)

$gbEstado = New-Object System.Windows.Forms.GroupBox
$gbEstado.Location = New-Object System.Drawing.Point(20, 210)
$gbEstado.Size = New-Object System.Drawing.Size(1024, 100)
$gbEstado.Anchor = $AT -bor $AL -bor $AR
$gbEstado.Text = "Estado y Direcciones de Acceso"

$txtEstadoInfo = New-Object System.Windows.Forms.TextBox
$txtEstadoInfo.Location = New-Object System.Drawing.Point(15, 22)
$txtEstadoInfo.Size = New-Object System.Drawing.Size(834, 68)
$txtEstadoInfo.Anchor = $AT -bor $AL -bor $AR
$txtEstadoInfo.Multiline = $true
$txtEstadoInfo.ReadOnly = $true
$txtEstadoInfo.ScrollBars = "Vertical"
$txtEstadoInfo.Text = "Estado: Detenido"
$txtEstadoInfo.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$gbEstado.Controls.Add($txtEstadoInfo)

$btnCopyLAN = New-Object System.Windows.Forms.Button
$btnCopyLAN.Location = New-Object System.Drawing.Point(859, 30)
$btnCopyLAN.Size = New-Object System.Drawing.Size(150, 45)
$btnCopyLAN.Anchor = $AT -bor $AR
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
$tabs.Location = New-Object System.Drawing.Point(20, 318)
$tabs.Size = New-Object System.Drawing.Size(1024, 415)
$tabs.Anchor = $AT -bor $AB -bor $AL -bor $AR

$tabLog = New-Object System.Windows.Forms.TabPage
$tabLog.Text = "Registro de Telemetria"
$tabLog.UseVisualStyleBackColor = $true
$tabLog.Padding = New-Object System.Windows.Forms.Padding(6)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Dock = [System.Windows.Forms.DockStyle]::Fill
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
$tabClientes.Padding = New-Object System.Windows.Forms.Padding(6)

# SplitContainer: el divisor es arrastrable, asi el usuario decide cuanto
# espacio da a la tabla y cuanto al detalle.
$split = New-Object System.Windows.Forms.SplitContainer
$split.Dock = [System.Windows.Forms.DockStyle]::Fill
$split.Orientation = [System.Windows.Forms.Orientation]::Horizontal
$split.Panel1MinSize = 90
$split.Panel2MinSize = 120

$lvClientes = New-Object System.Windows.Forms.ListView
$lvClientes.Dock = [System.Windows.Forms.DockStyle]::Fill
$lvClientes.View = [System.Windows.Forms.View]::Details
$lvClientes.FullRowSelect = $true
$lvClientes.GridLines = $true
$lvClientes.MultiSelect = $false
$lvClientes.HideSelection = $false
$lvClientes.Font = New-Object System.Drawing.Font("Consolas", 8.5)
[void]$lvClientes.Columns.Add("IP", 100)
[void]$lvClientes.Columns.Add("Nombre de red", 130)
[void]$lvClientes.Columns.Add("MAC", 120)
[void]$lvClientes.Columns.Add("Fabricante", 105)
[void]$lvClientes.Columns.Add("Dispositivo (UA)", 125)
[void]$lvClientes.Columns.Add("Navegador", 100)
[void]$lvClientes.Columns.Add("Veredicto", 175)
[void]$lvClientes.Columns.Add("GPU / SoC", 130)
[void]$lvClientes.Columns.Add("Pet.", 45)
[void]$lvClientes.Columns.Add("Ultima", 55)
$split.Panel1.Controls.Add($lvClientes)

$pnlBotones = New-Object System.Windows.Forms.Panel
$pnlBotones.Dock = [System.Windows.Forms.DockStyle]::Top
$pnlBotones.Height = 32

$btnReId = New-Object System.Windows.Forms.Button
$btnReId.Location = New-Object System.Drawing.Point(0, 2)
$btnReId.Size = New-Object System.Drawing.Size(120, 26)
$btnReId.Text = "Re-identificar"
$btnReId.Add_Click({
    [ClientRegistry]::ResetResolution()
    $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nRe-sondeando rDNS, ARP, TTL, mDNS y NetBIOS de todos los clientes.`r`n")
})
$pnlBotones.Controls.Add($btnReId)

$btnLimpiarClientes = New-Object System.Windows.Forms.Button
$btnLimpiarClientes.Location = New-Object System.Drawing.Point(126, 2)
$btnLimpiarClientes.Size = New-Object System.Drawing.Size(120, 26)
$btnLimpiarClientes.Text = "Limpiar lista"
$btnLimpiarClientes.Add_Click({
    [ClientRegistry]::Clear()
    $lvClientes.Items.Clear()
    $script:clientRows.Clear()
    $script:lastDetailIp = ""
    $script:lastDetailText = ""
    $txtClienteDetalle.Text = ""
})
$pnlBotones.Controls.Add($btnLimpiarClientes)

$btnExportClientes = New-Object System.Windows.Forms.Button
$btnExportClientes.Location = New-Object System.Drawing.Point(252, 2)
$btnExportClientes.Size = New-Object System.Drawing.Size(120, 26)
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
                NombreRed    = $c.BestName
                rDNS         = $c.HostName
                mDNS         = $c.MdnsName
                NetBIOS      = $c.NbName
                MAC          = $c.Mac
                MacAleatoria = $c.MacRandomized
                Fabricante   = $c.Vendor
                TTL          = $c.Ttl
                RTTms        = $c.RttMs
                SO_TTL       = $c.OsGuess
                Veredicto    = $c.Verdict
                BaseVeredicto= $c.VerdictDetail
                PuertosAbier = $c.OpenPorts
                TipoPorPuerto= $c.PortGuess
                BannerHTTP   = $c.Banners
                SSDP         = $c.SsdpServer
                Dispositivo  = $c.DeviceLabel
                Navegador    = $c.BrowserLabel
                GPU          = $c.Gpu
                Pantalla     = $c.ScreenInfo
                Idioma       = $c.AcceptLanguage
                Protocolo    = $c.Protocol
                FirmaCabecer = $c.HeaderSignature
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
$pnlBotones.Controls.Add($btnExportClientes)

$lblOui = New-Object System.Windows.Forms.Label
$lblOui.Location = New-Object System.Drawing.Point(382, 8)
$lblOui.Size = New-Object System.Drawing.Size(600, 18)
$lblOui.Anchor = $AT -bor $AL -bor $AR
$lblOui.ForeColor = [System.Drawing.Color]::DimGray
$lblOui.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$lblOui.Text = "Fabricante: $($script:ouiSource). Coloca oui.txt (IEEE) junto al script para cobertura completa."
$pnlBotones.Controls.Add($lblOui)

$txtClienteDetalle = New-Object System.Windows.Forms.TextBox
$txtClienteDetalle.Dock = [System.Windows.Forms.DockStyle]::Fill
$txtClienteDetalle.Multiline = $true
$txtClienteDetalle.ReadOnly = $true
$txtClienteDetalle.ScrollBars = "Both"
$txtClienteDetalle.WordWrap = $false
$txtClienteDetalle.BackColor = [System.Drawing.Color]::FromArgb(248, 248, 248)
$txtClienteDetalle.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$txtClienteDetalle.Text = "Selecciona un cliente para ver su detalle completo."

$split.Panel2.Controls.Add($txtClienteDetalle)
$split.Panel2.Controls.Add($pnlBotones)

$tabClientes.Controls.Add($split)
$tabs.TabPages.Add($tabClientes)
$form.Controls.Add($tabs)

# El divisor solo se puede posicionar cuando el control ya tiene altura real.
$form.Add_Shown({
    try { $split.SplitterDistance = 170 } catch { }
    # El estilo extendido de doble buffer elimina el parpadeo de la tabla.
    try { [NativeUi]::EnableListViewDoubleBuffer($lvClientes.Handle) } catch { }
})

# --- FORMATEO Y REFRESCO DEL PANEL DE CLIENTES ---
$script:clientRows = @{}

function Get-ClientLocalTime {
    param([long]$Ticks)
    if ($Ticks -le 0) { return "-" }
    return ([DateTime]::new($Ticks, [System.DateTimeKind]::Utc)).ToLocalTime().ToString("HH:mm:ss")
}

function Format-ByteSize {
    param([long]$Bytes)
    if ($Bytes -lt 1024)       { return "$Bytes B" }
    if ($Bytes -lt 1048576)    { return "{0:N1} KB" -f ($Bytes / 1024) }
    if ($Bytes -lt 1073741824) { return "{0:N1} MB" -f ($Bytes / 1048576) }
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

    $sb = New-Object System.Text.StringBuilder

    [void]$sb.AppendLine("=== VEREDICTO ===")
    [void]$sb.AppendLine("Dispositivo        : $(if ($c.Verdict) { $c.Verdict } else { '(sondeo en curso)' })")
    if ($c.VerdictDetail) { [void]$sb.AppendLine("Base               : $($c.VerdictDetail)") }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("=== IDENTIDAD DE RED ===")
    [void]$sb.AppendLine("IP / puerto origen : $($c.Ip)  (puerto efimero $($c.LastPort))")
    [void]$sb.AppendLine("DNS inverso        : $(if ($c.HostName) { $c.HostName } else { '(sin respuesta)' })")
    [void]$sb.AppendLine("Nombre mDNS .local : $(if ($c.MdnsName) { $c.MdnsName } else { '(sin respuesta)' })")
    [void]$sb.AppendLine("Nombre NetBIOS     : $(if ($c.NbName) { $c.NbName } else { '(sin respuesta)' })")

    $macLinea = if (-not $c.Mac) {
        "(sin respuesta ARP: fuera del segmento local o cliente inactivo)"
    } elseif ($c.MacRandomized) {
        "$($c.Mac)  [ALEATORIA - privacidad del dispositivo, el OUI no identifica al fabricante]"
    } else {
        "$($c.Mac)  [$($c.Vendor)]"
    }
    [void]$sb.AppendLine("MAC                : $macLinea")

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("=== HUELLA DE RED ===")
    [void]$sb.AppendLine("SO por TTL         : $(if ($c.OsGuess) { $c.OsGuess } else { '(sin respuesta ICMP: firewall del cliente o red que filtra)' })")
    [void]$sb.AppendLine("Latencia ICMP      : $(if ($c.RttMs -ge 0) { "$($c.RttMs) ms" } else { '-' })")
    [void]$sb.AppendLine("Puertos abiertos   : $(if ($c.OpenPorts) { $c.OpenPorts } else { '(ninguno de los 21 sondeados)' })")
    [void]$sb.AppendLine("Tipo por puertos   : $(if ($c.PortGuess) { $c.PortGuess } else { '-' })")
    [void]$sb.AppendLine("Banner HTTP        : $(if ($c.Banners) { $c.Banners } else { '-' })")
    [void]$sb.AppendLine("SSDP / UPnP        : $(if ($c.SsdpServer) { $c.SsdpServer } else { '(no responde a M-SEARCH)' })")

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("=== CAPA HTTP ===")
    [void]$sb.AppendLine("Dispositivo (UA)   : $($c.DeviceLabel)")
    [void]$sb.AppendLine("Navegador          : $($c.BrowserLabel)")
    [void]$sb.AppendLine("Protocolo          : $($c.Protocol)  |  keep-alive: $($c.KeepAlive)")
    [void]$sb.AppendLine("Idioma             : $(if ($c.AcceptLanguage) { $c.AcceptLanguage } else { '-' })")
    [void]$sb.AppendLine("Compresion         : $(if ($c.AcceptEncoding) { $c.AcceptEncoding } else { '-' })")
    [void]$sb.AppendLine("Referer            : $(if ($c.Referer) { $c.Referer } else { '-' })")
    [void]$sb.AppendLine("Firma de cabeceras : $(if ($c.HeaderSignature) { $c.HeaderSignature } else { '-' })")

    $hints = @()
    if ($c.ChUa)       { $hints += "ua=$($c.ChUa)" }
    if ($c.ChPlatform) { $hints += "plataforma=$($c.ChPlatform)" }
    if ($c.ChMobile)   { $hints += "movil=$($c.ChMobile)" }
    if ($c.ChModel)    { $hints += "modelo=$($c.ChModel)" }
    [void]$sb.AppendLine("Client Hints       : $(if ($hints.Count -gt 0) { $hints -join '  |  ' } else { '(no enviados: exigen contexto seguro, solo https o localhost)' })")
    [void]$sb.AppendLine("User-Agent         : $(if ($c.UserAgent) { $c.UserAgent } else { '(vacio)' })")

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("=== SONDA JS ===")
    if ($c.ProbeSummary) {
        [void]$sb.AppendLine($c.ProbeSummary)
        [void]$sb.AppendLine("Recibida           : $(Get-ClientLocalTime $c.ProbeTicks)")
    } else {
        [void]$sb.AppendLine("(sin datos: recarga una pagina HTML en el cliente)")
    }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("=== TRAFICO ===")
    [void]$sb.AppendLine("Peticiones / bytes : $($c.RequestCount)  |  $(Format-ByteSize $c.BytesSent)")
    [void]$sb.AppendLine("Visto              : primera $(Get-ClientLocalTime $c.FirstSeenTicks)  |  ultima $(Get-ClientLocalTime $c.LastSeenTicks)")

    if ($c.RawHeaders) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("=== CABECERAS CRUDAS DE LA ULTIMA PETICION (en orden de llegada) ===")
        [void]$sb.Append($c.RawHeaders)
    }

    $texto = $sb.ToString()

    # 1. Si el texto no cambio, no se toca el control. Como los tiempos del
    #    panel son absolutos (HH:mm:ss) y no relativos, un cliente en reposo
    #    no genera ni un solo repintado.
    $mismoCliente = ($script:lastDetailIp -eq $ip)
    if ($mismoCliente -and $texto -eq $script:lastDetailText) { return }

    $script:lastDetailIp   = $ip
    $script:lastDetailText = $texto

    $h = $txtClienteDetalle.Handle

    # 2. Al cambiar de cliente el contenido es otro: se empieza arriba.
    #    En el mismo cliente se conserva la LINEA SUPERIOR VISIBLE, que es la
    #    posicion real de la barra. Antes se restauraba SelectionStart y se
    #    llamaba a ScrollToCaret, que salta a donde este el cursor: ese era
    #    justamente el motivo del desplazamiento.
    $primeraLinea = if ($mismoCliente) { [NativeUi]::GetFirstVisibleLine($h) } else { 0 }
    $selStart = $txtClienteDetalle.SelectionStart
    $selLen   = $txtClienteDetalle.SelectionLength

    [NativeUi]::SetRedraw($h, $false)
    try {
        $txtClienteDetalle.Text = $texto
        if ($mismoCliente -and $selStart -le $txtClienteDetalle.TextLength) {
            $txtClienteDetalle.SelectionStart  = $selStart
            $txtClienteDetalle.SelectionLength = [Math]::Min($selLen, $txtClienteDetalle.TextLength - $selStart)
        }
        [NativeUi]::ScrollToLine($h, $primeraLinea)
    } finally {
        # 3. El repintado se reactiva y se invalida una sola vez: un unico
        #    trazado en pantalla en lugar de varios intermedios.
        [NativeUi]::SetRedraw($h, $true)
        $txtClienteDetalle.Invalidate()
    }
}

function Update-ClientList {
    $snapshot = [ClientRegistry]::Snapshot()
    $rotulo = if ($snapshot.Count -gt 0) { "Clientes conectados ($($snapshot.Count))" } else { "Clientes conectados" }
    if ($tabClientes.Text -ne $rotulo) { $tabClientes.Text = $rotulo }
    if ($snapshot.Count -eq 0) { return }

    $lvClientes.BeginUpdate()
    try {
        foreach ($c in $snapshot) {
            $item = $script:clientRows[$c.Ip]
            if ($null -eq $item) {
                $item = New-Object System.Windows.Forms.ListViewItem($c.Ip)
                for ($i = 0; $i -lt 9; $i++) { [void]$item.SubItems.Add("") }
                $item.Tag = $c.Ip
                [void]$lvClientes.Items.Add($item)
                $script:clientRows[$c.Ip] = $item
            }

            $nombre = if ($c.BestName) { ($c.BestName -split '\.')[0] } else { "..." }
            $mac    = if ($c.Mac) { $c.Mac } else { "..." }
            $vend   = if ($c.MacRandomized) { "MAC aleatoria" } elseif ($c.Vendor) { $c.Vendor } else { "..." }
            $ver    = if ($c.Verdict) { $c.Verdict } else { "sondeando..." }
            $gpu    = if ($c.Gpu) { $c.Gpu } else { "-" }

            # Escribir una celda invalida su fila aunque el valor sea el mismo,
            # y eso es la mitad del parpadeo. Solo se toca lo que cambio.
            $valores = @($nombre, $mac, $vend, $c.DeviceLabel, $c.BrowserLabel,
                         $ver, $gpu, [string]$c.RequestCount, (Format-Elapsed $c.LastSeenTicks))
            for ($i = 0; $i -lt $valores.Length; $i++) {
                if ($item.SubItems[$i + 1].Text -ne $valores[$i]) {
                    $item.SubItems[$i + 1].Text = $valores[$i]
                }
            }

            # Inactivo mas de 60 s: se atenua en vez de desaparecer, para no
            # perder el historial de quien se conecto durante la sesion.
            $idle = ([DateTime]::UtcNow - [DateTime]::new($c.LastSeenTicks, [System.DateTimeKind]::Utc)).TotalSeconds
            $color = if ($idle -gt 60) { [System.Drawing.Color]::Gray } else { [System.Drawing.Color]::Black }
            if ($item.ForeColor -ne $color) { $item.ForeColor = $color }
        }
    } finally {
        $lvClientes.EndUpdate()
    }

    if ($lvClientes.SelectedItems.Count -gt 0) { Show-ClientDetail }
}

$script:lastDetailIp   = ""
$script:lastDetailText = ""

$lvClientes.Add_SelectedIndexChanged({ Show-ClientDetail })

$clientsTimer = New-Object System.Windows.Forms.Timer
$clientsTimer.Interval = 1500
$clientsTimer.Add_Tick({
    if ($tabs.SelectedTab -eq $tabClientes) {
        Update-ClientList
        $estadoMdns = if ([MdnsListener]::Activo) {
            "mDNS pasivo: $([MdnsListener]::Count) nombres"
        } else {
            "mDNS pasivo: inactivo (solo en modo LAN)"
        }
        $rotulo = "Fabricante: $($script:ouiSource).  |  $estadoMdns"
        if ($lblOui.Text -ne $rotulo) { $lblOui.Text = $rotulo }
    }
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

# =====================================================================
#  SONDA JS (NIVEL 3)
#  Se inyecta antes de </body> solo en respuestas text/html y solo si el
#  usuario activa la casilla. Envia por POST a /__probe. Comilla simple
#  en el here-string: PowerShell no interpola nada de esto.
# =====================================================================
$script:probeScript = @'
<script>(function(){try{
var n=navigator,s=screen,cn=n.connection||{},gpu="";
try{var cv=document.createElement("canvas");
var gl=cv.getContext("webgl")||cv.getContext("experimental-webgl");
if(gl){var dx=gl.getExtension("WEBGL_debug_renderer_info");
gpu=dx?(gl.getParameter(dx.UNMASKED_RENDERER_WEBGL)+" | "+gl.getParameter(dx.UNMASKED_VENDOR_WEBGL)):gl.getParameter(gl.RENDERER);}
}catch(e){}
var p={screen:s.width+"x"+s.height+" @"+(window.devicePixelRatio||1)+"x "+(s.colorDepth||"?")+"bit",
viewport:window.innerWidth+"x"+window.innerHeight,gpu:gpu,
cores:n.hardwareConcurrency||"?",ram:n.deviceMemory||"?",touch:n.maxTouchPoints||0,
platform:n.platform||"",langs:(n.languages||[]).join(","),
tz:(function(){try{return Intl.DateTimeFormat().resolvedOptions().timeZone}catch(e){return""}})(),
net:(cn.effectiveType||"")+(cn.downlink?" ~"+cn.downlink+"Mbps":"")+(cn.rtt?" rtt"+cn.rtt+"ms":"")};
function send(o){try{var x=new XMLHttpRequest();x.open("POST","/__probe",true);
x.setRequestHeader("Content-Type","text/plain");x.send(JSON.stringify(o));}catch(e){}}
if(n.userAgentData&&n.userAgentData.getHighEntropyValues){
n.userAgentData.getHighEntropyValues(["platformVersion","model","architecture","bitness"])
.then(function(h){p.hints=h;send(p);})["catch"](function(){send(p);});}else{send(p);}
}catch(e){}})();</script>
'@
[ClientRegistry]::ProbeScript = $script:probeScript

# El mantenimiento no abre sockets por si mismo, asi que puede vivir desde el
# arranque. La escucha mDNS NO: se une a 224.0.0.251:5353 con un socket a la
# escucha, y eso hace saltar el aviso del firewall de Windows. Se arranca solo
# al iniciar el servidor en modo LAN, que es el unico caso donde aporta algo.
[ClientRegistry]::StartMaintenance()

# --- HANDLER CONCURRENTE ---
$requestHandlerScript = {
    param($context, $rootPath, $realRootPath, $logQueue, $serverState, $mimeDict, $corsEnabled)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $request  = $context.Request
    $response = $context.Response

    $clientIP = "?"
    $srcPort  = 0
    try {
        if ($request.RemoteEndPoint) {
            $clientIP = $request.RemoteEndPoint.Address.ToString()
            $srcPort  = $request.RemoteEndPoint.Port
        }
    } catch { }

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
            $response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, POST, OPTIONS")
            $response.AddHeader("Access-Control-Allow-Headers", "*")
        }
        $response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        $response.AddHeader("X-Content-Type-Options", "nosniff")

        if ($httpMethod -eq "OPTIONS") {
            $response.StatusCode = 204
            & $writeLog 'INFO' ''
            return
        }

        if ($null -eq $request.Url) {
            $response.StatusCode = 400
            & $writeLog 'WARN' ' - URL invalida'
            return
        }

        # --- NIVEL 3: recepcion de la sonda JS ---
        if ($request.Url.LocalPath -eq '/__probe') {
            if ($httpMethod -ne 'POST') {
                $response.StatusCode = 405
                & $writeLog 'WARN' ' - /__probe solo acepta POST'
                return
            }
            if ($request.ContentLength64 -gt 65536) {
                $response.StatusCode = 413
                & $writeLog 'WARN' ' - Sonda demasiado grande'
                return
            }
            try {
                $sr = New-Object System.IO.StreamReader($request.InputStream, [System.Text.Encoding]::UTF8)
                $body = $sr.ReadToEnd()
                $sr.Dispose()
                $o = $body | ConvertFrom-Json

                $det = New-Object System.Collections.Generic.List[string]
                $det.Add("Pantalla           : $($o.screen)   |  viewport $($o.viewport)")
                $det.Add("GPU / SoC          : $(if ($o.gpu) { $o.gpu } else { '(WebGL no disponible)' })")
                $det.Add("CPU / RAM          : $($o.cores) nucleos  |  $($o.ram) GB (redondeado por el navegador)")
                $det.Add("Puntos tactiles    : $($o.touch)  |  navigator.platform: $($o.platform)")
                $det.Add("Idiomas (JS)       : $($o.langs)")
                $det.Add("Zona horaria       : $($o.tz)")
                $det.Add("Red segun cliente  : $(if ($o.net) { $o.net } else { '-' })")
                if ($o.hints) {
                    $det.Add("High entropy hints : modelo=$($o.hints.model)  plataforma=$($o.hints.platformVersion)  arq=$($o.hints.architecture) $($o.hints.bitness)")
                }
                [ClientRegistry]::SetProbe($clientIP, [string]$o.gpu, [string]$o.screen, ($det -join "`r`n"))

                $response.StatusCode = 204
                & $writeLog 'INFO' ' - Sonda JS recibida'
            } catch {
                $response.StatusCode = 400
                & $writeLog 'WARN' ' - Sonda JS ilegible'
            }
            return
        }

        if ($httpMethod -ne "GET" -and $httpMethod -ne "HEAD") {
            $response.StatusCode = 405
            $response.AddHeader("Allow", "GET, HEAD, OPTIONS")
            & $writeLog 'WARN' ' - Metodo no permitido'
            return
        }

        # Url.LocalPath YA viene decodificado. Un segundo UnescapeDataString
        # convertiria %2520 en espacio y %252e%252e%252f en ../, anulando una
        # capa de defensa y corrompiendo nombres de archivo legitimos.
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

            $ext = [System.IO.Path]::GetExtension($fileToServe).ToLowerInvariant()
            if ($mimeDict.ContainsKey($ext)) {
                $response.ContentType = $mimeDict[$ext]
            } else {
                $response.ContentType = "application/octet-stream"
            }

            [long]$fileLength = $fileStream.Length
            $aborted = $false

            # --- NIVEL 3: inyeccion de la sonda en HTML ---
            # Se bufferiza el archivo entero, asi que se limita a 4 MB y se
            # excluyen las peticiones con Range (irrelevantes en HTML).
            $esHtml = $response.ContentType.StartsWith("text/html", [System.StringComparison]::OrdinalIgnoreCase)
            if ([ClientRegistry]::EnableJsProbe -and $esHtml -and $httpMethod -eq "GET" -and
                $fileLength -gt 0 -and $fileLength -lt 4194304) {

                $raw = New-Object byte[] ([int]$fileLength)
                $leidos = 0
                while ($leidos -lt $fileLength) {
                    $n = $fileStream.Read($raw, $leidos, [int]($fileLength - $leidos))
                    if ($n -le 0) { break }
                    $leidos += $n
                }

                $texto = [System.Text.Encoding]::UTF8.GetString($raw, 0, $leidos)
                $idx = $texto.LastIndexOf("</body>", [System.StringComparison]::OrdinalIgnoreCase)
                $sonda = [ClientRegistry]::ProbeScript
                if ($idx -ge 0) { $texto = $texto.Substring(0, $idx) + $sonda + $texto.Substring($idx) }
                else            { $texto = $texto + $sonda }

                $salida = [System.Text.Encoding]::UTF8.GetBytes($texto)
                $response.StatusCode = 200
                $response.ContentLength64 = $salida.Length

                try {
                    $response.OutputStream.Write($salida, 0, $salida.Length)
                    $bytesSent = $salida.Length
                }
                catch [System.Net.HttpListenerException] { $aborted = $true }
                catch [System.IO.IOException]            { $aborted = $true }
                catch [System.ObjectDisposedException]   { $aborted = $true }

                if ($aborted) { & $writeLog 'INFO' ' - Transferencia interrumpida (HTML+sonda)' }
                else          { & $writeLog 'INFO' ' - HTML con sonda inyectada' }
                return
            }

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
                # La desconexion del cliente (seek de video, imagen cancelada) se
                # captura aparte para no ensuciar el log con ERROR 500 falsos.
                catch [System.Net.HttpListenerException] { $aborted = $true }
                catch [System.IO.IOException]            { $aborted = $true }
                catch [System.ObjectDisposedException]   { $aborted = $true }
            }

            if ($aborted) { & $writeLog 'INFO' ' - Transferencia interrumpida' }
            else          { & $writeLog 'INFO' '' }
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
        # Registro del cliente: una escritura atomica por peticion. El
        # enriquecimiento lento (rDNS, ARP, ICMP, mDNS, puertos) lo dispara
        # ClientRegistry en un hilo aparte, nunca en este.
        try {
            $ua = ""; $lang = ""; $chUa = ""; $chPlt = ""; $chMob = ""; $chMod = ""
            $rawHeaders = ""; $firma = ""; $enc = ""; $ref = ""
            try {
                $h = $request.Headers
                $ua    = [string]$request.UserAgent
                $lang  = [string]$h["Accept-Language"]
                $chUa  = [string]$h["Sec-CH-UA"]
                $chPlt = [string]$h["Sec-CH-UA-Platform"]
                $chMob = [string]$h["Sec-CH-UA-Mobile"]
                $chMod = [string]$h["Sec-CH-UA-Model"]
                $enc   = [string]$h["Accept-Encoding"]
                $ref   = [string]$h["Referer"]

                # NIVEL 0: volcado en orden de llegada. El propio orden de las
                # cabeceras es huella de navegador.
                $claves = $h.AllKeys
                $firma  = $claves -join ','
                $sbH = New-Object System.Text.StringBuilder
                foreach ($k in $claves) { [void]$sbH.AppendLine("  $k : $($h[$k])") }
                $rawHeaders = $sbH.ToString()
            } catch { }

            [ClientRegistry]::Track($clientIP, $ua, $lang, $chUa, $chPlt, $chMob, $chMod, $bytesSent)
            [ClientRegistry]::SetHttpDetails($clientIP, $rawHeaders, $firma,
                                             [string]$request.ProtocolVersion,
                                             [bool]$request.KeepAlive, $srcPort, $ref, $enc)
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

    try { [MdnsListener]::Stop() } catch { }
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
    $script:lastDetailIp = ""
    $script:lastDetailText = ""
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

        # Solo aqui: en localhost el mDNS no aporta nada y no merece el aviso
        # del firewall. Es la unica escucha del programa aparte del propio HTTP.
        [MdnsListener]::Start()
        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nEscucha mDNS pasiva iniciada en 224.0.0.251:5353.`r`n")
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
    try { [MdnsListener]::Stop() } catch { }
    Stop-WebServer
})

[void]$form.ShowDialog()

# ShowDialog no libera el formulario: la limpieza va aqui, no dentro de FormClosed.
try { $logTimer.Dispose() }     catch { }
try { $clientsTimer.Dispose() } catch { }
try { $form.Dispose() }         catch { }
