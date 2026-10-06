using System;
using Fusion;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Threading.Tasks;

namespace IronstrikeServers;

// Hosting and joining modded servers.
//
// The game builds its StartGameArgs inside NetworkLifecycle.StartGame, an async method we cannot
// rewrite, and then hands them to NetworkRunner.StartGame. So we start sessions through the game's
// own path -- the same FadeToStartGame call its Private Match button makes, so the fade, scene load
// and player spawning are all the game's -- and intercept that one NetworkRunner.StartGame call to
// re-issue it with our lobby, token and properties added.
//
// Re-issuing rather than editing in place is deliberate. StartGameArgs is a struct the game passes
// by pointer, and whether a Harmony prefix sees that memory or a boxed copy depends on interop
// details. A copy we pass on ourselves works either way.
[HarmonyPatch]
internal static class Sessions
{
    internal enum Kind { Host, Join }

    internal sealed class Ctx
    {
        public Kind Kind;
        public string Code, Password, Name;
        public float Started;
        public IntPtr Runner;          // the runner our StartGame went to; its shutdown ends the session
        public bool Verified;          // client: confirmed the session carries our marker
        public bool Applied;           // our args went to NetworkRunner.StartGame
        public Task<StartGameResult> Task;
    }

    static Ctx pending;
    internal static Ctx Current { get; private set; }

    // Shown in the UI. Never contains a join code.
    internal static string Status { get; private set; } = "";

    static bool reissuing;

    // Self-test knobs, to bisect a start failure in one run. Never set outside the self-test.
    internal static string DebugRegion;
    internal static bool DebugSkipExtraProps;
    const float PendingTimeout = 30f;

    // --------------------------------------------------------------- entry points

    internal static void Host()
    {
        var c = Plugin.C;
        string code = c.PersistentCode.Value && Protocol.LooksLikeCode(c.SavedCode.Value)
            ? c.SavedCode.Value : Protocol.NewCode();
        if (c.PersistentCode.Value) c.SavedCode.Value = code;

        Start(new Ctx { Kind = Kind.Host, Code = code, Password = c.Password.Value,
                        Name = Protocol.Clip(c.ServerName.Value, Protocol.MaxName) });
    }

    internal static void Join(string code, string password, string name)
    {
        if (!Protocol.LooksLikeCode(code)) { Status = "That is not a valid code."; return; }
        Start(new Ctx { Kind = Kind.Join, Code = code, Password = password ?? "", Name = name });
    }

    static void Start(Ctx ctx)
    {
        var nl = GM.instance?.NetLifecycle;
        if (nl == null) { Status = "Not ready yet."; return; }
        // One start at a time: a second click while the first is fading in would start two sessions.
        if (pending != null && UnityEngine.Time.realtimeSinceStartup - pending.Started < PendingTimeout) return;
        if (Current?.Task != null) return;

        Browser.Close();
        ctx.Started = UnityEngine.Time.realtimeSinceStartup;
        pending = ctx;
        Status = ctx.Kind == Kind.Host ? "Starting server…" : "Joining…";
        Plugin.Log.LogMessage($"modded session: {(ctx.Kind == Kind.Host ? "hosting" : "joining")} " +
                              $"region={Plugin.C.Region.Value} password={(ctx.Password.Length > 0 ? "yes" : "no")}");

        // The mode is overridden below (Host or Client); AutoHostOrClient is only what the game's own
        // Private Match passes, so everything up to NetworkRunner.StartGame runs exactly as it does
        // for a private match.
        nl.FadeToStartGame(GameMode.AutoHostOrClient, ctx.Code);
    }

    internal static void Leave()
    {
        var r = GM.instance?.NetLifecycle?._runner;
        if (r != null && r.IsRunning) r.Shutdown(true, ShutdownReason.Ok, false);
    }

    // --------------------------------------------------------------- patches

    // Every session start in the game goes through NetworkRunner.StartGame (verified across all call
    // sites: menu buttons go FadeToStartGame -> StartGameFaded -> here without passing
    // NetworkLifecycle.StartGame). So this is also where a session the game starts without us -- the
    // haven, solo, a vanilla private match -- ends our modded one. Otherwise a modded host who went on
    // to host a vanilla private match would still be refusing its unmodded joiners.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkRunner), nameof(NetworkRunner.StartGame))]
    static bool RunnerStartGame(NetworkRunner __instance, StartGameArgs args, ref Task<StartGameResult> __result)
    {
        if (reissuing) return true;
        if (pending == null || args == null)
        {
            if (Current != null) Plugin.Log.LogInfo("modded session over: the game started another session");
            Current = null;
            return true;
        }

        var ctx = pending;
        pending = null;
        if (UnityEngine.Time.realtimeSinceStartup - ctx.Started > PendingTimeout) return true;   // stale

        try
        {
            Apply(args, ctx);
            reissuing = true;
            __result = __instance.StartGame(args);
            ctx.Applied = true;
            ctx.Task = __result;
            ctx.Runner = __instance.Pointer;
            Current = ctx;
            Plugin.Log.LogMessage($"PATCH LIVE: NetworkRunner.StartGame re-issued as {args.GameMode} in lobby '{args.CustomLobbyName}'");
            return false;
        }
        catch (Exception e)
        {
            // Never fall through to the unmodified args: that would start an ordinary private match
            // the player thinks is modded. Fail the start instead.
            Plugin.Log.LogError($"could not start modded session: {e}");
            Status = "Could not start: " + e.GetType().Name;
            Current = null;
            __result = null;
            return false;
        }
        finally { reissuing = false; }
    }

    static void Apply(StartGameArgs a, Ctx ctx)
    {
        a.CustomLobbyName = Protocol.Lobby;
        a.ConnectionToken = (Il2CppStructArray<byte>)Protocol.BuildToken(ctx.Code, ctx.Password);
        a.CustomPhotonAppSettings = Regions.Settings(DebugRegion ?? Plugin.C.Region.Value);

        if (ctx.Kind == Kind.Join)
        {
            a.GameMode = GameMode.Client;
            return;
        }

        var c = Plugin.C;
        a.GameMode = GameMode.Host;
        a.IsVisible = new Il2CppSystem.Nullable<bool>(true);
        a.IsOpen = new Il2CppSystem.Nullable<bool>(c.Open.Value);

        // Hosts may lower the slot count but not raise it past the project's own maximum: the game
        // sizes per-player state for it. (Not read from a.PlayerCount: the interop getter for a
        // Nullable inside a struct throws.)
        int gameMax = GameMaxPlayers();
        int want = Math.Max(2, c.MaxPlayers.Value);
        if (gameMax > 0) want = Math.Min(want, gameMax);
        a.PlayerCount = new Il2CppSystem.Nullable<int>(want);
        Plugin.Log.LogInfo($"host: players {want} (game default {(gameMax > 0 ? gameMax.ToString() : "unset")})");

        var props = a.SessionProperties ?? new Il2CppSystem.Collections.Generic.Dictionary<string, SessionProperty>();
        props[Protocol.KListing] = Protocol.Pack(new Protocol.Listing
        {
            Version = Protocol.Version,
            Name = ctx.Name,
            Desc = c.Description.Value,
            Password = ctx.Password.Length > 0,
            ModHash = Protocol.ModHash,
            Mods = Protocol.ModList,
            Rules = Api.TrainerRules(),
            Mode = RunMode(),
            StartedMinute = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60),
            ModVersion = Id.Version,
        });
        Plugin.Log.LogInfo($"host: {props.Count} session properties (game's plus our one), region {DebugRegion ?? Plugin.C.Region.Value}");
        a.SessionProperties = props;
    }

    // What the host's main menu run-type choice allows: Normal, Archon (the game's Endelai runs) or Any.
    static string RunMode()
    {
        try
        {
            var nl = GM.instance?.NetLifecycle;
            bool normal = nl?.AcceptingNormalRuns?.Value ?? true;
            bool archon = nl?.AcceptingEndelaiRuns?.Value ?? false;
            return normal && archon ? "Any" : archon ? "Archon" : "Normal";
        }
        catch (Exception) { return ""; }
    }

    internal static int GameMaxPlayers()
    {
        try { return NetworkProjectConfig.Global?.Simulation?.DefaultPlayers ?? 0; }
        catch (Exception) { return 0; }
    }

    // Leaving, being kicked, the host quitting, a timeout: the session's runner shuts down. A postfix,
    // so the game's own handling (back to the haven, its modal) runs first.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkLogic), nameof(NetworkLogic.OnShutdown))]
    static void RunnerShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        var ctx = Current;
        if (ctx == null || runner == null || runner.Pointer != ctx.Runner) return;
        Current = null;
        Status = ctx.Kind == Kind.Join && !ctx.Verified ? JoinFailure(shutdownReason.ToString()) : shutdownReason switch
        {
            ShutdownReason.Ok => ctx.Kind == Kind.Host ? "Server stopped." : "Left the server.",
            ShutdownReason.DisconnectedByPluginLogic => "Disconnected by the host.",
            _ => "Session ended: " + shutdownReason,
        };
        Plugin.Log.LogInfo($"modded session over: runner shut down ({shutdownReason})");
    }

    // --------------------------------------------------------------- tick

    internal static void Tick()
    {
        if (pending != null && UnityEngine.Time.realtimeSinceStartup - pending.Started > PendingTimeout)
        {
            pending = null;
            Status = "Timed out starting the session.";
        }

        var ctx = Current;
        if (ctx == null) return;

        if (ctx.Task != null && ctx.Task.IsCompleted)
        {
            var t = ctx.Task;
            ctx.Task = null;
            var res = t.Result;
            if (res == null || !res.Ok)
            {
                string why = res == null ? "unknown" : res.ShutdownReason.ToString();
                Plugin.Log.LogWarning($"modded session failed to start: {why} ({res?.ErrorMessage})");
                Status = ctx.Kind == Kind.Join ? JoinFailure(why)
                       : why == "GameIdAlreadyExists" ? "That code is already in use. Turn off the persistent code and retry."
                       : "Could not host: " + why;
                Current = null;
                return;
            }
            Status = ctx.Kind == Kind.Host ? "Hosting." : "Connected.";
            if (ctx.Kind == Kind.Host) Plugin.Log.LogMessage("hosting a modded server");
        }

        if (ctx.Kind == Kind.Join && !ctx.Verified) VerifyJoined(ctx);
    }

    static string JoinFailure(string reason) => reason switch
    {
        "ConnectionRefused" => "Refused by the host (different mods, wrong password, banned or closed).",
        "GameNotFound" => "No server with that code (in this region).",
        "GameIsFull" => "That server is full.",
        "GameClosed" => "That server is closed to new players.",
        _ => "Could not join: " + reason,
    };

    // A join by code goes to whatever session has that name, which could be an ordinary private
    // match. Ours always carry the marker property; anything else, leave.
    static void VerifyJoined(Ctx ctx)
    {
        var r = GM.instance?.NetLifecycle?._runner;
        if (r == null || !r.IsRunning) return;
        var si = r.SessionInfo;
        if (si == null || !si.IsValid) return;

        ctx.Verified = true;
        if (!SessionProps.Has(si.Properties, Protocol.KListing))
        {
            Plugin.Log.LogWarning("joined session is not a modded server; leaving");
            Status = "That code belongs to a normal private match, not a modded server.";
            Current = null;
            r.Shutdown(true, ShutdownReason.Ok, false);
            return;
        }
        Lists.AddRecent(ctx.Code, Protocol.Unpack(SessionProps.Str(si.Properties, Protocol.KListing))?.Name ?? ctx.Name);
        Plugin.Log.LogMessage("joined a modded server");
    }
}
