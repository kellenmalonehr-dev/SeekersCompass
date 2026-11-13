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
# With Grace Loadouts or Grace Items installed there too, they then rebuild their menus on top of ours.
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

# Mods that load after ours and add their own grace menu line (Grace Loadouts, Grace Items) rebuild
# their menu on top of ours, or back to their own, when we come or go. The first of them does (its
# *-gamefolder.ps1 refresh), and passes it on to the next.
function Update-LaterMods {
    $later = foreach ($dir in Get-ChildItem $mods -Directory -ErrorAction SilentlyContinue) {
        $ini = Join-Path $dir.FullName "package.ini"
        $script = Get-ChildItem $dir.FullName -Filter "*-gamefolder.ps1" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not (Test-Path $ini) -or -not $script) { continue }
        $order = Select-String -Path $ini -Pattern '^\s*order\s*=\s*(\d+)' | Select-Object -First 1
        $name = Select-String -Path $ini -Pattern '^\s*name\s*=\s*(.+?)\s*$' | Select-Object -First 1
        $order = if ($order) { [int]$order.Matches[0].Groups[1].Value } else { 0 }
        if ($order -gt 10) { @{ Order = $order; Name = $(if ($name) { $name.Matches[0].Groups[1].Value } else { $dir.Name }); Script = $script.FullName } }
    }
    $next = @($later | Sort-Object { $_.Order }, { $_.Name })[0]
    if (-not $next) { return }
    Write-Host "$($next.Name) is installed too: updating its grace menu..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File $next.Script -Action refresh -GameDir $GameDir | ForEach-Object { "  $_" } | Write-Host
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
    if ($removed) { Write-Host "Removed Seeker's Compass"; Update-LaterMods }
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

# Replace an older install completely, so files a newer version dropped don't linger.
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item $package $target -Recurse
Set-Content (Join-Path $target $marker) "Installed by Install_Without_ModEngine.bat; Uninstall_Without_ModEngine.bat removes this folder."
[IO.File]::WriteAllLines((Join-Path $target "package.ini"), [string[]]@(
    "# Read by Game\OfflineMods\offline_mods_loader.dll. Written by Seeker's Compass's installer.",
    "name = Seeker's Compass", "order = 10", "files = ."), $utf8)
if (Test-Path (Join-Path $legacy $marker)) {
    Remove-Item $legacy -Recurse -Force
    Write-Host "Moved Seeker's Compass from Game\SeekersCompass to Game\OfflineMods\SeekersCompass."
}
Update-LaterMods

Write-Host "`nDone. Start Elden Ring the way you normally do without Mod Engine:"
Write-Host "  - Seamless Co-op's launcher (ersc_launcher.exe): Seeker's Compass with your Seamless characters."
Write-Host "  - eldenring.exe directly, or Steam with an anti-cheat toggler: on its own save (ER0000.skc),"
Write-Host "    which starts as a copy of your characters. Your normal save is never written."
Write-Host "With Easy Anti-Cheat running, or through Mod Engine, it stays off."
Write-Host "If Easy Anti-Cheat refuses to start the normal game, run Uninstall_Without_ModEngine.bat."
