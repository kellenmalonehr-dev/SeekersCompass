@echo off
:: Takes Seeker's Compass back out of the ELDEN RING\Game folder. Saves are untouched.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-gamefolder.ps1" -Action uninstall %*
pause
