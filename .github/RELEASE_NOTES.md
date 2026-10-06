A community server browser and host tools for modded IRONSTRIKE, laid out like the classic Steam
server browser.

- **Browse** modded servers in a sortable table: name, mode, players, mods, cheats, region, uptime
  and version. Use the Favorites and History tabs, filters, region select and join by code.
- **Host** with a name, description, optional password and max players. Open or close the server
  at any time. Kick or ban players.
- **Players without the mod never see these servers and cannot join them.** Modded servers live in
  their own lobby, and the host refuses anyone whose mods differ.
- Public matchmaking (Play and HOST) stays locked while the mod is loaded. The Play card becomes
  **Servers** instead.

Everyone must run the **same mods at the same versions**. The browser shows when a server's mods
differ from yours. If you use the trainer, use **Ironstrike Trainer 1.0.3 or later**.

### Install

1. Install **BepInEx 6 IL2CPP**, this exact file:
   [BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip).
   All six items in it go directly beside `Ironstrike.exe`. Launch the game once and let it finish
   setting up.
2. Extract this release's zip over the game folder. It contains
   `BepInEx/plugins/IronstrikeServers.dll`.
3. In game, press **Servers** on the main menu, or **F7**.

On Linux/Proton, add `WINEDLLOVERRIDES="winhttp=n,b"` to the game's launch options.

This is an early release. Hosting and listing are tested; joining between two machines is being
tested now.
