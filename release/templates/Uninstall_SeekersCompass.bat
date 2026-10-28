@echo off
:: Removes Seeker's Compass from every profile Install_SeekersCompass.bat added it to
:: (or from the .me3 files dragged onto this file).
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-setup.ps1" -Action uninstall %*
pause
