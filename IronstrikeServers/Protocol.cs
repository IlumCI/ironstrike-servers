using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Unity.IL2CPP;

namespace IronstrikeServers;

// Everything both ends of a modded connection must agree on.
//
// Isolation works in two layers:
//   listing  -- modded servers publish into their own Photon lobby (StartGameArgs.CustomLobbyName).
//               The game only ever browses SessionLobby.ClientServer, so it never lists them.
//   joining  -- a host refuses any connect request whose ConnectionToken does not validate. The
//               game never sets a token, so an unmodded client is always refused, even with the code.
internal static class Protocol
{
    // Bump on any change to the token layout or the listing format.
    public const int Version = 2;
    public static readonly string Lobby = "ism-" + Version;

    // ONE session property carries the whole listing. Photon Fusion allows at most 10 custom session
    // properties (verified: "Max number of Custom Session Properties reached, only 10 properties are
    // allowed" -> ShutdownReason.InvalidArguments), and the game itself uses up to 8 one-letter keys
    // (o, t, v, a, afk, k, r, public), some set later in the session. Taking more than one would
    // starve the game's own updates.
    public const string KListing = "x_ism";

    // ------------------------------------------------------------- listing

    internal sealed class Listing
    {
        public int Version;
        public string Name = "", Desc = "", ModHash = "", Mods = "", Rules = "", Mode = "", ModVersion = "";
        public bool Password;
        public int StartedMinute;
    }

    // Fields joined by U+001F (the ASCII unit separator), which never appears in typed text; it is
    // stripped from every field anyway. Order is fixed; new fields go at the end so older readers
    // ignore them.
    const char Sep = '\u001f';

    public static string Pack(Listing l) => string.Join(Sep,
        l.Version.ToString(), F(l.Name, MaxName), F(l.Desc, MaxDesc), l.Password ? "1" : "0", F(l.ModHash, 32),
        F(l.Mods, MaxMods), F(l.Rules, MaxRules), F(l.Mode, 16), l.StartedMinute.ToString(), F(l.ModVersion, 24));

    static string F(string s, int max) => Clip(s, max).Replace(Sep, ' ');

    public static Listing Unpack(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var f = s.Split(Sep);
        string At(int i) => i < f.Length ? f[i] : "";
        if (!int.TryParse(At(0), out int v)) return null;
        int.TryParse(At(8), out int started);
        return new Listing
        {
            Version = v, Name = At(1), Desc = At(2), Password = At(3) == "1", ModHash = At(4),
            Mods = At(5), Rules = At(6), Mode = At(7), StartedMinute = started, ModVersion = At(9),
        };
    }

    public const int MaxName = 32, MaxDesc = 80, MaxMods = 200, MaxRules = 120;

    public static readonly string[] Regions =
        { "us", "usw", "eu", "asia", "jp", "kr", "in", "au", "sa", "cae", "za", "uae", "tr", "hk" };

    // ------------------------------------------------------------- mod set

    static string modHash, modList;

    // GUID@version of every loaded BepInEx plugin, sorted so load order does not matter.
    static List<string> Mods()
    {
        var l = new List<string>();
        foreach (var kv in IL2CPPChainloader.Instance.Plugins)
        {
            var md = kv.Value.Metadata;
            l.Add($"{md.GUID}@{md.Version}");
        }
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    public static string ModHash => modHash ??= Hex(Sha(string.Join("\n", Mods())), 8);

    // Short human-readable list for the browser: plugin names without the reverse-DNS prefix.
    public static string ModList
    {
        get
        {
            if (modList != null) return modList;
            var names = new List<string>();
            foreach (var kv in IL2CPPChainloader.Instance.Plugins)
                names.Add($"{kv.Value.Metadata.Name} {kv.Value.Metadata.Version}");
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return modList = Clip(string.Join(", ", names), MaxMods);
        }
    }

    // ------------------------------------------------------------- token
    //  0..3  "ISMS"
    //  4     protocol version
    //  5..12 mod-set hash
    // 13..20 password hash, salted with the join code (all zero = no password)
    // 21..28 install id, so a host can ban an install without learning who it is

    static readonly byte[] Magic = Encoding.ASCII.GetBytes("ISMS");
    const int TokenLen = 29;

    public static byte[] BuildToken(string code, string password)
    {
        var t = new byte[TokenLen];
        Magic.CopyTo(t, 0);
        t[4] = Version;
        Unhex(ModHash).CopyTo(t, 5);
        PwHash(code, password).CopyTo(t, 13);
        Unhex(Plugin.C.InstallId.Value).CopyTo(t, 21);
        return t;
    }

    public enum Verdict { Ok, NotModded, WrongVersion, ModsDiffer, BadPassword, Banned }

    public static Verdict Check(byte[] t, string code, string password, ICollection<string> banned)
    {
        if (t == null || t.Length < TokenLen) return Verdict.NotModded;
        for (int i = 0; i < 4; i++) if (t[i] != Magic[i]) return Verdict.NotModded;
        if (t[4] != Version) return Verdict.WrongVersion;
        if (Hex(t, 5, 8) != ModHash) return Verdict.ModsDiffer;
        if (!PwHash(code, password).AsSpan().SequenceEqual(t.AsSpan(13, 8))) return Verdict.BadPassword;
        if (banned.Contains(Hex(t, 21, 8))) return Verdict.Banned;
        return Verdict.Ok;
    }

    public static string InstallIdOf(byte[] t) => t != null && t.Length >= TokenLen ? Hex(t, 21, 8) : null;

    static byte[] PwHash(string code, string password)
    {
        if (string.IsNullOrEmpty(password)) return new byte[8];
        return Sha("ism-pw\n" + code + "\n" + password).Take(8).ToArray();
    }

    // ------------------------------------------------------------- codes and ids

    // Eight digits: easy to type on the game's VR keyboard, and longer than the game's own
    // six-digit private codes, so the two can never collide.
    public static string NewCode() => RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();

    public static string NewInstallId() => Hex(RandomNumberGenerator.GetBytes(8), 8);

    public static bool LooksLikeCode(string s) => s != null && s.Length >= 4 && s.Length <= 16 && s.All(char.IsLetterOrDigit);

    // ------------------------------------------------------------- helpers

    public static string Clip(string s, int max)
    {
        s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    static byte[] Sha(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));
    static string Hex(byte[] b, int n) => Hex(b, 0, n);
    static string Hex(byte[] b, int off, int n) => Convert.ToHexString(b, off, n).ToLowerInvariant();

    static byte[] Unhex(string s)
    {
        try { var b = Convert.FromHexString(s ?? ""); if (b.Length == 8) return b; } catch (FormatException) { }
        return new byte[8];
    }
}
