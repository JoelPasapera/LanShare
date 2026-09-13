Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# --- CONFIGURACIÓN DE LA VENTANA (GUI) ---
$form = New-Object System.Windows.Forms.Form
$form.Text = "Servidor Web PowerShell (Puerto 8080)"
$form.Size = New-Object System.Drawing.Size(520, 240)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

# Etiqueta de instrucción
$label = New-Object System.Windows.Forms.Label
$label.Location = New-Object System.Drawing.Point(20, 20)
$label.Size = New-Object System.Drawing.Size(460, 20)
$label.Text = "Selecciona la carpeta raíz de tu sitio web (con index.html):"
$form.Controls.Add($label)

# Campo de texto para la ruta (Ahora predeterminado en C:\)
$textBox = New-Object System.Windows.Forms.TextBox
$textBox.Location = New-Object System.Drawing.Point(20, 50)
$textBox.Size = New-Object System.Drawing.Size(360, 20)
$textBox.Text = "C:\"
$form.Controls.Add($textBox)

# Botón Buscar / Examinar
$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Location = New-Object System.Drawing.Point(390, 48)
$btnBrowse.Size = New-Object System.Drawing.Size(95, 25)
$btnBrowse.Text = "Examinar..."
$btnBrowse.Add_Click({
    $folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderBrowser.SelectedPath = $textBox.Text
    if ($folderBrowser.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $textBox.Text = $folderBrowser.SelectedPath
    }
})
$form.Controls.Add($btnBrowse)

# Botón Iniciar Servidor
$btnStart = New-Object System.Windows.Forms.Button
$btnStart.Location = New-Object System.Drawing.Point(20, 100)
$btnStart.Size = New-Object System.Drawing.Size(465, 40)
$btnStart.Text = "Iniciar Servidor en Puerto 8080"
$btnStart.BackColor = [System.Drawing.Color]::FromArgb(40, 167, 69)
$btnStart.ForeColor = [System.Drawing.Color]::White
$btnStart.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)

$btnStart.Add_Click({
    $ruta = $textBox.Text.Trim('"').Trim("'")
    
    if (-not (Test-Path $ruta -PathType Container)) {
        [System.Windows.Forms.MessageBox]::Show("La carpeta especificada no existe.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $form.Hide() # Ocultar ventana emergente
    
    # --- LÓGICA DEL SERVIDOR HTTP ---
    $puerto = 8080
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://localhost:$puerto/")
    $listener.Start()

    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host " Servidor Web Activo en http://localhost:$puerto/" -ForegroundColor Green
    Write-Host " Carpeta origen: $ruta" -ForegroundColor White
    Write-Host " Presiona Ctrl + C para detenerlo." -ForegroundColor Yellow
    Write-Host "==========================================`n" -ForegroundColor Cyan

    Start-Process "http://localhost:$puerto/"

    try {
        while ($listener.IsListening) {
            $context = $listener.GetContext()
            $request = $context.Request
            $response = $context.Response

            $relPath = $request.Url.LocalPath.TrimStart('/')
            if ([string]::IsNullOrEmpty($relPath)) { $relPath = "index.html" }

            $filePath = Join-Path $ruta $relPath

            if (Test-Path $filePath -PathType Leaf) {
                $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
                switch ($ext) {
                    ".html" { $response.ContentType = "text/html; charset=utf-8" }
                    ".css"  { $response.ContentType = "text/css" }
                    ".js"   { $response.ContentType = "application/javascript" }
                    ".png"  { $response.ContentType = "image/png" }
                    ".jpg"  { $response.ContentType = "image/jpeg" }
                    ".svg"  { $response.ContentType = "image/svg+xml" }
                    ".json" { $response.ContentType = "application/json" }
                    default { $response.ContentType = "application/octet-stream" }
                }

                $bytes = [System.IO.File]::ReadAllBytes($filePath)
                $response.ContentLength64 = $bytes.Length
                $response.OutputStream.Write($bytes, 0, $bytes.Length)
            } else {
                $response.StatusCode = 404
            }
            $response.Close()
        }
    } finally {
        $listener.Stop()
        $form.Close()
    }
})
$form.Controls.Add($btnStart)

# Mostrar la interfaz visual
[void]$form.ShowDialog()
