using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Photon.Realtime;
using Il2CppSystem.Collections.ObjectModel;

namespace IronstrikeServers;

internal static class SessionProps
{
    public static bool Has(ReadOnlyDictionary<string, SessionProperty> p, string k)
        => p != null && p.ContainsKey(k);

    public static string Str(ReadOnlyDictionary<string, SessionProperty> p, string k)
    {
        if (!Has(p, k)) return "";
        var v = p[k];
        return v != null && v.IsString ? (string)v : "";
    }

    public static int Int(ReadOnlyDictionary<string, SessionProperty> p, string k, int dflt = 0)
    {
        if (!Has(p, k)) return dflt;
        var v = p[k];
        return v != null && v.IsInt ? (int)v : dflt;
    }
}

internal static class Regions
{
    // A copy of the game's Photon settings with only the region changed. Same AppId: modded servers
    // are kept apart by lobby and token, not by app.
    public static AppSettings Settings(string region)
    {
        var s = PhotonAppSettings.Instance.AppSettings.GetCopy();
        if (Array.IndexOf(Protocol.Regions, region) >= 0) s.FixedRegion = region;
        return s;
    }

    public static string Next(string cur, int dir)
    {
        int i = Array.IndexOf(Protocol.Regions, cur);
        if (i < 0) i = 0;
        int n = Protocol.Regions.Length;
        return Protocol.Regions[((i + dir) % n + n) % n];
    }
}

// Favourites and recents are join codes, so they live in the config file only. Never log them.
internal static class Lists
{
    const int MaxRecent = 10;

    // Stored as "code|name;code|name". Names are the server's own, clipped and stripped of separators.
    public static List<(string Code, string Name)> Parse(string s)
    {
        var l = new List<(string, string)>();
        foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split('|');
            if (Protocol.LooksLikeCode(bits[0])) l.Add((bits[0], bits.Length > 1 ? bits[1] : ""));
        }
        return l;
    }

    static string Join(IEnumerable<(string Code, string Name)> l)
        => string.Join(";", l.Select(e => e.Code + "|" + Clean(e.Name)));

    static string Clean(string n) => Protocol.Clip(n, Protocol.MaxName).Replace(";", " ").Replace("|", " ");

    public static List<(string Code, string Name)> Favorites => Parse(Plugin.C.Favorites.Value);
    public static List<(string Code, string Name)> Recent => Parse(Plugin.C.Recent.Value);

    public static bool IsFavorite(string code) => Favorites.Any(e => e.Code == code);

    public static void ToggleFavorite(string code, string name)
    {
        var l = Favorites;
        if (l.RemoveAll(e => e.Code == code) == 0) l.Add((code, name));
        Plugin.C.Favorites.Value = Join(l);
    }

    public static void AddRecent(string code, string name)
    {
        var l = Recent;
        l.RemoveAll(e => e.Code == code);
        l.Insert(0, (code, name));
        if (l.Count > MaxRecent) l.RemoveRange(MaxRecent, l.Count - MaxRecent);
        Plugin.C.Recent.Value = Join(l);
    }

    public static HashSet<string> Banned()
        => new((Plugin.C.BannedIds.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    public static void Ban(string installId)
    {
        var b = Banned();
        if (b.Add(installId)) Plugin.C.BannedIds.Value = string.Join(",", b);
    }

    public static void ClearBans() => Plugin.C.BannedIds.Value = "";
}
