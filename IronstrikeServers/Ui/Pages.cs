using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace IronstrikeServers.Ui;

// The server window, modelled on the classic Steam server browser:
//
//   Servers                                                     modded lobby · EU          [X]
//   [Internet (12)] [Favorites (2)] [History (5)] [Host]                         status
//   ┌──────────────────────────────────────────────────────────────────────────────────────┐
//   │ lock fav  Server name   Mode   Players  Mods   Cheats   Region  Uptime  Version   ▲│
//   │ rows, one per server; click selects, click again connects                          ││
//   └──────────────────────────────────────────────────────────────────────────────────────┘
//   ┌ selected: name — description / mods / rules ────────────────────────────────────────┐
//   [ ] Not full [ ] Has players [ ] No password [ ] Same mods  Region <EU>   Join by code  Refresh  Connect
//
// The Host tab is a create-server form, or while hosting, the server's status and a player table
// with kick and ban. Views are rebuilt from state whenever that state changes.
internal static class Pages
{
    enum Tab { Internet, Favorites, History, Host }

    static Tab tab = Tab.Internet;
    static string selected;                 // code of the selected server (never logged)
    static bool showCode;
    static string lastSig = "";
    static float nextCheck;
    static int sortCol = 4;                 // index into Cols
    static bool sortDesc = true;

    // ---- geometry, in window units (the window is Panel.Width x Panel.Height)
    const float M = Panel.Margin;
    const float TabsY = Panel.TitleH + 6f, TabH = 64f;
    const float FrameY = TabsY + TabH;
    const float BarH = 82f, BarY = Panel.Height - M - BarH;
    const float InfoH = 128f, InfoY = BarY - 16f - InfoH;
    const float FrameH = InfoY - 14f - FrameY;
    const float Bevel = 7f, HeadH = 58f, RowH = 62f, ScrollW = 60f;
    const float T = 40f, S = 34f;           // text sizes

    // x and width inside the table frame.
    static readonly (string Name, float X, float W)[] Cols =
    {
        ("",            0,   58),   // password
        ("",           58,   58),   // favourite
        ("Server name", 116, 640),
        ("Mode",       756,  190),
        ("Players",    946,  180),
        ("Mods",      1126,  300),
        ("Cheats",    1426,  380),
        ("Region",    1806,  190),
        ("Uptime",    1996,  200),
        ("Version",   2196,  250),
    };

    const string Dim = "<color=#A0AA95>", End = "</color>";

    // --------------------------------------------------------------- open / close

    internal static void Toggle(Transform beside)
    {
        if (Panel.Visible) { Close(); return; }
        Open(beside);
    }

    internal static void Open(Transform beside) => OpenAt(beside, Api.IsHosting ? Tab.Host : Tab.Internet);
    internal static void OpenBrowser(Transform beside) => OpenAt(beside, Api.IsHosting ? Tab.Host : Tab.Internet);
    internal static void OpenHost(Transform beside) => OpenAt(beside, Tab.Host);

    static void OpenAt(Transform beside, Tab t)
    {
        Panel.Build();
        if (!Panel.Ready) { Plugin.Log.LogWarning("server window not ready yet"); return; }
        Panel.OnBack = Close;
        tab = t;
        Panel.Show(beside);
        Render();
        if (t != Tab.Host && !Browser.Active && !Api.InModdedSession && !preview) Browser.Refresh();
    }

    internal static void Close()
    {
        Panel.Hide();
        Browser.Close();
    }

    static void Go(Tab t)
    {
        tab = t;
        if (t != Tab.Host && !Browser.Active && !Api.InModdedSession && !preview) Browser.Refresh();
        Render();
    }

    internal static void Rerender() { if (Panel.Visible) Render(true); }

    internal static void Tick()
    {
        float now = Time.realtimeSinceStartup;
        if (preview) { PreviewTick(now); return; }
        if (!Panel.Visible) return;
        if (now < nextCheck) return;
        nextCheck = now + 0.5f;
        if (Signature() != lastSig) Render(true);
    }

    static string Signature()
    {
        string s = $"{tab}|{Sessions.Status}|{Browser.Status}|{Api.InModdedSession}|{Api.IsHosting}|{Browser.Version}";
        if (tab == Tab.Host && Hosting)
            s += "|" + string.Join(",", PlayersShown().Select(p => p.Ref.PlayerId + p.Name)) + "|" + HostControls.IsOpen
               + "|" + (int)(Time.realtimeSinceStartup / 60);     // uptime ticks over
        return s;
    }

    // --------------------------------------------------------------- render

    static void Render(bool keepScroll = false)
    {
        if (!Panel.Ready) return;
        lastSig = Signature();
        float scrollPos = Panel.Scroll != null ? Panel.Scroll.verticalNormalizedPosition : 1f;
        Panel.ClearViews();
        Panel.SetTitle(tab == Tab.Host ? (Hosting ? "Your server" : "Host a server") : "Servers",
                       $"modded lobby  ·  {Plugin.C.Region.Value.ToUpper()}");

        try
        {
            Tabs();
            if (tab == Tab.Host) { if (Hosting) HostingView(); else HostForm(); }
            else BrowserView();
        }
        catch (Exception e) { Plugin.Log.LogError($"server window render: {e}"); }

        if (Panel.Scroll != null) Panel.Scroll.verticalNormalizedPosition = keepScroll ? scrollPos : 1f;
    }

    static RectTransform View(string name, float y, float h) => Kit.Strip(Panel.Box, "V_" + name, y, h, M, M);

    // --------------------------------------------------------------- tabs

    static void Tabs()
    {
        var strip = View("Tabs", TabsY, TabH);
        float x = 0;
        void TabBtn(Tab t, string label, float w)
        {
            bool on = tab == t;
            // The active tab is taller and joins the frame below it, as Steam's did.
            var rt = Kit.Rect(strip, "Tab" + t, 0, 0, 0, 1, 0, on ? -Bevel : 0, 0, on ? 0 : 8);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            x += w + 6;
            var b = Kit.Button(rt, label, () => Go(t), S, bold: on);
            if (!on) b.Text.color = Kit.Muted;
        }
        int favs = preview ? 3 : Lists.Favorites.Count, recent = preview ? 3 : Lists.Recent.Count;
        TabBtn(Tab.Internet, $"Internet ({(preview ? 3 : Browser.All.Count)})", 300);
        TabBtn(Tab.Favorites, $"Favorites ({favs})", 300);
        TabBtn(Tab.History, $"History ({recent})", 280);
        TabBtn(Tab.Host, Api.IsHosting ? "Your server" : "Host", 260);

        string st = string.Join("     ", new[] { tab == Tab.Host ? "" : Browser.Status, Sessions.Status }
                                   .Where(s => !string.IsNullOrEmpty(s)));
        Kit.Label(Kit.Rect(strip, "Status", 0, 0, 1, 1, x + 30, 0, 10, 0), st, S, Kit.Muted, TextAlignmentOptions.MidlineRight);
    }

    // --------------------------------------------------------------- browser

    sealed class Line
    {
        public Browser.Server S;        // null for a saved server not listed right now
        public string Code, Name;
        public bool Fav;
    }

    static List<Line> Lines()
    {
        var fav = new HashSet<string>(preview ? new[] { "00000001", "00000004" } : Lists.Favorites.Select(f => f.Code));
        var all = preview ? MockServers() : Browser.All.ToList();
        var live = all.GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First());
        List<Line> list;

        if (tab == Tab.Internet)
        {
            var shown = preview ? all : Browser.Visible();
            list = shown.Select(s => new Line { S = s, Code = s.Code, Name = s.Name, Fav = fav.Contains(s.Code) }).ToList();
        }
        else
        {
            var saved = tab == Tab.Favorites ? Lists.Favorites : Lists.Recent;
            if (preview) saved = new() { ("00000001", "Friday co-op night"), ("00000004", "Arena testing"), ("00000009", "Weekend raid") };
            list = saved.Select(e => new Line
            {
                S = live.TryGetValue(e.Code, out var s) ? s : null,
                Code = e.Code,
                Name = string.IsNullOrEmpty(e.Name) ? "Unnamed server" : e.Name,
                Fav = fav.Contains(e.Code),
            }).ToList();
        }

        Func<Line, IComparable> key = sortCol switch
        {
            0 => l => l.S != null && l.S.Password,
            1 => l => l.Fav,
            2 => l => (l.Name ?? "").ToLowerInvariant(),
            3 => l => l.S?.Mode ?? "",
            5 => l => l.S != null && l.S.ModsMatch,
            6 => l => l.S?.Rules ?? "",
            7 => l => l.S?.Region ?? "",
            8 => l => l.S?.StartedMinute ?? int.MaxValue,
            9 => l => l.S?.ModVersion ?? "",
            _ => l => l.S?.Players ?? -1,
        };
        return (sortDesc ? list.OrderByDescending(key) : list.OrderBy(key)).ToList();
    }

    static void BrowserView()
    {
        var frame = View("Table", FrameY, FrameH);
        Kit.Frame(frame, Kit.Sunken);
        Kit.Fill(Kit.Rect(frame, "Bg", 0, 0, 1, 1, Bevel, Bevel, Bevel, Bevel), Kit.Well);

        // Column headers: raised cells, click to sort, again to reverse.
        var head = Kit.Strip(frame, "Header", Bevel, HeadH, Bevel, Bevel);
        for (int i = 0; i < Cols.Length; i++)
        {
            var c = Cols[i];
            int col = i;
            bool on = sortCol == i;
            var cell = Kit.At(head, "H" + i, c.X, c.W);
            var b = Kit.Button(cell, null, () =>
            {
                if (sortCol == col) sortDesc = !sortDesc;
                else { sortCol = col; sortDesc = col is 0 or 1 or 4 or 5; }
                Render(true);
            }, S);
            if (i == 0) Kit.Glyph(cell, Kit.LockIcon, 30, Kit.Muted);
            else if (i == 1) Kit.Glyph(cell, Kit.StarIcon, 30, Kit.Muted);
            else Kit.Label(cell, c.Name, S, on ? Kit.Text : Kit.Muted, padL: 16, padR: 36, shrink: false);
            if (on && i > 1)
            {
                var arrow = Kit.At(cell, "Sort", 8, 30, 30, fromRight: true);
                Kit.Glyph(arrow, sortDesc ? Kit.DownIcon : Kit.UpIcon, 30);
            }
        }
        var corner = Kit.At(head, "Corner", 0, ScrollW, fromRight: true);
        Kit.Frame(corner, Kit.Raised);

        // Rows.
        Panel.PlaceScroll(M + Bevel, Panel.Height - (FrameY + FrameH) + Bevel, M + Bevel, FrameY + Bevel + HeadH);
        var lines = Lines();
        if (selected != null && !lines.Any(l => l.Code == selected)) selected = null;

        float y = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var row = Kit.Strip(Panel.Content, "Row", y, RowH);
            y += RowH;
            bool sel = line.Code == selected;
            Kit.Flat(row, sel ? Kit.Select : (i % 2 == 0 ? Kit.Well : Kit.RowAlt), () =>
            {
                // Click selects; clicking the selected row again connects (Steam's double-click,
                // which a VR pointer cannot do).
                if (selected == line.Code) Connect(line);
                else { selected = line.Code; Render(true); }
            });
            Cells(row, line, sel);
        }
        if (lines.Count == 0)
        {
            var empty = Kit.Strip(Panel.Content, "Empty", 30, RowH * 2);
            string msg = tab switch
            {
                Tab.Favorites => "No favorites yet. Select a server and press Add to favorites.",
                Tab.History => "Servers you join show up here.",
                _ => Browser.All.Count > 0 ? "Every server is hidden by the filters below."
                                           : "No modded servers in this region right now. Host one, or try another region.",
            };
            Kit.Label(empty, msg, T, Kit.Muted, TextAlignmentOptions.Center);
        }
        Panel.Content.sizeDelta = new Vector2(Panel.Content.sizeDelta.x, Math.Max(y, 10));

        Info(lines.FirstOrDefault(l => l.Code == selected));
        BrowserBar(lines.FirstOrDefault(l => l.Code == selected));
    }

    static void Cells(RectTransform row, Line line, bool sel)
    {
        var s = line.S;
        var fg = sel ? Color.white : s == null ? Kit.Muted : Kit.Text;
        RectTransform Cell(int i) => Kit.At(row, "C" + i, Cols[i].X, Cols[i].W);
        void Txt(int i, string text, Color? c = null) => Kit.Label(Cell(i), text, T, c ?? fg, padL: 16, padR: 8, shrink: false);

        if (s != null && s.Password) Kit.Glyph(Cell(0), Kit.LockIcon, 34);
        if (line.Fav) Kit.Glyph(Cell(1), Kit.StarIcon, 34);
        Txt(2, line.Name ?? "");

        if (s == null)
        {
            Txt(3, "-"); Txt(4, "offline"); Txt(5, "-"); Txt(6, "-"); Txt(7, "-"); Txt(8, "-"); Txt(9, "-");
            return;
        }
        Txt(3, string.IsNullOrEmpty(s.Mode) ? "-" : s.Mode);
        bool full = s.Players >= s.Max;
        Txt(4, $"{s.Players} / {s.Max}", !s.Open || full ? (sel ? Color.white : Kit.Bad) : fg);
        int mods = string.IsNullOrEmpty(s.Mods) ? 0 : s.Mods.Split(',').Length;
        Txt(5, $"{mods} · " + (s.ModsMatch ? "match" : "differ"), sel ? Color.white : s.ModsMatch ? Kit.Good : Kit.Bad);
        Txt(6, Cheats(s.Rules));
        Txt(7, string.IsNullOrEmpty(s.Region) ? "-" : s.Region.ToUpper());
        Txt(8, Uptime(s.StartedMinute));
        Txt(9, string.IsNullOrEmpty(s.ModVersion) ? "-" : s.ModVersion);
    }

    static string Cheats(string rules)
    {
        if (string.IsNullOrEmpty(rules)) return "no trainer";
        var r = rules.Replace("trainer: ", "");
        return r == "no cheats" ? "none" : r;
    }

    static string Uptime(int startedMinute)
    {
        if (startedMinute <= 0) return "-";
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        long m = Math.Max(0, now - startedMinute);
        return m < 60 ? $"{m}m" : $"{m / 60}h {m % 60:00}m";
    }

    static void Info(Line line)
    {
        var pane = View("Info", InfoY, InfoH);
        Kit.Frame(pane, Kit.Sunken);
        var inner = Kit.Rect(pane, "In", 0, 0, 1, 1, 24, Bevel + 4, line != null && tab != Tab.Host ? 460 : 24, Bevel + 4);
        var top = Kit.Rect(inner, "Top", 0, 0.5f, 1, 1);
        var bottom = Kit.Rect(inner, "Bottom", 0, 0, 1, 0.5f);

        if (line != null)
        {
            bool isFav = preview ? line.Fav : Lists.IsFavorite(line.Code);
            Kit.Button(Kit.At(pane, "Fav", 22, 400, 70, fromRight: true), isFav ? "Remove favorite" : "Add to favorites",
                       () => { Lists.ToggleFavorite(line.Code, line.Name); Render(true); }, T);
        }

        if (line == null)
        {
            Kit.Label(top, "Select a server to see its details.", T, Kit.Muted);
            Kit.Label(bottom, "Click a selected server again, or press Connect, to join.", S, Kit.Muted);
            return;
        }
        var s = line.S;
        string desc = s == null ? "Not listed in this region right now. Connect tries its saved code."
                    : string.IsNullOrEmpty(s.Desc) ? "No description." : s.Desc;
        Kit.Label(top, $"<color={Kit.Tag(Kit.Accent)}>{line.Name}</color>     {desc}", T, Kit.Text);
        if (s != null)
        {
            string mods = s.ModsMatch
                ? $"<color={Kit.Tag(Kit.Good)}>Same mods as you:</color> {s.Mods}"
                : $"<color={Kit.Tag(Kit.Bad)}>Different mods, cannot join.</color> Theirs: {s.Mods}   {Dim}Yours: {Protocol.ModList}{End}";
            Kit.Label(bottom, mods + (string.IsNullOrEmpty(s.Rules) ? "" : $"     {Dim}{s.Rules}{End}"), S, Kit.Text);
        }
    }

    static void BrowserBar(Line line)
    {
        var bar = View("Bar", BarY, BarH);
        var c = Plugin.C;
        float x = 0;
        RectTransform Slot(float w) { var rt = Kit.At(bar, "Slot", x, w); x += w + 8; return rt; }
        void Filter(float w, string label, BepInEx.Configuration.ConfigEntry<bool> e)
            => Kit.Check(Slot(w), label, e.Value, () => { e.Value = !e.Value; Render(true); }, S);

        Filter(220, "Not full", c.HideFull);
        Filter(250, "Has players", c.HideEmpty);
        Filter(270, "No password", c.HidePassworded);
        Filter(250, "Same mods", c.HideMismatched);
        Kit.Label(Slot(120), "Region", S, Kit.Text, TextAlignmentOptions.MidlineRight);
        var reg = Kit.At(bar, "Region", x, 270, 62); x += 280;
        Kit.Spinner(reg, c.Region.Value.ToUpper(), () => StepRegion(-1, true), () => StepRegion(1, true), S);

        float rx = 0;
        RectTransform Right(float w) { var rt = Kit.At(bar, "Act", rx, w, 66, fromRight: true); rx += w + 12; return rt; }
        if (Api.InModdedSession && !Api.IsHosting)
            Kit.Button(Right(340), "Leave server", Sessions.Leave, T, bold: true);
        else
        {
            var connect = Kit.Button(Right(260), "Connect", () => { if (line != null) Connect(line); }, T, bold: true);
            connect.Button.interactable = line != null && (line.S == null || line.S.ModsMatch);
            Kit.Button(Right(230), "Refresh", () => { if (!preview) Browser.Refresh(); Render(true); }, T);
            Kit.Button(Right(310), "Join by code", AskCodeAndJoin, T);
        }
    }

    static void StepRegion(int d, bool refresh)
    {
        Plugin.C.Region.Value = Regions.Next(Plugin.C.Region.Value, d);
        if (refresh && !Api.InModdedSession && !preview) Browser.Refresh();
        Render(true);
    }

    // --------------------------------------------------------------- joining

    static void Connect(Line line)
    {
        if (line.S != null && !line.S.ModsMatch) return;
        if (preview) return;
        JoinMaybePassword(line.Code, line.Name, line.S?.Password);
    }

    static void AskCodeAndJoin()
    {
        Keyboard.Ask("Server code", 16, code =>
        {
            if (!Protocol.LooksLikeCode(code)) { Plugin.Log.LogInfo("join by code: not a code"); return; }
            JoinMaybePassword(code, "", null);
        });
    }

    // needsPassword: known from the listing, or null (codes, saved servers).
    static void JoinMaybePassword(string code, string name, bool? needsPassword)
    {
        if (needsPassword == false) { Sessions.Join(code, "", name); return; }
        string title = needsPassword == true ? "Server password" : "Password (leave empty if none)";
        if (!Keyboard.Ask(title, 32, pw => Sessions.Join(code, pw, name)))
            Sessions.Join(code, "", name);
    }

    // --------------------------------------------------------------- host form

    static void HostForm()
    {
        Panel.HideScroll();
        var c = Plugin.C;
        var frame = View("Form", FrameY, FrameH + 14 + InfoH);
        Kit.Frame(frame, Kit.Sunken);
        var form = Kit.Rect(frame, "In", 0, 0, 1, 1, 50, 30, 50, 30);
        float y = 10;
        const float Rh = 96f, LabelW = 520f;

        RectTransform Row(string label, string hint = null)
        {
            var row = Kit.Strip(form, "Row", y, Rh - 18);
            y += Rh;
            Kit.Label(Kit.At(row, "L", 0, LabelW), label, T, Kit.Text);
            if (hint != null) Kit.Label(Kit.Rect(row, "Hint", 0.62f, 0, 1, 1), hint, S, Kit.Muted, TextAlignmentOptions.MidlineRight);
            return row;
        }
        RectTransform Ctl(RectTransform row, float w) => Kit.At(row, "C", LabelW, w);

        Kit.Field(Ctl(Row("Server name"), 900), Protocol.Clip(c.ServerName.Value, Protocol.MaxName),
                  () => Keyboard.Ask("Server name", Protocol.MaxName, s => { if (s.Length > 0) c.ServerName.Value = s; }), T);
        Kit.Field(Ctl(Row("Description", "shown in the server list"), 900),
                  string.IsNullOrEmpty(c.Description.Value) ? $"{Dim}(none){End}" : Protocol.Clip(c.Description.Value, 50),
                  () => Keyboard.Ask("Description", Protocol.MaxDesc, s => c.Description.Value = s), T);
        Kit.Field(Ctl(Row("Password", "asked for on Connect; never shown in the list"), 600),
                  string.IsNullOrEmpty(c.Password.Value) ? $"{Dim}(none){End}" : "(set)",
                  () => Keyboard.Ask("Password (empty for none)", 32, s => c.Password.Value = s), T);
        Kit.Spinner(Ctl(Row("Max players"), 330), c.MaxPlayers.Value.ToString(),
                    () => { c.MaxPlayers.Value = Math.Max(2, c.MaxPlayers.Value - 1); Render(true); },
                    () => { c.MaxPlayers.Value = Math.Min(Math.Max(2, Sessions.GameMaxPlayers()), c.MaxPlayers.Value + 1); Render(true); }, T);
        Kit.Spinner(Ctl(Row("Region", "players browse one region at a time"), 330), c.Region.Value.ToUpper(),
                    () => StepRegion(-1, false), () => StepRegion(1, false), T);
        Kit.Check(Ctl(Row("Open to new players"), 600), c.Open.Value ? "Yes" : "No",
                  c.Open.Value, () => { c.Open.Value = !c.Open.Value; Render(true); }, T);
        Kit.Check(Ctl(Row("Keep the same code", "so friends' favorites keep working"), 600), c.PersistentCode.Value ? "Yes" : "No",
                  c.PersistentCode.Value, () => { c.PersistentCode.Value = !c.PersistentCode.Value; Render(true); }, T);

        var note = Kit.Strip(form, "Note", y + 20, 120);
        Kit.Label(Kit.Rect(note, "A", 0, 0.5f, 1, 1), "Only players running the same mods can see or join your server.", S, Kit.Text);
        Kit.Label(Kit.Rect(note, "B", 0, 0, 1, 0.5f), $"{Dim}Your mods: {Protocol.ModList}{End}", S, Kit.Text);

        var bar = View("Bar", BarY, BarH);
        Kit.Button(Kit.At(bar, "Start", 0, 340, 66, fromRight: true), "Start server", Sessions.Host, T, bold: true);
    }

    // --------------------------------------------------------------- hosting

    static bool Hosting => preview ? previewHosting : Api.IsHosting;

    static void HostingView()
    {
        var c = Plugin.C;
        var ctx = Sessions.Current;
        var players = PlayersShown();
        int max = Math.Max(2, c.MaxPlayers.Value);

        // Summary.
        float sumH = 110;
        var sum = View("Summary", FrameY, sumH);
        Kit.Frame(sum, Kit.Sunken);
        var sIn = Kit.Rect(sum, "In", 0, 0, 1, 1, 24, Bevel, 24, Bevel);
        Kit.Label(Kit.Rect(sIn, "Name", 0, 0, 0.7f, 1),
                  $"<color={Kit.Tag(Kit.Accent)}>{Protocol.Clip(c.ServerName.Value, Protocol.MaxName)}</color>     " +
                  $"{players.Count + 1} / {max} players     {c.Region.Value.ToUpper()}     " +
                  $"{(preview || HostControls.IsOpen ? "open" : "closed")}     {(string.IsNullOrEmpty(c.Password.Value) ? "no password" : "password")}",
                  T, Kit.Text);
        // Hidden by default: people stream and screenshot this window.
        string code = preview ? "12345678" : ctx?.Code ?? "?";
        Kit.Button(Kit.At(sIn, "Code", 0, 420, 66, fromRight: true), showCode ? "Code: " + code : "Show join code",
                   () => { showCode = !showCode; Render(true); }, T);

        // Player table.
        float tY = FrameY + sumH + 12, tH = FrameY + FrameH - tY;
        var frame = View("Players", tY, tH);
        Kit.Frame(frame, Kit.Sunken);
        Kit.Fill(Kit.Rect(frame, "Bg", 0, 0, 1, 1, Bevel, Bevel, Bevel, Bevel), Kit.Well);
        var head = Kit.Strip(frame, "Header", Bevel, HeadH, Bevel, Bevel);
        var hName = Kit.At(head, "HN", 0, 1500); Kit.Frame(hName, Kit.Raised); Kit.Label(hName, "Player", S, Kit.Muted, padL: 16);
        var hId = Kit.At(head, "HI", 1500, 500); Kit.Frame(hId, Kit.Raised); Kit.Label(hId, "Install", S, Kit.Muted, padL: 16);
        var hAct = Kit.Rect(head, "HA", 0, 0, 1, 1, 2000, 0, 0, 0); Kit.Frame(hAct, Kit.Raised); Kit.Label(hAct, "", S, Kit.Muted);

        Panel.PlaceScroll(M + Bevel, Panel.Height - (FrameY + FrameH) + Bevel, M + Bevel, tY + Bevel + HeadH);
        float y = 0;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var row = Kit.Strip(Panel.Content, "Row", y, RowH + 8);
            y += RowH + 8;
            Kit.Fill(row, i % 2 == 0 ? Kit.Well : Kit.RowAlt);
            Kit.Label(Kit.At(row, "N", 0, 1500), p.Name, T, Kit.Text, padL: 16);
            Kit.Label(Kit.At(row, "I", 1500, 500), p.InstallId != null ? p.InstallId.Substring(0, 6) + "…" : "-", S, Kit.Muted, padL: 16);
            Kit.Button(Kit.At(row, "Ban", 8, 170, RowH - 6, fromRight: true), "Ban", () => { HostControls.Ban(p); Render(true); }, S);
            Kit.Button(Kit.At(row, "Kick", 188, 170, RowH - 6, fromRight: true), "Kick", () => { HostControls.Kick(p); Render(true); }, S);
        }
        if (players.Count == 0)
            Kit.Label(Kit.Strip(Panel.Content, "Empty", 30, RowH * 2), "Nobody has joined yet. Friends find you under Internet, or with your join code.",
                      T, Kit.Muted, TextAlignmentOptions.Center);
        Panel.Content.sizeDelta = new Vector2(Panel.Content.sizeDelta.x, Math.Max(y, 10));

        var info = View("Info", InfoY, InfoH);
        Kit.Frame(info, Kit.Sunken);
        var iIn = Kit.Rect(info, "In", 0, 0, 1, 1, 24, Bevel + 4, 24, Bevel + 4);
        Kit.Label(Kit.Rect(iIn, "A", 0, 0.5f, 1, 1), "Kick removes a player from this session. Ban also keeps their install out of all your servers.", S, Kit.Text);
        Kit.Label(Kit.Rect(iIn, "B", 0, 0, 1, 0.5f), $"{Dim}Your mods: {Protocol.ModList}{End}", S, Kit.Text);

        var bar = View("Bar", BarY, BarH);
        Kit.Check(Kit.At(bar, "Open", 0, 480), "Open to new players", preview || HostControls.IsOpen,
                  () => { HostControls.IsOpen = !HostControls.IsOpen; Render(true); }, S);
        float rx = 0;
        RectTransform Right(float w) { var rt = Kit.At(bar, "Act", rx, w, 66, fromRight: true); rx += w + 12; return rt; }
        Kit.Button(Right(320), "Stop server", Sessions.Leave, T, bold: true);
        int bans = Lists.Banned().Count;
        if (bans > 0) Kit.Button(Right(360), $"Clear bans ({bans})", () => { Lists.ClearBans(); Render(true); }, T);
    }

    // --------------------------------------------------------------- debug preview

    // Every view with mock data, for screenshots without input. Mock codes are not real servers.
    static bool preview, previewHosting;
    static int previewStep;
    static float previewNext;

    static List<Browser.Server> MockServers()
    {
        int now = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);
        string mine = Protocol.ModList;
        return new()
        {
            new() { Code = "00000001", Name = "Friday co-op night", Desc = "Normal runs, chill, EU evenings", Mode = "Normal",
                    Mods = mine, Rules = "trainer: no cheats", Region = "eu", Players = 2, Max = 4, Open = true, ModsMatch = true,
                    StartedMinute = now - 47, ModVersion = Id.Version },
            new() { Code = "00000002", Name = "Archon grind", Desc = "Archon runs only, bring a healer", Mode = "Archon",
                    Mods = mine, Rules = "trainer: god mode, movement x", Region = "eu", Players = 3, Max = 4, Open = true,
                    Password = true, ModsMatch = true, StartedMinute = now - 135, ModVersion = Id.Version },
            new() { Code = "00000003", Name = "Old mod pack", Desc = "", Mode = "Any", Mods = "Ironstrike Trainer 0.9.0",
                    Region = "eu", Players = 4, Max = 4, Open = true, ModsMatch = false, StartedMinute = now - 9, ModVersion = "0.0.9" },
            new() { Code = "00000004", Name = "Arena testing", Desc = "Trying out builds, drop in", Mode = "Any",
                    Mods = mine, Rules = "", Region = "eu", Players = 1, Max = 3, Open = false, ModsMatch = true,
                    StartedMinute = now - 300, ModVersion = Id.Version },
        };
    }

    static List<HostControls.Player> MockPlayers() => new()
    {
        new() { Name = "Player Two", InstallId = "a1b2c3d4e5f60718" },
        new() { Name = "Player Three", InstallId = "0f1e2d3c4b5a6978" },
    };

    static List<HostControls.Player> PlayersShown() => preview ? MockPlayers() : HostControls.Players();

    internal static void StartPreview()
    {
        preview = true;
        previewStep = 0;
        previewNext = 0f;
        Plugin.Log.LogInfo("preview: cycling views with mock data");
    }

    static void PreviewTick(float now)
    {
        if (now < previewNext) return;
        var steps = new (string Name, Tab Tab, bool Hosting, string Sel)[]
        {
            ("Internet", Tab.Internet, false, "00000002"),
            ("Mismatch", Tab.Internet, false, "00000003"),
            ("Favorites", Tab.Favorites, false, "00000009"),
            ("HostForm", Tab.Host, false, null),
            ("Hosting", Tab.Host, true, null),
        };
        if (previewStep >= steps.Length) { preview = false; previewHosting = false; Close(); Plugin.Log.LogInfo("preview: done"); return; }
        var s = steps[previewStep++];
        previewHosting = s.Hosting;
        selected = s.Sel;
        showCode = false;
        if (!Panel.Visible) OpenAt(null, s.Tab); else { tab = s.Tab; Render(); }
        previewNext = now + 8f;
        Plugin.Log.LogInfo($"preview page: {s.Name}");
    }
}
