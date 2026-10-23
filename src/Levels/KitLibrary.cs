using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Levels;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SlipSkid.MoreMaps;

// Road pieces borrowed from other tracks ("from" on a route piece). Each track only has its own kit, so to use,
// say, Danibugne's banked corners on Aqueduc we load Danibugne once, keep hidden copies of the pieces we need,
// and build from those like from the base scene's own tiles.
// - Loading happens behind the level-loading transition (LoadAsyncScene patch), as a plain single-scene load
//   of the donor track before the base track: what the game itself does between levels. Level scenes hold no
//   network objects and set nothing up until LevelDefinition.Init, which never runs for a donor.
// - Copies live under an inactive DontDestroyOnLoad holder for the session, so a donor loads once.
// - Every machine takes the same piece from the same donor (TrackBuilder.FindTemplate), so builds match.
internal static class KitLibrary
{
    private static GameObject _holder;
    private static readonly Dictionary<string, GameObject> Templates = new();

    private static string Key(string scene, string code) => scene + "|" + code;

    // Scene name of a donor given as a level name (or scene name).
    internal static string DonorScene(string from)
    {
        if (string.IsNullOrEmpty(from) || !LevelManager.instance)
            return null;
        var level = LevelManager.instance.levels.FirstOrDefault(l => l && CustomLevels.RouteFor(l) == null &&
            (string.Equals(l.name, from, System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(l.sceneName, from, System.StringComparison.OrdinalIgnoreCase)));
        return level ? level.sceneName : null;
    }

    private static IEnumerable<(string scene, string code)> Needed(RouteData route) =>
        route.AllPieces().SelectMany(p => RoadKit.SegmentFor(p.type) is { } seg
                ? seg.Parts.Select(part => (DonorScene(seg.Donor), part.Code))
                : !string.IsNullOrEmpty(p.from) && p.type != RoutePlanner.Gap
                    ? new[] { (DonorScene(p.from), p.type) }
                    : Enumerable.Empty<(string, string)>())
            .Where(x => x.Item1 != null && x.Item1 != DonorScene(route.baseLevel)).Distinct();

    internal static bool HasAll(RouteData route) =>
        route == null || Needed(route).All(n => Templates.ContainsKey(Key(n.scene, n.code)));

    internal static GameObject Get(string from, string code) =>
        DonorScene(from) is { } scene && Templates.TryGetValue(Key(scene, code), out var t) ? t : null;

    // Loads each donor scene that still has pieces to give, and keeps inactive copies of them.
    internal static IEnumerator Harvest(RouteData route)
    {
        if (route == null)
            yield break;
        if (!_holder)
        {
            _holder = new GameObject("MoreMaps kit library");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
        }
        foreach (var donor in Needed(route).Where(n => !Templates.ContainsKey(Key(n.scene, n.code))).GroupBy(n => n.scene).ToList())
        {
            Plugin.Log.LogInfo($"KitLibrary: loading {donor.Key} for {string.Join(", ", donor.Select(d => d.code))}");
            var op = UnitySceneManager.LoadSceneAsync(donor.Key);
            while (!op.isDone)
                yield return null;
            var roots = UnitySceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var (scene, code) in donor)
                Guard.Run("KitLibrary", () =>
                {
                    var source = TrackBuilder.FindTemplate(roots, code);
                    if (!source)
                    {
                        Plugin.Log.LogError($"KitLibrary: {scene} has no usable '{code}'");
                        return;
                    }
                    // Under the inactive holder the copy never wakes up; TrackBuilder fixes it up per clone.
                    var copy = Object.Instantiate(source, _holder.transform);
                    copy.name = source.name;
                    copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    Templates[Key(scene, code)] = copy;
                });
        }
    }
}
