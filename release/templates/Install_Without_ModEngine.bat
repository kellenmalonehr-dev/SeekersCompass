@echo off
:: Puts Seeker's Compass into the ELDEN RING\Game folder, for playing without Mod Engine
:: (Seamless Co-op's own launcher, or the game started offline). Run again after updating.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\SeekersCompass-gamefolder.ps1" -Action install %*
pause
