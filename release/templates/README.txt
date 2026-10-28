SEEKER'S COMPASS
================

Marks weapons, spells, armor and talismans on the world map, filtered by the damage
stats you choose. Buy the Seeker's Compass from Merchant Kale at the Church of Elleh
(100 runes), then pick "Seeker's Compass" at any Site of Grace.

REQUIREMENTS
- Elden Ring (Steam)
- For options A and B: Mod Engine 3: https://github.com/garyttierney/me3/releases
- Option C needs nothing else.

INSTALL
Extract this zip to its own folder (not inside the ELDEN RING\Game folder), then pick
the option that matches how you play.

A) With Mod Engine 3, Seeker's Compass on its own (with or without Seamless Co-op)
   Install Mod Engine 3. With Steam running, double-click Launch_SeekersCompass.bat:
   - Seamless Co-op: found automatically and used, with your Seamless characters.
     (To play solo anyway, run:  Launch_SeekersCompass.bat -NoSeamless)
   - Without Seamless: the first time, it offers to bring your existing characters over.
     It COPIES your normal save into the mod's own save (ER0000.skc); your normal save
     is never changed.

B) With Mod Engine 3, added to a profile you already launch
   (another mod's launcher that uses Mod Engine 3, or a profile you made yourself)
   Run Install_SeekersCompass.bat and choose the profile's .me3 file (or drag .me3 files
   onto it). Then keep launching the way you already do. The installer:
   - adds one marked block to the profile and keeps a backup (*.before-seekers-compass);
   - refuses profiles whose mods change regulation.bin or the same files as Seeker's
     Compass (it would break them - those need a Seeker's Compass version built for that mod),
     and profiles with online play switched on;
   - if the profile would play on your normal save, offers to give it its own save
     (ER0000.skc, which Mod Engine 3 starts as a copy of your characters).
   Run it again if you move this folder or a mod update replaces its profile.
   Uninstall_SeekersCompass.bat takes it out of every profile it was added to.

C) Without Mod Engine (e.g. Seamless Co-op's own launcher, Elden Mod Loader setups)
   Close the game and run Install_Without_ModEngine.bat. It finds Elden Ring through Steam
   and adds xinput1_4.dll and a SeekersCompass folder to ELDEN RING\Game. Then start the
   game the way you normally do:
   - Seamless Co-op's launcher (ersc_launcher.exe): with your Seamless characters.
   - eldenring.exe directly, or Steam with an anti-cheat toggler: on the mod's own save
     (ER0000.skc), which starts as a copy of your characters.
   It stays off whenever Easy Anti-Cheat is running or Mod Engine started the game (so
   Convergence and option A/B setups are unaffected). It loads at game start by itself,
   so Elden Mod Loader's load_delay doesn't matter. The file swapping is Mod Engine 3's
   own code (see GameFolder\ModEngine3-LICENSE-*), so it keeps the game offline the same way.
   Run Install_Without_ModEngine.bat again to update; Uninstall_Without_ModEngine.bat
   removes both. If another mod already uses Game\xinput1_4.dll, use option A or B.
   Like any DLL in the game folder, Easy Anti-Cheat may refuse to start the normal online
   game while it's installed: uninstall it before playing online.

OFFLINE ONLY - YOUR ONLINE SAVE IS PROTECTED
- Seeker's Compass only runs when the game starts without Easy Anti-Cheat, so the modded
  game cannot connect to official servers.
- The mod never writes to your normal online save (ER0000.sl2): it plays on its own
  save, or on Seamless Co-op's save when Seamless is used.
- Mod Engine 2 is not supported: it has no separate-save option.

UNINSTALL
Option B: run Uninstall_SeekersCompass.bat. Option C: run Uninstall_Without_ModEngine.bat.
Then delete the extracted folder. Your normal save is unaffected.

NOTES
- Conflicts with mods that also replace regulation.bin, the grace menu talk script
  (script/talk/m00_00_00_00), the item/menu text (msg/*) or menu/*/01_common.
  Convergence players: use the Convergence version of this mod instead.
- Seamless Co-op's own launcher only loads Seamless itself; with option C installed,
  Seeker's Compass loads alongside it.
