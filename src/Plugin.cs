using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace SlipSkid.MoreMaps;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    // Never change Guid after release: it names every player's config file.
    public const string Guid = "slipskid.moremaps";
    public const string Name = "More Maps";
    public const string Version = "0.0.1";

    internal static ManualLogSource Log;
    internal static string Dir;

    internal static ConfigEntry<bool> DumpKit;
    internal static ConfigEntry<bool> DumpAllTracks;
    internal static ConfigEntry<bool> PreviewRoutes;
    internal static ConfigEntry<bool> QuitAfterDump;
    internal static ConfigEntry<bool> RunProbe;
    internal static ConfigEntry<string> Gallery;
    internal static ConfigEntry<string> Snapshot;
    internal static ConfigEntry<bool> DumpBiomes;

    private void Awake()
    {
        Log = Logger;
        Dir = Path.GetDirectoryName(Info.Location);
        DumpKit = Config.Bind("Debug", "DumpKit", false,
            "On every level load, write the road kit (tiles, start/finish, checkpoints, spawns...) to the log " +
            "and to kit-<scene>.json next to the DLL.");
        DumpAllTracks = Config.Bind("Debug", "DumpAllTracks", false,
            "At the main menu, load every vanilla track scene in turn and dump its road kit, then turn this off.");
        PreviewRoutes = Config.Bind("Debug", "PreviewRoutes", false,
            "At the main menu, build every custom route on its base scene, log a report and save preview images " +
            "(preview-<level>-*.png next to the DLL), then turn this off.");
        RunProbe = Config.Bind("Debug", "Probe", false,
            "At the main menu, log level preview data, every surface's physics and the car's stats, then turn this off.");
        Gallery = Config.Bind("Debug", "Gallery", "",
            "Comma-separated track names: at the main menu, render every road piece type of each to gallery-<track>.png " +
            "next to the DLL, then clear this.");
        Snapshot = Config.Bind("Debug", "Snapshot", "",
            "'Track: x,y,z; x,y,z | Track2: ...': at the main menu, render each point of a vanilla track (with its scenery) " +
            "from four sides and above to snapshot-<track>-<n>-*.png next to the DLL, then clear this.");
        DumpBiomes = Config.Bind("Debug", "DumpBiomes", false,
            "At the main menu, load every vanilla track and list each biome's scenery props to biomes-<track>.txt " +
            "next to the DLL, then turn this off.");
        QuitAfterDump = Config.Bind("Debug", "QuitAfterDump", false,
            "Quit the game when DumpAllTracks/PreviewRoutes finish.");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Single)
                TrackBuilder.BuiltRoute = null;
            Guard.Run("AutoDump", () => AutoDump.OnSceneLoaded(scene.name, this));
        };
        CustomLevels.LoadRoutes();
        var harmony = new Harmony(Guid);
        harmony.PatchAll();
        foreach (var method in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {method.DeclaringType?.Name}.{method.Name}");
        Log.LogInfo($"{Name} {Version} loaded");
    }
}
