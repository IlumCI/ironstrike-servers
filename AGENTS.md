# AGENTS.md: IronstrikeServers

These are notes for an agent working on this plugin. **Read the game-side `AGENTS.md` first**; it sits
next to `Ironstrike.exe` in the Linux analysis copy of the game. Its rules (§0) apply here unchanged:

- no AI attribution in commits
- no `sudo`
- never let a modded client reach public matchmaking
- no currency
- no private data in logs
- Windows-usable tooling

Facts marked **verified** were observed in the running game or in disassembly.

## 1. What this plugin guarantees

1. **Modded servers are invisible to vanilla.** They publish with
   `StartGameArgs.CustomLobbyName = "ism-<Protocol.Version>"`. The game only browses
   `SessionLobby.ClientServer`; in its `StartGame`, `JoinSessionLobby` is called with lobby enum 1
   and a null lobby id (**verified**, disassembly).
2. **Modded servers are unjoinable without the mod.** The host prefixes
   `NetworkLogic.OnConnectRequest` and refuses any connect request whose token fails
   `Protocol.Check`. The game never sets `ConnectionToken`, so a vanilla client is always refused.
   This also covers a vanilla player typing the code, since Photon joins by name across lobbies.
3. **Public matchmaking is locked** (`Lockout.cs`, the same mechanism as the trainer's SafeMode).
4. **A client leaves any session without the `x_ism` marker** (`Sessions.VerifyJoined`). Joining by
   code would otherwise land in an ordinary private match.

Do not weaken any of these.

## 2. How the game starts sessions (verified, disassembly of GameAssembly.dll)

| Flow | Calls |
| --- | --- |
| Private Match, enter code | `MainMenuUI.StartMultiplayer(code)` → `NetworkLifecycle.FadeToStartGame(GameMode.AutoHostOrClient, code)` |
| Private Match, other branch | `Random.Range(0, 999999).ToString()` → `FadeToStartGame(GameMode.Single, …)` |
| `NetworkLifecycle.StartGame` | async; its state machine `<StartGame>d__47.MoveNext` builds `StartGameArgs` on the stack, adds 4 `SessionProperty` values, then calls `NetworkRunner.StartGame(args)` **by pointer**. A separate branch calls `JoinSessionLobby(ClientServer)` for matchmaking. |
| `NetworkLogic.OnConnectRequest` | if `runner.IsServer` and `!GM.instance.NetLifecycle.acceptingConnections`, then `Refuse()`; otherwise further checks |

**`Fusion.GameMode` is 1-based in this Fusion build:** Single=1, Shared=2, Server=3, Host=4,
Client=5, AutoHostOrClient=6. The game-side AGENTS.md §9.3 lists them 0-based, which is wrong. Use
the enum names.

`SessionLobby`: Invalid=0, ClientServer=1, Shared=2, Custom=3.

## 3. Our injection

`Sessions.Start` sets `pending`, then calls `FadeToStartGame(AutoHostOrClient, code)`, exactly
like Private Match. A prefix on `NetworkRunner.StartGame(StartGameArgs)` takes `pending`, edits the
args and **re-issues** `__instance.StartGame(args)` under a re-entry guard. It returns false, with
the new task in `__result`.

- Re-issuing works whether the interop wrapper is a copy of the struct or aliases it.
- If editing throws, it does **not** fall through to vanilla args, because that would silently
  start a normal private match. It returns false with a null task instead.
- **Interop trap (verified):** *reading* a `Nullable<T>` property off `StartGameArgs`
  (e.g. `args.PlayerCount`) throws an NRE inside `Il2CppSystem.ValueType..ctor`. Setting one
  through `new Il2CppSystem.Nullable<T>(v)` is fine. The player cap comes from
  `NetworkProjectConfig.Global.Simulation.DefaultPlayers` instead.

## 4. The browser runner (verified working)

A second runner is created with `Object.Instantiate(GM.instance.NetLifecycle.RunnerPrefab)` and
kept with `DontDestroyOnLoad`. It calls
`JoinSessionLobby(SessionLobby.Custom, "ism-1", null, regionSettings, new Nullable<bool>())` and
receives `OnSessionListUpdated` while the haven session keeps running.

The prefab carries `NetworkLogic`. Prefixes on its `OnSessionListUpdated`, `OnShutdown`,
`OnConnectedToServer` and `OnDisconnectedFromServer` swallow callbacks for the browse runner, which
is recognised by pointer, or by GameObject name after `Close()`.

`useDefaultCloudPorts` must be a non-null `Il2CppSystem.Nullable<bool>`.

## 5. Protocol (`Protocol.cs`)

- **Exactly one session property of ours: `x_ism`.** Verified: Photon Fusion refuses more than **10
  custom session properties** ("Max number of Custom Session Properties reached, only 10 properties
  are allowed", which becomes `ShutdownReason.InvalidArguments` at StartGame). The game starts with 3
  and has up to 8 one-letter keys (`o, t, v, a, afk, k, r, public`), some set later in the session.
  Adding more properties of our own would break hosting or starve the game's own updates.
- **`x_ism` value:** fields joined by U+001F, in this order:
  1. version
  2. name
  3. description
  4. password (0/1)
  5. mod hash
  6. mod list
  7. rules
  8. mode
  9. start minute (unix minutes)
  10. mod version

  Append new fields; never reorder.
- **Token**, 29 bytes: `"ISMS"`, a version byte, the mod hash (8), `sha256("ism-pw\n"+code+"\n"+pw)[:8]`
  (zeros when there is no password), and the install id (8).
- **Mod hash:** sha256 over the sorted `GUID@version` of every BepInEx plugin, first 8 bytes.
- **Codes** are 8 digits, easy on the VR keyboard and disjoint from the game's codes (at most 6 digits).
- Bump `Protocol.Version` on any change to the token or the listing format. The lobby name follows
  it (`ism-<version>`), so versions never see each other.

## 5b. Session lifecycle (verified by disassembly of every call site)

- Every session start goes through `NetworkRunner.StartGame`. Menu buttons go
  `FadeToStartGame` → `StartGameFaded` → runner, skipping `NetworkLifecycle.StartGame`, so that
  choke point is where modded state is set (pending) or cleared (anything else).
- A session ends when its runner's `NetworkLogic.OnShutdown` fires (postfix; matched by runner
  pointer).
- Level changes inside a run do **not** restart the session: `GM.LoadAsyncSceneByIndex` only loads
  Unity scenes. Its one `StartGame` call is `GameMode.Server` (dedicated-server path). The tutorial
  starts `Single`.
- The game's own admission (`NetworkLogic.OnConnectRequest`) accepts when
  `NetworkLifecycle.acceptingConnections` is set, after pausing a solo play-while-you-wait run if
  needed. Our gate runs before it and only ever refuses.

## 6. Privacy

Codes, passwords and display names are never logged. Favourites and recents (codes) live only in
the config. The host page hides the code until tapped. Install ids are random and not tied to Steam.

## 7. Trainer interop

- The trainer's `Plugin.Allowed()` treats `IronstrikeServers.Api.InModdedSession` like a Private
  Match. It reads it by reflection (`ServersBridge`), so the two plugins do not depend on each other.
- The servers listing reads `IronstrikeTrainer.Api.ActiveRules()` by reflection.
- When the trainer is loaded, `Lockout` leaves button relabelling to it.

## 7b. UI (verified by screenshots of the running game)

- **Main menu takeover.** While `SafeMode` is on, the Play card and every button wired to
  `PressPlay`/`PressHost` are rebound (their `onClick` is replaced) to open the browser or the
  host tab. The card is relabelled "Servers". The original title's right edge is measured with
  `textBounds` and our title is right-aligned to it, because the title rect spans the whole card.
  The trainer finds the HOST pill by name (`HostButton`) once it is rebound.
- **Window.** A copy of the Options canvas, widened to 2600 x 1650. The game's art is hidden and the
  window is drawn classic-Steam style. Bevel frames are 8x8 textures with 2-texel 9-slice borders
  (`pixelsPerUnitMultiplier = 1/3`); icons are 16x16 pixel art made at runtime with `HideAndDontSave`.
- **Fonts.** Taken from the clone: AgenorNeue for body text, GemunuLibre-Bold for the title.
- **The window is per scene** (no `DontDestroyOnLoad`). A world-space canvas carried across a
  scene load keeps the old camera and stops taking clicks.
- **Views** are objects named `V_*`, rebuilt on any state change. The reused scroll view is moved
  to the last sibling so the table frame does not cover its rows.
- **Interop traps hit here:**
  - `ColorUtility.ToHtmlStringRGBA` is stripped ("Method unstripping failed"), so colours are
    formatted by hand.
  - The Options checkbox row has no description text, which is why the first row-based UI lost
    every second line.

## 8. Debug aids

`[07 Debug]`:
- `AutoOpenAfterSeconds` opens the browser for screenshots.
- `PreviewUi`, with `AutoOpenAfterSeconds`, shows the main menu, then cycles every view with mock
  data (logging `preview page: <name>`) so each can be screenshotted without input.
- `AutoHostSelfTest` hosts, then lists the lobby from the browse runner, to check the listing end to
  end without input.

Set both back afterwards. The game takes about 5 minutes to reach the haven under Proton on this
machine, so wait on the log rather than a fixed sleep.

## 9. Open work

- Verify a second machine joining: accepted with matching mods, refused when vanilla, refused with
  a wrong password or a mod mismatch.
- Verify kick and ban with a real second player.
- Check placement and clickability of the panel and keyboard in VR.
- The game's own `t` (run type) property is not yet decoded for the listing.
- When re-issuing `StartGame` fails, the game awaits a null task. It survived that in testing, but a
  cleaner failure path would help.
