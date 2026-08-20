# Clean-PC preview test

Test the release ZIP on Windows 10 or 11 x64 with a legitimate JP or Global
installation. Complete **Download All** in the game first, then close it.

1. Download the ZIP and `.sha256` from this repository's Releases page.
2. Verify the ZIP hash with `Get-FileHash`.
3. Extract the complete ZIP. Do not run the EXE from inside the archive.
4. Run `UmaDesktopPet.exe` and complete setup if it appears.
5. Check click, hold-to-pat, drag/release, right-click, carrot feeding, the
   animated Mood arrow, quiet mode, settings, and quit.
6. Open Study. Start, pause, resume, and stop a session. Confirm the desk and
   study motion appear only while the session is active.
7. Confirm a completed session applies its Energy and Carrot Jelly changes,
   then lets you collect the pending Moni.
8. Open Shop and Inventory. Buy, equip, and put away a desk item, then confirm
   the desk collection footer updates.
9. With the menu open, resize from the window edges and corners. Confirm Oguri
   and the menu scale together. Close the menu and confirm resizing is disabled
   and the transparent pet-only window remains movable.
10. Relaunch and confirm the selected installation, care state, study state,
    Moni, and owned/equipped desk items persist.
11. Disconnect from the network and relaunch once to confirm local-only use.

If anything fails, save a screenshot and this log before relaunching:

`%USERPROFILE%\AppData\LocalLow\pqqqqqdev\Uma Desktop Pet\Player.log`

Include the Windows version, JP or Global region, and whether setup found the
game automatically. Do not upload any game files.
