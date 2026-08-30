# Clean-PC Tachyon update test

Test the exact same candidate ZIP on Windows 10 or 11 x64 with a legitimate JP
installation and a legitimate Global installation. Complete **Download All**
in each game first, then close it. Do not use exported or copied game assets.

1. Download the ZIP and `.sha256` from this repository's Releases page.
2. Verify the ZIP hash with `Get-FileHash`.
3. Extract the complete ZIP. Do not run the EXE from inside the archive.
4. Run `UmaDesktopPet.exe`, select the region's game files, and complete setup
   if it appears.
5. Run the full Tachyon matrix below before moving to the other region.
6. Repeat from step 4 with the other region using the same extracted candidate.

## JP and Global Tachyon matrix

Mark both columns only after observing the behavior in that region. Experiment
is randomized and may not be the first autonomous reaction; allow autonomous
reactions to continue rather than using a recording or diagnostic trigger.

| Check | JP | Global |
| --- | --- | --- |
| **Settings > Desktop Uma** lists Oguri Cap and Agnes Tachyon. Selecting Tachyon closes the menu, reloads the scene, and shows Tachyon. | [ ] | [ ] |
| Relaunching restores Tachyon without changing the selected installation. Switching back to Oguri and relaunching restores Oguri; select Tachyon again for the remaining checks. | [ ] | [ ] |
| Tachyon's installed character-specific idle plays normally, and a short click plays her installed character-specific tap reaction before returning to idle. | [ ] | [ ] |
| While left alone and not in quiet mode or Study, Tachyon eventually begins **Experiment** automatically. There is no Experiment button or new activity in Home or Study. | [ ] | [ ] |
| Experiment uses the packaged compact solid-top 3D side table adapted from Kenney's CC0 **Furniture Kit 2.0** `table`, recolored for Tachyon's laboratory palette and with no backboard or canopy, plus all five exact installed reagent-bottle props and, across its phases, all five exact installed Tachyon formula effects. No laptop, display, celestial orb, PNG, or other flat background image appears. | [ ] | [ ] |
| Inspect the scene from more than one angle: every table leg is grounded, every bottle visibly contacts the opaque worktop, nothing clips or floats, and the formula effects support the motion without obscuring Tachyon. | [ ] | [ ] |
| JP and Global use the same workstation mesh and installed-asset recipe; neither region adds optional laptops, displays, orbs, whiteboards, or equipment racks. | [ ] | [ ] |
| A complete Experiment removes every temporary 3D prop, creates no Moni, Carrot Jelly, item, pending reward, or research result, and returns to normal idle. | [ ] | [ ] |
| During another Experiment, click, pat, drag, or open the menu. The scene stops cleanly, every temporary prop disappears, no pose freezes, and Tachyon returns to a valid idle or requested interaction. | [ ] | [ ] |
| With quiet mode enabled, autonomous reactions remain suppressed. With a Study session active, Experiment remains suppressed and the ordinary Study desk and motion behave normally. | [ ] | [ ] |
| After finishing **Download All**, **Settings > Reload game files** keeps Tachyon and Experiment available. If the installation path changes, **Change game files** accepts the new JP or Global root. | [ ] | [ ] |

## shared regression checks

Run these checks in both regions after the Tachyon matrix:

1. Check click, hold-to-pat, drag/release, right-click, carrot feeding, the
   animated Mood arrow, quiet mode, settings, and quit with each selectable Uma.
2. Open Study. Start, pause, resume, and stop a session. Confirm the desk and
   study motion appear only while the session is active.
3. Confirm a completed session applies its Energy and Carrot Jelly changes,
   then lets you collect the pending Moni.
4. Open Shop and Inventory. Buy, equip, and put away a desk item, then confirm
   the desk collection footer updates.
5. Buy and equip **Tazuna's Red Pen**, then begin Study with each Uma. Confirm the
   pen is clearly visible, larger than before, diagonally placed, red rather than
   green/white, and does not clip or float. Confirm no other Study prop is
   recolored, then put the pen away and confirm it disappears.
6. With the menu open, resize from the window edges and corners. Confirm the
   selected Uma and the menu scale together. Close the menu and confirm resizing
   is disabled and the transparent pet-only window remains movable.
7. Relaunch and confirm the selected installation, selected Uma, care state,
   study state, Moni, and owned/equipped desk items persist.
8. Disconnect from the network and relaunch once to confirm local-only use.
9. Audit the extracted candidate and confirm it contains no umamusume models,
   textures, motions, 3D props, databases, bundles, or other game assets.
10. Confirm `Licenses/KenneyFurnitureKit-CC0-1.0.txt` is present and identifies
   Kenney's Furniture Kit 2.0 and its CC0 1.0 license. Confirm the packaged
   `THIRD_PARTY_NOTICES.txt` identifies `table`, the canonical source page,
   selected FBX hash, and UmaDesktopPet modifications.

If anything fails, save a screenshot and this log before relaunching:

`%USERPROFILE%\AppData\LocalLow\pqqqqqdev\Uma Desktop Pet\Player.log`

Include the Windows version, JP or Global region, selected Uma, and whether
setup found the game automatically. Do not upload any game files.
