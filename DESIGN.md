# Design notes

(The generator also uses this file to find the project root.)

## Engine constraints that shaped the design
- **Map tooltips are one line.** `WorldMapPointParam` text comes from `PlaceName` and is clipped at
  about 54 characters; newlines hide everything after the first line. Hover text is fitted to 50
  characters by trying progressively shorter variants (`Catalog.Fit`).
- **A map point watches exactly one event flag.** Visibility logic (stats AND/OR item types) is
  therefore computed into derived flags (`Visibility`), one per item type and stat plus one "all of
  type" per item type, and each item gets one row per flag it should respond to.
- **No new map icons without GFX edits.** `02_120_worldmap.gfx` imports every `MENU_MAP_N` sprite by
  name, so the mod repaints slots nothing uses (86, 89, 214, 233). 212 is imported but never drawn;
  62 and 65 are used by grace-list tabs. Slots in the 80s draw above location art.
- **Dialog answers only register in map-tile flags.** The original yes/no prompt flow (since replaced
  by the grace menu) never saw answers written to 5-digit global flags.
- **The grace menu is a talk script.** Lines are `c1_19`/`c5_19`/`c1_149` (149 adds the glowing
  indicator vanilla uses for Flasks), the list is shown with `c1_10`, and the choice is read with
  function 23. The patch finds states by content because Convergence renumbers and recompiles them.

## Data sources
- Item locations: MSB `Treasure` events (including merged `_01`/`_02` open-world tiles, converted back
  to the small-tile grid) and enemies whose `NpcParam.itemLotId_map` lot has a pickup flag.
- Dungeon interiors are drawn on the world map through `WorldMapLegacyConvParam`; marks for items
  inside a location are snapped into a badge row on its icon (`LocationSnap`), skipping NPC and grace
  icons.
- Armor stat bonuses come from `SpEffectParam` (resident SpEffects). Paramdex's definition lags the
  game by a few reserved bits, so it is read with SoulsFormats' padding check relaxed (read-only;
  see `patches/`).

## Saves and online safety
- Mod Engine 3 starts the game without EAC, so it can't reach official servers.
- The launcher's profile always names a non-default save: Seamless Co-op's (`ER0000.co2`, or the
  extension in `ersc_settings.ini`) or the mod's own `ER0000.skc`, optionally seeded once by copying
  `ER0000.sl2`. Nothing ever writes `ER0000.sl2`.
- Mod Engine 2 isn't supported because it can't redirect the save.
