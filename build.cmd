@echo off
rem One-click build: produces dist\Docs2MD.exe (single file) + tessdata folder.
setlocal
cd /d "%~dp0"

echo [1/3] Fetching OCR language data...
powershell -NoProfile -ExecutionPolicy Bypass -File get-tessdata.ps1
if errorlevel 1 goto :err

echo [2/3] Publishing single-file exe...
dotnet publish -c Release -r win-x64 --self-contained false ^
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist
if errorlevel 1 goto :err

echo [3/3] Copying tessdata next to exe...
xcopy /e /i /y tessdata dist\tessdata >nul

echo.
echo Done. Double-click: %~dp0dist\Docs2MD.exe
pause
exit /b 0

:err
echo.
echo BUILD FAILED - see message above. (Is the .NET 8 SDK installed?)
pause
exit /b 1
