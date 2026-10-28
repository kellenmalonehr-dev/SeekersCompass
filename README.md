# Seeker's Compass

An Elden Ring mod that marks weapons, spells, armor and talismans on the world map, filtered by
the damage stats you choose (STR / DEX / INT / FAI / ARC) and by item type. Hover a mark for the
item's scaling and requirements, spell costs, armor stat bonuses or talisman effect.

Buy the **Seeker's Compass** from Merchant Kalé at the Church of Elleh (100 runes), then pick
**Seeker's Compass** at any Site of Grace to switch filters on and off. Active lines glow.

Releases are on Nexus Mods. This repository is the source: a generator that reads the game's own
data and writes the mod's files, plus the launcher scripts shipped in the release zips.

**Offline only.** The mod only runs when the game starts without Easy Anti-Cheat (through Mod
Engine 3, Seamless Co-op's launcher or an offline start), so it can't reach official servers; and
it never writes to the normal online save.

## What's in the release zips

| File | Purpose |
|---|---|
| `SeekersCompass/` | The mod's game data files (generated; see below) |
| `Launch_SeekersCompass.bat` | Runs `SeekersCompass-launch.ps1` |
| `SeekersCompass-launch.ps1` | Finds Elden Ring through Steam's library list, detects Seamless Co-op (`Game/SeamlessCoop/ersc.dll`), writes `SeekersCompass.me3` and starts Mod Engine 3. Without Seamless it uses its own save (`ER0000.skc`) and, once, offers to **copy** `ER0000.sl2` into it. It never writes to `ER0000.sl2` and downloads nothing. |
| `Install_SeekersCompass.bat`, `Uninstall_SeekersCompass.bat`, `SeekersCompass-setup.ps1` (both zips) | Add or remove one marked package block in Mod Engine 3 profiles (backed up first), so players keep their own launcher. In a ConvergenceER folder it targets Convergence's `me3/*.me3` profiles (loading after `convergence-er`); otherwise the player picks profiles. It refuses profiles where another package replaces the same files (above all `regulation.bin`) or `start_online` is on, and offers a separate `savefile` to a profile that would play on `ER0000.sl2`. |

| `Install_Without_ModEngine.bat`, `Uninstall_Without_ModEngine.bat`, `SeekersCompass-gamefolder.ps1`, `GameFolder/` (base-game zip) | For players without Mod Engine. Copies `GameFolder/xinput1_4.dll` into `ELDEN RING/Game` and the mod files plus `seekers_compass_loader.dll` into `Game/SeekersCompass/` (refusing if another mod owns either); the uninstaller removes only what it installed. See [the loader](#the-game-folder-loader-no-mod-engine). |

Sources: [`release/templates/`](release/templates/). Zips are built by [`release/package.ps1`](release/package.ps1).

## Building from source

Requirements:
- Windows, [.NET 9 SDK](https://dotnet.microsoft.com/download), git
- Elden Ring (Steam), with these folders unpacked into the `Game` folder using
  [UXM Selective Unpack](https://github.com/Nordgaren/UXM-Selective-Unpack):
  `map/mapstudio`, `event`, `msg` (all languages), `menu`, `script/talk`
- Any installed Steam game that ships `oo2core_8_win64.dll` or `oo2core_9_win64.dll`
  (Elden Ring's own `oo2core_6` is too old for SoulsFormats' compression code)

```
powershell -ExecutionPolicy Bypass -File setup.ps1
dotnet build src/BuildCompass.Gen -c Release
dotnet src/BuildCompass.Gen/bin/Release/net9.0/BuildCompass.Gen.dll build
dotnet src/BuildCompass.Gen/bin/Release/net9.0/BuildCompass.Gen.dll verify
```

`setup.ps1` fetches the dependencies at pinned commits (SoulsFormatsNEXT, Paramdex, DarkScript3),
applies [`patches/soulsformats-ignore-orphaned-bits.patch`](patches/) and downloads Microsoft's
texconv. The game folder and Oodle DLL are found automatically; override with the
`BUILDCOMPASS_GAME` and `BUILDCOMPASS_OODLE` environment variables.

`build` writes `mod/`. Options:
- `--base <folder>` builds on top of another mod's files (e.g. `ConvergenceER/mod`), so the output
  carries that mod's own regulation, text, menu art and grace script.
- `--out <folder>` sets the output folder.

The game-folder loader also needs [Rust](https://rustup.rs) with the `x86_64-pc-windows-gnu` host
(no Visual Studio needed); `setup.ps1` fetches the rest (Mod Engine 3's source, Zig):

```
bash src/SeekersCompass.Loader/build.sh
```

Release zips: `powershell -ExecutionPolicy Bypass -File release\package.ps1 -Version 1.0.0`
(finds Convergence through the Convergence Launcher's settings, or pass `-Convergence <folder>`;
needs the loader from `build.sh` in `out/loader`).

## The game-folder loader (no Mod Engine)

[`src/SeekersCompass.Loader`](src/SeekersCompass.Loader) lets the mod load when the game is started
without Mod Engine, e.g. by Seamless Co-op's own launcher (which only loads Seamless).

- **`proxy/` → `Game/xinput1_4.dll`** (C, built with Zig). The game imports `xinput1_4.dll` (by
  ordinal), so it loads this at startup, before any game code runs. Every XInput call jumps to
  Windows' own DLL. It then decides: Easy Anti-Cheat running, or Mod Engine 2/3 started the game
  (or is loaded) → stay off. Otherwise it loads the Rust loader, telling it whether Seamless
  Co-op's launcher (`ersc_launcher.exe`) started the game. Each decision goes to
  `Game/SeekersCompass/launch.log`.
- **`src/` → `Game/SeekersCompass/seekers_compass_loader.dll`** (Rust). Mod Engine 3 v0.13.0's own
  mod host (MIT OR Apache-2.0), copied verbatim except for one allocator change noted in
  `asset_hooks.rs`, plus its crates from `deps/me3` as path dependencies. `lib.rs` replaces Mod
  Engine 3's launcher-driven attach with a fixed profile: the mod folder as the only package,
  `ER0000.skc` as the save unless Seamless keeps its own `.co2`, Arxan neutralized, online play off.
  Everything that needs worker threads waits for the game's entry point, outside the Windows
  loader lock. Errors and panics are logged to `loader.log`, and the game then runs unmodded.
- **Building on windows-gnu:** Rust's bundled GNU `dlltool` has no assembler next to it, so
  `build-tools/` provides `dlltool`/`as` stand-ins that hand it Zig's. `RUSTC_BOOTSTRAP=1` enables
  the closure features Mod Engine 3's hook code uses.
- **After a game update:** update `deps/me3` (and the copied modules) to the Mod Engine 3 release
  that supports it, rebuild, and ship.

## How it works

- **Items:** MSB treasure events and unique enemy drops are resolved through `ItemLotParam_map`.
  Weapons count for every stat they scale with at grade D or better; spells for every stat they
  require; armor for stats its SpEffects raise or a hand-tagged build (`data/tags.json`).
- **Map marks:** one `WorldMapPointParam` row per item and matching filter. Each row watches one
  event flag; the grace menu recomputes those flags from the toggles when it closes. Hover text
  is a `PlaceName` FMG entry; a second entry, shown once the pickup flag is set, adds "(collected)".
- **Grace menu:** `t000001000` (in `script/talk/m00_00_00_00.talkesdbnd.dcx`) gets one list line and a
  new state group for the toggle list. The patch finds the grace menu by its lines rather than
  state numbers, so it also applies to Convergence's differently compiled script.
- **Icons:** map icons repaint unused `MENU_MAP_` slots 86, 89, 214 and 233 in
  `menu/*/01_common.tpf.dcx`; the compass gets item icon 40900. Art is in `data/icons/`.
- **Languages:** every unpacked `msg/<lang>` gets the mod's text (`Loc`): item names from that language's
  FMGs, stat/slot/menu words from the game's own `GR_MenuText`/`EventTextForTalk` entries (same ids as
  the English words). Only the compass's name and description and "(collected)" are the mod's own translations.
- **Settings:** event flags 78990-78994 (stats), 78982-78985 (types) and 78910-78935 (derived).

Limits: boss rewards, remembrances, shops and invader drops have no fixed spot and aren't marked;
weapon stats use the uninfused scaling. Conflicts with other mods that replace `regulation.bin`,
`msg/engus`, `menu/*/01_common` or the grace talk script (use the Convergence build for Convergence).

## Credits

- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) and
  [Paramdex](https://github.com/soulsmods/Paramdex) (soulsmods)
- [DarkScript3](https://github.com/AinTunez/DarkScript3) (AinTunez) for EMEDF instruction definitions
- [Mod Engine 3](https://github.com/garyttierney/me3) (garyttierney and contributors), whose mod host the
  game-folder loader reuses (MIT OR Apache-2.0; licenses in `src/SeekersCompass.Loader`)
- [dearxan](https://crates.io/crates/dearxan) (via Mod Engine 3) for neutralizing Arxan
- [Zig](https://ziglang.org) and [Rust](https://www.rust-lang.org) toolchains for the loader
- [DirectXTex texconv](https://github.com/microsoft/DirectXTex) (Microsoft)
- The Convergence team
- Icon art generated with ChatGPT; built with AI coding assistance
