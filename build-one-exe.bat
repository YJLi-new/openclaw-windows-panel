@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "PROJECT_DIR=%ROOT_DIR%\src\OpenClawControlPanel"
set "OUT=%ROOT_DIR%\openclaw-control-panel.exe"
set "ICO=%PROJECT_DIR%\openclaw-control-panel.ico"
set "RSP=%TEMP%\openclaw-control-panel-sources.rsp"

if not exist "%CSC%" (
  echo [ERROR] C# compiler not found: %CSC%
  exit /b 1
)

if not exist "%PROJECT_DIR%" (
  echo [ERROR] Project directory not found: %PROJECT_DIR%
  exit /b 1
)

if exist "%RSP%" del /f /q "%RSP%" >nul 2>nul
for /r "%PROJECT_DIR%" %%F in (*.cs) do (
  >>"%RSP%" echo "%%F"
)

if not exist "%RSP%" (
  echo [ERROR] No C# source files found under: %PROJECT_DIR%
  exit /b 1
)

if exist "%ICO%" (
  "%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%" /win32icon:"%ICO%" @"%RSP%" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll
) else (
  "%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%" @"%RSP%" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll
)

set "BUILD_EXIT=%ERRORLEVEL%"
if exist "%RSP%" del /f /q "%RSP%" >nul 2>nul

if not "%BUILD_EXIT%"=="0" (
  echo [ERROR] Build failed.
  exit /b %BUILD_EXIT%
)

echo [OK] Built single EXE: %OUT%
exit /b 0
