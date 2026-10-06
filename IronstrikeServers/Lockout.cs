using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace IronstrikeServers;

// Keeps a modded client out of public multiplayer. The developer asked modders, in a note left in
// the binary, never to let mods reach public games; modded players get their own lobby instead.
//
// Same lock as the trainer's SafeMode: Play (PressPlay) and HOST (PressHost) are blocked, and so is
// the matchmaking they lead to. When the trainer is also loaded both plugins block (harmless), but
// only the trainer relabels the buttons so the two do not fight over the text.
[HarmonyPatch]
internal static class Lockout
{
    static bool announced;
    internal static bool On => Plugin.C.SafeMode.Value;

    static bool? trainer;
    internal static bool TrainerLoaded => trainer ??=
        BepInEx.Unity.IL2CPP.IL2CPPChainloader.Instance.Plugins.ContainsKey(Id.TrainerGuid);

    internal static void Announce()
    {
        if (announced) return;
        announced = true;
        Plugin.Log.LogMessage(On
            ? "SAFE MODE ON: Play and HOST locked; use the modded server browser (F7) instead."
            : "SAFE MODE OFF: public matchmaking is NOT blocked. Do not run cheats like this.");
    }

    // ---------------------------------------------------------------- entry points

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressPlay))]
    static bool BlockPlay()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: Play (quick match) is locked while modded.");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.PressHost))]
    static bool BlockHost()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: HOST (public) is locked while modded.");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.StartMatchmaking))]
    static bool BlockMatchmaking()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: blocked StartMatchmaking (public lobby).");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkLifecycle), nameof(NetworkLifecycle.FindOrHostMatchmaking))]
    static bool BlockFindOrHost()
    {
        if (!On) return true;
        Plugin.Log.LogWarning("SAFE MODE: blocked FindOrHostMatchmaking (public lobby).");
        return false;
    }

    // ---------------------------------------------------------------- the buttons

    static readonly HashSet<string> PublicMethods = new() { "PressPlay", "PressHost" };
    static readonly Dictionary<IntPtr, string> originalLabel = new();
    const float LockedAlpha = 0.35f;

    // Re-applied every second rather than once: the game toggles these buttons itself (online
    // status, run-type choices), and a lock that only lasts until its next refresh is no lock.
    internal static void LockPublicButtons()
    {
        MainMenuUI mm = null;
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid()) { mm = m; break; }
        if (mm == null) return;
        if (TrainerLoaded) return;      // the trainer locks and relabels them itself

        bool locked = On;
        int changed = 0;
        foreach (var b in mm.GetComponentsInChildren<Button>(true))
        {
            if (!IsPublic(b)) continue;
            if (!(b.interactable != locked && Mathf.Approximately(Alpha(b), locked ? LockedAlpha : 1f)))
            {
                b.interactable = !locked;
                SetAlpha(b, locked ? LockedAlpha : 1f);
                changed++;
            }
            Relabel(b, locked);     // every pass, so I2 localisation never wins
        }
        if (changed > 0) Plugin.Log.LogInfo($"{(locked ? "locked" : "unlocked")} {changed} public play button(s)");
    }

    // A main-menu card has a big title and a small description; the HOST pill has one short label.
    // Keep the card's title (PLAY stays PLAY, greyed) and say why in the description. Replacing the
    // title with a long string overflowed the card's huge font.
    static void Relabel(Button b, bool locked)
    {
        var own = OwnLabels(b);
        if (own.Count == 0) return;
        foreach (var t in own)
            if (!originalLabel.ContainsKey(t.Pointer)) originalLabel[t.Pointer] = t.text;

        TextMeshProUGUI target = own[0];
        foreach (var t in own)
            if (originalLabel[t.Pointer].Length > originalLabel[target.Pointer].Length) target = t;
        string text = own.Count == 1 ? "LOCKED" : "Locked while modded";

        if (locked)
        {
            foreach (var c in target.GetComponents<Component>())
                if (c != null && c.GetIl2CppType().Name == "Localize") UnityEngine.Object.Destroy(c);
            if (target.text != text)
            {
                target.text = text;
                target.enableAutoSizing = true;
                target.fontSizeMin = target.fontSize * 0.5f;
                target.fontSizeMax = target.fontSize;
            }
        }
        else if (target.text != originalLabel[target.Pointer])
            target.text = originalLabel[target.Pointer];
    }

    // Labels belonging to this button, not to a button nested inside it (Play contains HOST).
    static List<TextMeshProUGUI> OwnLabels(Button b)
    {
        var list = new List<TextMeshProUGUI>();
        foreach (var t in b.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            // Walked by hand: the GetComponentInParent<T>(bool) overload fails an Il2CppInterop
            // generic constraint check at runtime.
            Button owner = null;
            for (var p = t.transform; p != null && owner == null; p = p.parent) owner = p.GetComponent<Button>();
            if (owner != null && owner.Pointer == b.Pointer) list.Add(t);
        }
        return list;
    }

    static bool IsPublic(Button b)
    {
        for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            if (PublicMethods.Contains(b.onClick.GetPersistentMethodName(i))) return true;
        return false;
    }

    static float Alpha(Button b)
    {
        var cg = b.GetComponent<CanvasGroup>();
        return cg != null ? cg.alpha : 1f;
    }

    static void SetAlpha(Button b, float alpha)
    {
        var cg = b.GetComponent<CanvasGroup>() ?? b.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = alpha;
    }
}
