using System;
using System.Collections.Generic;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace IronstrikeServers;

// Host side: who gets in, and who gets thrown out.
[HarmonyPatch]
internal static class HostControls
{
    static bool loggedGate;

    static NetworkRunner Runner => GM.instance?.NetLifecycle?._runner;

    internal static bool Hosting
    {
        get
        {
            if (!Api.IsHosting) return false;
            var r = Runner;
            return r != null && r.IsRunning && r.IsServer;
        }
    }

    // The game's own handler only checks acceptingConnections. Ours runs first and refuses anything
    // without a valid token -- every unmodded client, whatever code it typed. Accepted requests fall
    // through to the game so its own rules (run in progress, kicked players) still apply.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnConnectRequest))]
    static bool Gate(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, Il2CppStructArray<byte> token)
    {
        if (!loggedGate) { loggedGate = true; Plugin.Log.LogInfo("PATCH LIVE: NetworkLogic.OnConnectRequest"); }

        var ctx = Sessions.Current;
        if (ctx == null || ctx.Kind != Sessions.Kind.Host) return true;

        Protocol.Verdict v;
        try { v = Protocol.Check(token != null ? (byte[])token : null, ctx.Code, ctx.Password, Lists.Banned()); }
        catch (Exception e) { Plugin.Log.LogError($"token check failed, refusing: {e.Message}"); v = Protocol.Verdict.NotModded; }

        if (v == Protocol.Verdict.Ok)
        {
            Plugin.Log.LogInfo("connect request: accepted (modded, matching)");
            return true;
        }
        Plugin.Log.LogInfo($"connect request: refused ({v})");
        request.Refuse();
        return false;
    }

    // ------------------------------------------------------------------ live controls

    internal static bool IsOpen
    {
        get { var si = Runner?.SessionInfo; return si != null && si.IsValid && si.IsOpen; }
        set
        {
            Plugin.C.Open.Value = value;
            var si = Runner?.SessionInfo;
            if (Hosting && si != null && si.IsValid) si.IsOpen = value;
        }
    }

    internal sealed class Player
    {
        public PlayerRef Ref;
        public string Name;
        public string InstallId;
    }

    // Everyone but the host. Names come from the game's own networked DisplayName and are only shown
    // on screen, never logged.
    internal static List<Player> Players()
    {
        var list = new List<Player>();
        var nl = GM.instance?.NetLifecycle;
        var r = Runner;
        if (nl == null || r == null || !Hosting) return list;

        var spawned = nl._spawnedPlayers;
        if (spawned == null) return list;

        foreach (var kv in spawned)
        {
            var p = kv.Key;
            if (p == r.LocalPlayer) continue;
            string name = "";
            try { name = kv.Value?.DisplayName ?? ""; } catch (Exception) { }
            string id = null;
            try { var t = r.GetPlayerConnectionToken(p); id = Protocol.InstallIdOf(t != null ? (byte[])t : null); } catch (Exception) { }
            list.Add(new Player { Ref = p, Name = string.IsNullOrWhiteSpace(name) ? "Player " + p.PlayerId : name, InstallId = id });
        }
        return list;
    }

    internal static void Kick(Player p)
    {
        var r = Runner;
        if (!Hosting || r == null) return;
        r.Disconnect(p.Ref);
        Plugin.Log.LogInfo("kicked a player");
    }

    internal static void Ban(Player p)
    {
        if (p.InstallId != null) Lists.Ban(p.InstallId);
        Kick(p);
        Plugin.Log.LogInfo(p.InstallId != null ? "banned a player" : "kicked a player (no install id to ban)");
    }
}
