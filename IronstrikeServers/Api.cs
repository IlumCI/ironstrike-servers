using System;
using System.Reflection;

namespace IronstrikeServers;

// For other plugins, read by reflection so nobody needs a hard dependency on this one.
// The trainer reads InModdedSession to treat a modded server like a private match.
public static class Api
{
    public static bool InModdedSession => Sessions.Current != null;
    public static bool IsHosting => Sessions.Current?.Kind == Sessions.Kind.Host;

    static MethodInfo trainerRules;
    static bool looked;

    // The trainer publishes a short summary of active cheats; shown in the listing so joiners know
    // what kind of game they are walking into.
    internal static string TrainerRules()
    {
        try
        {
            if (!looked)
            {
                looked = true;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("IronstrikeTrainer.Api", false);
                    if (t != null) { trainerRules = t.GetMethod("ActiveRules", BindingFlags.Public | BindingFlags.Static); break; }
                }
            }
            return trainerRules?.Invoke(null, null) as string ?? "";
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"could not read trainer rules: {e.Message}");
            return "";
        }
    }
}
