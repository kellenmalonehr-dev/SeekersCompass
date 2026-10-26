# Fetches the build dependencies at the exact versions this project was built and tested with.
#   powershell -ExecutionPolicy Bypass -File setup.ps1
# - deps/SoulsFormatsNEXT, deps/Paramdex, deps/DarkScript3 (git, pinned commits)
# - patches/soulsformats-ignore-orphaned-bits.patch applied to SoulsFormatsNEXT
# - tools/texconv/texconv.exe (Microsoft DirectXTex release)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$deps = @(
    @{ Name = "SoulsFormatsNEXT"; Url = "https://github.com/soulsmods/SoulsFormatsNEXT.git"; Commit = "9e1c36f54962f80c143e3c1fe8a709204aefacf9" },
    @{ Name = "Paramdex";         Url = "https://github.com/soulsmods/Paramdex.git";         Commit = "ff7245e524329bc3eab00036723d2bd53384cedf" },
    @{ Name = "DarkScript3";      Url = "https://github.com/AinTunez/DarkScript3.git";      Commit = "733f024253930b70db8e3d0039c4b53b6ee0a2d4" }
)
New-Item -ItemType Directory -Force (Join-Path $root "deps") | Out-Null
foreach ($d in $deps) {
    $dir = Join-Path $root "deps\$($d.Name)"
    if (Test-Path (Join-Path $dir ".git")) { Write-Host "$($d.Name): already present"; continue }
    Write-Host "$($d.Name): fetching $($d.Commit.Substring(0, 9))"
    git -c core.longpaths=true init -q $dir
    git -C $dir config core.longpaths true
    git -C $dir remote add origin $d.Url
    git -C $dir fetch -q --depth 1 origin $d.Commit
    git -C $dir checkout -q FETCH_HEAD
}

# Read-only escape hatch for a param whose definition lags the game (see the patch header).
$sf = Join-Path $root "deps\SoulsFormatsNEXT"
$patch = Join-Path $root "patches\soulsformats-ignore-orphaned-bits.patch"
$row = Join-Path $sf "SoulsFormats\Formats\PARAM\PARAM\Row.cs"
if (Select-String -Path $row -Pattern "IgnoreOrphanedBits" -Quiet) { Write-Host "SoulsFormatsNEXT: patch already applied" }
else { git -C $sf apply $patch; Write-Host "SoulsFormatsNEXT: patch applied" }

$texconv = Join-Path $root "tools\texconv\texconv.exe"
if (-not (Test-Path $texconv)) {
    New-Item -ItemType Directory -Force (Split-Path $texconv) | Out-Null
    Write-Host "texconv: downloading"
    Invoke-WebRequest "https://github.com/microsoft/DirectXTex/releases/download/may2026/texconv.exe" -OutFile $texconv
}

Write-Host "`nDone. Build with:  dotnet build src\BuildCompass.Gen -c Release"
