# Uma Desktop Pet

i wanted a proper oguri desktop pet, so i made one 🥕

you can click her, pat her, pick her up, feed her carrot jelly, study with
her, and decorate her desk.

## download

open [Releases](../../releases) and download:

- `UmaDesktopPet-v0.1.0-preview.5-windows-x64.zip`
- `UmaDesktopPet-v0.1.0-preview.5-windows-x64.zip.sha256`

extract the whole ZIP to a short folder like:

```text
%USERPROFILE%\UmaDesktopPet
```

then run `UmaDesktopPet.exe`.

windows may show an unknown publisher warning because i havent code signed it yet.

## before you open it

- Windows 10 or 11, 64-bit
- jp or global umamusume installed
- finish Download All in the game settings
- close the game

if setup appears, select the game folder or its `Persistent` folder.

## controls

- click Oguri for a reaction
- hold left click to pat her
- drag her to pick her up and move her
- right click to open Home, Study, Shop, and Settings
- complete a 25- or 50-minute study session to earn Moni and Carrot Jelly
- use Moni in the Shop and equip owned desk items from Inventory
- resize Oguri and the menu from an edge or corner while the menu is open
- press `Esc` to cancel or close the menu

study timers run while the app is open. if the app closes during a session,
the session reopens paused.

## if Oguri doesnt load

finish Download All, close umamusume, then choose **Change game files...** from her settings.

you can select the game folder, `umamusume.exe`, `umamusume_Data`, or the `Persistent` folder.

if it still breaks, open an Issue and tell me whether you use jp or global. send this log:

```text
%USERPROFILE%\AppData\LocalLow\pqqqqqdev\Uma Desktop Pet\Player.log
```

please send the log, not your game files.

## build from source

this project uses Unity `2022.3.62f2`.

```powershell
.\Tools\install-dependencies.ps1
.\Tools\build-windows.ps1 -UnityPath "C:\path\to\2022.3.62f2\Editor\Unity.exe"
.\Tools\package-windows.ps1 -Version 0.1.0-preview.5
```

fan project only. not affiliated with cygames. no umamusume assets are included in the download; she loads them from your own local install.
