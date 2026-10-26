SEEKER'S COMPASS - CONVERGENCE VERSION
======================================

Built on top of The Convergence {CONVERGENCE_VERSION}. It includes Convergence's own changes to
the files it touches, so it must load after Convergence (the installer sets that up).

INSTALL
1. Extract this zip into your ConvergenceER folder (the one with Start_Convergence.bat).
   You should then have:
     ConvergenceER\SeekersCompass-Convergence\
     ConvergenceER\Install_SeekersCompass.bat
     ConvergenceER\Uninstall_SeekersCompass.bat
     ConvergenceER\SeekersCompass-setup.ps1
2. Run Install_SeekersCompass.bat once. It adds Seeker's Compass to Convergence's launch
   profiles (me3\convergence.me3 and me3\convergence - seamless.me3) and backs them up first.
3. Launch Convergence the way you normally do (Start_Convergence.bat).

AFTER A CONVERGENCE UPDATE
Convergence updates may replace its launch profiles. If the Seeker's Compass grace option
disappears, run Install_SeekersCompass.bat again. Also check for a matching Seeker's Compass
update: this version only matches Convergence {CONVERGENCE_VERSION}.

UNINSTALL
Run Uninstall_SeekersCompass.bat, then delete the files listed above. Saves are unaffected.

NOTES
- In Convergence, most spells and talismans are handed out by Convergence's own events and
  shops rather than as world pickups, so the map marks mostly weapons and armor there.
