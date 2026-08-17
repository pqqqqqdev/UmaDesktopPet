# Clean-PC preview test

Test the release ZIP on Windows 10 or 11 x64 with a legitimate JP or Global
installation. Complete **Download All** in the game first, then close it.

1. Download the ZIP and `.sha256` from this repository's Releases page.
2. Verify the ZIP hash with `Get-FileHash`.
3. Extract the complete ZIP. Do not run the EXE from inside the archive.
4. Run `UmaDesktopPet.exe` and complete setup if it appears.
5. Check click, hold-to-pat, drag/release, right-click, carrot feeding, quiet
   mode, settings, and quit.
6. Relaunch and confirm the selected installation and care state persist.
7. Disconnect from the network and relaunch once to confirm local-only use.

If anything fails, save a screenshot and this log before relaunching:

`%USERPROFILE%\AppData\LocalLow\pqqqqqdev\Uma Desktop Pet\Player.log`

Include the Windows version, JP or Global region, and whether setup found the
game automatically. Do not upload any game files.
