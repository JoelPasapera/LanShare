[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Test-IsAdmin {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

$esAdmin = Test-IsAdmin

# --- CONFIGURACIÓN DE LA INTERFAZ ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Servidor Web Pro - PowerShell (SPA & Multi-Thread)"
$form.Size = New-Object System.Drawing.Size(540, 320)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

# 1. Indicador de Permisos
$labelPermiso = New-Object System.Windows.Forms.Label
$labelPermiso.Location = New-Object System.Drawing.Point(20, 15)
$labelPermiso.Size = New-Object System.Drawing.Size(320, 20)
$labelPermiso.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)

if ($esAdmin) {
    $labelPermiso.Text = "Permisos: Administrador (Elevado)"
    $labelPermiso.ForeColor = [System.Drawing.Color]::DarkGreen
} else {
    $labelPermiso.Text = "Permisos: Usuario Estándar (Sin Administrador)"
    $labelPermiso.ForeColor = [System.Drawing.Color]::DarkRed
}
$form.Controls.Add($labelPermiso)

$btnEscalar = New-Object System.Windows.Forms.Button
$btnEscalar.Location = New-Object System.Drawing.Point(350, 10)
$btnEscalar.Size = New-Object System.Drawing.Size(150, 25)
$btnEscalar.Text = "Escalar Privilegios"
$btnEscalar.Enabled = -not $esAdmin
$btnEscalar.Add_Click({
    if ($PSCommandPath) {
        Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
        $form.Close()
    } else {
        [System.Windows.Forms.MessageBox]::Show("Guarda el código en un archivo .ps1 para reabrirlo como Administrador.", "Aviso", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
    }
})
$form.Controls.Add($btnEscalar)

$line = New-Object System.Windows.Forms.Label
$line.Location = New-Object System.Drawing.Point(20, 40)
$line.Size = New-Object System.Drawing.Size(480, 2)
$line.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$form.Controls.Add($line)

# 2. Selector de Ruta
$labelRuta = New-Object System.Windows.Forms.Label
$labelRuta.Location = New-Object System.Drawing.Point(20, 55)
$labelRuta.Size = New-Object System.Drawing.Size(480, 20)
$labelRuta.Text = "Carpeta raíz de la aplicación web:"
$form.Controls.Add($labelRuta)

$txtRuta = New-Object System.Windows.Forms.TextBox
$txtRuta.Location = New-Object System.Drawing.Point(20, 78)
$txtRuta.Size = New-Object System.Drawing.Size(370, 23)
$txtRuta.Text = "C:\"
$form.Controls.Add($txtRuta)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(400, 76)
$btnBrowse.Size = New-Object System.Drawing.Size(100, 27)
$btnBrowse.Text = "Examinar..."
$btnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.SelectedPath = $txtRuta.Text
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $txtRuta.Text = $dialog.SelectedPath
    }
})
$form.Controls.Add($btnBrowse)

# 3. Puerto TCP
$labelPuerto = New-Object System.Windows.Forms.Label
$labelPuerto.Location = New-Object System.Drawing.Point(20, 118)
$labelPuerto.Size = New-Object System.Drawing.Size(80, 20)
$labelPuerto.Text = "Puerto TCP:"
$form.Controls.Add($labelPuerto)

$txtPuerto = New-Object System.Windows.Forms.TextBox
$txtPuerto.Location = New-Object System.Drawing.Point(100, 115)
$txtPuerto.Size = New-Object System.Drawing.Size(80, 23)
$txtPuerto.Text = "8080"
$form.Controls.Add($txtPuerto)

# 4. Estado
$labelEstado = New-Object System.Windows.Forms.Label
$labelEstado.Location = New-Object System.Drawing.Point(20, 150)
$labelEstado.Size = New-Object System.Drawing.Size(480, 20)
$labelEstado.Text = "Estado: Detenido"
$labelEstado.ForeColor = [System.Drawing.Color]::Red
$labelEstado.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($labelEstado)

# 5. Botón Iniciar / Detener
$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 185)
$btnStart.Size = New-Object System.Drawing.Size(480, 45)
$btnStart.Text = "Iniciar Servidor y Abrir Puerto"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)

# --- ARQUITECTURA MULTIHILO CON SPA FALLBACK ---
$script:listener = $null
$script:worker = New-Object System.ComponentModel.BackgroundWorker
$script:worker.WorkerSupportsCancellation = $true

$script:worker.Add_DoWork({
    param($sender, $e)
    $listener = $e.Argument.Listener
    $basePath = $e.Argument.BasePath

    while ($listener.IsListening -and -not $sender.CancellationPending) {
        try {
            $context = $listener.GetContext()

            [System.Threading.ThreadPool]::QueueUserWorkItem({
                param($state)
                $ctx = $state.Context
                $root = $state.BasePath
                
                try {
                    $request = $ctx.Request
                    $response = $ctx.Response

                    $decodedPath = [System.Uri]::UnescapeDataString($request.Url.LocalPath).TrimStart('/')
                    if ([string]::IsNullOrEmpty($decodedPath)) { $decodedPath = "index.html" }

                    $candidatePath = Join-Path $root $decodedPath
                    $fullPath = [System.IO.Path]::GetFullPath($candidatePath)

                    $fileToServe = $null

                    # 1. Verificar si el archivo físico existe y respeta la raíz
                    if ($fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path $fullPath -PathType Leaf)) {
                        $fileToServe = $fullPath
                    } 
                    # 2. Lógica SPA Fallback: Si no existe el archivo, no tiene extensión de recurso y existe index.html
                    else {
                        $requestedExtension = [System.IO.Path]::GetExtension($decodedPath)
                        $indexPath = Join-Path $root "index.html"

                        if ([string]::IsNullOrEmpty($requestedExtension) -and (Test-Path $indexPath -PathType Leaf)) {
                            $fileToServe = $indexPath
                        }
                    }

                    # Servir archivo
                    if ($fileToServe) {
                        $ext = [System.IO.Path]::GetExtension($fileToServe).ToLower()
                        switch ($ext) {
                            ".html"  { $response.ContentType = "text/html; charset=utf-8" }
                            ".css"   { $response.ContentType = "text/css" }
                            ".js"    { $response.ContentType = "application/javascript" }
                            ".json"  { $response.ContentType = "application/json" }
                            ".png"   { $response.ContentType = "image/png" }
                            ".jpg"   { $response.ContentType = "image/jpeg" }
                            ".svg"   { $response.ContentType = "image/svg+xml" }
                            ".webp"  { $response.ContentType = "image/webp" }
                            ".woff2" { $response.ContentType = "font/woff2" }
                            default  { $response.ContentType = "application/octet-stream" }
                        }

                        $bytes = [System.IO.File]::ReadAllBytes($fileToServe)
                        $response.ContentLength64 = $bytes.Length
                        $response.OutputStream.Write($bytes, 0, $bytes.Length)
                    } else {
                        $response.StatusCode = 404
                    }
                } catch {
                    try { $ctx.Response.StatusCode = 500 } catch {}
                } finally {
                    try { $ctx.Response.Close() } catch {}
                }
            }, @{ Context = $context; BasePath = $basePath }) | Out-Null

        } catch [System.Net.HttpListenerException], [System.ObjectDisposedException] {
            break
        } catch {
            # Errores generales
        }
    }
})

$btnStart.Add_Click({
    if ($script:listener -and $script:listener.IsListening) {
        $script:worker.CancelAsync()
        if ($script:listener) {
            $script:listener.Stop()
            $script:listener.Close()
            $script:listener = $null
        }
        $labelEstado.Text = "Estado: Detenido"
        $labelEstado.ForeColor = [System.Drawing.Color]::Red
        $btnStart.Text = "Iniciar Servidor y Abrir Puerto"
        $btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
        return
    }

    $rutaInput = $txtRuta.Text.Trim('"').Trim("'")
    $puerto = $txtPuerto.Text.Trim()

    if (-not (Test-Path $rutaInput -PathType Container)) {
        [System.Windows.Forms.MessageBox]::Show("La ruta especificada no existe.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $basePath = [System.IO.Path]::GetFullPath($rutaInput)
    if (-not $basePath.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString())) {
        $basePath += [System.IO.Path]::DirectorySeparatorChar
    }

    if (Test-IsAdmin) {
        $ruleName = "Permitir Puerto HTTP $puerto"
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $puerto -Action Allow -Enabled True | Out-Null
        }
    }

    try {
        $script:listener = New-Object System.Net.HttpListener
        $script:listener.Prefixes.Add("http://localhost:$puerto/")
        $script:listener.Start()
    } catch {
        [System.Windows.Forms.MessageBox]::Show("No se pudo iniciar el servidor en el puerto $puerto.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $args = @{ Listener = $script:listener; BasePath = $basePath }
    $script:worker.RunWorkerAsync($args)

    $labelEstado.Text = "Estado: Corriendo en http://localhost:$puerto/"
    $labelEstado.ForeColor = [System.Drawing.Color]::Green
    $btnStart.Text = "Detener Servidor"
    $btnStart.BackColor = [System.Drawing.Color]::FromArgb(220, 53, 69)

    Start-Process "http://localhost:$puerto/"
})

$form.Controls.Add($btnStart)

$form.Add_FormClosing({
    if ($script:worker.IsBusy) {
        $script:worker.CancelAsync()
    }
    if ($script:listener -and $script:listener.IsListening) {
        $script:listener.Stop()
        $script:listener.Close()
    }
})

[void]$form.ShowDialog()
