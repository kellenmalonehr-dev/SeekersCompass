#!/usr/bin/env bash
# Builds the loader into out/loader/: xinput1_4.dll (the proxy, for the game folder) and
# SeekersCompass/seekers_compass_loader.dll (the Rust loader, next to the mod's files).
#
# Needs Rust (~/.cargo, windows-gnu host) and tools/zig. Rust's bundled GNU dlltool has no
# assembler next to it, so tools/as provides dlltool.exe/as.exe stand-ins that hand it zig's.
# RUSTC_BOOTSTRAP enables the closure features Mod Engine 3's hook code uses on stable Rust.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/out/loader"
zig="$root/tools/zig/zig.exe"
mkdir -p "$out/SeekersCompass" "$root/tools/as"

for tool in as dlltool; do
    [ -f "$root/tools/as/$tool.exe" ] || "$zig" cc -target x86_64-windows-gnu -municode -O2 \
        -o "$root/tools/as/$tool.exe" "$here/build-tools/$tool.c"
done
selfcontained="$(ls -d "$HOME"/.rustup/toolchains/stable-x86_64-pc-windows-gnu/lib/rustlib/x86_64-pc-windows-gnu/bin/self-contained)"

REAL_DLLTOOL="$(cygpath -w "$selfcontained/dlltool.exe")" \
PATH="$root/tools/as:$selfcontained:$HOME/.cargo/bin:$PATH" RUSTC_BOOTSTRAP=1 \
    cargo build --release --manifest-path "$here/Cargo.toml"
cp "$here/target/release/seekers_compass_loader.dll" "$out/SeekersCompass/"

"$zig" cc -target x86_64-windows-gnu -shared -O2 -Wall \
    -o "$out/xinput1_4.dll" "$here/proxy/proxy.c" "$here/proxy/exports.def"
rm -f "$out"/*.lib "$out"/*.pdb
ls -la "$out" "$out/SeekersCompass"
