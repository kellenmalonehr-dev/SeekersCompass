# Nexus Mods page — Seeker's Compass

Copy each block into the matching field on the Nexus upload page.

---

## Mod name

Seeker's Compass - Build-Filtered Item Map

## Summary (short description, max 255 characters)

Marks weapons, spells, armor and talismans on the world map, filtered by your damage stats. Hover a mark for scaling, requirements and effects. Buy the compass from Merchant Kalé and set filters at any Site of Grace. Convergence version included.

## Category / tags (suggestions)

Category: Gameplay (or User Interface). Tags: Map, Items, Quality of Life, Convergence, Seamless Co-op.

## Requirements (fill in the Requirements section)

- Elden Ring (Steam, current patch)
- Mod Engine 3 (base-game file; Convergence already includes it)
- For the Convergence file only: The Convergence 3.0.3.0
- Optional: Seamless Co-op

## Description (BBCode — paste into the description editor in BBCode mode)

```
[size=5][b]Seeker's Compass[/b][/size]

Never miss the gear your build wants. Seeker's Compass marks every weapon, spell, armor piece and talisman lying in the world on your map, filtered to the damage stats you choose. Hover a mark to see what the item does.

[size=4][color=#e06666][b]Offline only - official servers are blocked[/b][/color][/size]
[list]
[*]The mod only runs through [b]Mod Engine 3[/b], which starts Elden Ring [b]without Easy Anti-Cheat[/b]. Without EAC the game cannot connect to official servers. Launching from Steam normally loads none of this mod.
[*]The mod uses its [b]own save file[/b] ([i]ER0000.skc[/i]; Seamless Co-op and Convergence use their own saves too). Your normal online save is never touched, so a modded character can't end up on official servers.
[*]Mod Engine 2 is not supported, because it can't keep a separate save.
[/list]

[size=4][b]How it works[/b][/size]
[list=1]
[*]Buy the [b]Seeker's Compass[/b] from [b]Merchant Kalé[/b] at the Church of Elleh (100 runes).
[*]Rest at any Site of Grace and choose [b]Seeker's Compass[/b].
[*]Switch on the damage stats and item types you care about. Active lines glow, just like the Flasks upgrade indicator.
[*]Open your map. Matching items are marked.
[/list]

[size=4][b]Filters[/b][/size]
[list]
[*][b]Stats:[/b] Strength, Dexterity, Intelligence, Faith, Arcane
[*][b]Item types:[/b] Weapons, Spells, Armor, Talismans
[*]Stats only: matching items of every type.
[*]Types only: every item of those types.
[*]Both: only matching items within the chosen types (e.g. Faith + Weapons = Faith weapons).
[/list]
Weapons count for every stat they scale with at grade D or better. Spells count for every stat they require. Armor counts for stats it raises (e.g. Olivinus Glintstone Crown, INT +3) or a build it clearly belongs to.

[size=4][b]Map marks[/b][/size]
[list]
[*]Hand-drawn style icons: sword (weapons), tome (spells), helm (armor), pendant (talismans).
[*]Hover a mark for details:
[list]
[*]Weapons: scaling grade and requirement per stat, e.g. [i]Claymore | STR D 16, DEX D 13[/i]
[*]Spells: requirements and FP cost, e.g. [i]Night Comet | INT 38, 21 FP[/i]
[*]Armor: slot and stat bonuses, e.g. [i]Olivinus Glintstone Crown | Head, INT +3[/i]
[*]Talismans: their effect
[/list]
[*]Items you've already picked up stay marked as [i](collected)[/i], with full details.
[*]Items inside ruins, caves and catacombs sit on that location's icon, next to any NPC marker, instead of hiding underneath it.
[/list]

[size=4][b]Coverage[/b][/size]
Base game and Shadow of the Erdtree world pickups and unique enemy drops. Items from bosses, remembrances, shops and invaders have no fixed spot in the world, so they aren't marked.

[size=4][b]Convergence[/b][/size]
A separate file is built on top of [b]The Convergence 3.0.3.0[/b] and marks Convergence's own weapons and armor with Convergence's scaling. Convergence hands out most spells and talismans through its own events and shops, so in Convergence the map marks mostly weapons and armor.

[size=4][b]Installation[/b][/size]
[b]Base game[/b]
[list=1]
[*]Install [url=https://github.com/garyttierney/me3/releases]Mod Engine 3[/url].
[*]Extract [b]SeekersCompass-x.y.z.zip[/b] to its own folder (not inside ELDEN RING\Game).
[*]With Steam running, double-click [b]Launch_SeekersCompass.bat[/b].
[/list]
The launcher handles the rest, with nothing to edit:
[list]
[*][b]Seamless Co-op[/b] is found automatically and used, with your Seamless characters. To play solo anyway, run [i]Launch_SeekersCompass.bat -NoSeamless[/i].
[*][b]Without Seamless[/b], the first launch offers to bring your existing characters over. It copies your normal save into the mod's own save and never changes the original.
[/list]

[b]Convergence[/b]
[list=1]
[*]Extract [b]SeekersCompass-x.y.z-Convergence-3.0.3.0.zip[/b] into your [b]ConvergenceER[/b] folder (the one with Start_Convergence.bat).
[*]Run [b]Install_SeekersCompass.bat[/b] once. It adds Seeker's Compass to Convergence's own launch profiles (with and without Seamless Co-op) and backs them up first.
[*]Launch Convergence the way you normally do.
[/list]
If a Convergence update replaces its launch profiles and the grace option disappears, run Install_SeekersCompass.bat again.

[size=4][b]Uninstall[/b][/size]
Delete the extracted folder. Convergence: run Uninstall_SeekersCompass.bat first, then delete the Seeker's Compass files. Back up your save before installing or removing any mod.

[size=4][b]Compatibility[/b][/size]
[list]
[*]Works with Seamless Co-op. Everyone in a session should run the same version.
[*]Offline / Seamless Co-op only (see the top of this page).
[*]Conflicts with mods that replace [i]regulation.bin[/i], the grace menu talk script ([i]script/talk/m00_00_00_00[/i]), item/menu text ([i]msg/engus[/i]) or [i]menu/*/01_common[/i]. Convergence players: use the Convergence file.
[*]The Convergence file only works with the Convergence version it was built for.
[/list]

[size=4][b]Credits[/b][/size]
[list]
[*]SoulsFormatsNEXT and Paramdex (soulsmods) for reading and writing game files
[*]DarkScript3 (AinTunez) for the event-script instruction definitions
[*]Mod Engine 3 team
[*]The Convergence team
[*]DirectXTex texconv (Microsoft) for texture conversion
[*]Item and map icon art generated with ChatGPT
[/list]
```

## File descriptions (one per upload)

**SeekersCompass-1.0.0.zip** (Main file)
> Seeker's Compass for the base game and Shadow of the Erdtree (tested with the DLC installed). Requires Mod Engine 3. Run Launch_SeekersCompass.bat; offline only, with its own save file.

**SeekersCompass-1.0.0-Convergence-3.0.3.0.zip** (Main file)
> Seeker's Compass built on top of The Convergence 3.0.3.0. Extract into your ConvergenceER folder, run Install_SeekersCompass.bat once, then launch Convergence as usual.

## Images

Upload from `release/images/` (1920x1080). Suggested order:
1. Map_FilteredItem.jpg — hero image (hover tooltip on a map mark)
2. SeekersCompass_Filters.jpg — the filter menu with glowing active lines
3. SeekersCompass_SiteofGrace.jpg — the Seeker's Compass entry in the grace menu
4. SeekersCompass_Inventory.jpg — buying the compass from Kalé
5. SeekersCompass_Map_Purchase_Location.jpg — where to buy it

## Changelog

**1.0.0** — First release.

## Before you publish

- Set **Permissions** on Nexus (e.g. whether others may reupload or modify).
- Tick the **AI-generated content** disclosure: the icons were made with ChatGPT, and the mod was built with AI coding help.
