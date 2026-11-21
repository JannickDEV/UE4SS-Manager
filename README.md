# UE4SS Manager

An installer and mod manager for [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) (RE-UE4SS), in the spirit of the MelonLoader installer: it finds your Unreal Engine games, lets you pick a UE4SS version, installs or updates it, and manages your Lua and C++ mods.

One self-contained `UE4SS-Manager.exe` for Windows 10/11, no .NET install needed.

## Features

**Finds your games**
- Steam: every library from `libraryfolders.vdf`, named from the app manifests
- GOG (offline installers and Galaxy, from the registry) and Epic Games Store manifests
- Library folders you add in Options (for example `E:\GOG`); common spots like `X:\GOG Games` are checked automatically
- Add any game by hand by picking its exe
- A game counts as Unreal when it has `<Project>\Binaries\Win64\*-Shipping.exe` (WinGDK for Game Pass builds), even when the project sits a few folders deep
- Shows the engine version from the exe and whether UE4SS is installed
- Right-click a game to hide it. Hidden games have no section of their own: press Ctrl+H in the window to switch the list to them (and back), where right-click > Unhide brings one back

**Installs UE4SS**
- Stable releases or the rolling `experimental-latest` build, straight from GitHub
- Normal or zDEV (developer) build
- Or install from a release zip you downloaded yourself
- Update keeps `UE4SS-settings.ini` (the new default is saved as `UE4SS-settings.ini.new`), your `mods.txt` / `mods.json` choices and disabled mods, and backs them up first
- Moving from the 2.x layout (everything in `Win64`) to the 3.x layout (`Win64\ue4ss`) carries your mods and settings over
- Uninstall either keeps your mods and settings or removes everything
- Records what it installed in `Win64\ue4ss-manager.json`, so updates and uninstalls only touch UE4SS files. Installs made by hand are detected too.
- Offers to restart as administrator when a game lives under Program Files, and refuses to touch files while the game is running

**Manages mods**
- Lists Lua (`Scripts\main.lua`) and C++ (`dlls\main.dll`) mods with their on/off state
- Toggle a mod: updates `mods.txt`, `mods.json` and `enabled.txt` the way UE4SS reads them, new entries go above `Keybinds`
- Install from zips or folders, or drag and drop them onto the Mods tab. Zips laid out as `MyMod\...`, `Mods\MyMod\...`, `ue4ss\Mods\MyMod\...` or a bare `Scripts\` all work. Reinstalling a mod keeps config files the new version doesn't ship.
- Delete mods, open their folders
- Quick switches for the common `UE4SS-settings.ini` options (console, GUI console, graphics API, hot reload)

## Building

Needs the .NET 8 SDK.

```
dotnet test
dotnet publish src/UE4SSManager.App -c Release -r win-x64 -o publish
```

`publish\UE4SS-Manager.exe` is the single-file build. GitHub Actions builds it on every push and pull request (download it from the run's artifacts) and attaches it to a release when a `v*` tag is pushed.

The app is WPF, so it only runs on Windows. `UE4SSManager.Core` (detection, releases, installer, mods) is plain .NET 8 and its tests run anywhere.

## Layout

- `src/UE4SSManager.Core`: game detection, GitHub releases, UE4SS install/update/uninstall, mod management
- `src/UE4SSManager.App`: the WPF window
- `tests/UE4SSManager.Core.Tests`: xUnit tests for the core

Settings live in `%AppData%\UE4SS-Manager`, downloads and backups in `%LocalAppData%\UE4SS-Manager`.
