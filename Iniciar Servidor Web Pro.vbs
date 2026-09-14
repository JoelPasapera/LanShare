' Lanzador sin parpadeo para ServidorWebPro.ps1
'
' El script ya oculta su propia consola, pero entre que Windows la crea y que
' el script consigue esconderla pasa una fraccion de segundo y se ve un
' destello. Arrancando desde aqui la ventana nace oculta y no parpadea nunca.
'
' Uso: deja este archivo en la MISMA carpeta que ServidorWebPro.ps1 y haz
' doble clic. Si quieres que arranque ya como administrador, crea un acceso
' directo a este .vbs y marca "Ejecutar como administrador" en Propiedades.

Option Explicit

Dim fso, shell, carpeta, ps1, comando

Set fso   = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

carpeta = fso.GetParentFolderName(WScript.ScriptFullName)
ps1     = fso.BuildPath(carpeta, "ServidorWebPro.ps1")

If Not fso.FileExists(ps1) Then
    MsgBox "No se encontro ServidorWebPro.ps1 en:" & vbCrLf & vbCrLf & carpeta & _
           vbCrLf & vbCrLf & "Deja este lanzador en la misma carpeta que el script.", _
           vbCritical, "Servidor Web Pro"
    WScript.Quit 1
End If

comando = "powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """"

' El 0 final es lo que hace que la ventana nazca oculta.
' El False significa no esperar: el lanzador termina de inmediato.
shell.Run comando, 0, False
