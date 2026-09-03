@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MateViewFix.ps1" Uninstall %*
if errorlevel 1 pause
