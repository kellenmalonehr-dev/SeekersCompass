# Puts Seeker's Compass into the ELDEN RING\Game folder (or takes it out again), for playing
# without Mod Engine: Seamless Co-op's own launcher, Steam with an anti-cheat toggler, or
# eldenring.exe directly. Nothing of the game's own is changed or replaced.
#   SeekersCompass-gamefolder.ps1 -Action install|uninstall [-GameDir <...\ELDEN RING\Game>]
# Installs:
#   Game\xinput1_4.dll                          small proxy the game loads at startup; passes
#                                               controller calls to Windows' own xinput1_4.dll
#   Game\SeekersCompass\                        the mod's files
#   Game\SeekersCompass\seekers_compass_loader.dll
#                                               swaps them in (Mod Engine 3's own code), unless
#                                               Easy Anti-Cheat is running or Mod Engine started the game
param(
    [ValidateSet("install", "uninstall")][string]$Action = "install",
    [string]$GameDir
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$marker = ".installed-by-seekers-compass"

function Find-GameDir {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libs = @($steam.Replace('/', '\'))
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value.Replace('\\', '\') }
    }
    foreach ($lib in $libs | Select-Object -Unique) {
        $manifest = Join-Path $lib "steamapps\appmanifest_1245620.acf"   # Elden Ring's Steam app id
        if (-not (Test-Path $manifest)) { continue }
        $dir = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
        $game = Join-Path $lib "steamapps\common\$dir\Game"
        if (Test-Path (Join-Path $game "eldenring.exe")) { return $game }
    }
    return $null
}

# Our proxy names its loader; any other xinput1_4.dll belongs to something else.
function Test-OurProxy([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($path)
    # The name is stored as UTF-16; read the file at both byte alignments.
    foreach ($start in 0, 1) {
        if ([Text.Encoding]::Unicode.GetString($bytes, $start, $bytes.Length - $start) -match "seekers_compass_loader\.dll") { return $true }
    }
    $false
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "eldenring.exe"))) {
    Write-Host "Elden Ring's Game folder was not found through Steam."
    Write-Host "Run this again with the folder, e.g.:  Install_Without_ModEngine.bat -GameDir `"D:\SteamLibrary\steamapps\common\ELDEN RING\Game`""
    exit 1
}
if (Get-Process eldenring -ErrorAction SilentlyContinue) { Write-Host "Close Elden Ring first."; exit 1 }

$proxy = Join-Path $GameDir "xinput1_4.dll"
$target = Join-Path $GameDir "SeekersCompass"
Write-Host "Elden Ring: $GameDir"

if ($Action -eq "uninstall") {
    if (Test-OurProxy $proxy) { Remove-Item $proxy; Write-Host "Removed xinput1_4.dll" }
    elseif (Test-Path $proxy) { Write-Host "Left xinput1_4.dll alone: it isn't Seeker's Compass's." }
    if (Test-Path (Join-Path $target $marker)) { Remove-Item $target -Recurse -Force; Write-Host "Removed the SeekersCompass folder" }
    elseif (Test-Path $target) { Write-Host "Left the SeekersCompass folder alone: this installer didn't create it." }
    Write-Host "`nDone. Your saves (including the mod's ER0000.skc) are untouched."
    exit 0
}

$package = Join-Path $root "SeekersCompass"
$files = Join-Path $root "GameFolder"
if (-not (Test-Path (Join-Path $package "regulation.bin")) -or -not (Test-Path (Join-Path $files "xinput1_4.dll"))) {
    Write-Host "Extract the whole zip first: the SeekersCompass and GameFolder folders must be next to this script."
    exit 1
}
if ((Test-Path $proxy) -and -not (Test-OurProxy $proxy)) {
    Write-Host "Another mod already uses Game\xinput1_4.dll, so Seeker's Compass can't be installed this way."
    Write-Host "Use Launch_SeekersCompass.bat or Install_SeekersCompass.bat (Mod Engine 3) instead."
    exit 1
}
if ((Test-Path $target) -and -not (Test-Path (Join-Path $target $marker))) {
    Write-Host "Game\SeekersCompass already exists and wasn't made by this installer. Move it away and run this again."
    exit 1
}

# Replace an older install completely, so files a newer version dropped don't linger.
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $package $target -Recurse
Copy-Item (Join-Path $files "seekers_compass_loader.dll") $target
Set-Content (Join-Path $target $marker) "Installed by Install_Without_ModEngine.bat; Uninstall_Without_ModEngine.bat removes this folder."
Copy-Item (Join-Path $files "xinput1_4.dll") $proxy -Force

Write-Host "`nDone. Start Elden Ring the way you normally do without Mod Engine:"
Write-Host "  - Seamless Co-op's launcher (ersc_launcher.exe): Seeker's Compass with your Seamless characters."
Write-Host "  - eldenring.exe directly, or Steam with an anti-cheat toggler: on its own save (ER0000.skc),"
Write-Host "    which starts as a copy of your characters. Your normal save is never written."
Write-Host "With Easy Anti-Cheat running, or through Mod Engine, it stays off."
Write-Host "If Easy Anti-Cheat refuses to start the normal game, run Uninstall_Without_ModEngine.bat."
