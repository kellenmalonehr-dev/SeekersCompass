@echo off
:: Adds Seeker's Compass to Mod Engine 3 profiles, so you keep launching the way you already do.
:: In a ConvergenceER folder it uses Convergence's own profiles. Anywhere else it asks which
:: profile(s) to use; you can also drag .me3 files onto this file. Run again after a mod update.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-setup.ps1" -Action install %*
pause
