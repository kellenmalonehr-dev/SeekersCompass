# Adds Seeker's Compass to (or removes it from) Mod Engine 3 profiles: Convergence's, one that
# another mod's launcher uses, or one you made yourself. You keep launching the way you already
# do. Only a marked block (plus, if you agree, a marked savefile line) is added; the rest of each
# profile is left as it was, and a backup is kept. Safe to run more than once.
#   SeekersCompass-setup.ps1 -Action install|uninstall [-AddSavefile] [profile.me3 ...]
# Without profiles: Convergence's own profiles when this sits in a ConvergenceER folder;
# otherwise a file picker (install) or every profile it was added to before (uninstall).
param(
    [ValidateSet("install", "uninstall")][string]$Action = "install",
    [switch]$AddSavefile, # agree up front to give a profile without its own save one
    [Parameter(ValueFromRemainingArguments = $true)][object[]]$Profiles
)
$ErrorActionPreference = "Stop"
$Profiles = @($Profiles | ForEach-Object { $_ } | ForEach-Object { "$_" }) # an array passed as one argument -> its items
$root = $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding $false
$begin = "# >>> Seeker's Compass (managed by Seeker's Compass setup - do not edit)"
$end = "# <<< Seeker's Compass"
$savefileLine = 'savefile = "ER0000.skc" # added by Seeker''s Compass'
$record = Join-Path $root ".seekers-compass-profiles.txt"

# The package next to this script: the Convergence build, or the base-game one.
$pkg = @("SeekersCompass-Convergence", "SeekersCompass") | ForEach-Object { Join-Path $root $_ } |
    Where-Object { Test-Path (Join-Path $_ "regulation.bin") } | Select-Object -First 1
if ($Action -eq "install" -and -not $pkg) {
    Write-Host "The Seeker's Compass folder was not found next to this script. Extract the whole zip first."
    exit 1
}
# A build made on top of another mod names that mod's package id here (e.g. convergence-er).
$requires = if ($pkg -and (Test-Path (Join-Path $pkg "requires-package.txt"))) { (Get-Content (Join-Path $pkg "requires-package.txt") -Raw).Trim() } else { "" }

# Profiles keep their own line breaks (Windows or Unix style).
function New-Line([string]$text) { if ($text.Contains("`r`n")) { "`r`n" } else { "`n" } }

function Remove-OurLines([string]$text, [string]$nl) {
    $text = [regex]::Replace($text, "(\r?\n)*" + [regex]::Escape($begin) + "[\s\S]*?" + [regex]::Escape($end) + "\r?\n?", $nl)
    [regex]::Replace($text, "(?m)^" + [regex]::Escape($savefileLine) + "\r?\n", "")
}

# Just enough TOML for me3 profiles: top-level keys, and each [[package(s)]] / [[natives]] entry.
function Read-Profile([string]$path, [string]$text) {
    $dir = Split-Path $path -Parent
    $info = @{ Top = @{}; Packages = @(); Natives = @(); Style = "package" }
    $table = $null; $entry = $null
    foreach ($line in $text -split "\r?\n") {
        $t = $line.Trim()
        if ($t -match '^\[\[\s*(packages?|natives)\s*\]\]') {
            $entry = @{}; $table = $Matches[1]
            if ($table -eq "natives") { $info.Natives += $entry } else { $info.Packages += $entry; $info.Style = $table }
        } elseif ($t -match '^\[') {
            $table = "other"; $entry = $null
        } elseif ($t -match '^([A-Za-z_]+)\s*=\s*(.+)$') {
            $key = $Matches[1]; $raw = $Matches[2].Trim()
            $value = if ($raw -match "^'([^']*)'") { $Matches[1] }
                     elseif ($raw -match '^"((?:[^"\\]|\\.)*)"') { $Matches[1] -replace '\\(.)', '$1' }
                     else { ($raw -split '#')[0].Trim() }
            if ($null -eq $table) { $info.Top[$key] = $value }
            elseif ($entry) { $entry[$key] = $value }
        }
    }
    foreach ($e in @($info.Packages) + @($info.Natives)) {
        if ($e.path) { $e.full = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($e.path)) { $e.path } else { Join-Path $dir $e.path })).TrimEnd('\') }
    }
    $info
}

function Get-RelativeFiles([string]$dir) {
    $prefix = $dir.TrimEnd('\').Length + 1
    @(Get-ChildItem $dir -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($prefix).ToLowerInvariant() })
}

function Format-TomlPath([string]$path) {
    if ($path -notmatch "'") { "'$path'" } else { '"' + $path.Replace('\', '\\').Replace('"', '\"') + '"' }
}

function Install-Into([string]$path) {
    $text = [IO.File]::ReadAllText($path)
    $nl = New-Line $text
    $clean = Remove-OurLines $text $nl
    $p = Read-Profile $path $clean
    $name = Split-Path $path -Leaf

    if ($p.Top["start_online"] -eq "true") {
        return "skipped: it turns on online play (start_online = true). Seeker's Compass is offline only."
    }
    $ours = $pkg.TrimEnd('\')
    $already = @($p.Packages | Where-Object { $_.id -eq "seekers-compass" -or $_.full -eq $ours })
    if ($already.Count) { return "skipped: it already loads Seeker's Compass." }

    $base = $null
    if ($requires) {
        $base = @($p.Packages | Where-Object { $_.id -eq $requires })[0]
        if (-not $base) { return "skipped: this Seeker's Compass version is built for '$requires', which this profile doesn't load." }
    }

    # Two packages replacing the same file can't both work; above all, only one regulation.bin can be used.
    $ourFiles = Get-RelativeFiles $ours | Where-Object { $_ -ne "requires-package.txt" }
    foreach ($other in $p.Packages) {
        if (-not $other.full -or ($base -and $other -eq $base) -or -not (Test-Path $other.full)) { continue }
        $shared = @(Get-RelativeFiles $other.full | Where-Object { $ourFiles -contains $_ })
        if ($shared.Count) {
            $label = if ($other.id) { "'$($other.id)'" } else { $other.path }
            $why = if ($shared -contains "regulation.bin") { "has its own regulation.bin (only one can be used)" } else { "replaces the same files ($($shared[0])$(if ($shared.Count -gt 1) { ', ...' }))" }
            return "skipped: the mod $label in this profile $why. Seeker's Compass would break it; it needs a Seeker's Compass version built for that mod."
        }
    }

    # The modded game must never play on the normal online save. Seamless Co-op uses its own save.
    $seamless = @($p.Natives | Where-Object { $_.path -match 'ersc\.dll$' }).Count -gt 0
    $save = $p.Top["savefile"]
    $addSave = $false
    if (-not $seamless -and (-not $save -or $save -match '\.sl2$')) {
        if ($save) { return "skipped: it plays on the normal save ($save). Change its savefile first." }
        Write-Host "`n$name plays on your normal save (ER0000.sl2). Seeker's Compass needs a separate one."
        Write-Host "Mod Engine 3 will start it with a copy of your characters in ER0000.skc; your normal save stays untouched."
        $answer = if ($AddSavefile) { "y" } else { Read-Host "Give this profile its own save (ER0000.skc)? [Y/N]" }
        if ($answer -notmatch '^\s*y') { return "skipped: it would play on your normal save." }
        $addSave = $true
    }

    $header = "[[$($p.Style)]]"
    $block = "$begin${nl}$header${nl}id = `"seekers-compass`"${nl}path = $(Format-TomlPath $ours)${nl}"
    if ($base) { $block += "load_after = [{ id = `"$requires`", optional = false }]$nl" }
    $block += $end

    $new = $clean
    if ($addSave) {
        # Top-level keys must come before the first table: put it under profileVersion.
        $m = [regex]::Match($new, '(?m)^profileVersion\s*=.*\r?\n')
        $at = if ($m.Success) { $m.Index + $m.Length } else { 0 }
        $new = $new.Insert($at, "$savefileLine$nl")
    }
    $new = $new.TrimEnd() + "$nl$nl" + $block + "$nl"

    # Back up the profile as its mod left it (refreshed when an update replaced it since).
    $backup = "$path.before-seekers-compass"
    if ($text -eq $clean -or -not (Test-Path $backup)) { [IO.File]::WriteAllText($backup, $text, $utf8) }
    if ($new -ne $text) { [IO.File]::WriteAllText($path, $new, $utf8) }
    $paths = @(if (Test-Path $record) { Get-Content $record }) + $path | Select-Object -Unique
    [IO.File]::WriteAllLines($record, [string[]]$paths, $utf8)
    "added$(if ($addSave) { ' (with its own save, ER0000.skc)' })."
}

function Uninstall-From([string]$path) {
    $text = [IO.File]::ReadAllText($path)
    $nl = New-Line $text
    $new = (Remove-OurLines $text $nl).TrimEnd()
    # Unchanged since the backup apart from our lines: put back the backup exactly as it was.
    $backup = "$path.before-seekers-compass"
    $original = if (Test-Path $backup) { [IO.File]::ReadAllText($backup) } else { $null }
    $new = if ($original -and $original.TrimEnd() -ceq $new) { $original } else { $new + $nl }
    if ($new -ne $text) { [IO.File]::WriteAllText($path, $new, $utf8) }
    if (Test-Path $record) {
        $left = @(Get-Content $record | Where-Object { $_ -ne $path })
        if ($left.Count) { [IO.File]::WriteAllLines($record, [string[]]$left, $utf8) } else { Remove-Item $record }
    }
    "removed."
}

# ---- which profiles ----
$convergence = @("convergence.me3", "convergence - seamless.me3") | ForEach-Object { Join-Path $root "me3\$_" } | Where-Object { Test-Path $_ }
if (-not $Profiles) {
    if ($convergence) { $Profiles = $convergence }
    elseif ($Action -eq "uninstall") { $Profiles = @(if (Test-Path $record) { Get-Content $record }) }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        $dlg = New-Object Windows.Forms.OpenFileDialog
        $dlg.Title = "Choose the Mod Engine 3 profile(s) to add Seeker's Compass to"
        $dlg.Filter = "Mod Engine 3 profiles (*.me3)|*.me3"
        $dlg.Multiselect = $true
        $me3Profiles = Join-Path $env:LOCALAPPDATA "garyttierney\me3\config\profiles"
        if (Test-Path $me3Profiles) { $dlg.InitialDirectory = $me3Profiles }
        if ($dlg.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { Write-Host "No profile chosen."; exit 1 }
        $Profiles = $dlg.FileNames
    }
}
if (-not $Profiles) { Write-Host "No profiles to change."; exit 0 }

$failed = 0
foreach ($path in $Profiles) {
    $path = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path $path)) { Write-Host "$path`: not found."; $failed++; continue }
    $result = if ($Action -eq "install") { Install-Into $path } else { Uninstall-From $path }
    Write-Host "$(Split-Path $path -Leaf): $result"
    if ($result -like "skipped*") { $failed++ }
}
if ($Action -eq "install" -and $failed -eq $Profiles.Count) {
    Write-Host "`nSeeker's Compass was not added to any profile."
} elseif ($Action -eq "install") {
    Write-Host "`nLaunch the way you normally do; Seeker's Compass is now part of the profile(s) marked 'added'."
    Write-Host "If you move this folder, or a mod update replaces its profile, run this again."
} else {
    Write-Host "`nSeeker's Compass is no longer loaded by those profiles."
}
exit $failed
