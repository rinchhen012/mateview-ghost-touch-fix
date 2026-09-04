@echo off
setlocal
set "TARGET=%LOCALAPPDATA%\MateViewGuardian"
set "STARTUP=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\MateViewGuardian.cmd"

if exist "%TARGET%\MateViewGuardian.App.exe" (
  "%TARGET%\MateViewGuardian.App.exe" --restore-and-exit
  if errorlevel 1 (
    echo Restore failed. MateView Guardian was left installed so it can be retried.
    exit /b 1
  )
)
taskkill /F /IM MateViewGuardian.App.exe >nul 2>&1
if exist "%STARTUP%" del /F /Q "%STARTUP%"
start "" /B cmd.exe /D /C "timeout /T 2 /NOBREAK >nul & rmdir /S /Q "%TARGET%""
echo MateView Guardian will be removed. The MateView touch strip was restored first.
