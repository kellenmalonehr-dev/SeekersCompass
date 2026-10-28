# Fetches the build dependencies at the exact versions this project was built and tested with.
#   powershell -ExecutionPolicy Bypass -File setup.ps1
# - deps/SoulsFormatsNEXT, deps/Paramdex, deps/DarkScript3 (git, pinned commits)
# - patches/soulsformats-ignore-orphaned-bits.patch applied to SoulsFormatsNEXT
# - deps/me3 (Mod Engine 3 v0.13.0, for the game-folder loader)
# - tools/texconv/texconv.exe (Microsoft DirectXTex release)
# - tools/zig (Zig 0.13.0, for the loader's proxy DLL)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$deps = @(
    @{ Name = "SoulsFormatsNEXT"; Url = "https://github.com/soulsmods/SoulsFormatsNEXT.git"; Commit = "9e1c36f54962f80c143e3c1fe8a709204aefacf9" },
    @{ Name = "Paramdex";         Url = "https://github.com/soulsmods/Paramdex.git";         Commit = "ff7245e524329bc3eab00036723d2bd53384cedf" },
    @{ Name = "DarkScript3";      Url = "https://github.com/AinTunez/DarkScript3.git";      Commit = "733f024253930b70db8e3d0039c4b53b6ee0a2d4" },
    # Mod Engine 3 v0.13.0: its crates are path dependencies of the game-folder loader.
    @{ Name = "me3";              Url = "https://github.com/garyttierney/me3.git";          Commit = "989b522214e39c83d701f4182c4f0a86ad79a30a" }
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

# Zig's C compiler builds the loader's proxy DLL and stands in for the GNU assembler (see
# src/SeekersCompass.Loader/build.sh). Checked against ziglang.org's published SHA-256.
$zig = Join-Path $root "tools\zig\zig.exe"
if (-not (Test-Path $zig)) {
    Write-Host "zig: downloading 0.13.0"
    $zip = Join-Path $root "tools\zig.zip"
    Invoke-WebRequest "https://ziglang.org/download/0.13.0/zig-windows-x86_64-0.13.0.zip" -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne "D859994725EF9402381E557C60BB57497215682E355204D754EE3DF75EE3C158") { throw "zig download checksum mismatch" }
    Expand-Archive $zip (Join-Path $root "tools") -Force
    Rename-Item (Join-Path $root "tools\zig-windows-x86_64-0.13.0") "zig"
    Remove-Item $zip
}

Write-Host "`nDone. Build with:  dotnet build src\BuildCompass.Gen -c Release"
Write-Host "The game-folder loader also needs Rust (rustup, host x86_64-pc-windows-gnu); then run:"
Write-Host "  bash src/SeekersCompass.Loader/build.sh"
