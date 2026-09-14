@echo off
setlocal enabledelayedexpansion

rem ---------------------------------------------------------------------------
rem  Compila Servidor Web Pro en un unico .exe, sin instalar nada.
rem
rem  Usa el csc.exe que .NET Framework 4.8 ya trae en todos los Windows 10/11.
rem  No hace falta Visual Studio ni SDK. Resultado: bin\ServidorWebPro.exe
rem ---------------------------------------------------------------------------

rem Anclar el directorio al del propio script. Sin esto, al hacer doble clic el
rem directorio de trabajo puede ser otro y no se encontraria ningun archivo.
cd /d "%~dp0"

echo ===========================================================
echo   Servidor Web Pro - compilacion
echo ===========================================================
echo.
echo Carpeta: %CD%
echo.

rem --- Comprobar que estamos en el proyecto y no dentro del ZIP ---
if not exist "Program.cs" (
    echo [ERROR] No se encuentra Program.cs en esta carpeta.
    echo.
    echo         Causa mas probable: estas ejecutando el script DENTRO del ZIP.
    echo         Windows deja abrir archivos sin descomprimir, pero el resto del
    echo         proyecto no esta disponible.
    echo.
    echo         Solucion: clic derecho en el ZIP - "Extraer todo", y ejecuta
    echo         build.cmd desde la carpeta ya extraida.
    echo.
    goto :fin
)

rem --- Localizar el compilador ---
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] No se encontro csc.exe de .NET Framework 4.x
    echo         Buscado en: %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\
    echo         Comprueba que .NET Framework 4.8 esta instalado.
    echo.
    goto :fin
)
echo Compilador: %CSC%

rem --- Recopilar fuentes en un archivo de respuesta ---
rem  Se usa un .rsp en vez de pasar 30 rutas por linea de comandos: evita el
rem  limite de longitud y los problemas con espacios en las rutas.
set "RSP=%TEMP%\swp_sources_%RANDOM%.rsp"
if exist "%RSP%" del "%RSP%"

set /a COUNT=0
for /r %%f in (*.cs) do (
    set "F=%%f"
    rem Excluir bin y obj sin lanzar un proceso por archivo
    if "!F:\bin\=!"=="!F!" if "!F:\obj\=!"=="!F!" (
        echo "%%f">>"%RSP%"
        set /a COUNT+=1
    )
)

echo Archivos fuente: %COUNT%
echo.

if %COUNT% LSS 20 (
    echo [ERROR] Se esperaban unos 30 archivos .cs y solo se encontraron %COUNT%.
    echo         El proyecto parece incompleto. Vuelve a extraer el ZIP entero.
    echo.
    if exist "%RSP%" del "%RSP%"
    goto :fin
)

if not exist "icono.ico" (
    echo [AVISO] No se encuentra icono.ico: el .exe saldra sin icono.
    echo.
)

if not exist "bin" mkdir "bin"
set "LOG=bin\build.log"

echo Compilando...
echo.

"%CSC%" /nologo ^
    /target:winexe ^
    /out:"bin\ServidorWebPro.exe" ^
    /platform:anycpu ^
    /optimize+ ^
    /warn:3 ^
    /debug- ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    /win32icon:"icono.ico" ^
    @"%RSP%" > "%LOG%" 2>&1

set "RESULT=%ERRORLEVEL%"
if exist "%RSP%" del "%RSP%"

rem Mostrar la salida del compilador en pantalla ademas de guardarla
type "%LOG%"
echo.

if not "%RESULT%"=="0" (
    echo ===========================================================
    echo   [ERROR] La compilacion fallo.
    echo   Los mensajes de arriba estan guardados en: %LOG%
    echo ===========================================================
    echo.
    goto :fin
)

if not exist "bin\ServidorWebPro.exe" (
    echo [ERROR] El compilador termino sin error pero no genero el .exe
    echo.
    goto :fin
)

rem Un .pdb de una compilacion anterior no sirve de nada al lado del .exe
if exist "bin\ServidorWebPro.pdb" del "bin\ServidorWebPro.pdb"

echo ===========================================================
echo   [OK] Compilacion correcta
echo ===========================================================
echo.
for %%s in ("bin\ServidorWebPro.exe") do echo   Archivo : %%~fs
for %%s in ("bin\ServidorWebPro.exe") do echo   Tamano  : %%~zs bytes
for %%s in ("icono.ico") do echo   De eso, icono incrustado: %%~zs bytes
echo.
echo   Ese .exe es autonomo: copialo donde quieras y doble clic.
echo   Para el modo LAN, clic derecho - Ejecutar como administrador.
echo.

choice /c SN /n /m "Abrir la carpeta bin ahora? (S/N): "
if errorlevel 2 goto :fin
start "" explorer.exe "%CD%\bin"

:fin
echo.
pause
endlocal
