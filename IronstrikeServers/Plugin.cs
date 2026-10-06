using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace IronstrikeServers;

internal static class Id
{
    public const string Guid    = "eu.euroswarms.ironstrike.servers";
    public const string Name    = "Ironstrike Servers";
    public const string Version = "0.1.0";

    public const string TrainerGuid = "eu.euroswarms.ironstrike.trainer";
}

[BepInPlugin(Id.Guid, Id.Name, Id.Version)]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;
    internal static Cfg C;

    public override void Load()
    {
        Log = base.Log;
        C = new Cfg(Config);
        if (string.IsNullOrEmpty(C.InstallId.Value)) C.InstallId.Value = Protocol.NewInstallId();

        var h = new Harmony(Id.Guid);
        h.PatchAll(typeof(Driver));
        h.PatchAll(typeof(Lockout));
        h.PatchAll(typeof(Sessions));
        h.PatchAll(typeof(HostControls));
        h.PatchAll(typeof(Browser));

        Log.LogInfo($"{Id.Name} v{Id.Version} loaded. Lobby '{Protocol.Lobby}'.");
    }
}

internal sealed class Cfg
{
    public readonly ConfigEntry<bool> Enabled, SafeMode, EnableHotkeys;
    public readonly ConfigEntry<string> Region;

    public readonly ConfigEntry<string> ServerName, Description, Password, SavedCode;
    public readonly ConfigEntry<int> MaxPlayers;
    public readonly ConfigEntry<bool> Open, PersistentCode;

    public readonly ConfigEntry<bool> HideFull, HideEmpty, HidePassworded, HideMismatched, OnlyFavorites;
    public readonly ConfigEntry<int> SortBy;
    public readonly ConfigEntry<string> Favorites, Recent;

    public readonly ConfigEntry<string> InstallId, BannedIds;
    public readonly ConfigEntry<float> AutoOpenAfter;
    public readonly ConfigEntry<bool> AutoHost, PreviewUi;

    public Cfg(ConfigFile f)
    {
        Enabled = f.Bind("01 General", "Enabled", true, "Master switch.");
        SafeMode = f.Bind("01 General", "SafeMode", true,
            "Lock Play and HOST (public matchmaking) while modded. The developer asked that mods\n"
            + "never reach public games; modded servers live in their own lobby instead.");
        EnableHotkeys = f.Bind("01 General", "EnableHotkeys", true, "F7 opens the server browser.");

        Region = f.Bind("02 Network", "Region", "us",
            "Photon region for browsing and hosting (us, usw, eu, asia, jp, kr, in, au, sa, cae, za, uae, tr, hk).\n"
            + "Servers are only visible to players browsing the same region.");

        ServerName  = f.Bind("03 Host", "ServerName", "Modded server", "Shown in the browser. Max 32 characters.");
        Description = f.Bind("03 Host", "Description", "", "Shown in the browser. Max 80 characters.");
        Password    = f.Bind("03 Host", "Password", "",
            "Empty = no password. Never sent anywhere in plain text: joiners send a salted hash.");
        MaxPlayers  = f.Bind("03 Host", "MaxPlayers", 4, "Slots including the host.");
        Open        = f.Bind("03 Host", "Open", true, "Whether new players may join. Can be changed live.");
        PersistentCode = f.Bind("03 Host", "PersistentCode", false,
            "Reuse the same join code every time you host, so favourites keep working.");
        SavedCode   = f.Bind("03 Host", "SavedCode", "", "Managed by the mod.");

        HideFull       = f.Bind("04 Browser", "HideFull", false);
        HideEmpty      = f.Bind("04 Browser", "HideEmpty", false, "Only show servers with players in them.");
        HidePassworded = f.Bind("04 Browser", "HidePassworded", false);
        HideMismatched = f.Bind("04 Browser", "HideModMismatch", false,
            "Hide servers whose mod set differs from yours (you could not join them anyway).");
        OnlyFavorites  = f.Bind("04 Browser", "OnlyFavorites", false);
        SortBy         = f.Bind("04 Browser", "SortBy", 0, "0 players, 1 name, 2 favourites first.");

        Favorites = f.Bind("05 Lists", "Favorites", "", "Managed by the mod. Join codes; keep this file private.");
        Recent    = f.Bind("05 Lists", "Recent", "", "Managed by the mod. Join codes; keep this file private.");

        InstallId = f.Bind("06 Identity", "InstallId", "",
            "Random id generated on first run. Sent to hosts so they can ban this install. Not tied to\n"
            + "your Steam account. Delete it to get a new one.");
        BannedIds = f.Bind("06 Identity", "BannedIds", "", "Installs you have banned from your servers.");

        AutoOpenAfter = f.Bind("07 Debug", "AutoOpenAfterSeconds", 0f,
            "Debug aid: open the server browser this many seconds after the game starts. 0 disables.");
        AutoHost = f.Bind("07 Debug", "AutoHostSelfTest", false,
            "Debug aid: after auto-open, host a server, then list the lobby from a second runner to\n"
            + "check the listing. Turn off again afterwards.");
        PreviewUi = f.Bind("07 Debug", "PreviewUi", false,
            "Debug aid: after AutoOpenAfterSeconds, bring up the main menu, then cycle every server page\n"
            + "with mock data, for screenshots. Turn off again afterwards.");
    }
}
