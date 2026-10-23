using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace GunStabilizer;

[BepInPlugin(Guid, "Gun Stabilizer", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.gunstabilizer";
    public const string Version = "1.11.3";

    internal new static ManualLogSource Log;

    internal static ConfigEntry<string> ToggleKeyConfig;
    internal static ConfigEntry<float> GunnerReaction;
    internal static ConfigEntry<float> GyroDrift;
    internal static ConfigEntry<float> SightSlip;
    internal static ConfigEntry<float> SightField, SightLeadMax, SightLeadMin;
    internal static ConfigEntry<float> SightShake;
    internal static ConfigEntry<bool> HoldSight;
    internal static ConfigEntry<bool> AffectAI;
    internal static ConfigEntry<bool> Boresight;
    internal static ConfigEntry<bool> ShowIndicator;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<string> FcsKeyConfig;
    internal static ConfigEntry<bool> FcsAutoAtStart, ShowFcs;
    internal static ConfigEntry<string> FcsShowCorrection;
    internal static ConfigEntry<bool> FcsRequirePart, FcsAffectAI, ShowLeadMark;

    internal static Key ToggleKey = Key.B;
    internal static Key FcsKey = Key.Y;
    internal static string ToggleKeyName => ToggleKey.ToString();

    public override void Load()
    {
        Log = base.Log;
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Gun Stabilizer {Version} is turned off in the Mod menu");
            return;
        }

        ToggleKeyConfig = Config.Bind("Controls", "Stabilizer toggle key", "B",
            "Key that switches the stabilizer of your vehicle on and off in battle (Unity Input System key name).");
        
        if (string.Equals(ToggleKeyConfig.Value, "G", StringComparison.OrdinalIgnoreCase)) ToggleKeyConfig.Value = "B";
        GyroDrift = Config.Bind("Stabilizer", "Gyro drift", 0f, new ConfigDescription(
            "Slow wander of the stabilized line of sight that the gunner has to correct. 0 = off (drift balanced out by the crew), " +
            "1 = realistic uncompensated drift of an old stabilizer (0.1-0.25 mrad/s). Very noticeable at high zoom.",
            new AcceptableValueRange<float>(0f, 2f)));
        SightSlip = Config.Bind("Stabilizer", "Sight slip threshold (deg)", 2f, new ConfigDescription(
            "Only used when 'Sight lead' is 0: how far the hull may push the gun away from the line of sight (fast neutral turn) " +
            "before the line of sight is carried along with the hull. With 'Sight lead' above 0 that limit is used instead. 0 = off.",
            new AcceptableValueRange<float>(0f, 10f)));
        SightField = Config.Bind("Stabilizer", "Sight lead (fraction of view)", 0.2f, new ConfigDescription(
            "How far the line of sight may run ahead of the gun, as a fraction of the sight's field of view: the sight is " +
            "carried by the gun, so you can't look around freely or into the turret. Near the limit the mouse gets heavier " +
            "and the gun has to catch up. 0 = unlimited.", new AcceptableValueRange<float>(0f, 1f)));
        SightLeadMax = Config.Bind("Stabilizer", "Sight lead max (deg)", 6f, new ConfigDescription(
            "Upper limit of the lead without zoom.", new AcceptableValueRange<float>(0.5f, 25f)));
        SightLeadMin = Config.Bind("Stabilizer", "Sight lead min (deg)", 2f, new ConfigDescription(
            "Lower limit of the lead at high zoom (thermal sight x20-x50). Must stay above the normal lag of the gun behind the sight " +
            "while driving, otherwise the lag drags the line of sight along and the picture creeps sideways.",
            new AcceptableValueRange<float>(0.3f, 10f)));
        GunnerReaction = Config.Bind("Stabilizer", "Gunner reaction time (s)", 0.5f, new ConfigDescription(
            "How quickly the crew gunner corrects the aim of an unstabilized gun (third-person view and AI vehicles). In the gunner's sight you correct it yourself.",
            new AcceptableValueRange<float>(0.05f, 3f)));
        SightShake = Config.Bind("Sight", "Sight shake when firing", 0f, new ConfigDescription(
            "How much the gunner's sight kicks when your own gun fires: 1 = like the game (with an autocannon the picture never " +
            "stops shaking), 0 = steady. The third-person view and shaking from hits stay as in the game.",
            new AcceptableValueRange<float>(0f, 1f)));
        HoldSight = Config.Bind("Sight", "Hold the line of sight", true,
            "The gunner's sight stays exactly where you aimed it: only your mouse (and the hull, for an unstabilized gun) moves it. " +
            "Anything else that tries to swing the picture (for example after a shot) is undone and noted in the log.");
        AffectAI = Config.Bind("Stabilizer", "Affect AI vehicles", true,
            "AI vehicles without a stabilizer also get guns that move with the hull.");
        Boresight = Config.Bind("Sight", "Boresight weapons", true,
            "Weapons mounted beside the sight (coaxial autocannon, machine gun) converge with the line of sight at the sight's " +
            "range, like boresighted weapons on a real tank: the sight's reticle and the rounds meet the aim circle for every fire " +
            "group. Off = the game's behaviour (such weapons fire parallel to the sight line, off by their mounting offset).");
        ShowIndicator = Config.Bind("Display", "Show indicator", true,
            "Show the stabilizer status (on, off, faults) on screen when your vehicle has a stabilizer.");
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output.");
        FcsKeyConfig = Config.Bind("Fire control", "Mode key", "Y",
            "Key that switches the fire control between normal mode (the laser rangefinder only measures the range) and FCS mode " +
            "(the measured range and the loaded ammunition set the gun's elevation automatically).");
        FcsAutoAtStart = Config.Bind("Fire control", "FCS mode at start", false,
            "Each vehicle starts in FCS mode (automatic ballistic correction) instead of the normal mode.");
        ShowFcs = Config.Bind("Display", "Show fire control", true,
            "Show the fire control line (range and IDLE / RANGING / READY / NO RANGE) under the reticle while in the sight.");
        FcsShowCorrection = Config.Bind("Fire control", "Show correction", "Mils", new ConfigDescription(
            "Show the calculated gun elevation correction in the fire control line (for checking): Mils (6400 per circle), Degrees or Off.",
            new AcceptableValueList<string>("Mils", "Degrees", "Off")));
        FcsRequirePart = Config.Bind("Fire control", "Require computer part", false,
            "On: only vehicles with a Fire Control Computer part (Modern Day → Weapon) have fire control and a laser rangefinder. " +
            "Off: vehicles without the part keep the basic fire control (range correction only, the laser as set in Thermal Sight).");
        FcsAffectAI = Config.Bind("Fire control", "AI uses the computer", true,
            "AI vehicles with a Fire Control Computer part estimate range and target speed far better (smaller aiming errors).");
        ShowLeadMark = Config.Bind("Display", "Show lead mark", true,
            "In FCS mode with lead, a small diamond in the sight shows where the target will be when the shell arrives.");

        ParseKeys();
        ToggleKeyConfig.SettingChanged += (_, _) => ParseKeys(); 
        FcsKeyConfig.SettingChanged += (_, _) => ParseKeys();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<StabController>();
        Log.LogInfo($"Gun Stabilizer {Version} loaded, toggle key {ToggleKeyName}, fire control mode key {FcsKey}");
    }

    private static void ParseKeys()
    {
        if (Enum.TryParse<Key>(ToggleKeyConfig.Value, true, out var key)) ToggleKey = key;
        else
        {
            ToggleKey = Key.B;
            Log.LogWarning($"Unknown toggle key '{ToggleKeyConfig.Value}', using B");
        }
        if (Enum.TryParse<Key>(FcsKeyConfig.Value, true, out var fcs)) FcsKey = fcs;
        else
        {
            FcsKey = Key.Y;
            Log.LogWarning($"Unknown fire control key '{FcsKeyConfig.Value}', using Y");
        }
    }
}
