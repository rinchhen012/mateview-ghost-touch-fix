@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MateViewFix.ps1" Install %*
if errorlevel 1 pause
