# Builds both release zips for Nexus: vanilla and Convergence.
#   powershell -ExecutionPolicy Bypass -File release\package.ps1 [-Version 1.0.0] [-Convergence <ConvergenceER folder>]
# The Convergence folder defaults to where the Convergence Launcher installed it; without one,
# only the vanilla zip is built.
param(
    [string]$Version = "1.0.0",
    [string]$Convergence
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$gen = Join-Path $root "src\BuildCompass.Gen\bin\Release\net9.0\BuildCompass.Gen.dll"
$stage = Join-Path $PSScriptRoot "staging"
$dist = Join-Path $PSScriptRoot "dist"
$utf8 = New-Object Text.UTF8Encoding $false
# Windows PowerShell 5 zips (Compress-Archive, ZipFile) get backslash paths, which some unzip tools
# and mod managers mangle; the generator (.NET 9) writes standard forward-slash entries.
function New-Zip($dir, $zip) { dotnet $gen zip $dir $zip }
function Write-Text($path, $text) { [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8) }

dotnet build (Join-Path $root "src\BuildCompass.Gen") -c Release -v q | Out-Null
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $dist | Out-Null

# ---------------- vanilla ----------------
$v = Join-Path $stage "vanilla"
dotnet $gen build --out "release\staging\vanilla\SeekersCompass" | Select-String "placements|grace menu:|wrote|Exception"
Copy-Item (Join-Path $PSScriptRoot "templates\README.txt") $v
Copy-Item (Join-Path $PSScriptRoot "templates\Launch_SeekersCompass.bat") $v
# The launcher writes SeekersCompass.me3 at each launch (Seamless detection, save choice).
Copy-Item (Join-Path $PSScriptRoot "templates\SeekersCompass-launch.ps1") $v
$vzip = Join-Path $dist "SeekersCompass-$Version.zip"
New-Zip $v $vzip

# ---------------- Convergence ----------------
if (-not $Convergence) {
    $launcher = Join-Path $env:APPDATA "ConvergenceLauncher\settings.json"
    if (Test-Path $launcher) { $Convergence = (Get-Content $launcher -Raw | ConvertFrom-Json).InstallationPath }
}
if (-not $Convergence -or -not (Test-Path (Join-Path $Convergence "mod\regulation.bin"))) {
    Write-Host "Convergence not found; skipping the Convergence zip (pass -Convergence <folder>)."
    Get-ChildItem $dist | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
    return
}
$c = Join-Path $stage "convergence"
$cver = (Get-Content (Join-Path $Convergence "version.txt") -Raw).Trim()
dotnet $gen build --base (Join-Path $Convergence "mod") --out "release\staging\convergence\SeekersCompass-Convergence" | Select-String "placements|grace menu:|wrote|Exception"
# The install script adds our package (load_after convergence-er) to Convergence's own me3 profiles,
# so players keep launching with Start_Convergence.bat. me3 maps every package's files in load
# order and the last one wins a shared path, so our merged files override Convergence's.
foreach ($f in "SeekersCompass-setup.ps1", "Install_SeekersCompass.bat", "Uninstall_SeekersCompass.bat") {
    Copy-Item (Join-Path $PSScriptRoot "templates\$f") $c
}
Write-Text (Join-Path $c "README - Seekers Compass.txt") ((Get-Content (Join-Path $PSScriptRoot "templates\README-Convergence.txt") -Raw).Replace("{CONVERGENCE_VERSION}", $cver))
$czip = Join-Path $dist "SeekersCompass-$Version-Convergence-$cver.zip"
New-Zip $c $czip

Get-ChildItem $dist | ForEach-Object { "{0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB) }
