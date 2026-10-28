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
- Mod Engine 3 (base-game file, options A and B; Convergence already includes it). Option C (no Mod Engine) needs nothing else.
- For the Convergence file only: The Convergence 3.0.4.0
- Optional: Seamless Co-op

## Description (BBCode — paste into the description editor in BBCode mode)

```
[size=5][b]Seeker's Compass[/b][/size]

Never miss the gear your build wants. Seeker's Compass marks every weapon, spell, armor piece and talisman lying in the world on your map, filtered to the damage stats you choose. Hover a mark to see what the item does.

[size=4][color=#e06666][b]Offline only - official servers are blocked[/b][/color][/size]
[list]
[*]The mod only runs when Elden Ring starts [b]without Easy Anti-Cheat[/b]: through Mod Engine 3, Seamless Co-op's launcher, or an offline start. Without EAC the game cannot connect to official servers. Whenever EAC is running, Seeker's Compass stays off.
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

[size=4][b]Languages[/b][/size]
Works in all 15 of the game's languages. Item names, stat names, armor slots and menu words use the game's own official translations; only the compass's name and description and the "(collected)" tag were translated for this mod (corrections welcome). Arabic shows those in English.

[size=4][b]Coverage[/b][/size]
Base game and Shadow of the Erdtree world pickups and unique enemy drops. Items from bosses, remembrances, shops and invaders have no fixed spot in the world, so they aren't marked.

[size=4][b]Convergence[/b][/size]
A separate file is built on top of [b]The Convergence 3.0.4.0[/b] and marks Convergence's own weapons and armor with Convergence's scaling. Convergence hands out most spells and talismans through its own events and shops, so in Convergence the map marks mostly weapons and armor.

[size=4][b]Installation[/b][/size]
[b]Base game[/b]
Extract [b]SeekersCompass-x.y.z.zip[/b] to its own folder (not inside ELDEN RING\Game), then pick the option that matches how you play.

[b]A) With Mod Engine 3, on its own (with or without Seamless Co-op):[/b] install [url=https://github.com/garyttierney/me3/releases]Mod Engine 3[/url], then with Steam running double-click [b]Launch_SeekersCompass.bat[/b]. Nothing to edit:
[list]
[*][b]Seamless Co-op[/b] is found automatically and used, with your Seamless characters. To play solo anyway, run [i]Launch_SeekersCompass.bat -NoSeamless[/i].
[*][b]Without Seamless[/b], the first launch offers to bring your existing characters over. It copies your normal save into the mod's own save and never changes the original.
[/list]
[b]B) With Mod Engine 3, alongside other mods you already launch through it:[/b] run [b]Install_SeekersCompass.bat[/b] and pick that setup's [i].me3[/i] profile (or drag profiles onto it), then keep launching the way you already do. It adds one marked block and keeps a backup. It refuses profiles where it would break another mod (mods that change regulation.bin or the same files) or that switch online play on. If the profile would play on your normal save, it offers to give it its own save. [b]Uninstall_SeekersCompass.bat[/b] takes it back out.

[b]C) Without Mod Engine (Seamless Co-op's own launcher, Elden Mod Loader setups):[/b] close the game and run [b]Install_Without_ModEngine.bat[/b]. It finds Elden Ring through Steam and adds [i]xinput1_4.dll[/i] and a [i]SeekersCompass[/i] folder to ELDEN RING\Game. Then start the game the way you normally do:
[list]
[*][b]Seamless Co-op's launcher (ersc_launcher.exe):[/b] Seeker's Compass loads alongside Seamless, with your Seamless characters.
[*][b]eldenring.exe directly, or Steam with an anti-cheat toggler:[/b] on the mod's own save, which starts as a copy of your characters.
[*]It stays off whenever Easy Anti-Cheat is running or Mod Engine started the game, so Convergence and A/B setups are unaffected. It loads at game start by itself, so Elden Mod Loader's load delay doesn't matter.
[*]The file loading is Mod Engine 3's own open-source code, built into a DLL; it keeps the game offline the same way. Source: see the GitHub link below.
[*]Like any DLL in the game folder, Easy Anti-Cheat may refuse to start the normal online game while it's installed: run [b]Uninstall_Without_ModEngine.bat[/b] before playing online. If another mod already uses [i]xinput1_4.dll[/i], use A or B.
[/list]

[b]Convergence[/b]
[list=1]
[*]Extract [b]SeekersCompass-x.y.z-Convergence-3.0.4.0.zip[/b] into your [b]ConvergenceER[/b] folder (the one with Start_Convergence.bat).
[*]Run [b]Install_SeekersCompass.bat[/b] once. It adds Seeker's Compass to Convergence's own launch profiles (with and without Seamless Co-op) and backs them up first.
[*]Launch Convergence the way you normally do.
[/list]
Convergence updates can replace the whole ConvergenceER folder. After one, get the Seeker's Compass file for that Convergence version, extract it again and run Install_SeekersCompass.bat.

[size=4][b]Uninstall[/b][/size]
Option B: run Uninstall_SeekersCompass.bat. Option C: run Uninstall_Without_ModEngine.bat. Then delete the extracted folder. Convergence: run Uninstall_SeekersCompass.bat first, then delete the Seeker's Compass files. Back up your save before installing or removing any mod.

[size=4][b]Compatibility[/b][/size]
[list]
[*]Works with Seamless Co-op. Everyone in a session should run the same version.
[*]Works alongside other Mod Engine 3 mods that don't replace the same files (e.g. cosmetic parts mods and DLL mods): add it to their profile with Install_SeekersCompass.bat.
[*]Works with Elden Mod Loader DLL mods (option C, or Mod Engine 3 natives with A/B).
[*]Offline / Seamless Co-op only (see the top of this page).
[*]Conflicts with mods that replace [i]regulation.bin[/i], the grace menu talk script ([i]script/talk/m00_00_00_00[/i]), item/menu text ([i]msg/engus[/i]) or [i]menu/*/01_common[/i]. Convergence players: use the Convergence file.
[*]The Convergence file only works with the Convergence version it was built for.
[/list]

[size=4][b]Credits[/b][/size]
[list]
[*]SoulsFormatsNEXT and Paramdex (soulsmods) for reading and writing game files
[*]DarkScript3 (AinTunez) for the event-script instruction definitions
[*]Mod Engine 3 (garyttierney and contributors): the no-Mod Engine option reuses its file-loading, save and Arxan code (MIT/Apache-2.0)
[*]Source: https://github.com/kellenmalonehr-dev/SeekersCompass
[*]The Convergence team
[*]DirectXTex texconv (Microsoft) for texture conversion
[*]Item and map icon art generated with ChatGPT
[/list]
```

## File descriptions (one per upload)

**SeekersCompass-1.2.0.zip** (Main file)
> Seeker's Compass for the base game and Shadow of the Erdtree (tested with the DLC installed). Three ways to play: Launch_SeekersCompass.bat (Mod Engine 3), Install_SeekersCompass.bat to add it to a Mod Engine 3 profile you already use, or Install_Without_ModEngine.bat for Seamless Co-op's own launcher and other setups without Mod Engine. Offline only, never on your normal save.

**SeekersCompass-1.2.0-Convergence-3.0.4.0.zip** (Main file)
> Seeker's Compass built on top of The Convergence 3.0.4.0. Extract into your ConvergenceER folder, run Install_SeekersCompass.bat once, then launch Convergence as usual.

## Images

Upload from `release/images/` (1920x1080). Suggested order:
1. Map_FilteredItem.jpg — hero image (hover tooltip on a map mark)
2. SeekersCompass_Filters.jpg — the filter menu with glowing active lines
3. SeekersCompass_SiteofGrace.jpg — the Seeker's Compass entry in the grace menu
4. SeekersCompass_Inventory.jpg — buying the compass from Kalé
5. SeekersCompass_Map_Purchase_Location.jpg — where to buy it

## Changelog

**1.2.0** — Play without Mod Engine: Install_Without_ModEngine.bat lets Seamless Co-op's own launcher (and other non-Mod Engine setups) load Seeker's Compass. Install_SeekersCompass.bat adds it to any Mod Engine 3 profile you already launch, with safety checks. The Convergence file is rebuilt for The Convergence 3.0.4.0.

**1.1.0** — All 15 game languages: hover text uses each language's item names and the game's own stat, armor slot and menu words.

**1.0.0** — First release.

## Before you publish

- Set **Permissions** on Nexus (e.g. whether others may reupload or modify).
- Tick the **AI-generated content** disclosure: the icons were made with ChatGPT, and the mod was built with AI coding help.
- 1.2.0 ships two DLLs (GameFolder/xinput1_4.dll and seekers_compass_loader.dll), which will likely trigger Nexus' file review again. Point the reviewer at src/SeekersCompass.Loader in the GitHub source (push the 1.2.0 commit first): the proxy is ~150 lines of C and the loader is Mod Engine 3 v0.13.0's own mod-host code.
