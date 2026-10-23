using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace CombatEffects;




[BepInPlugin(Guid, "Combat Effects", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.combateffects";
    public const string Version = "0.3.8";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<bool> Enabled, MuzzleFlashes, Impacts, Fires, Exhaust, TrackDust, Environment;
    internal static ConfigEntry<float> LightBrightness, HeatHaze, Fog, CullDistance;
    internal static ConfigEntry<int> MaxEffects;
    internal static ConfigEntry<bool> DebugLogging;


    
    
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
            Log.LogInfo($"Combat Effects {Version} is turned off in the Mod menu");
            return;
        }

        Enabled = Config.Bind("General", "Use these effects", true,
            "On: muzzle flashes, hits, explosions, fires, smoke, exhaust and track dust come from the effect library. " +
            "Off: the game's own effects (works at once, no restart).");
        MuzzleFlashes = Config.Bind("Effects", "Muzzle flashes", true, "Muzzle flashes, gun smoke and bore evacuator smoke. Off = the game's own.");
        Impacts = Config.Bind("Effects", "Hits and explosions", true, "Shell, bullet and missile hits on armour and ground, ricochets, explosions. Off = the game's own.");
        Fires = Config.Bind("Effects", "Fires and smoke", true, "Burning vehicles, ammunition cook-off flames, vehicle explosions and wreck smoke. Off = the game's own.");
        Exhaust = Config.Bind("Effects", "Engine exhaust", true, "Engine exhaust smoke. Off = the game's own.");
        TrackDust = Config.Bind("Effects", "Track dust", true, "Dust thrown up by tracks. Off = the game's own.");
        Environment = Config.Bind("Effects", "Mines and trees", true, "Landmine explosions and dust of falling trees.");
        LightBrightness = Config.Bind("Look", "Light brightness", 1f, new ConfigDescription(
            "Brightness of the light that flashes and fires cast on their surroundings (0 = no light).", new AcceptableValueRange<float>(0f, 5f)));
        HeatHaze = Config.Bind("Look", "Heat haze", 1f, new ConfigDescription(
            "Strength of the air shimmer over fires and muzzle blasts (0 = none).", new AcceptableValueRange<float>(0f, 3f)));
        Fog = Config.Bind("Look", "Fog on effects", 1f, new ConfigDescription(
            "How much the game's fog and haze cover distant effects (1 = like the rest of the scene).", new AcceptableValueRange<float>(0f, 1f)));
        CullDistance = Config.Bind("Performance", "Effect distance (m)", 2500f, new ConfigDescription(
            "Effects farther from the camera than this are not shown (zooming in extends it).", new AcceptableValueRange<float>(200f, 6000f)));
        MaxEffects = Config.Bind("Performance", "Max effects at once", 160, new ConfigDescription(
            "Upper limit of effects playing at the same time; the oldest stop first.", new AcceptableValueRange<int>(20, 600)));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output (which effect was picked for each event).");

        Enabled.SettingChanged += (_, _) => Effects.OnSettingsChanged();
        foreach (var e in new[] { MuzzleFlashes, Impacts, Fires, Exhaust, TrackDust, Environment })
            e.SettingChanged += (_, _) => Effects.OnSettingsChanged();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Controller>();
        Log.LogInfo($"Combat Effects {Version} loaded");
    }

    
    internal static bool On(ConfigEntry<bool> category) => Enabled.Value && category.Value && Library.Ready && !EditorWatch.Active;
}
