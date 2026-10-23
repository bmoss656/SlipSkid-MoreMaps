using System;
using System.Collections.Generic;

namespace SlipSkid.MoreMaps;

// Every Harmony patch body runs inside Guard: an exception in our code must never escape into the game's
// method, where it would break vanilla behaviour and every other mod patched onto the same method.
// Each distinct failure is logged once, then the patch steps aside.
internal static class Guard
{
    private static readonly HashSet<string> Reported = new();

    internal static void Run(string system, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            Report(system, e);
        }
    }

    internal static void Report(string system, Exception e)
    {
        if (Reported.Add($"{system}|{e.GetType().Name}|{e.Message}"))
            Plugin.Log.LogError($"{system}: {e}");
    }
}
