using System;
using System.Collections;
using Game.Levels;
using HarmonyLib;
using Player;
using UnityEngine;

namespace SlipSkid.MoreMaps;

[HarmonyPatch(typeof(LevelManager), "Awake")]
internal static class LevelManagerAwakePatch
{
    private static void Postfix(LevelManager __instance) =>
        Guard.Run("Levels", () =>
        {
            if (LevelManager.instance == __instance)
                CustomLevels.Register(__instance);
        });
}

// Runs on every machine (StartLevelClientRpc -> LoadLevel(name) -> here), before the scene loads.
[HarmonyPatch(typeof(Game.SceneManager), nameof(Game.SceneManager.LoadLevel), typeof(Level))]
internal static class SceneManagerLoadLevelPatch
{
    private static void Prefix(Level selectedLevel) =>
        Guard.Run("Levels", () =>
        {
            CustomLevels.Pending = selectedLevel;
            Plugin.Log.LogInfo($"Levels: loading '{selectedLevel?.name}' (scene {selectedLevel?.sceneName})");
        });
}

// LevelDefinition.Init builds the tile lookup and counts tile surfaces, so the new track must be in place
// before it runs. ObstacleManager.InitLevel (just before) only looks up the ObstacleLevelDefinition.
[HarmonyPatch(typeof(LevelDefinition), nameof(LevelDefinition.Init))]
internal static class LevelDefinitionInitPatch
{
    private static void Prefix(LevelDefinition __instance)
    {
        var level = CustomLevels.Pending;
        CustomLevels.Pending = null;
        // Only the scene that level asked for (not, say, a tutorial loaded some other way afterwards).
        if (level && level.sceneName != __instance.gameObject.scene.name)
            level = null;
        var route = CustomLevels.RouteFor(level);
        if (Plugin.DumpKit.Value)
            Guard.Run("KitDump", () => KitDump.Run(__instance, level ? level.name : "?"));
        if (route != null)
            Guard.Run("TrackBuilder", () => TrackBuilder.Build(__instance, route));
    }
}

// Our levels' level-select preview, pictures and surface share, filled in the first time the list is built.
[HarmonyPatch(typeof(Menus.LevelSelect), "BuildUI")]
internal static class LevelSelectBuildUIPatch
{
    private static void Prefix() => Guard.Run("LevelSelect", LevelSelectInfo.Prepare);
}

// SceneManager.LoadAsyncScene, for our levels:
// - first loads any other tracks the route borrows pieces from (KitLibrary), behind the loading transition;
// - loads the scene even when it's already the active one (the game skips that, e.g. going straight from a level
//   to another on the same scene), so we never reuse a scene whose road we changed. (MapKitLoader does the same.)
[HarmonyPatch(typeof(Game.SceneManager), "LoadAsyncScene")]
internal static class LoadAsyncScenePatch
{
    private static bool Prefix(Game.SceneManager __instance, string sceneName, Action onComplete)
    {
        try
        {
            var route = CustomLevels.RouteFor(CustomLevels.Pending);
            var needDonors = route != null && !KitLibrary.HasAll(route);
            var sameScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == sceneName &&
                            (route != null || TrackBuilder.BuiltRoute != null);
            if (!needDonors && !sameScene)
                return true;
            Plugin.Log.LogInfo($"Levels: loading {sceneName}" + (needDonors ? " after borrowing pieces from other tracks" : " afresh"));
            __instance.StartCoroutine(Reload(needDonors ? route : null, sceneName, onComplete));
            return false;
        }
        catch (Exception e)
        {
            Guard.Report("Levels", e);
            return true;
        }
    }

    private static IEnumerator Reload(RouteData donorsFor, string sceneName, Action onComplete)
    {
        if (donorsFor != null)
            yield return KitLibrary.Harvest(donorsFor);
        var op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone)
            yield return null;
        onComplete?.Invoke();
    }
}

// Time trial looks up the developers' ghost by level name in Addressables; ours have none, so answer "no
// ghost" instead of logging a failed lookup. (Same as MapKitLoader.)
[HarmonyPatch(typeof(PlayerGhostUtils), "LoadGameGhost", typeof(string), typeof(Action<(bool, PlayerGhostUtils.PlayerGhost)>))]
internal static class GameGhostPatch
{
    private static bool Prefix(string levelName, Action<(bool, PlayerGhostUtils.PlayerGhost)> loadedCallBack)
    {
        try
        {
            if (!CustomLevels.IsCustomName(levelName))
                return true;
            loadedCallBack?.Invoke((false, default));
            return false;
        }
        catch (Exception e)
        {
            Guard.Report("Levels", e);
            return true;
        }
    }
}

// Once the level has initialised (scenery instantiated): clear scenery off our road, fit the trails camera,
// then add the route's own background props.
[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.InitLevel))]
internal static class InitLevelPatch
{
    private static void Postfix()
    {
        Guard.Run("SceneFit", () =>
        {
            if (TrackBuilder.BuiltRoute != null && LevelManager.instance)
                SceneFit.Apply(LevelManager.instance.levelDefinition, TrackBuilder.LastBuild);
        });
        Guard.Run("Scenery", () =>
        {
            if (TrackBuilder.BuiltRoute != null && LevelManager.instance)
                Scenery.Place(LevelManager.instance.levelDefinition, TrackBuilder.BuiltRoute, TrackBuilder.LastBuild);
        });
    }
}
