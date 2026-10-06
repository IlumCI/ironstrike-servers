using System;
using Il2CppInterop.Runtime;

namespace IronstrikeServers.Ui;

// Text entry through the game's own VR keyboard -- the one Private Match uses for codes -- so it
// works with controllers in VR and with the mouse flat, and looks like the rest of the game.
internal static class Keyboard
{
    internal static bool Ask(string title, int maxLength, Action<string> done)
    {
        var kb = VRKeyboard.instance;
        if (kb == null) { Plugin.Log.LogWarning("VR keyboard not available in this scene"); return false; }

        int oldMax = kb.maxInputLength;
        bool finished = false;

        Action<string> finish = s =>
        {
            if (finished) return;
            finished = true;
            kb.maxInputLength = oldMax;
            Panel.Reshow();
            done((s ?? "").Trim());
            Pages.Rerender();
        };
        Action back = () =>
        {
            if (finished) return;
            finished = true;
            kb.maxInputLength = oldMax;
            Panel.Reshow();
        };

        var f = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(finish);
        var b = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(back);
        Panel.KeepAlive(f);
        Panel.KeepAlive(b);

        kb.maxInputLength = maxLength;
        Panel.Hide();
        try { kb.Show(title, f, b); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"VR keyboard: {e.Message}");
            kb.maxInputLength = oldMax;
            Panel.Reshow();
            return false;
        }
        return true;
    }
}
