# Puts Seeker's Compass into the ELDEN RING\Game folder (or takes it out again), for playing
# without Mod Engine: Seamless Co-op's own launcher, Steam with an anti-cheat toggler, or
# eldenring.exe directly. Nothing of the game's own is changed or replaced.
#   SeekersCompass-gamefolder.ps1 -Action install|uninstall [-GameDir <...\ELDEN RING\Game>]
# Installs (the folder layout is shared with Grace Loadouts, which installs the same way):
#   Game\xinput1_4.dll                          small proxy the game loads at startup; passes
#                                               controller calls to Windows' own xinput1_4.dll
#   Game\OfflineMods\offline_mods_loader.dll    swaps in the mods' files (Mod Engine 3's own code),
#                                               unless Easy Anti-Cheat is running or Mod Engine
#                                               started the game
#   Game\OfflineMods\SeekersCompass\            the mod's files
# Seeker's Compass 1.2 installed into Game\SeekersCompass instead; installing moves it.
# Grace Loadouts and Grace Items, separate mods up to 1.3, are part of it now: their installs there
# are removed and what the player saved in them is kept.
param(
    [ValidateSet("install", "uninstall")][string]$Action = "install",
    [string]$GameDir
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$marker = ".installed-by-seekers-compass"
$utf8 = New-Object Text.UTF8Encoding $false

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

# What Game\xinput1_4.dll is: "offline" (the shared loader's proxy), "seekers-1.2" (Seeker's
# Compass 1.2's own proxy), "other" (another mod's), or $null (none). The proxy names its loader.
function Get-ProxyKind([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $bytes = [IO.File]::ReadAllBytes($path)
    # The name is stored as UTF-16; read the file at both byte alignments.
    foreach ($start in 0, 1) {
        $text = [Text.Encoding]::Unicode.GetString($bytes, $start, $bytes.Length - $start)
        if ($text -match "offline_mods_loader\.dll") { return "offline" }
        if ($text -match "seekers_compass_loader\.dll") { return "seekers-1.2" }
    }
    "other"
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "eldenring.exe"))) {
    Write-Host "Elden Ring's Game folder was not found through Steam."
    Write-Host "Run this again with the folder, e.g.:  Install_Without_ModEngine.bat -GameDir `"D:\SteamLibrary\steamapps\common\ELDEN RING\Game`""
    exit 1
}
if (Get-Process eldenring -ErrorAction SilentlyContinue) { Write-Host "Close Elden Ring first."; exit 1 }

$proxy = Join-Path $GameDir "xinput1_4.dll"
$mods = Join-Path $GameDir "OfflineMods"
$target = Join-Path $mods "SeekersCompass"
$legacy = Join-Path $GameDir "SeekersCompass"
Write-Host "Elden Ring: $GameDir"

if ($Action -eq "uninstall") {
    $removed = $false
    foreach ($dir in $target, $legacy) {
        if (Test-Path (Join-Path $dir $marker)) { Remove-Item $dir -Recurse -Force; $removed = $true }
        elseif (Test-Path $dir) { Write-Host "Left $dir alone: this installer didn't create it." }
    }
    if ($removed) { Write-Host "Removed Seeker's Compass (settings, loadouts and Grace Items progress included)" }
    # The loader and proxy go once no mod uses them any more.
    $left = @(Get-ChildItem $mods -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "package.ini") })
    if (-not $left.Count) {
        foreach ($f in "offline_mods_loader.dll", "loader-version.txt", "loader.log", "launch.log") {
            $p = Join-Path $mods $f; if (Test-Path $p) { Remove-Item $p }
        }
        if ((Get-ProxyKind $proxy) -in "offline", "seekers-1.2") { Remove-Item $proxy; Write-Host "Removed xinput1_4.dll" }
        if ((Test-Path $mods) -and -not (Get-ChildItem $mods -Force)) { Remove-Item $mods }
    }
    Write-Host "`nDone. Your saves (including the mods' ER0000.skc) are untouched."
    exit 0
}

$package = Join-Path $root "SeekersCompass"
$files = Join-Path $root "GameFolder"
if (-not (Test-Path (Join-Path $package "regulation.bin")) -or -not (Test-Path (Join-Path $files "xinput1_4.dll"))) {
    Write-Host "Extract the whole zip first: the SeekersCompass and GameFolder folders must be next to this script."
    exit 1
}
$kind = Get-ProxyKind $proxy
if ($kind -eq "other") {
    Write-Host "Another mod already uses Game\xinput1_4.dll, so Seeker's Compass can't be installed this way."
    Write-Host "Use Launch_SeekersCompass.bat or Install_SeekersCompass.bat (Mod Engine 3) instead."
    exit 1
}
if ((Test-Path $target) -and -not (Test-Path (Join-Path $target $marker))) {
    Write-Host "Game\OfflineMods\SeekersCompass already exists and wasn't made by this installer. Move it away and run this again."
    exit 1
}

# The shared loader: install it, or update it if this copy is newer than the installed one.
New-Item -ItemType Directory -Force $mods | Out-Null
$ours = [int](Get-Content (Join-Path $files "loader-version.txt"))
$versionFile = Join-Path $mods "loader-version.txt"
$installed = if (Test-Path $versionFile) { [int](Get-Content $versionFile) } else { 0 }
if ($ours -gt $installed -or -not (Test-Path (Join-Path $mods "offline_mods_loader.dll")) -or $kind -ne "offline") {
    Copy-Item (Join-Path $files "offline_mods_loader.dll") $mods -Force
    Copy-Item (Join-Path $files "loader-version.txt") $mods -Force
    Copy-Item (Join-Path $files "xinput1_4.dll") $proxy -Force
}

# What the player owns in an install: settings, saved loadouts and Grace Items progress.
$keep = "seekers_compass.ini", "loadouts", "characters"
$kept = Join-Path $mods ".seekers-compass-keep"
if (Test-Path $kept) { Remove-Item $kept -Recurse -Force }
New-Item -ItemType Directory -Force $kept | Out-Null
foreach ($k in $keep) { $p = Join-Path $target $k; if (Test-Path $p) { Move-Item $p $kept } }

# Grace Loadouts and Grace Items were separate mods up to Seeker's Compass 1.3 and are part of it
# now. Their installs are taken out; what the player saved in them comes along.
foreach ($old in @{ Dir = "GraceLoadouts"; Marker = ".installed-by-grace-loadouts"; Data = "loadouts"; Name = "Grace Loadouts" },
                 @{ Dir = "GraceItems"; Marker = ".installed-by-grace-items"; Data = "characters"; Name = "Grace Items" }) {
    $dir = Join-Path $mods $old.Dir
    if (-not (Test-Path (Join-Path $dir $old.Marker))) { continue }
    $data = Join-Path $dir $old.Data
    if ((Test-Path $data) -and -not (Test-Path (Join-Path $kept $old.Data))) { Move-Item $data $kept }
    Remove-Item $dir -Recurse -Force
    Write-Host "$($old.Name) is part of Seeker's Compass now: its separate install was removed and its $($old.Data) kept."
}

# Replace an older install completely, so files a newer version dropped don't linger.
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $package $target -Recurse
foreach ($k in $keep) {
    $p = Join-Path $kept $k
    if (-not (Test-Path $p)) { continue }
    $to = Join-Path $target $k
    if (Test-Path $to) { Remove-Item $to -Recurse -Force }   # the zip's default settings give way to the player's
    Move-Item $p $to
}
Remove-Item $kept -Recurse -Force
Set-Content (Join-Path $target $marker) "Installed by Install_Without_ModEngine.bat; Uninstall_Without_ModEngine.bat removes this folder."
$ini = [string[]]@(
    "# Read by Game\OfflineMods\offline_mods_loader.dll. Written by Seeker's Compass's installer.",
    "name = Seeker's Compass", "order = 10", "files = .")
# The mod's DLL (loadouts, Grace Items, Damage Dial, auto pickup); its settings are beside it.
if (Test-Path (Join-Path $target "seekers_compass.dll")) { $ini += "native = seekers_compass.dll" }
[IO.File]::WriteAllLines((Join-Path $target "package.ini"), $ini, $utf8)
if (Test-Path (Join-Path $legacy $marker)) {
    Remove-Item $legacy -Recurse -Force
    Write-Host "Moved Seeker's Compass from Game\SeekersCompass to Game\OfflineMods\SeekersCompass."
}

Write-Host "`nDone. Start Elden Ring the way you normally do without Mod Engine:"
Write-Host "  - Seamless Co-op's launcher (ersc_launcher.exe): Seeker's Compass with your Seamless characters."
Write-Host "  - eldenring.exe directly, or Steam with an anti-cheat toggler: on its own save (ER0000.skc),"
Write-Host "    which starts as a copy of your characters. Your normal save is never written."
Write-Host "With Easy Anti-Cheat running, or through Mod Engine, it stays off."
Write-Host "If Easy Anti-Cheat refuses to start the normal game, run Uninstall_Without_ModEngine.bat."
