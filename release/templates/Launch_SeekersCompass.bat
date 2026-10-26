@echo off
:: Starts Elden Ring with Seeker's Compass through Mod Engine 3 (Steam must be running).
:: Detects Seamless Co-op automatically and never writes to your normal online save.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-launch.ps1" %*
if errorlevel 1 pause
