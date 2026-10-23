using System;
using System.Collections.Generic;
using System.Linq;
using World;

namespace SlipSkid.MoreMaps;

// Route files name surfaces simply ("Ice", "Mud", "Reverse"); the game's are "IceSurface", "MudSurface"...
// and GoodCards' reversed steering is "CustomContent_Reverse". Lookups are by name, so they don't depend on
// SurfaceManager.surfaces' order (which other mods change).
internal static class Surfaces
{
    // Used when a surface's mod isn't installed: the road stays drivable, if not the same.
    private static readonly Dictionary<string, string> Fallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Reverse"] = "Ice",
    };

    private static readonly HashSet<string> Warned = new();

    internal static Surface Find(string name)
    {
        var found = Lookup(name);
        if (found)
            return found;
        if (Fallbacks.TryGetValue(name, out var fallback) && Lookup(fallback) is { } other && other)
        {
            if (Warned.Add(name))
                Plugin.Log.LogWarning($"Surfaces: no '{name}' surface (is its mod installed?); using {fallback} instead. " +
                                      "Other players may see the real one.");
            return other;
        }
        if (Warned.Add(name))
            Plugin.Log.LogError($"Surfaces: unknown surface '{name}'. Known: " +
                                string.Join(", ", All().Select(s => s.name)));
        return null;
    }

    private static Surface Lookup(string name) =>
        All().FirstOrDefault(s => Matches(s.name, name));

    internal static bool Matches(string surfaceName, string name) =>
        string.Equals(surfaceName, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(surfaceName, name + "Surface", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(surfaceName, "CustomContent_" + name, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Surface> All() =>
        SurfaceManager.instance && SurfaceManager.instance.surfaces != null
            ? SurfaceManager.instance.surfaces.Where(s => s)
            : Enumerable.Empty<Surface>();
}
