# Uma Desktop Pet

i wanted proper umamusume desktop pets, so i made them 🥕

you can choose Oguri Cap or Agnes Tachyon, click her, pat her, pick her up,
feed her carrot jelly, study with her, and decorate her desk.

each Uma has her own idle and tap animations. they also react on their own
with richer randomized idles that wait until they are free and cleanly stop
when you interact, open the menu, or begin Study.

Tachyon can occasionally run her automatic two-step **Experiment** signature
idle: research, then breakthrough, at a real modeled 3D laboratory workstation.
the packaged workstation is a compact solid-top 3D side table adapted from
Kenney's CC0 **Furniture Kit 2.0** `table` model. it is recolored for Tachyon's
work surface and has no backboard or canopy. its exact source, license, and
source hash are recorded in
`THIRD_PARTY_NOTICES.txt`. five exact reagent-bottle props and five Tachyon
formula effects are read at runtime from the installed game and placed on or
around that work surface. both regions use the same workstation and Experiment
recipe. no laptop, display, celestial orb, PNG, or other flat background image
is used, and no game asset is copied or packaged. the scene is not a manual
activity, does not spend or award currency or items, and does not create a
research outcome.

## download

open [Releases](../../releases) and download:

- `UmaDesktopPet-v0.1.0-preview.6-windows-x64.zip`
- `UmaDesktopPet-v0.1.0-preview.6-windows-x64.zip.sha256`

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

- click your Uma for her character-specific reaction
- hold left click to pat her
- drag her to pick her up and move her
- right click to open Home, Study, Shop, and Settings
- choose **Settings > Desktop Uma** to switch between Oguri Cap and Agnes
  Tachyon; your selection is saved for the next launch
- complete a 25- or 50-minute study session to earn Moni and Carrot Jelly
- use Moni in the Shop and equip owned desk items from Inventory
- resize your Uma and the menu from an edge or corner while the menu is open
- press `Esc` to cancel or close the menu

study timers run while the app is open. if the app closes during a session,
the session reopens paused.

Tachyon's Experiment happens automatically while she is idle. quiet mode,
an active Study session, and user interaction suppress or interrupt it. after
an interruption, the temporary 3D props are cleaned up and Tachyon returns to normal
idle; there is no Experiment button in Home or Study.

## if your Uma or Tachyon's Experiment doesnt load

finish Download All in the JP or Global game, close umamusume, then choose
**Settings > Reload game files**. if the installation moved, choose
**Change game files** instead.

you can select the game folder, `umamusume.exe`, `umamusume_Data`, or the
`Persistent` folder.

if it still breaks, open an Issue and tell me whether you use jp or global.
send this log:

```text
%USERPROFILE%\AppData\LocalLow\pqqqqqdev\Uma Desktop Pet\Player.log
```

please send the log, not your game files.

## build from source

this project uses Unity `2022.3.62f2`.

```powershell
.\Tools\install-dependencies.ps1
.\Tools\build-windows.ps1 -UnityPath "C:\path\to\2022.3.62f2\Editor\Unity.exe"
$version = Read-Host "Release version"
.\Tools\package-windows.ps1 -Version $version
```

fan project only. not affiliated with cygames. no umamusume assets are included
in the download or exported by the app; the umamusume characters, motions, and
installed reagent props and effects it uses are loaded at runtime from your own
local JP or Global installation.
