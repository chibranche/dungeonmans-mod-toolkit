@echo off
rem Installs (or repairs) Dungeonmans ModKit. Run again after a Steam update.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ModKit\Install-ModKit.ps1"
pause
