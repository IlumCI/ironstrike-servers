using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using HarmonyLib;
using UnityEngine;
using Il2CppSystem.Threading.Tasks;

namespace IronstrikeServers;

// The server list.
//
// Listing needs a runner sitting in our Photon lobby, but the game's own runner is busy running the
// haven. So the browser has a second runner, made from the game's RunnerPrefab so it is configured
// exactly like the real one. That prefab carries NetworkLogic, the game's callback handler; the
// patches below keep every callback for the browse runner away from the game, which would otherwise
// treat its lobby traffic and shutdown as its own.
[HarmonyPatch]
internal static class Browser
{
    internal sealed class Server
    {
        public string Code, Name, Desc, Mods, Rules, Region, Mode, ModVersion;
        public int Players, Max, StartedMinute;
        public bool Open, Password, ModsMatch, Favorite;
    }

    static NetworkRunner runner;
    static Task<StartGameResult> joining;
    static List<Server> servers = new();
    static bool loggedList, probing;

    internal static bool Active => runner != null;
    internal static string Status { get; private set; } = "";
    internal static int Version { get; private set; }      // bumps on every list change, for the UI
    internal static IReadOnlyList<Server> All => servers;

    static bool IsBrowse(NetworkRunner r) => runner != null && r != null && r.Pointer == runner.Pointer;

    // --------------------------------------------------------------- open / close

    internal static void Refresh() => Refresh(false);

    // vanillaProbe (self-test only): list the game's own public lobby instead, to check that modded
    // servers do not appear in it. Read-only: nothing is joined.
    internal static void Refresh(bool vanillaProbe)
    {
        try
        {
            Close();
            var prefab = GM.instance?.NetLifecycle?.RunnerPrefab;
            if (prefab == null) { SetStatus("Not ready yet."); return; }

            runner = UnityEngine.Object.Instantiate(prefab);
            runner.gameObject.name = "IronstrikeServers.BrowseRunner";
            UnityEngine.Object.DontDestroyOnLoad(runner.gameObject);

            string region = Plugin.C.Region.Value;
            probing = vanillaProbe;
            joining = vanillaProbe
                ? runner.JoinSessionLobby(SessionLobby.ClientServer, null, null, Regions.Settings(region), new Il2CppSystem.Nullable<bool>())
                : runner.JoinSessionLobby(SessionLobby.Custom, Protocol.Lobby, null, Regions.Settings(region), new Il2CppSystem.Nullable<bool>());
            servers = new List<Server>();
            Version++;
            SetStatus($"Searching {region.ToUpper()}…");
            Plugin.Log.LogInfo($"browser: joining lobby '{Protocol.Lobby}' in {region}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"browser: could not open lobby: {e}");
            SetStatus("Could not search: " + e.GetType().Name);
            Close();
        }
    }

    internal static void Close()
    {
        var r = runner;
        runner = null;
        joining = null;
        if (r == null) return;
        try { r.Shutdown(true, ShutdownReason.Ok, false); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"browser: shutdown failed, destroying: {e.Message}");
            if (r != null && r.gameObject != null) UnityEngine.Object.Destroy(r.gameObject);
        }
    }

    internal static void Tick()
    {
        var t = joining;
        if (t == null || !t.IsCompleted) return;
        joining = null;
        var res = t.Result;
        if (res == null || !res.Ok)
        {
            string why = res == null ? "unknown" : res.ShutdownReason.ToString();
            Plugin.Log.LogWarning($"browser: lobby join failed: {why} ({res?.ErrorMessage})");
            SetStatus("Could not reach the lobby: " + why);
            Close();
            return;
        }
        if (servers.Count == 0) SetStatus("No modded servers in " + Plugin.C.Region.Value.ToUpper() + " right now.");
    }

    static void SetStatus(string s) { Status = s; Version++; }

    // --------------------------------------------------------------- callbacks

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnSessionListUpdated))]
    static bool SessionList(NetworkRunner runner, Il2CppSystem.Collections.Generic.List<SessionInfo> sessionList)
    {
        if (!IsBrowse(runner)) return true;
        if (!loggedList) { loggedList = true; Plugin.Log.LogInfo("PATCH LIVE: NetworkLogic.OnSessionListUpdated (browser)"); }

        try
        {
            var list = new List<Server>();
            int skipped = 0;
            for (int i = 0; sessionList != null && i < sessionList.Count; i++)
            {
                var si = sessionList[i];
                if (si == null || !si.IsValid) continue;
                var p = si.Properties;
                var l = Protocol.Unpack(SessionProps.Str(p, Protocol.KListing));
                if (l == null) { skipped++; continue; }   // not ours

                list.Add(new Server
                {
                    Code = si.Name,
                    Name = l.Name,
                    Desc = l.Desc,
                    Mods = l.Mods,
                    Rules = l.Rules,
                    Mode = l.Mode,
                    ModVersion = l.ModVersion,
                    StartedMinute = l.StartedMinute,
                    Region = si.Region ?? "",
                    Players = si.PlayerCount,
                    Max = si.MaxPlayers,
                    Open = si.IsOpen,
                    Password = l.Password,
                    ModsMatch = l.Version == Protocol.Version && l.ModHash == Protocol.ModHash,
                });
            }
            foreach (var s in list)
                Plugin.Log.LogInfo($"browser: entry players={s.Players}/{s.Max} open={s.Open} mods={(s.ModsMatch ? "match" : "differ")} " +
                                   $"pw={s.Password} named={s.Name.Length > 0} rules={s.Rules.Length > 0}");
            servers = list;
            Version++;
            Status = list.Count == 0 ? "No modded servers in " + Plugin.C.Region.Value.ToUpper() + " right now."
                                     : $"{list.Count} server{(list.Count == 1 ? "" : "s")} in {Plugin.C.Region.Value.ToUpper()}";
            if (probing)
            {
                Plugin.Log.LogMessage($"self-test: vanilla lobby lists {list.Count + skipped} session(s), modded among them: {list.Count}");
                servers = new List<Server>();
                return false;
            }
            Plugin.Log.LogInfo($"browser: {list.Count} modded session(s)" + (skipped > 0 ? $", {skipped} foreign ignored" : ""));
        }
        catch (Exception e) { Plugin.Log.LogError($"browser: bad session list: {e}"); }
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnShutdown))]
    static bool Shutdown(NetworkRunner runner, ShutdownReason shutdownReason) => !IsBrowse(runner) && !Closed(runner);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnConnectedToServer))]
    static bool Connected(NetworkRunner runner) => !IsBrowse(runner) && !Closed(runner);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnDisconnectedFromServer))]
    static bool Disconnected(NetworkRunner runner) => !IsBrowse(runner) && !Closed(runner);

    // A browse runner we already let go of still reports its own shutdown a frame later; recognise it
    // by name so the game never mistakes that for its session ending.
    static bool Closed(NetworkRunner r)
    {
        try { return r != null && r.gameObject != null && r.gameObject.name == "IronstrikeServers.BrowseRunner"; }
        catch (Exception) { return false; }
    }

    // --------------------------------------------------------------- the view

    internal static List<Server> Visible()
    {
        var c = Plugin.C;
        var fav = new HashSet<string>(Lists.Favorites.Select(f => f.Code));
        var q = servers.Select(s => { s.Favorite = fav.Contains(s.Code); return s; });

        if (c.HideFull.Value) q = q.Where(s => s.Players < s.Max && s.Open);
        if (c.HideEmpty.Value) q = q.Where(s => s.Players > 0);
        if (c.HidePassworded.Value) q = q.Where(s => !s.Password);
        if (c.HideMismatched.Value) q = q.Where(s => s.ModsMatch);
        if (c.OnlyFavorites.Value) q = q.Where(s => s.Favorite);

        q = c.SortBy.Value switch
        {
            1 => q.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
            2 => q.OrderByDescending(s => s.Favorite).ThenByDescending(s => s.Players),
            _ => q.OrderByDescending(s => s.Players).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
        };
        return q.ToList();
    }

    internal static readonly string[] SortNames = { "Players", "Name", "Favourites" };
}
