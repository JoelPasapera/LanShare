[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --- RESOLUCIÓN NATIVA WIN32 ANTI-JUNCTION / SYMLINK, TOCTOU Y EXPLORADOR MODERNO ---
if (-not ([System.Management.Automation.PSTypeName]'NativePath').Type) {
    Add-Type -TypeDefinition @"
    using System;
    using System.Text;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    public static class NativePath {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        public static string GetRealPathFromHandle(SafeFileHandle hFile) {
            if (hFile == null || hFile.IsInvalid || hFile.IsClosed) return string.Empty;
            StringBuilder sb = new StringBuilder(1024);
            if (GetFinalPathNameByHandle(hFile, sb, 1024, 0) == 0) return string.Empty;
            string realPath = sb.ToString();
            return realPath.StartsWith(@"\\?\") ? realPath.Substring(4) : realPath;
        }

        public static string GetRealPath(string path) {
            if (string.IsNullOrEmpty(path)) return path;
            using (SafeFileHandle hFile = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
                if (hFile.IsInvalid) return path;
                StringBuilder sb = new StringBuilder(1024);
                if (GetFinalPathNameByHandle(hFile, sb, 1024, 0) == 0) return path;
                string realPath = sb.ToString();
                return realPath.StartsWith(@"\\?\") ? realPath.Substring(4) : realPath;
            }
        }
    }

    public class ModernFolderPicker {
        public string InitialFolder { get; set; }
        public string SelectedPath { get; private set; }

        public bool ShowDialog(IntPtr owner) {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

                if (!string.IsNullOrEmpty(InitialFolder) && System.IO.Directory.Exists(InitialFolder)) {
                    IShellItem item;
                    SHCreateItemFromParsingName(InitialFolder, IntPtr.Zero, typeof(IShellItem).GUID, out item);
                    if (item != null) dialog.SetFolder(item);
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
            } catch {}
            finally {
                Marshal.ReleaseComObject(dialog);
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

function Test-IsAdmin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

$esAdmin = Test-IsAdmin
$script:logQueue = New-Object System.Collections.Concurrent.ConcurrentQueue[string]
$script:psInstance = $null

# --- INTERFAZ GRÁFICA ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Servidor Web Pro - Hardened & Multi-Threaded"
$form.Size = New-Object System.Drawing.Size(680, 580)
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
        Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
        $form.Close()
    } else {
        [System.Windows.Forms.MessageBox]::Show("Guarda el codigo en un archivo .ps1 para ejecutarlo como Administrador.", "Aviso", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
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
$gbModo.Size = New-Object System.Drawing.Size(620, 55)
$gbModo.Text = "Modo de Red"

$rbLocal = New-Object System.Windows.Forms.RadioButton
$rbLocal.Location = New-Object System.Drawing.Point(15, 20)
$rbLocal.Size = New-Object System.Drawing.Size(200, 25)
$rbLocal.Text = "Modo Local (localhost)"
$rbLocal.Checked = $true
$gbModo.Controls.Add($rbLocal)

$rbLAN = New-Object System.Windows.Forms.RadioButton
$rbLAN.Location = New-Object System.Drawing.Point(230, 20)
$rbLAN.Size = New-Object System.Drawing.Size(250, 25)
$rbLAN.Text = "Modo LAN (Todas las Interfaces)"
$gbModo.Controls.Add($rbLAN)
$form.Controls.Add($gbModo)

$labelRuta = New-Object System.Windows.Forms.Label
$labelRuta.Location = New-Object System.Drawing.Point(20, 112)
$labelRuta.Size = New-Object System.Drawing.Size(400, 18)
$labelRuta.Text = "Carpeta raiz de la aplicacion web:"
$form.Controls.Add($labelRuta)

$txtRuta = New-Object System.Windows.Forms.TextBox
$txtRuta.Location = New-Object System.Drawing.Point(20, 132)
$txtRuta.Size = New-Object System.Drawing.Size(370, 23)
$txtRuta.Text = "C:\"
$form.Controls.Add($txtRuta)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(400, 130)
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
$labelPuerto.Location = New-Object System.Drawing.Point(505, 112)
$labelPuerto.Size = New-Object System.Drawing.Size(70, 18)
$labelPuerto.Text = "Puerto TCP:"
$form.Controls.Add($labelPuerto)

$txtPuerto = New-Object System.Windows.Forms.TextBox
$txtPuerto.Location = New-Object System.Drawing.Point(505, 132)
$txtPuerto.Size = New-Object System.Drawing.Size(135, 23)
$txtPuerto.Text = "8080"
$form.Controls.Add($txtPuerto)

$labelEstado = New-Object System.Windows.Forms.Label
$labelEstado.Location = New-Object System.Drawing.Point(20, 168)
$labelEstado.Size = New-Object System.Drawing.Size(620, 20)
$labelEstado.Text = "Estado: Detenido"
$labelEstado.ForeColor = [System.Drawing.Color]::Red
$labelEstado.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($labelEstado)

$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 193)
$btnStart.Size = New-Object System.Drawing.Size(620, 38)
$btnStart.Text = "Iniciar Servidor"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($btnStart)

$labelConsole = New-Object System.Windows.Forms.Label
$labelConsole.Location = New-Object System.Drawing.Point(20, 240)
$labelConsole.Size = New-Object System.Drawing.Size(250, 18)
$labelConsole.Text = "Registro de Telemetria (Live Logs):"
$labelConsole.Font = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($labelConsole)

$txtLog = New-Object System.Windows.Forms.TextBox
$txtLog.Location = New-Object System.Drawing.Point(20, 260)
$txtLog.Size = New-Object System.Drawing.Size(620, 260)
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = "Vertical"
$txtLog.BackColor = [System.Drawing.Color]::FromArgb(30, 30, 30)
$txtLog.ForeColor = [System.Drawing.Color]::FromArgb(220, 220, 220)
$txtLog.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$form.Controls.Add($txtLog)

# Polling de logs thread-safe en la GUI
$logTimer = New-Object System.Windows.Forms.Timer
$logTimer.Interval = 100
$logTimer.Add_Tick({
    $logMsg = ""
    while ($script:logQueue.TryDequeue([ref]$logMsg)) {
        $txtLog.AppendText($logMsg)
        $txtLog.SelectionStart = $txtLog.TextLength
        $txtLog.ScrollToCaret()
    }
})
$logTimer.Start()

# --- HANDLER CONCURRENTE (RUNSPACEPOOL) CON SEGURIDAD TOCTOU Y HTTP RANGE RFC 9110 ---
$requestHandlerScript = {
    param($context, $rootPath, $realRootUri, $logQueue)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $request = $context.Request
    $response = $context.Response
    $clientIP = $request.RemoteEndPoint.Address.ToString()
    $httpMethod = $request.HttpMethod
    $rawUrl = $request.Url.PathAndQuery
    [long]$bytesSent = 0

    try {
        $response.AddHeader("Access-Control-Allow-Origin", "*")
        $response.AddHeader("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS")
        $response.AddHeader("Access-Control-Allow-Headers", "*")
        $response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        $response.AddHeader("Accept-Ranges", "bytes")

        if ($httpMethod -eq "OPTIONS") {
            $response.StatusCode = 204
            $sw.Stop()
            $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`n$httpMethod $rawUrl 204 $($sw.ElapsedMilliseconds)ms 0B - Client: $clientIP`r`n")
            return
        }

        $decodedPath = [System.Uri]::UnescapeDataString($request.Url.LocalPath).TrimStart('/')
        if ([string]::IsNullOrEmpty($decodedPath)) { $decodedPath = "index.html" }

        $candidatePath = Join-Path $rootPath $decodedPath
        $fullPath = [System.IO.Path]::GetFullPath($candidatePath)
        $fileToServe = $null

        if (Test-Path $fullPath -PathType Leaf) {
            $fileToServe = $fullPath
        } else {
            $requestedExt = [System.IO.Path]::GetExtension($decodedPath)
            $indexPath = Join-Path $rootPath "index.html"
            if ([string]::IsNullOrEmpty($requestedExt) -and (Test-Path $indexPath -PathType Leaf)) {
                $fileToServe = $indexPath
            }
        }

        if ($fileToServe) {
            # 1. ATOMIC OPEN-FIRST
            $fileStream = $null
            try {
                $fileStream = [System.IO.File]::Open(
                    $fileToServe, 
                    [System.IO.FileMode]::Open, 
                    [System.IO.FileAccess]::Read, 
                    [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
                )
            } catch {
                $response.StatusCode = 404
                $sw.Stop()
                $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN]`r`n$httpMethod $rawUrl 404 $($sw.ElapsedMilliseconds)ms 0B - Unable to open file handle - Client: $clientIP`r`n")
                return
            }

            try {
                # 2. VALIDATE HANDLE ATOMICALLY (Prevención TOCTOU)
                $realHandlePath = [NativePath]::GetRealPathFromHandle($fileStream.SafeFileHandle)
                $realHandleUri = New-Object System.Uri($realHandlePath)

                if (-not $realRootUri.IsBaseOf($realHandleUri)) {
                    $response.StatusCode = 403
                    $sw.Stop()
                    $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [SECURITY WARN]`r`n$httpMethod $rawUrl 403 $($sw.ElapsedMilliseconds)ms 0B - TOCTOU Symlink Bypass Blocked - Client: $clientIP`r`n")
                    return
                }

                # 3. MIME TYPES
                $ext = [System.IO.Path]::GetExtension($fileToServe).ToLower()
                switch ($ext) {
                    ".html"  { $response.ContentType = "text/html; charset=utf-8" }
                    ".css"   { $response.ContentType = "text/css" }
                    ".js"    { $response.ContentType = "application/javascript" }
                    ".json"  { $response.ContentType = "application/json" }
                    ".png"   { $response.ContentType = "image/png" }
                    ".jpg"   { $response.ContentType = "image/jpeg" }
                    ".mp4"   { $response.ContentType = "video/mp4" }
                    ".webm"  { $response.ContentType = "video/webm" }
                    ".svg"   { $response.ContentType = "image/svg+xml" }
                    ".webp"  { $response.ContentType = "image/webp" }
                    ".woff2" { $response.ContentType = "font/woff2" }
                    default  { $response.ContentType = "application/octet-stream" }
                }

                $fileLength = $fileStream.Length
                $rangeHeader = $request.Headers["Range"]

                # 4. HTTP RANGE (RFC 9110)
                if (-not [string]::IsNullOrEmpty($rangeHeader) -and $rangeHeader -match "^bytes=(\d*)-(\d*)$") {
                    $rawStart = $matches[1]
                    $rawEnd = $matches[2]
                    [long]$start = 0
                    [long]$end = $fileLength - 1

                    if ([string]::IsNullOrEmpty($rawStart) -and -not [string]::IsNullOrEmpty($rawEnd)) {
                        $suffixLength = [long]$rawEnd
                        if ($suffixLength -gt 0) { $start = [Math]::Max(0, $fileLength - $suffixLength) }
                    }
                    elseif (-not [string]::IsNullOrEmpty($rawStart) -and [string]::IsNullOrEmpty($rawEnd)) {
                        $start = [long]$rawStart
                    }
                    elseif (-not [string]::IsNullOrEmpty($rawStart) -and -not [string]::IsNullOrEmpty($rawEnd)) {
                        $start = [long]$rawStart
                        $end = [long]$rawEnd
                    }

                    if ($end -ge $fileLength) { $end = $fileLength - 1 }

                    if ($start -ge $fileLength -or $start -gt $end) {
                        $response.StatusCode = 416
                        $response.AddHeader("Content-Range", "bytes */$fileLength")
                        $sw.Stop()
                        $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN]`r`n$httpMethod $rawUrl 416 $($sw.ElapsedMilliseconds)ms 0B - Range Not Satisfiable - Client: $clientIP`r`n")
                        return
                    }

                    $response.StatusCode = 206
                    $contentLength = $end - $start + 1
                    $response.ContentLength64 = $contentLength
                    $response.AddHeader("Content-Range", "bytes $start-$end/$fileLength")

                    if ($httpMethod -eq "GET") {
                        $fileStream.Seek($start, [System.IO.SeekOrigin]::Begin) | Out-Null
                        $buffer = New-Object byte[] 65536
                        $bytesRemaining = $contentLength

                        while ($bytesRemaining -gt 0) {
                            $bytesToRead = [Math]::Min($buffer.Length, $bytesRemaining)
                            $bytesRead = $fileStream.Read($buffer, 0, $bytesToRead)
                            if ($bytesRead -le 0) { break }
                            $response.OutputStream.Write($buffer, 0, $bytesRead)
                            $bytesRemaining -= $bytesRead
                            $bytesSent += $bytesRead
                        }
                    }
                } else {
                    $response.StatusCode = 200
                    $response.ContentLength64 = $fileLength

                    if ($httpMethod -eq "GET") {
                        $fileStream.CopyTo($response.OutputStream)
                        $bytesSent = $fileLength
                    }
                }

                $sw.Stop()
                $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`n$httpMethod $rawUrl $($response.StatusCode) $($sw.ElapsedMilliseconds)ms ${bytesSent}B - Client: $clientIP`r`n")

            } finally {
                if ($null -ne $fileStream) { $fileStream.Close() }
            }
        } else {
            $response.StatusCode = 404
            $sw.Stop()
            $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [WARN]`r`n$httpMethod $rawUrl 404 $($sw.ElapsedMilliseconds)ms 0B - File Not Found - Client: $clientIP`r`n")
        }
    } catch {
        $sw.Stop()
        $ex = $_.Exception
        try { $context.Response.StatusCode = 500 } catch {}
        $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR]`r`n$httpMethod $rawUrl - Client: $clientIP`r`nException [$($ex.GetType().FullName)]:`r`n$($ex.Message)`r`n")
    } finally {
        try { $context.Response.Close() } catch {}
    }
}

# --- MASTER LISTENER LOOP ---
$serverMasterScript = {
    param($bindingPrefix, $rootPath, $realRootPath, $logQueue, $handlerScriptBlock)

    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add($bindingPrefix)

    try {
        $listener.Start()
    } catch {
        $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR]`r`nHttpListener Start Error: $($_.Exception.Message)`r`n")
        return
    }

    $realRootUri = New-Object System.Uri($realRootPath)
    $pool = [runspacefactory]::CreateRunspacePool(2, 25)
    $pool.Open()

    $tasks = New-Object System.Collections.Generic.List[PSObject]

    while ($listener.IsListening) {
        try {
            $context = $listener.GetContext()

            $psWorker = [powershell]::Create()
            $psWorker.RunspacePool = $pool
            [void]$psWorker.AddScript($handlerScriptBlock)
            [void]$psWorker.AddArgument($context)
            [void]$psWorker.AddArgument($rootPath)
            [void]$psWorker.AddArgument($realRootUri)
            [void]$psWorker.AddArgument($logQueue)
            
            $asyncResult = $psWorker.BeginInvoke()
            [void]$tasks.Add([PSCustomObject]@{ Pipe = $psWorker; Status = $asyncResult })

            for ($i = $tasks.Count - 1; $i -ge 0; $i--) {
                if ($tasks[$i].Status.IsCompleted) {
                    try {
                        $tasks[$i].Pipe.EndInvoke($tasks[$i].Status)
                        $tasks[$i].Pipe.Dispose()
                    } catch {}
                    $tasks.RemoveAt($i)
                }
            }

        } catch [System.Net.HttpListenerException], [System.ObjectDisposedException] {
            break
        } catch {
            $logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [ERROR]`r`nMaster Loop Error: $($_.Exception.Message)`r`n")
        }
    }

    foreach ($t in $tasks) {
        try { $t.Pipe.Dispose() } catch {}
    }
    try { $pool.Close(); $pool.Dispose() } catch {}
    try { $listener.Stop(); $listener.Close() } catch {}
}

# --- CONTROL DE INICIO Y DETENCIÓN ---
$btnStart.Add_Click({
    if ($script:psInstance) {
        try {
            $script:psInstance.Stop()
            $script:psInstance.Dispose()
        } catch {}
        $script:psInstance = $null

        $gbModo.Enabled = $true
        $txtPuerto.Enabled = $true
        $txtRuta.Enabled = $true
        $btnBrowse.Enabled = $true
        $labelEstado.Text = "Estado: Detenido"
        $labelEstado.ForeColor = [System.Drawing.Color]::Red
        $btnStart.Text = "Iniciar Servidor"
        $btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
        $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nServidor detenido por el usuario.`r`n")
        return
    }

    $puertoRaw = $txtPuerto.Text.Trim()
    [int]$puerto = 0
    if (-not [int]::TryParse($puertoRaw, [ref]$puerto) -or $puerto -lt 1 -or $puerto -gt 65535) {
        [System.Windows.Forms.MessageBox]::Show("Ingrese un puerto valido (1-65535).", "Error de Validacion", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $rutaInput = $txtRuta.Text.Trim('"').Trim("'")
    if (-not (Test-Path $rutaInput -PathType Container)) {
        [System.Windows.Forms.MessageBox]::Show("La ruta especificada no existe.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $basePath = [System.IO.Path]::GetFullPath($rutaInput)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }
    
    $realRootPath = [NativePath]::GetRealPath($basePath)
    if (-not $realRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $realRootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $bindingPrefix = ""
    $displayUrl = ""

    if ($rbLocal.Checked) {
        $bindingPrefix = "http://localhost:$puerto/"
        $displayUrl = "http://localhost:$puerto/"
    } else {
        if (-not (Test-IsAdmin)) {
            [System.Windows.Forms.MessageBox]::Show("El Modo LAN requiere ejecutar la aplicacion como Administrador.", "Permisos Insuficientes", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
            return
        }

        $bindingPrefix = "http://+:$puerto/"
        $localIP = (Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias "Wi-Fi","Ethernet*" -ErrorAction SilentlyContinue | Select-Object -First 1).IPAddress
        if (-not $localIP) { $localIP = "IP_DE_TU_RED" }
        $displayUrl = "http://${localIP}:$puerto/"

        $ruleName = "Permitir HTTP Servidor Pro $puerto"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $puerto -Action Allow -Enabled True | Out-Null
        }
    }

    $script:psInstance = [powershell]::Create()
    [void]$script:psInstance.AddScript($serverMasterScript)
    [void]$script:psInstance.AddArgument($bindingPrefix)
    [void]$script:psInstance.AddArgument($basePath)
    [void]$script:psInstance.AddArgument($realRootPath)
    [void]$script:psInstance.AddArgument($script:logQueue)
    [void]$script:psInstance.AddArgument($requestHandlerScript)
    [void]$script:psInstance.BeginInvoke()

    $gbModo.Enabled = $false
    $txtPuerto.Enabled = $false
    $txtRuta.Enabled = $false
    $btnBrowse.Enabled = $false

    $labelEstado.Text = "Estado: Corriendo en $displayUrl"
    $labelEstado.ForeColor = [System.Drawing.Color]::Green
    $btnStart.Text = "Detener Servidor"
    $btnStart.BackColor = [System.Drawing.Color]::FromArgb(220, 53, 69)

    $script:logQueue.Enqueue("$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [INFO]`r`nServidor iniciado en $displayUrl [Raiz: $basePath]`r`n")
    Start-Process "http://localhost:$puerto/"
})

$form.Controls.Add($btnStart)

$form.Add_FormClosing({
    $logTimer.Stop()
    if ($script:psInstance) {
        try {
            $script:psInstance.Stop()
            $script:psInstance.Dispose()
        } catch {}
    }
})

[void]$form.ShowDialog()
