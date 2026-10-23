using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ActiveProtection;












[BepInPlugin(Guid, "Active Protection", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.activeprotection";
    public const string Version = "1.9.8";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<float> ChanceAtgm, ChanceHe, ChanceAp, ChanceApfsds;
    internal static ConfigEntry<float> KineticPenetrationLeft, KineticDeflection;
    internal static ConfigEntry<int> MinThreatCaliber;
    internal static ConfigEntry<bool> ModulesDestructible;
    internal static ConfigEntry<float> SoundVolume;
    internal static ConfigEntry<bool> ShowAllZones;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<string> SmokeKeyConfig;
    internal static ConfigEntry<bool> GunnersLase, ShowWarnings;
    internal static ConfigEntry<bool> HudWarningText, HudWarningArrow, HudMessages;
    internal static ConfigEntry<float> LaseTime;
    internal static UnityEngine.InputSystem.Key SmokeKey = UnityEngine.InputSystem.Key.G;

    public override void Load()
    {
        Log = base.Log;
        PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Active Protection {Version} is turned off in the Mod menu");
            return;
        }

        ChanceAtgm = Config.Bind("Interception", "Hit chance per charge: ATGM", 0.9f, Range(
            "Chance that one protective charge destroys a guided missile.", 0f, 1f));
        ChanceHe = Config.Bind("Interception", "Hit chance per charge: HE shells", 0.85f, Range(
            "Chance that one protective charge destroys (detonates early) a high-explosive shell.", 0f, 1f));
        ChanceAp = Config.Bind("Interception", "Hit chance per charge: AP shells", 0.6f, Range(
            "Chance that one protective charge hits a full-calibre armour-piercing shell (it is deflected and weakened, not destroyed).", 0f, 1f));
        ChanceApfsds = Config.Bind("Interception", "Hit chance per charge: APFSDS", 0.5f, Range(
            "Chance that one protective charge hits a long-rod dart (it is deflected and weakened, not destroyed).", 0f, 1f));
        KineticPenetrationLeft = Config.Bind("Interception", "Kinetic rounds: penetration left after a hit", 0.45f, Range(
            "An intercepted AP shell or APFSDS dart keeps flying, but yawed and slowed: this share of its penetration is left (0.45 = 55% less).", 0.05f, 1f));
        KineticDeflection = Config.Bind("Interception", "Kinetic rounds: deflection (degrees)", 4f, Range(
            "How far an intercepted kinetic round is knocked off its course (random up to this angle).", 0f, 20f));
        MinThreatCaliber = Config.Bind("Interception", "Smallest threat (mm)", 20, new ConfigDescription(
            "Projectiles smaller than this (machine-gun bullets, fragments) are never treated as threats.",
            new AcceptableValueRange<int>(5, 200)));
        ModulesDestructible = Config.Bind("Interception", "Modules can be destroyed", true,
            "A detector or launcher hit by a shell, a bullet or a fragment stops working until the end of the battle.");
        SoundVolume = Config.Bind("Sound", "APS shot volume", 1f, Range("0 = silent.", 0f, 2f));
        ShowAllZones = Config.Bind("Designer", "Show all APS sectors and links", false,
            "Shows the detection and interception zones of every APS module and all their links, not only of the selected module.");
        SmokeKeyConfig = Config.Bind("EO countermeasures", "Aerosol screen key", "G",
            "Fires the aerosol grenade launchers that cover the direction you are looking (Unity Input System key name).");
        
        if (string.Equals(SmokeKeyConfig.Value, "X", StringComparison.OrdinalIgnoreCase)) SmokeKeyConfig.Value = "G";
        ShowWarnings = Config.Bind("EO countermeasures", "Show warnings on screen", true,
            "Laser / missile warnings of your vehicle's EO sensors: text with the threat direction and an arrow around the screen centre, plus a tone.");
        GunnersLase = Config.Bind("EO countermeasures", "Enemy gunners lase their targets", true,
            "A gunner who keeps his gun laid on a vehicle is measuring the range with a laser (modern fire control does this before " +
            "firing): EO sensors of that vehicle detect it. Off = only the thermal sight's laser rangefinder (middle mouse button) is detected.");
        
        HudWarningText = Config.Bind("HUD elements", "Warning text", true, "The threat line at the top of the screen (type, clock direction, distance).");
        HudWarningArrow = Config.Bind("HUD elements", "Warning arrow", true, "The red arrow around the screen centre pointing at the threat.");
        HudMessages = Config.Bind("HUD elements", "Aerosol screen message", true, "The short AEROSOL SCREEN message when the smoke grenades fire.");
        LaseTime = Config.Bind("EO countermeasures", "Aiming time before lasing (s)", 1f, Range(
            "How long a gun has to stay laid on a vehicle before it counts as lasing it.", 0.2f, 5f));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output (detections, assignments, shots).");
        ParseKeys();
        SmokeKeyConfig.SettingChanged += (_, _) => ParseKeys();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Controller>();
        Log.LogInfo($"Active Protection {Version} loaded");
    }

    private static ConfigDescription Range(string description, float min, float max) =>
        new(description, new AcceptableValueRange<float>(min, max));

    private static void ParseKeys()
    {
        if (Enum.TryParse<UnityEngine.InputSystem.Key>(SmokeKeyConfig.Value, true, out var key)) SmokeKey = key;
        else
        {
            SmokeKey = UnityEngine.InputSystem.Key.G;
            Log.LogWarning($"Unknown aerosol screen key '{SmokeKeyConfig.Value}', using G");
        }
    }
}
