@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-And-Run-Windows.ps1" %*
exit /b %ERRORLEVEL%
