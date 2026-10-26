@echo off
:: Removes Seeker's Compass from The Convergence's launch profiles.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-setup.ps1" -Action uninstall
pause
