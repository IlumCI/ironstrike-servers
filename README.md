# Ironstrike Servers

A community server browser and host tools for modded IRONSTRIKE.

Vanilla IRONSTRIKE gives modded players one way to play together: a Private Match code. This
plugin adds a proper server list, host controls and joining by code. Modded servers stay completely
separate from everyone else:

- **Invisible to unmodded players.** Modded servers are published in their own lobby. The game
  only ever lists its own, so a player without this mod never sees them.
- **Unjoinable without the same mods.** Every joiner sends a small token. The host refuses anyone
  whose token is missing or wrong: an unmodded player is refused even if they type the code, and
  so is anyone whose mod set differs from the host's.
- **Public matchmaking stays locked.** Play and HOST are disabled while the mod is loaded, as the
  developer asked. Modded play goes through this browser instead.

## Features

**Browsing**
- Server list with player count, description, password and full/closed markers, plus whether the
  server runs the same mods as you.
- Filters: hide full or closed servers, passworded ones, or ones with other mods; show favourites
  only.
- Sort by players, by name, or favourites first.
- Region select (US, EU, Asia and others). Servers are listed per region.
- Join by code.
- Favourites and recently joined servers. A favourite stays joinable by its code even when it is
  not currently listed.

**Hosting**
- Name, description, optional password and max players.
- Open or close the server to new players at any time.
- A player list with **kick** and **ban**. A ban applies to that player's install, not their Steam
  account, and lasts until you clear the list.
- Ruleset in the listing: your installed mods, plus the trainer's active cheats if
  [Ironstrike Trainer](https://github.com/IlumCI/ironstrike-trainer) is loaded.
- Optionally keep the same code every time you host, so your friends' favourites keep working.

**The window** works like the classic Steam server browser:
- Tabs for Internet, Favorites, History and Host.
- A sortable table with columns for password, favourite, name, mode, players, mods (match or
  differ), cheats, region, uptime and version.
- An info line for the selected server.
- Filters along the bottom, next to Join by code, Refresh and Connect.
- Click a server to select it; click it again (or press Connect) to join.

**Getting there:** while public play is locked, the main menu's **Play** card becomes **Servers**
(it opens the browser) and its **HOST** pill hosts a modded server. **F7** opens the browser
anywhere. Text entry uses the game's own VR keyboard.

## Install

1. Install BepInEx 6 IL2CPP, build **be.788**:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip).
   Extract **all six** items (`winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`,
   `changelog.txt`, `dotnet/`, `BepInEx/`) directly next to `Ironstrike.exe`.
2. Start the game once and wait for the main menu. The first start takes a minute or so while
   BepInEx sets itself up.
3. Drop `IronstrikeServers.dll` into `BepInEx/plugins/`.

If nothing happens on launch, run `bepinex-doctor.ps1` from this repo in PowerShell.

On Linux under Proton, add `WINEDLLOVERRIDES="winhttp=n,b" %command%` to the Steam launch options.

## Playing with friends

Everyone needs the **same set of mods**, at the same versions. The browser tells you when a server's
mods differ from yours, and lists theirs.

- **Host:** SERVERS → Host a server → set a name and, if you like, a password → Start server. Your
  code is under "Your server" (hidden until you tap it, in case you are streaming).
- **Join:** SERVERS → pick the server from the list, or use Join by code.

## Config

`BepInEx/config/eu.euroswarms.ironstrike.servers.cfg`. Everything is also settable in game.

| Section | Keys |
| --- | --- |
| 01 General | `Enabled`, `SafeMode`, `EnableHotkeys` |
| 02 Network | `Region` |
| 03 Host | `ServerName`, `Description`, `Password`, `MaxPlayers`, `Open`, `PersistentCode` |
| 04 Browser | `HideFull`, `HidePassworded`, `HideModMismatch`, `OnlyFavorites`, `SortBy` |
| 05 Lists | `Favorites`, `Recent` (managed by the mod) |
| 06 Identity | `InstallId`, `BannedIds` |

The config holds join codes and your password, so **don't share the config file**. The log
(`BepInEx/LogOutput.log`) never contains codes, passwords or player names, so it is safe to attach
to a bug report.

## How it works

The game starts multiplayer sessions with Photon Fusion. When you host or join, this plugin goes
through the game's own Private Match path, so the fade, scene loading and spawning are all the
game's. It changes only the final `NetworkRunner.StartGame` call, adding:

- `CustomLobbyName = "ism-<protocol>"`, which keeps the session out of the game's lobby.
- A `ConnectionToken` with a protocol version, a hash of the mod set, a salted password hash and a
  random install id.
- Session properties for the listing (name, description, mods, rules).

The host checks the token in `NetworkLogic.OnConnectRequest` before the game's own checks run.

The browser lists the lobby from a second runner, created from the game's own runner prefab, while
you are still in the haven.

Modded sessions use the game's own Photon app, the same as a vanilla private match.

## Building

```
dotnet build IronstrikeServers/IronstrikeServers.csproj -c Release
```

The reference assemblies are committed in `refs/`. `./build.sh <gameDir>` or
`.\build.ps1 -GameDir <gameDir>` builds against the game's own interop and deploys. CI builds every
push, and tags `v*` publish a release.

## License

AGPL-3.0.
