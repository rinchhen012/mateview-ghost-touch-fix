@echo off
setlocal
set "SOURCE=%~dp0"
set "TARGET=%LOCALAPPDATA%\MateViewGuardian"

if not exist "%SOURCE%MateViewGuardian.App.exe" (
  echo MateViewGuardian.App.exe is missing from this package.
  exit /b 1
)

if not exist "%TARGET%" mkdir "%TARGET%"
xcopy "%SOURCE%*" "%TARGET%\" /E /I /Y /Q >nul
if errorlevel 1 exit /b 1
start "" "%TARGET%\MateViewGuardian.App.exe"
echo MateView Guardian was installed and opened.
