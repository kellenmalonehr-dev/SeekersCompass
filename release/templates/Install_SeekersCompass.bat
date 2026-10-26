@echo off
:: Adds Seeker's Compass to The Convergence's launch profiles. Run once (and again after a
:: Convergence update). Then launch Convergence the usual way.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-setup.ps1" -Action install
pause
