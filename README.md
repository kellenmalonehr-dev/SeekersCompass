# Seeker's Compass

An Elden Ring mod that marks weapons, spells, armor and talismans on the world map, filtered by
the damage stats you choose (STR / DEX / INT / FAI / ARC) and by item type. Hover a mark for the
item's scaling and requirements, spell costs, armor stat bonuses or talisman effect.

Buy the **Seeker's Compass** from Merchant Kalé at the Church of Elleh (100 runes), then pick
**Seeker's Compass** at any Site of Grace to switch filters on and off. Active lines glow.

Releases are on Nexus Mods. This repository is the source: a generator that reads the game's own
data and writes the mod's files, plus the launcher scripts shipped in the release zips.

**Offline only.** The mod runs through Mod Engine 3, which starts the game without Easy Anti-Cheat,
so it can't reach official servers; and it never writes to the normal online save.

## What's in the release zips

| File | Purpose |
|---|---|
| `SeekersCompass/` | The mod's game data files (generated; see below) |
| `Launch_SeekersCompass.bat` | Runs `SeekersCompass-launch.ps1` |
| `SeekersCompass-launch.ps1` | Finds Elden Ring through Steam's library list, detects Seamless Co-op (`Game/SeamlessCoop/ersc.dll`), writes `SeekersCompass.me3` and starts Mod Engine 3. Without Seamless it uses its own save (`ER0000.skc`) and, once, offers to **copy** `ER0000.sl2` into it. It never writes to `ER0000.sl2` and downloads nothing. |
| Convergence zip: `Install_SeekersCompass.bat`, `Uninstall_SeekersCompass.bat`, `SeekersCompass-setup.ps1` | Add or remove one marked `[[package]]` block in Convergence's `me3/*.me3` profiles (backed up first), so Convergence's own launcher loads the mod |

Sources: [`release/templates/`](release/templates/). Zips are built by [`release/package.ps1`](release/package.ps1).

## Building from source

Requirements:
- Windows, [.NET 9 SDK](https://dotnet.microsoft.com/download), git
- Elden Ring (Steam), with these folders unpacked into the `Game` folder using
  [UXM Selective Unpack](https://github.com/Nordgaren/UXM-Selective-Unpack):
  `map/mapstudio`, `event`, `msg/engus`, `menu`, `script/talk`
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

Release zips: `powershell -ExecutionPolicy Bypass -File release\package.ps1 -Version 1.0.0`
(finds Convergence through the Convergence Launcher's settings, or pass `-Convergence <folder>`).

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
- **Settings:** event flags 78990-78994 (stats), 78982-78985 (types) and 78910-78935 (derived).

Limits: boss rewards, remembrances, shops and invader drops have no fixed spot and aren't marked;
weapon stats use the uninfused scaling. Conflicts with other mods that replace `regulation.bin`,
`msg/engus`, `menu/*/01_common` or the grace talk script (use the Convergence build for Convergence).

## Credits

- [SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) and
  [Paramdex](https://github.com/soulsmods/Paramdex) (soulsmods)
- [DarkScript3](https://github.com/AinTunez/DarkScript3) (AinTunez) for EMEDF instruction definitions
- [Mod Engine 3](https://github.com/garyttierney/me3)
- [DirectXTex texconv](https://github.com/microsoft/DirectXTex) (Microsoft)
- The Convergence team
- Icon art generated with ChatGPT; built with AI coding assistance
