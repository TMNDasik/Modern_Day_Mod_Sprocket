using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SoundOverhaul;






[BepInPlugin(Guid, "Sound Overhaul", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.soundoverhaul";
    public const string Version = "0.3.7";

    internal new static ManualLogSource Log;
    internal static string PluginDir;
    internal static bool Loaded;

    internal static ConfigEntry<bool> UseOriginal;
    internal static ConfigEntry<string> SoundFolder;
    internal static ConfigEntry<float> WeaponsVolume, InsideVolume, InsideOuter, LoadingVolume, MechanismVolume;
    internal static ConfigEntry<float> EngineVolume, TrackVolume, DriveVolume, ImpactVolume;
    internal static ConfigEntry<bool> DebugLogging;

    
    internal static bool Active => Loaded && !UseOriginal.Value;


    
    
    internal static string ModFolder(string root, string name)
    {
        var md = Path.Combine(root, "MD" + name);
        var old = Path.Combine(root, name);
        return Directory.Exists(md) || !Directory.Exists(old) ? md : old;
    }
    public override void Load()
    {
        Log = base.Log;
        PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Sound Overhaul {Version} is turned off in the Mod menu");
            return;
        }

        UseOriginal = Config.Bind("General", "Use original sounds", false,
            "On: Sprocket's own sounds and the mods' previous sounds come back (switch any time, also in battle).");
        SoundFolder = Config.Bind("General", "Sound folder", "",
            "Folder with the sound files (the same layout as the original collection: Weapons, Effects, Vehicle, …). " +
            "Empty = the Sound folder next to this mod.");
        WeaponsVolume = Config.Bind("Volume", "Gun shots", 1f, new ConfigDescription(
            "Cannons, autocannons and machine guns heard from outside.", new AcceptableValueRange<float>(0f, 2f)));
        InsideVolume = Config.Bind("Volume", "Gun shots inside the vehicle", 1f, new ConfigDescription(
            "Your own vehicle's guns while you look through the sight (the crew compartment sound).", new AcceptableValueRange<float>(0f, 2f)));
        InsideOuter = Config.Bind("Volume", "Sight view punch", 0.5f, new ConfigDescription(
            "How much of the outside shot is mixed into your own shots while you look through the sight. " +
            "The crew compartment recordings alone sound muffled; 0 = only them, 1 = as loud as they are.", new AcceptableValueRange<float>(0f, 1f)));
        LoadingVolume = Config.Bind("Volume", "Loading", 1f, new ConfigDescription(
            "Breech, shell insertion, ammo rack doors, spent cases.", new AcceptableValueRange<float>(0f, 2f)));
        MechanismVolume = Config.Bind("Volume", "Autoloader and launchers", 1f, new ConfigDescription(
            "AZ / MZ carousels, ATGM launches and flight, APS and smoke grenade launchers.", new AcceptableValueRange<float>(0f, 2f)));
        EngineVolume = Config.Bind("Volume", "Engines", 1f, new ConfigDescription(
            "Diesel engines: running, start and shutdown (the gas turbine has its own volume in Powertrain).", new AcceptableValueRange<float>(0f, 2f)));
        TrackVolume = Config.Bind("Volume", "Tracks", 1f, new ConfigDescription(
            "Track clatter and squeak; inside your own vehicle also the rattle of the hull.", new AcceptableValueRange<float>(0f, 2f)));
        DriveVolume = Config.Bind("Volume", "Turret and gun drives", 1f, new ConfigDescription(
            "Turret traverse motor and brake, gun elevation drive.", new AcceptableValueRange<float>(0f, 2f)));
        ImpactVolume = Config.Bind("Volume", "Hits and explosions", 1f, new ConfigDescription(
            "Hits on armour and the ground, explosions, fires, destroyed engines, blown-off turrets, falling trees, " +
            "APS and smoke grenade bursts.", new AcceptableValueRange<float>(0f, 2f)));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output (loaded sounds, played events).");

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");
        
        

        Library.StartLoading();
        AddComponent<Controller>();
        Loaded = true;
        Log.LogInfo($"Sound Overhaul {Version} loaded");
    }

    internal static string Root
    {
        get
        {
            var custom = SoundFolder?.Value;
            if (!string.IsNullOrWhiteSpace(custom) && Directory.Exists(custom)) return custom;
            return Path.Combine(ModFolder(PluginDir, "SoundOverhaul"), "Sound");
        }
    }
}
