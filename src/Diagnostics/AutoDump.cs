using System.Collections;
using System.Linq;
using Game.Levels;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SlipSkid.MoreMaps;

// [Debug] DumpAllTracks: once the main menu is up, load every vanilla track scene in turn (plain Unity scene
// loads, outside any match), run KitDump on each, then go back to the main menu. Turns itself off afterwards.
// With [Debug] PreviewRoutes it also builds each of our routes on its base scene and dumps the result.
// Diagnostic only: the scenes' own Awake code may log errors because no match is running.
internal static class AutoDump
{
    private static bool _started;

    internal static void OnSceneLoaded(string scene, MonoBehaviour host)
    {
        if (_started || scene != "MainMenu" || !(Plugin.DumpAllTracks.Value || Plugin.PreviewRoutes.Value || Plugin.RunProbe.Value ||
                                                     !string.IsNullOrWhiteSpace(Plugin.Gallery.Value) ||
                                                     !string.IsNullOrWhiteSpace(Plugin.Snapshot.Value) || Plugin.DumpBiomes.Value))
            return;
        _started = true;
        host.StartCoroutine(Routine());
    }

    private static IEnumerator Routine()
    {
        yield return new WaitForSeconds(3f);
        var levels = LevelManager.instance ? LevelManager.instance.levels : null;
        if (levels == null)
        {
            Plugin.Log.LogError("AutoDump: no LevelManager");
            yield break;
        }

        if (Plugin.RunProbe.Value)
            Guard.Run("Probe", Probe.Run);
        foreach (var scene in (Plugin.Gallery.Value ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray())
            yield return Probe.Gallery(scene);

        foreach (var spec in (Plugin.Snapshot.Value ?? "").Split('|').Select(x => x.Trim()).Where(x => x.Contains(":")).ToArray())
            yield return Probe.Snapshot(spec);

        if (Plugin.DumpAllTracks.Value)
            foreach (var level in levels.Where(l => l && CustomLevels.RouteFor(l) == null).ToArray())
            {
                yield return Load(level.sceneName);
                var def = Object.FindObjectOfType<LevelDefinition>();
                if (def)
                    Guard.Run("AutoDump", () => KitDump.Run(def, level.name));
                else
                    Plugin.Log.LogWarning($"AutoDump: {level.sceneName} has no LevelDefinition");
            }

        if (Plugin.DumpBiomes.Value)
            foreach (var level in levels.Where(l => l && CustomLevels.RouteFor(l) == null).ToArray())
            {
                yield return Load(level.sceneName);
                var def = Object.FindObjectOfType<LevelDefinition>();
                if (!def)
                    continue;
                ShowScenery(def);
                Guard.Run("AutoDump", () => BiomeDump.Run(def, level.sceneName));
            }

        if (Plugin.PreviewRoutes.Value)
            foreach (var level in levels.Where(l => l && CustomLevels.RouteFor(l) != null).ToArray())
            {
                yield return KitLibrary.Harvest(CustomLevels.RouteFor(level));
                yield return Load(level.sceneName);
                var def = Object.FindObjectOfType<LevelDefinition>();
                var route = CustomLevels.RouteFor(level);
                if (!def)
                    continue;
                if (Plugin.DumpKit.Value)
                    Guard.Run("AutoDump", () => KitDump.Run(def, level.name));
                Guard.Run("AutoDump", () => TrackBuilder.Build(def, route));
                // The game's own init on the built track (tile lookup: duplicate ids throw; surface count), then
                // the scenery.
                ShowScenery(def);
                Guard.Run("SceneFit", () => SceneFit.Apply(def, TrackBuilder.LastBuild));
                Guard.Run("Scenery", () => Scenery.Place(def, route, TrackBuilder.LastBuild));
                yield return null;
                LevelSelectInfo.Prepare();
                yield return LevelSelectInfo.RenderPreviewMesh(level);
                Guard.Run("AutoDump", () => TrackPreview.Report(def, route));
                yield return TrackPreview.Capture(def, route);
            }

        Plugin.DumpAllTracks.Value = false;
        Plugin.PreviewRoutes.Value = false;
        Plugin.RunProbe.Value = false;
        Plugin.Gallery.Value = "";
        Plugin.Snapshot.Value = "";
        Plugin.DumpBiomes.Value = false;
        Plugin.Log.LogInfo("AutoDump: done");
        if (Plugin.QuitAfterDump.Value)
        {
            Application.Quit();
            yield break;
        }
        yield return Load("MainMenu");
    }

    // Runs the game's level init as far as it gets outside a match, then shows the biome scenery the tiles
    // call for (Init stops before that), so checks see its colliders and pictures look like the game.
    internal static void ShowScenery(LevelDefinition def)
    {
        try
        {
            def.Init();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogInfo($"AutoDump: LevelDefinition.Init stopped outside a match ({e.GetType().Name}), as expected");
        }
        try
        {
            var t = HarmonyLib.Traverse.Create(def);
            t.Method("CountTilesSurfaces").GetValue();
            var biome = t.Field("_maxSurfaceTileCount").GetValue<World.Surface>();
            var visuals = t.Field("_environmentBiomes").GetValue<System.Collections.IDictionary>();
            if (biome && visuals != null && visuals.Contains(biome))
            {
                var entry = visuals[biome];
                var holder = (GameObject)entry.GetType().GetField("Item2").GetValue(entry);
                holder.SetActive(true);
                Plugin.Log.LogInfo($"AutoDump: showing the {biome.name} scenery");
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"AutoDump: couldn't show the scenery: {e.GetType().Name} {e.Message}");
        }
    }

    internal static IEnumerator Load(string scene)
    {
        Plugin.Log.LogInfo($"AutoDump: loading {scene}");
        var op = UnitySceneManager.LoadSceneAsync(scene);
        while (!op.isDone)
            yield return null;
        yield return null;
        yield return null;
    }
}
