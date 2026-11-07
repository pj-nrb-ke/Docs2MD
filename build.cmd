@echo off
rem Build script for Docs2MD.
rem   build.cmd           -- builds desktop EXE (default)
rem   build.cmd web       -- builds Blazor web app into dist-web\
rem   build.cmd all       -- builds both
rem   build.cmd installer -- builds desktop EXE then the Inno Setup installer (dist\Docs2MD-Setup.exe)
setlocal
cd /d "%~dp0"

set TARGET=%1
if "%TARGET%"=="" set TARGET=desktop

if /i "%TARGET%"=="all" (
    call :desktop
    if errorlevel 1 goto :err
    call :web
    if errorlevel 1 goto :err
    goto :done
)

if /i "%TARGET%"=="web" (
    call :web
    if errorlevel 1 goto :err
    goto :done
)

if /i "%TARGET%"=="installer" (
    call :installer
    if errorlevel 1 goto :err
    goto :done
)

call :desktop
if errorlevel 1 goto :err
goto :done

rem ── Desktop build ────────────────────────────────────────────────────
:desktop
echo [1/3] Fetching OCR language data...
powershell -NoProfile -ExecutionPolicy Bypass -File get-tessdata.ps1
if errorlevel 1 exit /b 1

echo [2/3] Publishing single-file exe...
dotnet publish Docs2MD.Desktop\Docs2MD.Desktop.csproj -c Release -r win-x64 ^
  --self-contained false ^
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist
if errorlevel 1 exit /b 1

echo [3/3] Copying tessdata next to exe...
xcopy /e /i /y tessdata dist\tessdata >nul
exit /b 0

rem ── Web build ────────────────────────────────────────────────────────
:web
echo [1/2] Fetching OCR language data...
powershell -NoProfile -ExecutionPolicy Bypass -File get-tessdata.ps1
if errorlevel 1 exit /b 1

echo [2/2] Publishing Blazor web app...
dotnet publish Docs2MD.Web\Docs2MD.Web.csproj -c Release -o dist-web
if errorlevel 1 exit /b 1
exit /b 0

rem ── Installer build ──────────────────────────────────────────────────
:installer
call :desktop
if errorlevel 1 exit /b 1
echo [*] Building Inno Setup installer...
set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo Inno Setup 6 not found. Install it:  winget install -e --id JRSoftware.InnoSetup
    exit /b 1
)
"%ISCC%" installer\Docs2MD.iss
exit /b %errorlevel%

:done
echo.
if /i "%TARGET%"=="web" echo Done. Deploy dist-web\ to your server (Azure App Service, etc.)
if /i "%TARGET%"=="desktop" echo Done. Double-click: %~dp0dist\Docs2MD.exe
if /i "%TARGET%"=="installer" echo Done. Installer: %~dp0dist\Docs2MD-Setup.exe
if /i "%TARGET%"=="all" echo Done. dist\ = Desktop exe   dist-web\ = Web app
echo.
pause
exit /b 0

:err
echo.
echo BUILD FAILED - see message above. (Is the .NET 8 SDK installed?)
pause
exit /b 1
