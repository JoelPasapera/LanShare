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
if (-not ([System.Management.Automation.PSTypeName]'NativePath').Type) {
    Add-Type -TypeDefinition @"
    using System;
    using System.Text;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

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
"@
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
$form.Size = New-Object System.Drawing.Size(680, 668)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

$labelPermiso = New-Object System.Windows.Forms.Label
$labelPermiso.Location = New-Object System.Drawing.Point(20, 15)
$labelPermiso.Size = New-Object System.Drawing.Size(350, 20)
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
$btnEscalar.Location = New-Object System.Drawing.Point(490, 10)
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
$line.Size = New-Object System.Drawing.Size(620, 2)
$line.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$form.Controls.Add($line)

$gbModo = New-Object System.Windows.Forms.GroupBox
$gbModo.Location = New-Object System.Drawing.Point(20, 50)
$gbModo.Size = New-Object System.Drawing.Size(620, 80)
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
$chkCors.Size = New-Object System.Drawing.Size(580, 22)
$chkCors.Text = "Habilitar CORS abierto (Access-Control-Allow-Origin: *) - solo si tu app lo necesita"
$chkCors.Checked = $false
$gbModo.Controls.Add($chkCors)

$form.Controls.Add($gbModo)

$labelRuta = New-Object System.Windows.Forms.Label
$labelRuta.Location = New-Object System.Drawing.Point(20, 137)
$labelRuta.Size = New-Object System.Drawing.Size(400, 18)
$labelRuta.Text = "Carpeta raiz de la aplicacion web:"
$form.Controls.Add($labelRuta)

$txtRuta = New-Object System.Windows.Forms.TextBox
$txtRuta.Location = New-Object System.Drawing.Point(20, 157)
$txtRuta.Size = New-Object System.Drawing.Size(370, 23)
if ($PSScriptRoot) { $txtRuta.Text = $PSScriptRoot } else { $txtRuta.Text = "" }
$form.Controls.Add($txtRuta)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(400, 155)
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
$labelPuerto.Location = New-Object System.Drawing.Point(505, 137)
$labelPuerto.Size = New-Object System.Drawing.Size(80, 18)
$labelPuerto.Text = "Puerto TCP:"
$form.Controls.Add($labelPuerto)

$txtPuerto = New-Object System.Windows.Forms.TextBox
$txtPuerto.Location = New-Object System.Drawing.Point(505, 157)
$txtPuerto.Size = New-Object System.Drawing.Size(135, 23)
$txtPuerto.Text = "8080"
$form.Controls.Add($txtPuerto)

$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 193)
$btnStart.Size = New-Object System.Drawing.Size(620, 38)
$btnStart.Text = "Iniciar Servidor"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnStart)

$gbEstado = New-Object System.Windows.Forms.GroupBox
$gbEstado.Location = New-Object System.Drawing.Point(20, 240)
$gbEstado.Size = New-Object System.Drawing.Size(620, 100)
$gbEstado.Text = "Estado y Direcciones de Acceso"

$txtEstadoInfo = New-Object System.Windows.Forms.TextBox
$txtEstadoInfo.Location = New-Object System.Drawing.Point(15, 22)
$txtEstadoInfo.Size = New-Object System.Drawing.Size(430, 68)
$txtEstadoInfo.Multiline = $true
$txtEstadoInfo.ReadOnly = $true
$txtEstadoInfo.ScrollBars = "Vertical"
$txtEstadoInfo.Text = "Estado: Detenido"
$txtEstadoInfo.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$gbEstado.Controls.Add($txtEstadoInfo)

$btnCopyLAN = New-Object System.Windows.Forms.Button
$btnCopyLAN.Location = New-Object System.Drawing.Point(455, 30)
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

$labelConsole = New-Object System.Windows.Forms.Label
$labelConsole.Location = New-Object System.Drawing.Point(20, 348)
$labelConsole.Size = New-Object System.Drawing.Size(300, 18)
$labelConsole.Text = "Registro de Telemetria (Live Logs):"
$labelConsole.Font = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($labelConsole)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(20, 368)
$txtLog.Size = New-Object System.Drawing.Size(620, 245)
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
$txtLog.ForeColor = [System.Drawing.Color]::FromArgb(220, 220, 220)
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$form.Controls.Add($txtLog)

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
    Stop-WebServer
})

[void]$form.ShowDialog()

# ShowDialog no libera el formulario: la limpieza va aqui, no dentro de FormClosed.
try { $logTimer.Dispose() } catch { }
try { $form.Dispose() }     catch { }
