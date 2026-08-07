@echo off
rem Double-clickable launcher for RSPaster (script mode, no build needed).
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0RSPaster.ps1"
