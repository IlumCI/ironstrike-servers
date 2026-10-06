using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace IronstrikeServers.Ui;

// The window: a copy of the game's Options window, widened into a landscape browser and emptied, so
// our own views can be laid out inside it.
//
// Copying keeps what is hard to get right from scratch and impossible to test in VR from here -- the
// world-space canvas, the tracked-device raycaster, the window art (9-sliced, so it widens cleanly),
// the fonts, the scroll view with its mask and scrollbar. Everything that made it the Options menu is
// stripped, so none of the game's menu logic runs on our content. (Injecting rows into the real
// Options page broke its layout; this shares nothing with it at runtime.)
internal static class Panel
{
    // Landscape, like a desktop server browser. The original window is 1750 x 1700.
    internal const float Width = 2600f, Height = 1650f;
    // Window chrome: title bar height and the inner margin.
    internal const float TitleH = 92f, Margin = 36f;

    static GameObject panel;
    static RectTransform box;
    static TextMeshProUGUI title, subtitle;
    static bool hasPlace;

    internal static RectTransform Box => box;
    internal static ScrollRect Scroll { get; private set; }
    internal static RectTransform Content { get; private set; }

    // Game art, for buttons and check boxes that look like the game's own.
    internal static Sprite ButtonSprite, SquareSprite, CheckSprite;
    internal static TMP_FontAsset TitleFont, BodyFont, LightFont;

    static readonly List<object> alive = new();     // Il2Cpp delegates die with their managed source

    internal static Action OnBack;                   // the window's own X button

    static readonly HashSet<string> Strip = new()
        { "OptionsMenuUI", "OptionsOrganizerUI", "OptionsItemUI", "Transitioner", "Localize" };

    internal static bool Ready => panel != null;
    internal static bool Visible => panel != null && panel.activeSelf;

    // ------------------------------------------------------------------ show / hide

    internal static void Show(Transform beside, bool keepPlace = false)
    {
        if (panel == null) Build();
        if (panel == null) { Plugin.Log.LogWarning("server window not ready yet"); return; }
        if (!keepPlace || !hasPlace) { Place(beside); hasPlace = true; }
        panel.SetActive(true);
    }

    internal static void Hide() { if (panel != null) panel.SetActive(false); }

    // Back after being out of the way (the game's keyboard), where it was.
    internal static void Reshow() { if (panel != null) panel.SetActive(true); }

    internal static void SetTitle(string s, string sub = "")
    {
        if (title != null && title.text != s) title.text = s;
        if (subtitle != null && subtitle.text != sub) subtitle.text = sub;
    }

    // ------------------------------------------------------------------ build

    internal static void Build()
    {
        if (panel != null) return;
        Kit.EnsureSprites();
        // Scene-bound on purpose: a world-space canvas keeps its scene's camera and event setup, so a
        // window carried across a scene load stops taking clicks. Each scene builds its own.
        hasPlace = false;

        OptionsMenuUI src = null;
        foreach (var m in Resources.FindObjectsOfTypeAll<OptionsMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) { src = m; break; }   // skip prefab assets
        if (src == null) return;

        var canvas = src.transform.Find("Canvas");
        if (canvas == null) { Plugin.Log.LogWarning("Options menu has no Canvas child"); return; }

        var go = UnityEngine.Object.Instantiate(canvas.gameObject);
        go.name = "IronstrikeServersWindow";
        go.transform.localScale = canvas.lossyScale;
        go.SetActive(false);

        // Art and fonts are taken by name, before anything is stripped.
        TakeArt(go.transform);

        StripBehaviour(go);
        foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
        {
            cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
        }

        go.GetComponent<RectTransform>().sizeDelta = new Vector2(Width, Height);

        box = go.transform.Find("MenuBox")?.GetComponent<RectTransform>();
        if (box == null) { Plugin.Log.LogWarning("Options canvas has no MenuBox"); return; }

        // The game's window art goes; the window is drawn classic-Steam style instead.
        foreach (var n in new[] { "background", "bgfill", "Separator", "BackButton", "Title", "FirstTimeStuff" })
            box.Find(n)?.gameObject.SetActive(false);
        Kit.Layer = box.gameObject.layer;
        Kit.Font = BodyFont ?? TitleFont;
        Chrome();

        Scroll = box.Find("Scroll View")?.GetComponent<ScrollRect>();
        Content = box.Find("Scroll View/Viewport/Content")?.GetComponent<RectTransform>();
        if (Scroll == null || Content == null) { Plugin.Log.LogWarning("no scroll view in Options canvas"); return; }
        for (int i = Content.childCount - 1; i >= 0; i--)
        {
            var c = Content.GetChild(i).gameObject;
            c.SetActive(false);
            UnityEngine.Object.Destroy(c);
        }
        // The Options rows and section titles were cloned in as children too; none are needed.
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var c = go.transform.GetChild(i);
            if (c != box.transform) UnityEngine.Object.Destroy(c.gameObject);
        }

        StyleScrollbar();
        panel = go;
        Plugin.Log.LogInfo($"server window built: fonts title={Name(TitleFont)} body={Name(BodyFont)} " +
                           $"sprites button={Name(ButtonSprite)} square={Name(SquareSprite)} check={Name(CheckSprite)}");
    }

    // Frame, title bar and close button. Named F_* so ClearViews leaves them alone.
    static void Chrome()
    {
        var frame = Kit.Rect(box, "F_Window", 0, 0, 1, 1);
        Kit.Frame(frame, Kit.Raised, true);            // also blocks clicks from passing through
        frame.SetAsFirstSibling();

        var bar = Kit.Strip(box, "F_TitleBar", 10, TitleH - 14, 14, 14);
        title = Kit.Label(Kit.Rect(bar, "Title", 0, 0, 0.6f, 1, 20, 0, 0, 0), "Servers", 50, Kit.Accent);
        if (TitleFont != null) title.font = TitleFont;
        subtitle = Kit.Label(Kit.Rect(bar, "Sub", 0.35f, 0, 1, 1, 0, 0, 110, 0), "", 34, Kit.Muted, TextAlignmentOptions.MidlineRight);

        var close = Kit.At(bar, "Close", 6, 76, 64, fromRight: true);
        Kit.Button(close, null, () => { if (OnBack != null) OnBack(); else Hide(); }, 44, Kit.CloseIcon);
    }

    static void StyleScrollbar()
    {
        var sb = Scroll.verticalScrollbar;
        if (sb == null) return;
        var track = sb.GetComponent<Image>();
        if (track != null) { track.sprite = Kit.Sunken; track.type = Image.Type.Sliced; track.pixelsPerUnitMultiplier = 1f / 3f; track.color = Color.white; }
        var handle = sb.handleRect != null ? sb.handleRect.GetComponent<Image>() : null;
        if (handle != null) { handle.sprite = Kit.Raised; handle.type = Image.Type.Sliced; handle.pixelsPerUnitMultiplier = 1f / 3f; handle.color = Color.white; }
        var cb = sb.colors;
        cb.normalColor = new Color(0.92f, 0.92f, 0.92f, 1); cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1);
        sb.colors = cb;
        var vp = Scroll.viewport != null ? Scroll.viewport.GetComponent<Image>() : null;
        if (vp != null) vp.color = new Color(1, 1, 1, 1);      // mask only; content draws its own rows
    }

    static string Name(UnityEngine.Object o) => o != null ? o.name : "<none>";

    static void TakeArt(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var img = t.GetComponent<Image>();
            if (img != null && img.sprite != null)
            {
                string n = img.sprite.name;
                if (ButtonSprite == null && n == "button15-bw-fill") ButtonSprite = img.sprite;
                else if (SquareSprite == null && n == "squarebutton-bw") SquareSprite = img.sprite;
                else if (CheckSprite == null && n == "check-solid") CheckSprite = img.sprite;
            }
            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null && tmp.font != null)
            {
                string n = tmp.font.name;
                if (TitleFont == null && n.StartsWith("GemunuLibre-Bold")) TitleFont = tmp.font;
                else if (BodyFont == null && n.StartsWith("AgenorNeue")) BodyFont = tmp.font;
                else if (LightFont == null && n.StartsWith("GemunuLibre-Light")) LightFont = tmp.font;
            }
        }
    }

    internal static void StripBehaviour(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Component>(true))
            if (c != null && Strip.Contains(c.GetIl2CppType().Name))
                UnityEngine.Object.Destroy(c);
    }

    // Removes every view object we added (named V_*) and the scroll content; the window art, title,
    // X button and scroll view itself stay.
    internal static void ClearViews()
    {
        if (box == null) return;
        for (int i = box.childCount - 1; i >= 0; i--)
        {
            var c = box.GetChild(i);
            if (!c.name.StartsWith("V_")) continue;
            c.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(c.gameObject);
        }
        for (int i = Content.childCount - 1; i >= 0; i--)
        {
            var c = Content.GetChild(i).gameObject;
            c.SetActive(false);
            UnityEngine.Object.Destroy(c);
        }
    }

    // Puts the scroll view in a rect of the window body, as pixel insets from the box edges.
    internal static void PlaceScroll(float left, float bottom, float right, float top)
    {
        var rt = Scroll.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        Scroll.gameObject.SetActive(true);
        // Above the views drawn behind it (the table's frame and background), which are added later.
        Scroll.transform.SetAsLastSibling();
    }

    internal static void HideScroll() => Scroll.gameObject.SetActive(false);

    // Replace the event outright: RemoveAllListeners() leaves a cloned button's persistent (editor-
    // wired) listeners in place, and it would still fire its original handler.
    internal static void Bind(Button b, Action fn)
    {
        var d = DelegateSupport.ConvertDelegate<UnityAction>(new Action(() =>
        {
            try { fn(); } catch (Exception e) { Plugin.Log.LogError($"server window click: {e}"); }
        }));
        alive.Add(d);
        b.onClick = new Button.ButtonClickedEvent();
        b.onClick.AddListener(d);
    }

    internal static void KeepAlive(object o) => alive.Add(o);

    internal static void Fit(TextMeshProUGUI t)
    {
        if (t == null) return;
        float size = t.fontSize;
        t.enableWordWrapping = false;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = size * 0.55f;
        t.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ------------------------------------------------------------ placement

    static void Place(Transform beside)
    {
        float width = Width * panel.transform.lossyScale.x;

        if (beside != null)
        {
            // Over the main menu, slightly in front of it: the browser is the Play card opened up.
            panel.transform.SetPositionAndRotation(beside.position - beside.forward * width * 0.05f,
                                                   beside.rotation);
            return;
        }

        var cam = GM.instance?.MainCamera;
        if (cam == null) return;
        var fwd = cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        float dist = Mathf.Max(1.2f, width * 0.75f);
        panel.transform.SetPositionAndRotation(cam.transform.position + fwd * dist,
                                               Quaternion.LookRotation(fwd));
    }
}
