# Adds (or removes) Seeker's Compass in The Convergence's Mod Engine 3 profiles, so the normal
# Start_Convergence.bat launches it. Only a marked block is touched; the rest of each profile is
# left exactly as Convergence wrote it. Safe to run more than once.
#   SeekersCompass-setup.ps1 -Action install | uninstall
param([ValidateSet("install", "uninstall")][string]$Action = "install")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$profiles = "convergence.me3", "convergence - seamless.me3"
$begin = "# >>> Seeker's Compass (managed by Install_SeekersCompass.bat - do not edit)"
$end = "# <<< Seeker's Compass"
$block = @"
$begin
[[package]]
id = "seekers-compass"
path = "./../SeekersCompass-Convergence"
load_after = [{ id = "convergence-er", optional = false }]
$end
"@
$utf8 = New-Object Text.UTF8Encoding $false

if ($Action -eq "install" -and -not (Test-Path (Join-Path $root "SeekersCompass-Convergence\regulation.bin"))) {
    Write-Host "SeekersCompass-Convergence folder not found next to this script."
    Write-Host "Extract the whole zip into your ConvergenceER folder first."
    exit 1
}

$changed = 0
foreach ($name in $profiles) {
    $path = Join-Path $root "me3\$name"
    if (-not (Test-Path $path)) { Write-Host "Skipping $name (not found)."; continue }
    $text = [IO.File]::ReadAllText($path)
    # Remove any existing block first, so install never duplicates it.
    $pattern = "(\r?\n)*" + [regex]::Escape($begin) + "[\s\S]*?" + [regex]::Escape($end) + "\r?\n?"
    $clean = [regex]::Replace($text, $pattern, "`r`n")
    if ($Action -eq "install") {
        $backup = "$path.before-seekers-compass"
        if (-not (Test-Path $backup)) { [IO.File]::WriteAllText($backup, $text, $utf8) }
        $new = $clean.TrimEnd() + "`r`n`r`n" + $block.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n"
    } else {
        $new = $clean.TrimEnd() + "`r`n"
    }
    if ($new -ne $text) { [IO.File]::WriteAllText($path, $new, $utf8); $changed++ }
    Write-Host "$Action`: $name"
}
if ($Action -eq "install") {
    Write-Host "`nDone. Launch Convergence the usual way (Start_Convergence.bat)."
    Write-Host "If a Convergence update replaces its profiles, run Install_SeekersCompass.bat again."
} else {
    Write-Host "`nDone. Seeker's Compass is no longer loaded. You can now delete SeekersCompass-Convergence."
}
