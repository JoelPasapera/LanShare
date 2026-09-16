@echo off
setlocal enabledelayedexpansion

rem ---------------------------------------------------------------------------
rem  Compila y ejecuta la bateria de pruebas de LanShare.
rem
rem  Compila los fuentes de la aplicacion JUNTO a los de las pruebas, en vez de
rem  referenciar el .exe ya construido. Asi se pueden probar tambien los tipos
rem  internos (DnsWire, NativePath) sin abrirlos al exterior.
rem
rem  Se excluye src\Program.cs porque tiene su propio Main.
rem ---------------------------------------------------------------------------

cd /d "%~dp0"

echo ===========================================================
echo   LanShare - compilacion de pruebas
echo ===========================================================
echo.

if not exist "..\src\Program.cs" (
    echo [ERROR] No se encuentra ..\src. Ejecuta este script desde la carpeta
    echo         tests del proyecto ya extraido.
    echo.
    goto :fin
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] No se encontro csc.exe de .NET Framework 4.x
    echo.
    goto :fin
)

set "RSP=%TEMP%\lanshare_tests_%RANDOM%.rsp"
if exist "%RSP%" del "%RSP%"

set /a APP=0
for /r "..\src" %%f in (*.cs) do (
    set "F=%%f"
    if "!F:\bin\=!"=="!F!" if "!F:\obj\=!"=="!F!" if /i not "%%~nxf"=="Program.cs" (
        echo "%%f">>"%RSP%"
        set /a APP+=1
    )
)

set /a PRUEBAS=0
for /r "." %%f in (*.cs) do (
    set "F=%%f"
    if "!F:\bin\=!"=="!F!" if "!F:\obj\=!"=="!F!" (
        echo "%%f">>"%RSP%"
        set /a PRUEBAS+=1
    )
)

echo   Fuentes de la aplicacion : %APP%
echo   Fuentes de las pruebas   : %PRUEBAS%
echo.

if %PRUEBAS% LSS 10 (
    echo [ERROR] Faltan archivos de prueba. Vuelve a extraer el proyecto.
    if exist "%RSP%" del "%RSP%"
    goto :fin
)

if not exist "bin" mkdir "bin"
set "LOG=bin\build-tests.log"

echo Compilando...
echo.

"%CSC%" /nologo ^
    /target:exe ^
    /out:"bin\LanShare.Tests.exe" ^
    /platform:anycpu ^
    /optimize+ ^
    /warn:3 ^
    /debug- ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Web.Extensions.dll ^
    @"%RSP%" > "%LOG%" 2>&1

set "RESULT=%ERRORLEVEL%"
if exist "%RSP%" del "%RSP%"

type "%LOG%"
echo.

if not "%RESULT%"=="0" (
    call :detallar "error" "ERRORES DE COMPILACION"
    echo ===========================================================
    echo   [ERROR] Las pruebas no compilan.
    echo   Salida completa en: %LOG%
    echo ===========================================================
    echo.
    goto :fin
)

echo ===========================================================
echo   Ejecutando
echo ===========================================================

"bin\LanShare.Tests.exe"
set "TESTS=%ERRORLEVEL%"

if not "%TESTS%"=="0" (
    echo.
    echo   [ERROR] Hay pruebas que fallan.
    echo.
) else (
    echo   [OK] Bateria completa superada.
    echo.
)

goto :fin

rem ---------------------------------------------------------------------------
:detallar
echo -----------------------------------------------------------
echo   %~2
echo -----------------------------------------------------------
for /f "usebackq tokens=1* delims=(" %%a in (`findstr /c:": %~1 CS" "%LOG%" 2^>nul`) do (
    call :mostrar_uno "%%a" "%%b"
)
echo.
goto :eof

:mostrar_uno
set "ARCHIVO=%~1"
set "RESTO=%~2"
for /f "tokens=1 delims=," %%n in ("%RESTO%") do set "LINEA=%%n"
for /f "tokens=1* delims=:" %%x in ("%RESTO%") do set "MENSAJE=%%y"

echo.
echo   %ARCHIVO% : linea %LINEA%
echo   %MENSAJE%

if not exist "%ARCHIVO%" goto :eof
for /f "usebackq tokens=1* delims=:" %%p in (`findstr /n "^^" "%ARCHIVO%" ^| findstr /b /c:"%LINEA%:"`) do (
    set "CODIGO=%%q"
    setlocal enabledelayedexpansion
    echo      ^|  !CODIGO!
    endlocal
)
goto :eof

:fin
echo.
pause
endlocal
