using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeServers.Ui;

// The main menu's way in.
//
// While public play is locked (SafeMode), the Play card and its HOST pill would just be dead buttons.
// So they are taken over instead: Play opens the modded server browser, HOST opens hosting. Same
// cards, same place, same look -- the menu reads as if modded servers were the game's own mode.
// Their editor-wired PressPlay/PressHost listeners are replaced, not added to, so public matchmaking
// is unreachable from them (and still blocked by Lockout's prefixes if anything else calls it).
//
// With SafeMode off, public play keeps its buttons and a SERVERS pill goes on the Private Match card.
internal static class MenuButton
{
    enum Role { Browse, Host }

    sealed class Claim
    {
        public Button Button;
        public Role Role;
        public List<TextMeshProUGUI> Labels;
        public TextMeshProUGUI Title, Desc;
    }

    static MainMenuUI menu;
    static readonly Dictionary<IntPtr, Claim> claimed = new();
    static GameObject pill;
    static TextMeshProUGUI pillLabel;
    static bool loggedTakeover;

    internal static bool OwnsPublicButtons => claimed.Count > 0;

    internal static void Tick()
    {
        var mm = FindMenu();
        if (mm == null) return;
        if (menu == null || menu.Pointer != mm.Pointer)
        {
            menu = mm;                      // new scene, new menu
            claimed.Clear();
            pill = null;
            loggedTakeover = false;
        }

        if (Lockout.On) TakeOver(mm);
        else Pill(mm);
    }

    static MainMenuUI FindMenu()
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) return m;
        return null;
    }

    static Transform Anchor(MainMenuUI mm)
    {
        var canvas = mm.GetComponentInChildren<Canvas>(true);
        return canvas != null ? canvas.transform : mm.transform;
    }

    // ------------------------------------------------------------------ takeover

    static void TakeOver(MainMenuUI mm)
    {
        int fresh = 0;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (b == null || claimed.ContainsKey(b.Pointer)) continue;
            Role? role = WiredTo(b, "PressPlay") ? Role.Browse : WiredTo(b, "PressHost") ? Role.Host : null;
            if (role == null) continue;

            var c = new Claim { Button = b, Role = role.Value, Labels = OwnLabels(b) };
            PickTitleAndDesc(c);
            if (c.Desc != null) AnchorTitleEnd(c.Title);
            foreach (var t in c.Labels) Unlocalize(t);

            var anchor = Anchor(mm);
            Panel.Bind(b, role == Role.Browse ? () => Pages.OpenBrowser(anchor) : () => Pages.OpenHost(anchor));
            claimed[b.Pointer] = c;
            fresh++;
        }
        if (fresh > 0 && !loggedTakeover)
        {
            loggedTakeover = true;
            Plugin.Log.LogMessage($"main menu: took over {claimed.Count} Play/HOST button(s) for modded servers");
        }

        // Every pass: the game toggles these itself (online status, run types), and the trainer's
        // SafeMode may have greyed them out before we claimed them.
        foreach (var c in claimed.Values)
        {
            if (c.Button == null) continue;
            if (!c.Button.interactable) c.Button.interactable = true;
            var cg = c.Button.GetComponent<CanvasGroup>();
            if (cg != null && (cg.alpha < 1f || !cg.interactable)) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
            Write(c);
        }
    }

    // A card (Play) has a big title and a smaller description; a pill (HOST) has one short label.
    static void PickTitleAndDesc(Claim c)
    {
        if (c.Labels.Count == 0) return;
        TextMeshProUGUI shortest = c.Labels[0], longest = c.Labels[0];
        foreach (var t in c.Labels)
        {
            if (t.text.Length < shortest.text.Length) shortest = t;
            if (t.text.Length > longest.text.Length) longest = t;
        }
        c.Title = shortest;
        c.Desc = c.Labels.Count > 1 && longest.Pointer != shortest.Pointer ? longest : null;
    }

    // The card's title sits left of its icon. Our longer title must end where the original one did, so
    // measure the original text's right edge and right-align ours to it, growing leftwards.
    static void AnchorTitleEnd(TextMeshProUGUI t)
    {
        if (t == null) return;
        try
        {
            t.ForceMeshUpdate();
            float textRight = t.textBounds.max.x;
            var rt = t.rectTransform;
            if (float.IsNaN(textRight) || float.IsInfinity(textRight)) return;
            float delta = textRight - rt.rect.xMax;
            rt.offsetMin = new Vector2(rt.offsetMin.x + delta, rt.offsetMin.y);
            rt.offsetMax = new Vector2(rt.offsetMax.x + delta, rt.offsetMax.y);
            t.alignment = TextAlignmentOptions.Right;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"main menu: could not align card title: {e.Message}"); }
    }

    static void Write(Claim c)
    {
        string title = c.Role == Role.Host ? "HOST" : c.Desc != null ? "Servers" : "SERVERS";
        Set(c.Title, title, shrink: false);
        Set(c.Desc, "Modded server browser", shrink: true);
    }

    // Titles keep the card's big font and may run past their (Play-sized) rect; descriptions shrink
    // to stay inside the card.
    static void Set(TextMeshProUGUI t, string s, bool shrink)
    {
        if (t == null || t.text == s) return;
        t.text = s;
        t.enableWordWrapping = false;
        if (shrink)
        {
            if (!t.enableAutoSizing) { t.fontSizeMax = t.fontSize; t.fontSizeMin = t.fontSize * 0.6f; }
            t.enableAutoSizing = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
        else
        {
            t.enableAutoSizing = false;
            t.overflowMode = TextOverflowModes.Overflow;
        }
    }

    static void Unlocalize(TextMeshProUGUI t)
    {
        foreach (var c in t.GetComponents<Component>())
            if (c != null && c.GetIl2CppType().Name == "Localize") UnityEngine.Object.Destroy(c);
    }

    // Labels belonging to this button, not to a button nested inside it (Play contains HOST).
    static List<TextMeshProUGUI> OwnLabels(Button b)
    {
        var list = new List<TextMeshProUGUI>();
        foreach (var t in b.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            Button owner = null;
            for (var p = t.transform; p != null && owner == null; p = p.parent) owner = p.GetComponent<Button>();
            if (owner != null && owner.Pointer == b.Pointer) list.Add(t);
        }
        return list;
    }

    static bool WiredTo(Button b, string method)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (b.onClick.GetPersistentMethodName(i) == method) return true;
        return false;
    }

    // ------------------------------------------------------------------ pill (SafeMode off)

    static void Pill(MainMenuUI mm)
    {
        if (pill != null)
        {
            if (pillLabel != null && pillLabel.text != "SERVERS") pillLabel.text = "SERVERS";
            return;
        }

        Button card = Find(mm, "PressPrivateMatch", false);
        Button host = Find(mm, "PressHost", true) ?? Find(mm, "PressHost", false);
        if (card == null || host == null)
        {
            Plugin.Log.LogWarning("could not find the Private Match card or a HOST pill; F7 still opens the browser");
            pill = new GameObject("ServersButtonUnavailable");   // stop retrying
            return;
        }

        var go = UnityEngine.Object.Instantiate(host.gameObject, card.transform);
        go.name = "ServersButton";
        Panel.StripBehaviour(go);
        var cg = go.GetComponent<CanvasGroup>();
        if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
        var btn = go.GetComponent<Button>();
        btn.interactable = true;

        CopyCornerInset(host.GetComponent<RectTransform>(), host.transform.parent.GetComponent<RectTransform>(),
                        go.GetComponent<RectTransform>(), card.GetComponent<RectTransform>());

        pillLabel = go.GetComponentInChildren<TextMeshProUGUI>(true);
        if (pillLabel != null) { Unlocalize(pillLabel); Panel.Fit(pillLabel); }
        var anchor = Anchor(mm);
        Panel.Bind(btn, () => Pages.Toggle(anchor));

        pill = go;
        Plugin.Log.LogMessage("main menu: SERVERS pill added to the Private Match card");
    }

    static Button Find(MainMenuUI mm, string method, bool visibleOnly)
    {
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (visibleOnly && !b.gameObject.activeInHierarchy) continue;
            if (b.name == "ServersButton" || b.name == "TrainerButton") continue;
            if (WiredTo(b, method)) return b;
        }
        return null;
    }

    static void CopyCornerInset(RectTransform src, RectTransform srcCard, RectTransform dst, RectTransform dstCard)
    {
        var a = new Il2CppStructArray<Vector3>(4); src.GetWorldCorners(a);
        var c = new Il2CppStructArray<Vector3>(4); srcCard.GetWorldCorners(c);
        Vector3 inset = srcCard.InverseTransformPoint(a[2]) - srcCard.InverseTransformPoint(c[2]);   // 2 = top-right

        dst.anchorMin = dst.anchorMax = new Vector2(1f, 1f);
        dst.pivot = new Vector2(1f, 1f);
        dst.sizeDelta = src.rect.size;
        dst.anchoredPosition = new Vector2(inset.x, inset.y);
    }
}
