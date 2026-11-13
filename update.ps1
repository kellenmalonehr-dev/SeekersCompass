# Rebuilds everything after a game update (or any change): the generator, the mod files for
# vanilla and Convergence, the DLL, the loader, and the release zips.
#   powershell -ExecutionPolicy Bypass -File update.ps1 [-Version 2.0.0] [-SkipLoader]
#
# Before running, after a game update:
#   1. Unpack the new game files with UXM (map, event, msg, menu, script, regulation.bin).
#   2. If the game's version changed, move the fromsoftware-rs pin in
#      src\SeekersCompass.Dll\Cargo.toml (both lines) to a commit that supports it, and
#      TESTED_GAME in src\SeekersCompass.Dll\src\lib.rs to the new version.
# After running:
#   3. Install release\dist\SeekersCompass-<version>.zip (Install_Without_ModEngine.bat), start
#      the game once, and read Game\OfflineMods\SeekersCompass\seekers_compass.log: its first
#      lines give the game version and say which features are on, and why any is off.
#   4. In game: the map marks, the grace menu (Seeker's Compass, Loadouts, Camp), equipping a
#      loadout, resting for the buff, a merchant's bell bearing, a plant picked up in passing,
#      and one hub room. Solo, then in Seamless Co-op.
param(
    [string]$Version = "2.0.0",
    [switch]$SkipLoader
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$bash = (Get-Command bash -ErrorAction SilentlyContinue).Source
if (-not $bash) { $bash = "C:\Program Files\Git\bin\bash.exe" }
if (-not (Test-Path $bash)) { throw "bash (Git for Windows) is needed for the DLL build scripts." }
function Step($name, [scriptblock]$run) {
    Write-Host "`n=== $name" -ForegroundColor Cyan
    & $run
    if ($LASTEXITCODE) { throw "$name failed" }
}

Step "DLL (src\SeekersCompass.Dll)" { & $bash (Join-Path $root "src\SeekersCompass.Dll\build.sh") | Select-Object -Last 4 }
if (-not $SkipLoader) {
    Step "Loader (src\SeekersCompass.Loader)" { & $bash (Join-Path $root "src\SeekersCompass.Loader\build.sh") | Select-Object -Last 6 }
}
# Builds the generator, then the vanilla and Convergence mod files, and zips them with the DLL.
Step "Mod files and release zips" { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "release\package.ps1") -Version $Version }
Step "Check of the vanilla build" {
    & dotnet (Join-Path $root "src\BuildCompass.Gen\bin\Release\net9.0\BuildCompass.Gen.dll") verify "release\staging\vanilla\SeekersCompass" | Select-Object -Last 5
}
Write-Host "`nDone. Zips are in release\dist. Next: steps 3 and 4 at the top of this script." -ForegroundColor Green
