# Definir puerto y prefijo
$puerto = 8080
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$puerto/")
$listener.Start()

Write-Host "Servidor web corriendo en http://localhost:$puerto/" -ForegroundColor Green
Write-Host "Presiona Ctrl + C en esta consola para detenerlo." -ForegroundColor Yellow

try {
    while ($listener.IsListening) {
        # Esperar una petición HTTP
        $context = $listener.GetContext()
        $response = $context.Response

        # Contenido HTML que enviará la página
        $html = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Servidor PowerShell</title>
</head>
<body>
    <h1>¡Hola desde el puerto 8080 en PowerShell!</h1>
    <p>Esta página está siendo servida directamente con un script de PowerShell.</p>
</body>
</html>
"@

        # Convertir texto a bytes y enviar respuesta
        $buffer = [System.Text.Encoding]::UTF8.GetBytes($html)
        $response.ContentLength64 = $buffer.Length
        $response.ContentType = "text/html; charset=utf-8"
        $response.OutputStream.Write($buffer, 0, $buffer.Length)
        $response.Close()
    }
} finally {
    $listener.Stop()
}