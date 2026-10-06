using System;
using HarmonyLib;
using IronstrikeServers.Ui;
using UnityEngine;

namespace IronstrikeServers;

// GM.Update is a Unity message called from native code, so IL2CPP cannot inline it away: the one
// reliable per-frame hook in this game. Everything here is cheap or rate-limited.
[HarmonyPatch]
internal static class Driver
{
    static bool sawUpdate, hotkeysOff;
    static float nextSlow, firstUpdateAt = -1f;
    static bool autoOpened;
    static int selfTest;      // 0 idle, 1 hosting requested, 2 browse requested
    static float selfTestAt;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GM), nameof(GM.Update))]
    static void Update()
    {
        if (!sawUpdate)
        {
            sawUpdate = true;
            Plugin.Log.LogInfo("HOOK CONFIRMED: GM.Update fired.");
            Lockout.Announce();
        }
        if (!Plugin.C.Enabled.Value) return;

        try
        {
            Sessions.Tick();
            Browser.Tick();
            Pages.Tick();
        }
        catch (Exception e) { Plugin.Log.LogError($"tick: {e}"); }

        float now = Time.realtimeSinceStartup;
        if (now >= nextSlow)
        {
            nextSlow = now + 1f;
            try
            {
                Panel.Build();
                MenuButton.Tick();
                Lockout.LockPublicButtons();
            }
            catch (Exception e)
            {
                nextSlow = now + 10f;
                Plugin.Log.LogError($"menu upkeep: {e}");
            }
        }

        if (Plugin.C.EnableHotkeys.Value && !hotkeysOff) Hotkeys();
        MaybeAutoOpen(now);
    }

    static void MaybeAutoOpen(float now)
    {
        float after = Plugin.C.AutoOpenAfter.Value;
        if (autoOpened) { SelfTest(now); return; }
        if (after <= 0f) return;
        if (firstUpdateAt < 0f) firstUpdateAt = now;
        if (now - firstUpdateAt < after || !Panel.Ready) return;
        autoOpened = true;
        if (Plugin.C.PreviewUi.Value)
        {
            ShowMainMenu();
            selfTest = 3; selfTestAt = now + 10f;
            return;
        }
        Plugin.Log.LogInfo($"auto-opening server browser after {after}s");
        Pages.Open(null);
        if (Plugin.C.AutoHost.Value) { selfTest = 1; selfTestAt = now + 8f; }
    }

    // Hosting self-test, bisecting on failure: full setup, then region US, then without the newer
    // listing properties. After the first success, list our lobby and probe the vanilla one.
    static readonly (string Name, string Region, bool SkipExtra)[] HostVariants =
        { ("full", null, false), ("region us", "us", false), ("no extra props", null, true) };
    static int variant;

    static void SelfTest(float now)
    {
        if (selfTest == 0 || now < selfTestAt) return;
        switch (selfTest)
        {
            case 3:
                selfTest = 0;
                Pages.StartPreview();
                return;
            case 1:
                var v = HostVariants[variant];
                Sessions.DebugRegion = v.Region;
                Sessions.DebugSkipExtraProps = v.SkipExtra;
                Plugin.Log.LogInfo($"self-test: hosting ({v.Name})");
                Sessions.Host();
                selfTest = 6; selfTestAt = now + 25f;
                return;
            case 6:
                if (Api.IsHosting)
                {
                    Plugin.Log.LogMessage($"self-test: hosting works with variant '{HostVariants[variant].Name}'");
                    selfTest = 2;
                }
                else if (++variant < HostVariants.Length)
                {
                    Plugin.Log.LogWarning($"self-test: variant failed; status '{Sessions.Status}'");
                    selfTest = 1; selfTestAt = now + 15f;   // the game fades back to the haven first
                }
                else { Plugin.Log.LogWarning("self-test: every hosting variant failed"); selfTest = 7; }
                return;
            case 2:
                Plugin.Log.LogInfo($"self-test: browsing the modded lobby while hosting={Api.IsHosting}");
                Browser.Refresh();
                selfTest = 4; selfTestAt = now + 20f;
                return;
            case 4:
                Plugin.Log.LogInfo($"self-test: probing the vanilla lobby while hosting={Api.IsHosting}");
                Browser.Refresh(vanillaProbe: true);
                selfTest = 7; selfTestAt = now + 20f;
                return;
            default:
                Browser.Close();
                Sessions.DebugRegion = null;
                Sessions.DebugSkipExtraProps = false;
                Plugin.Log.LogInfo("self-test: done");
                selfTest = 0;
                return;
        }
    }

    // Preview only: the game's own Show, placed in front of the player, so the takeover of the Play
    // card can be screenshotted flat.
    static void ShowMainMenu()
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<MainMenuUI>())
            if (m != null && m.gameObject.scene.IsValid())
            {
                try { m.Show(MenuBase.PositionType.PlaceAtPlayer); Plugin.Log.LogInfo("preview: main menu shown"); }
                catch (Exception e) { Plugin.Log.LogWarning($"preview: could not show main menu: {e.Message}"); }
                return;
            }
        Plugin.Log.LogWarning("preview: no main menu in this scene");
    }

    static void Hotkeys()
    {
        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) Pages.Toggle(null);
        }
        catch (Exception e)
        {
            hotkeysOff = true;
            Plugin.Log.LogWarning($"hotkeys off ({e.GetType().Name}); use the SERVERS button.");
        }
    }
}
