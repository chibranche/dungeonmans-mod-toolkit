@echo off
rem Stops ModKit (and every mod) from loading. Your mods and their settings stay on disk.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ModKit\Install-ModKit.ps1" -Uninstall
pause
