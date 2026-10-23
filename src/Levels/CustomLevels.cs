using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Levels;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlipSkid.MoreMaps;

// Our levels: one per route file in routes/ next to the DLL. Each is a copy of its base level (same scene,
// biome, lighting, music) with its own name, appended to LevelManager.levels on every machine.
// The game selects levels by INDEX (SelectLevelClientRpc) and loads them by NAME (StartLevelClientRpc), so
// every player needs the same route files, in the same order. There is no check for that yet.
internal static class CustomLevels
{
    internal static readonly List<RouteData> Routes = new();
    private static readonly Dictionary<Level, RouteData> RouteOf = new();

    // The level being loaded on this machine (set by SceneManager.LoadLevel, on host and clients alike).
    internal static Level Pending;

    internal static bool IsCustomName(string name) =>
        name != null && Routes.Exists(r => string.Equals(r.name, name.Trim(), System.StringComparison.OrdinalIgnoreCase));

    internal static RouteData RouteFor(Level level) =>
        level && RouteOf.TryGetValue(level, out var r) ? r : null;

    internal static void LoadRoutes()
    {
        Routes.Clear();
        var dir = Path.Combine(Plugin.Dir, "routes");
        if (!Directory.Exists(dir))
        {
            Plugin.Log.LogWarning($"Levels: no routes folder at {dir}");
            return;
        }
        // Sorted by file name so every machine builds the same list in the same order.
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => Path.GetFileName(f), System.StringComparer.Ordinal))
        {
            try
            {
                var route = JsonConvert.DeserializeObject<RouteData>(File.ReadAllText(file));
                route.file = Path.GetFileName(file);
                Routes.Add(route);
                Plugin.Log.LogInfo($"Levels: route '{route.name}' ({route.file}) on {route.baseLevel}, {route.pieces?.Count ?? 0} pieces");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Levels: can't read {file}: {e.Message}");
            }
        }
    }

    // LevelManager.Awake postfix. LevelManager lives for the whole session, but register defensively
    // (by name) in case it's ever recreated.
    internal static void Register(LevelManager manager)
    {
        if (!manager || manager.levels == null)
            return;
        var list = manager.levels.ToList();
        foreach (var route in Routes)
        {
            if (list.Any(l => l && l.name == route.name))
                continue;
            var baseLevel = list.FirstOrDefault(l => l && (l.name == route.baseLevel || l.sceneName == route.baseLevel));
            if (!baseLevel)
            {
                Plugin.Log.LogError($"Levels: '{route.name}': no base level '{route.baseLevel}'. Levels: " +
                                    string.Join(", ", list.Where(l => l).Select(l => $"{l.name}={l.sceneName}")));
                continue;
            }
            var level = Object.Instantiate(baseLevel);
            level.name = route.name;
            ((Object)level).name = route.name;
            // The vanilla preview meshes show the vanilla track; the level select copes with none.
            level.previewMeshes = new Level.LevelPreviewMesh[0];
            // Beating our ghost must not unlock the base track's reward.
            level.ghostUnlockableContent = default;
            level.inGameStats.isReversible = false;
            if (route.difficulty > 0)
                level.difficulty = route.difficulty;
            level.hideFlags = HideFlags.DontUnloadUnusedAsset;
            list.Add(level);
            RouteOf[level] = route;
            Plugin.Log.LogInfo($"Levels: registered '{level.name}' (scene {level.sceneName}) at index {list.Count - 1}");
        }
        manager.levels = list.ToArray();
    }
}
