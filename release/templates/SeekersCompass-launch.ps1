# Launches Elden Ring with Seeker's Compass through Mod Engine 3, with no files for the player to edit.
# - Seamless Co-op is found through Steam and used automatically, with Seamless's own save.
# - Without Seamless the mod plays on its own save (ER0000.skc). The first time, the player can
#   bring their characters over: ER0000.sl2 is COPIED to ER0000.skc, never the other way, so the
#   normal online save is never written.
# Mod Engine 3 starts the game without Easy Anti-Cheat, so the modded game can't reach official servers.
param(
    [string]$Me3,        # optional explicit path to me3.exe
    [switch]$DryRun,     # show what would happen without launching or copying
    [switch]$NoSeamless  # play solo on the mod's own save even if Seamless Co-op is installed
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding $false
$modSave = "ER0000.skc"
$noImportMarker = Join-Path $here ".no-save-import"

if (-not (Test-Path (Join-Path $here "SeekersCompass\regulation.bin"))) {
    Write-Host "The SeekersCompass folder was not found next to this launcher."
    Write-Host "Extract the whole zip into one folder and try again."
    exit 1
}

# ---- find the Elden Ring install via Steam ---------------------------------------------------
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

$game = Find-GameDir
$ersc = if ($game) { Join-Path $game "SeamlessCoop\ersc.dll" } else { $null }
$seamless = -not $NoSeamless -and $ersc -and (Test-Path $ersc)

# ---- choose the save and write the profile ---------------------------------------------------
if ($seamless) {
    # Use the extension Seamless itself is configured for (save_file_extension in ersc_settings.ini).
    $ext = "co2"
    $ini = Join-Path $game "SeamlessCoop\ersc_settings.ini"
    if (Test-Path $ini) {
        $m = [regex]::Match((Get-Content $ini -Raw), '(?m)^\s*save_file_extension\s*=\s*"?([A-Za-z0-9]+)"?')
        if ($m.Success) { $ext = $m.Groups[1].Value }
    }
    $save = "ER0000.$ext"
    Write-Host "Seamless Co-op found: $ersc"
    Write-Host "Using your Seamless Co-op characters ($save)."
} else {
    $save = $modSave
    Write-Host $(if ($NoSeamless) { "Playing solo" } else { "Seamless Co-op not found; playing offline" }) "on Seeker's Compass's own save ($save)."

    # One-time offer to bring existing characters over (copy only; the original stays as it is).
    $saveRoot = Join-Path $env:APPDATA "EldenRing"
    $candidates = @(Get-ChildItem $saveRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
        (Test-Path (Join-Path $_.FullName "ER0000.sl2")) -and -not (Test-Path (Join-Path $_.FullName $modSave)) })
    if ($candidates.Count -gt 0 -and -not (Test-Path $noImportMarker)) {
        Write-Host ""
        Write-Host "Bring your existing characters into Seeker's Compass?"
        Write-Host "Your normal save is COPIED, not changed, and stays safe for online play."
        $answer = if ($DryRun) { "y (dry run)" } else { Read-Host "Copy them? [Y/N]" }
        if ($answer -match '^\s*y') {
            foreach ($c in $candidates) {
                $from = Join-Path $c.FullName "ER0000.sl2"; $to = Join-Path $c.FullName $modSave
                if ($DryRun) { Write-Host "  would copy $from -> $to" }
                else { Copy-Item $from $to; Write-Host "  Copied your characters into $to" }
            }
        } elseif (-not $DryRun) {
            [IO.File]::WriteAllText($noImportMarker, "Player chose to start fresh; delete this file to be asked again.", $utf8)
            Write-Host "  Starting fresh. (Delete .no-save-import next to the launcher to be asked again.)"
        }
    }
}

$profileText = @"
profileVersion = "v1"
savefile = "$save"

[[supports]]
game = "eldenring"

[[packages]]
id = "seekers-compass"
path = "SeekersCompass"
"@
# The mod's DLL (loadouts, Grace Items, Damage Dial, auto pickup); its settings are beside it.
if (Test-Path (Join-Path $here "SeekersCompass\seekers_compass.dll")) { $profileText += "`n`n[[natives]]`npath = 'SeekersCompass\seekers_compass.dll'`n" }
if ($seamless) { $profileText += "`n`n[[natives]]`npath = '$ersc'`n" }
$profilePath = Join-Path $here "SeekersCompass.me3"
# Other mods can be added to this profile by their installers (Grace Loadouts, Grace Items): keep
# their marked blocks. Each names its setup script and its place in the order ("# order:";
# Grace Loadouts 1.0's has none: 20).
$addedBlocks = @()
if (Test-Path $profilePath) {
    foreach ($m in [regex]::Matches([IO.File]::ReadAllText($profilePath), "(?m)^# >>> (.+?) \(managed by[^\r\n]*\r?\n([\s\S]*?)^# <<< \1[^\r\n]*")) {
        $name = $m.Groups[1].Value; $body = $m.Groups[2].Value
        $order = if ($body -match "(?m)^# order: (\d+)") { [int]$Matches[1] } elseif ($name -eq "Grace Loadouts") { 20 } else { 0 }
        $setup = if ($body -match "(?m)^# setup: (.+?)\r?$") { $Matches[1] } else { $null }
        $addedBlocks += @{ Name = $name; Order = $order; Setup = $setup }
        $profileText = $profileText.TrimEnd() + "`n`n$($m.Value -replace "\r\n", "`n")`n"
    }
}

# ---- find Mod Engine 3 -----------------------------------------------------------------------
# Only registry and file lookups here: native commands that print to stderr (like `assoc` when
# nothing is registered) would abort the script under $ErrorActionPreference = "Stop".
function Find-Me3 {
    if ($Me3 -and (Test-Path $Me3)) { return $Me3 }
    $onPath = Get-Command me3 -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    # The installer's entry in Windows' installed-programs list.
    $uninstall = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*", "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    foreach ($entry in Get-ItemProperty $uninstall -ErrorAction SilentlyContinue) {
        if ($entry.DisplayName -notmatch '^(me3|Mod Engine 3)' -or -not $entry.InstallLocation) { continue }
        $exe = Get-ChildItem $entry.InstallLocation -Recurse -Depth 3 -Filter "me3.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($exe) { return $exe.FullName }
    }
    return $null
}
# The installer also associates .me3 files with its launcher.
$associated = (Test-Path "HKCU:\Software\Classes\.me3") -or (Test-Path "HKLM:\Software\Classes\.me3")
$me3Exe = Find-Me3

if ($DryRun) {
    Write-Host "`n--- profile that would be written ---`n$profileText"
    Write-Host "`nMod Engine 3: $(if ($me3Exe) { $me3Exe } elseif ($associated) { '.me3 file association' } else { 'NOT FOUND' })"
    exit 0
}
if (-not $me3Exe -and -not $associated) {
    Write-Host "`nMod Engine 3 was not found."
    Write-Host "Install it from https://github.com/garyttierney/me3/releases and run this launcher again."
    exit 1
}
[IO.File]::WriteAllText($profilePath, $profileText.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8)
# After a Seeker's Compass update its grace menu changed: the first mod after it re-merges onto it,
# and passes that on to the next (a merge already made for these menus is reused, so this is quick).
$next = @($addedBlocks | Where-Object { $_.Order -gt 10 -and $_.Setup } | Sort-Object { $_.Order })[0]
if ($next -and (Test-Path $next.Setup)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $next.Setup -Action refresh $profilePath | ForEach-Object { Write-Host "$($next.Name) - $_" }
} elseif ($next) {
    Write-Host "$($next.Name)'s folder has moved: run its installer again and choose SeekersCompass.me3."
}

# ---- launch ----------------------------------------------------------------------------------
Write-Host "`nLaunching Elden Ring with Seeker's Compass (offline - official servers are not available)..."
if ($me3Exe) {
    # Like Convergence's launcher: only errors in me3's console, and run it detached so this
    # window closes instead of streaming me3's file log for the whole session.
    $env:ME3_CONSOLE_LOG_LEVEL = "error"
    Start-Process -FilePath $me3Exe -ArgumentList @("launch", "--auto-detect", "-p", "`"$profilePath`"") -WindowStyle Minimized
} else {
    Start-Process $profilePath
}
exit 0
