@echo off
setlocal

REM Windows launcher for watch.ps1
REM Usage: watch.cmd [dotnet watch args]

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0watch.ps1" %*
exit /b %ERRORLEVEL%
