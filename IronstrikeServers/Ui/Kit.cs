using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeServers.Ui;

// The look: the classic Steam server browser -- olive panels, beveled buttons, sunken lists, small
// pixel icons, dense rows. Everything here is drawn by the mod at runtime (bevel frames are tiny
// 9-sliced textures, icons are pixel art), so it needs no assets and looks the same everywhere.
//
// Objects are created on the window's UI layer: the game's raycasters, including the VR
// TrackedDeviceGraphicRaycaster, filter by layer, and a fresh GameObject defaults to layer 0.
internal static class Kit
{
    internal static TMP_FontAsset Font;
    internal static int Layer = 5;

    // Classic Steam ("VGUI") palette.
    internal static readonly Color Face     = Hex(0x4C5844);    // window and buttons
    internal static readonly Color Well     = Hex(0x3E4637);    // lists, fields
    internal static readonly Color RowAlt   = Hex(0x434C3B);
    internal static readonly Color Light    = Hex(0x889180);    // bevel highlight
    internal static readonly Color Dark     = Hex(0x282E22);    // bevel shadow
    internal static readonly Color Select   = Hex(0x958831);    // selected row
    internal static readonly Color Text     = Hex(0xDEE5D7);
    internal static readonly Color Muted    = Hex(0xA0AA95);
    internal static readonly Color Accent   = Hex(0xC4B550);    // the yellow Steam used for titles
    internal static readonly Color Good     = Hex(0x9BD47A);
    internal static readonly Color Bad      = Hex(0xE07A5F);
    internal static readonly Color Clear    = new(0f, 0f, 0f, 0f);

    internal static Color Hex(int rgb, float a = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    // By hand: ColorUtility.ToHtmlStringRGBA is stripped from this build ("Method unstripping failed").
    internal static string Tag(Color c)
    {
        static int B(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        return $"#{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
    }

    // ------------------------------------------------------------------ drawn sprites

    internal static Sprite Raised, Sunken, Pressed, LockIcon, StarIcon, CheckIcon, UpIcon, DownIcon, CloseIcon, DotIcon;

    // Bevel borders are 2 texels; at this multiplier they come out ~6 canvas units, a crisp 2-3 px
    // line at normal viewing distance.
    const float BevelScale = 1f / 3f;

    internal static void EnsureSprites()
    {
        if (Raised != null) return;
        Raised  = Bevel(Face, Light, Dark);
        Pressed = Bevel(Hex(0x434C3B), Dark, Light);
        Sunken  = Bevel(Well, Dark, Light);

        LockIcon = Icon(new[]
        {
            "................",
            ".....######.....",
            "....##....##....",
            "....#......#....",
            "....#......#....",
            "....#......#....",
            "..############..",
            "..############..",
            "..#####..#####..",
            "..####....####..",
            "..#####..#####..",
            "..######.#####..",
            "..######.#####..",
            "..############..",
            "..############..",
            "................",
        }, Hex(0xE6D27A));
        StarIcon = Icon(new[]
        {
            ".......##.......",
            ".......##.......",
            "......####......",
            "......####......",
            ".....######.....",
            "################",
            ".##############.",
            "...##########...",
            "....########....",
            "....########....",
            "...####..####...",
            "...###....###...",
            "..###......###..",
            "..##........##..",
            ".#............#.",
            "................",
        }, Hex(0xF2D04B));
        CheckIcon = Icon(new[]
        {
            "................",
            "................",
            "..............##",
            ".............###",
            "............###.",
            "...........###..",
            ".##.......###...",
            ".###.....###....",
            "..###...###.....",
            "...###.###......",
            "....#####.......",
            ".....###........",
            "......#.........",
            "................",
            "................",
            "................",
        }, Text);
        UpIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            ".......##.......", "......####......", ".....######.....", "....########....",
            "...##########...", "..############..", "................", "................",
            "................", "................", "................", "................",
        }, Text);
        DownIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            "..############..", "...##########...", "....########....", ".....######.....",
            "......####......", ".......##.......", "................", "................",
            "................", "................", "................", "................",
        }, Text);
        CloseIcon = Icon(new[]
        {
            "................", "................", "..##........##..", "..###......###..",
            "...###....###...", "....###..###....", ".....######.....", "......####......",
            "......####......", ".....######.....", "....###..###....", "...###....###...",
            "..###......###..", "..##........##..", "................", "................",
        }, Text);
        DotIcon = Icon(new[]
        {
            "................", "................", "................", "................",
            "................", "......####......", ".....######.....", ".....######.....",
            ".....######.....", ".....######.....", "......####......", "................",
            "................", "................", "................", "................",
        }, Color.white);
    }

    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        t.hideFlags = HideFlags.HideAndDontSave;     // survive scene loads and asset unloads
        return t;
    }

    // An 8x8 face with a 2-texel bevel: light on top/left, dark on bottom/right (or swapped for sunken).
    static Sprite Bevel(Color face, Color tl, Color br)
    {
        const int n = 8, b = 2;
        var tex = NewTex(n, n);
        var px = new Il2CppStructArray<Color>(n * n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // y = 0 is the bottom row.
                bool bottom = y < b, top = y >= n - b, left = x < b, right = x >= n - b;
                Color c = face;
                if (top || left) c = tl;
                if (bottom || right) c = br;
                if ((top && right && x - (n - b) < (n - 1 - y)) || (bottom && left && x < y)) c = tl;   // mitred corners
                px[y * n + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                              SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // Pixel art: '#' is the colour, anything else transparent, first string is the top row.
    static Sprite Icon(string[] rows, Color c)
    {
        int h = rows.Length, w = rows[0].Length;
        var tex = NewTex(w, h);
        var px = new Il2CppStructArray<Color>(w * h);
        for (int r = 0; r < h; r++)
            for (int x = 0; x < w; x++)
                px[(h - 1 - r) * w + x] = rows[r][x] == '#' ? c : Clear;
        tex.SetPixels(px);
        tex.Apply();
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // ------------------------------------------------------------------ rects

    internal static RectTransform Rect(Transform parent, string name, float x0, float y0, float x1, float y1,
                                       float l = 0, float b = 0, float r = 0, float t = 0)
    {
        var go = new GameObject(name);
        go.layer = Layer;
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
        return rt;
    }

    // A fixed-height strip at y from the parent's top, full width less insets.
    internal static RectTransform Strip(Transform parent, string name, float y, float h, float l = 0, float r = 0)
    {
        var rt = Rect(parent, name, 0, 1, 1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(l, -y - h);
        rt.offsetMax = new Vector2(-r, -y);
        return rt;
    }

    // A fixed-size box at x from the parent's left (or right edge when fromRight), vertically centred.
    internal static RectTransform At(Transform parent, string name, float x, float w, float h = -1, bool fromRight = false)
    {
        var rt = Rect(parent, name, fromRight ? 1 : 0, h < 0 ? 0 : 0.5f, fromRight ? 1 : 0, h < 0 ? 1 : 0.5f);
        rt.pivot = new Vector2(fromRight ? 1 : 0, 0.5f);
        rt.sizeDelta = new Vector2(w, h < 0 ? 0 : h);
        rt.anchoredPosition = new Vector2(fromRight ? -x : x, 0);
        return rt;
    }

    internal static Image Fill(RectTransform rt, Color c, bool raycast = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    internal static Image Frame(RectTransform rt, Sprite bevel, bool raycast = false)
    {
        EnsureSprites();
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = bevel;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = BevelScale;
        img.color = Color.white;
        img.raycastTarget = raycast;
        return img;
    }

    internal static Image Glyph(RectTransform parent, Sprite icon, float size, Color? tint = null)
    {
        var rt = Rect(parent, "Icon", 0.5f, 0.5f, 0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = icon;
        img.preserveAspect = true;
        img.color = tint ?? Color.white;
        img.raycastTarget = false;
        return img;
    }

    // ------------------------------------------------------------------ text

    internal static TextMeshProUGUI Label(RectTransform parent, string s, float size, Color c,
                                          TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
                                          float padL = 0, float padR = 0, bool shrink = true)
    {
        var rt = Rect(parent, "Text", 0, 0, 1, 1, padL, 0, padR, 0);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) t.font = Font;
        t.richText = true;
        t.raycastTarget = false;
        t.alignment = align;
        t.color = c;
        t.fontSize = size;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        if (shrink)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = size * 0.7f;
        }
        t.text = s ?? "";
        return t;
    }

    // ------------------------------------------------------------------ controls

    internal sealed class Btn
    {
        public Button Button;
        public Image Bg;
        public TextMeshProUGUI Text;
    }

    // ColorBlock tints multiply the sprite, so the bevel art is drawn at full brightness and the
    // states darken or keep it.
    static void Tints(Button b, float normal = 0.92f, float hover = 1f, float press = 0.72f)
    {
        var cb = b.colors;
        cb.normalColor = new Color(normal, normal, normal, 1);
        cb.highlightedColor = new Color(hover, hover, hover, 1);
        cb.pressedColor = new Color(press, press, press, 1);
        cb.selectedColor = cb.normalColor;
        cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.55f);
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;
    }

    // A raised, beveled push button.
    internal static Btn Button(RectTransform rt, string text, Action click, float size, Sprite icon = null, bool bold = false)
    {
        var bg = Frame(rt, Raised, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        Tints(b);
        TextMeshProUGUI t = null;
        if (icon != null && string.IsNullOrEmpty(text)) Glyph(rt, icon, size);
        else if (text != null) t = Label(rt, bold ? $"<b>{text}</b>" : text, size, Text, TextAlignmentOptions.Center, 14, 14);
        Ui.Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg, Text = t };
    }

    // A flat clickable area (table rows, header cells): fill colour on hover/press only.
    internal static Btn Flat(RectTransform rt, Color fill, Action click)
    {
        var bg = Fill(rt, Color.white, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        var cb = b.colors;
        cb.normalColor = fill;
        cb.highlightedColor = Color.Lerp(fill, Light, 0.25f);
        cb.pressedColor = Color.Lerp(fill, Dark, 0.4f);
        cb.selectedColor = fill;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.05f;
        b.colors = cb;
        Ui.Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg };
    }

    // A sunken check box with its label; the whole thing is clickable.
    internal static Btn Check(RectTransform rt, string text, bool on, Action click, float size)
    {
        var hit = Fill(rt, Clear, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        var cb = b.colors;
        cb.normalColor = Clear;
        cb.highlightedColor = new Color(1, 1, 1, 0.06f);
        cb.pressedColor = new Color(1, 1, 1, 0.12f);
        cb.selectedColor = Clear;
        b.colors = cb;

        float box = size * 1.25f;
        var frame = At(rt, "Box", 4, box, box);
        Frame(frame, Sunken);
        if (on) Glyph(frame, CheckIcon, box * 0.8f);
        var t = Label(rt, text, size, Text, TextAlignmentOptions.MidlineLeft, box + 18, 4);
        Ui.Panel.Bind(b, click);
        return new Btn { Button = b, Bg = hit, Text = t };
    }

    // A sunken text field showing a value; clicking it edits (via the game's VR keyboard).
    internal static Btn Field(RectTransform rt, string value, Action click, float size)
    {
        var bg = Frame(rt, Sunken, true);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        Tints(b, 0.95f, 1.1f, 0.85f);
        var t = Label(rt, value, size, Text, TextAlignmentOptions.MidlineLeft, 18, 18);
        Ui.Panel.Bind(b, click);
        return new Btn { Button = b, Bg = bg, Text = t };
    }

    // [<] value [>]
    internal static void Spinner(RectTransform rt, string value, Action dec, Action inc, float size)
    {
        float h = rt.rect.height > 1 ? rt.rect.height : size * 1.6f;
        var minus = At(rt, "Dec", 0, h);
        Button(minus, "<", dec, size, bold: true);
        var plus = At(rt, "Inc", 0, h, fromRight: true);
        Button(plus, ">", inc, size, bold: true);
        var val = Rect(rt, "Val", 0, 0, 1, 1, h + 6, 0, h + 6, 0);
        Frame(val, Sunken);
        Label(val, value, size, Text, TextAlignmentOptions.Center);
    }
}
