using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace Powertrain;










[BepInPlugin(Guid, "Powertrain", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.powertrain";
    public const string Version = "1.7.10";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<float> SpoolUpTime, SpoolDownTime, FuelMultiplier;
    internal static ConfigEntry<float> Volume, FullPowerPitch;
    internal static ConfigEntry<bool> GearboxEnabled, ShowGearPanel, RemapCommandKey;
    internal static ConfigEntry<bool> GearKeyLabel, GearModeLabel, GearStatus, GearStrip;
    internal static ConfigEntry<string> ShiftUpKeyConfig, ShiftDownKeyConfig;
    internal static ConfigEntry<float> CreepThrottle, MouseShiftStep, ReverseLockSpeed;
    internal static ConfigEntry<int> VisibleGears;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<string> TdaKeyConfig;
    internal static ConfigEntry<float> TdaTime, TdaRecharge, TdaDensity, TdaLinger;
    internal static ConfigEntry<string> FssKeyConfig;
    internal static ConfigEntry<float> FssDischargeTime, FssAutoDelay, FssCrewDelay, FssVolume;
    internal static Key ShiftUpKey = Key.E, ShiftDownKey = Key.Q, TdaKey = Key.H, FssKey = Key.Digit6;

    public override void Load()
    {
        Log = base.Log;
        PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Powertrain {Version} is turned off in the Mod menu");
            return;
        }

        SpoolUpTime = Config.Bind("Gas turbine", "Spool-up time (s)", 2.5f, Range(
            "Time for the gas generator to go from idle to full power after the throttle is opened (turbine lag).", 0.3f, 8f));
        SpoolDownTime = Config.Bind("Gas turbine", "Spool-down time (s)", 1.5f, Range(
            "Time for the gas generator to fall from full power back to idle.", 0.3f, 6f));
        FuelMultiplier = Config.Bind("Gas turbine", "Fuel use multiplier", 1f, Range(
            "1 = a turbine burns about 1.4 times the fuel of a diesel of the same power at full load, and a lot at idle.", 0f, 3f));
        Volume = Config.Bind("Sound", "Turbine volume", 1f, Range("0 = silent.", 0f, 2f));
        FullPowerPitch = Config.Bind("Sound", "Pitch at full power", 1.55f, Range(
            "Pitch of the turbine whine at full power relative to idle (the pitch and loudness follow the turbine speed).", 1f, 2.5f));

        GearboxEnabled = Config.Bind("Gearbox", "Player selects the gear", true,
            "Off = the game's own automatic gearbox (W forward, S back) for every vehicle.");
        ShiftUpKeyConfig = Config.Bind("Gearbox", "Shift up key", "E", "Shift towards forward / a higher gear (Unity Input System key name).");
        ShiftDownKeyConfig = Config.Bind("Gearbox", "Shift down key", "Q", "Shift towards reverse / a lower gear.");
        RemapCommandKey = Config.Bind("Gearbox", "Move Command vehicle from Q to K", true,
            "The game uses Q to command other vehicles; it is moved to K so Q can shift down (G and H are smoke grenades and engine smoke). " +
            "Turning it off takes effect after restarting the game.");
        MouseShiftStep = Config.Bind("Gearbox", "Mouse shift distance", 45f, Range(
            "Hold Left Alt and move the mouse up/down this far (pixels) for one gear.", 10f, 200f));
        CreepThrottle = Config.Bind("Gearbox", "Creep throttle", 0.06f, Range(
            "With a gear engaged and no throttle the vehicle creeps forward (or back in reverse) a little. 0 = no creep.", 0f, 0.3f));
        ReverseLockSpeed = Config.Bind("Gearbox", "Direction change lock speed (km/h)", 2f, Range(
            "Above this speed you cannot shift into the opposite direction (D <-> R, forward gears <-> R).", 0f, 20f));
        ShowGearPanel = Config.Bind("Display", "Show gear panel", true, "The gear panel in the bottom right corner.");
        VisibleGears = Config.Bind("Display", "Gear panel size (gears)", 6, new ConfigDescription(
            "How many gear positions the panel shows at once; with more gears the column scrolls to follow the selected one.",
            new AcceptableValueRange<int>(3, 12)));
        
        GearKeyLabel = Config.Bind("HUD elements", "Gear panel key label", true, "Gear panel: the Q / E label above it.");
        GearModeLabel = Config.Bind("HUD elements", "Gear panel mode label", true, "Gear panel: AUTO / MANUAL in its top corner.");
        GearStatus = Config.Bind("HUD elements", "Gear panel status text", true, "Gear panel: the gear text at the bottom (D, 1, N, NO DRIVER).");
        GearStrip = Config.Bind("HUD elements", "Gear panel state strip", true,
            "Gear panel: the coloured strip at the bottom (green engaged, amber engaging, grey neutral, red no driver).");
        TdaKeyConfig = Config.Bind("Engine smoke (TDA)", "Key", "H",
            "Switches the engine smoke on and off (vehicles whose engine has \"TDA smoke\" ticked in the designer).");
        TdaTime = Config.Bind("Engine smoke (TDA)", "Smoke time (s)", 60f, Range(
            "Total smoking time: while the smoke is on it counts down second by second; at zero the smoke stops.", 10f, 300f));
        TdaRecharge = Config.Bind("Engine smoke (TDA)", "Recharge (s per s)", 1f, Range(
            "While the smoke is off the time comes back at this rate (1 = one second per second, 0 = never).", 0f, 5f));
        TdaDensity = Config.Bind("Engine smoke (TDA)", "Smoke density", 1f, Range("Amount of visible smoke (1 = normal).", 0.2f, 3f));
        TdaLinger = Config.Bind("Engine smoke (TDA)", "Smoke lingers (s)", 45f, Range(
            "How long the laid smoke keeps hiding the vehicle from the AI before it thins out.", 10f, 180f));
        FssKeyConfig = Config.Bind("Fire suppression", "Key", "Digit6",
            "Fires the manual fire suppression system when your vehicle burns (engines with \"Extinguisher: Manual\" in the designer; Unity Input System key name).");
        FssDischargeTime = Config.Bind("Fire suppression", "Discharge time (s)", 1.5f, Range(
            "How long one extinguisher bottle takes to put the fire out (the flames die down meanwhile).", 0.3f, 5f));
        FssAutoDelay = Config.Bind("Fire suppression", "Automatic reaction (s)", 1f, Range(
            "Time from the start of a fire until the automatic system goes off (fire sensors).", 0f, 5f));
        FssCrewDelay = Config.Bind("Fire suppression", "Crew reaction, manual system (s)", 3f, Range(
            "Vehicles you are not driving (AI, your other vehicles) fire their manual system themselves after this time.", 0.5f, 15f));
        FssVolume = Config.Bind("Fire suppression", "Sound volume", 1f, Range("Discharge sound of the extinguisher bottle. 0 = silent.", 0f, 2f));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output.");

        
        ParseKeys();
        ShiftUpKeyConfig.SettingChanged += (_, _) => ParseKeys();
        ShiftDownKeyConfig.SettingChanged += (_, _) => ParseKeys();
        TdaKeyConfig.SettingChanged += (_, _) => ParseKeys();
        FssKeyConfig.SettingChanged += (_, _) => ParseKeys();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Controller>();
        Log.LogInfo($"Powertrain {Version} loaded");
    }

    private static ConfigDescription Range(string description, float min, float max) =>
        new(description, new AcceptableValueRange<float>(min, max));

    private static void ParseKeys()
    {
        ShiftUpKey = Enum.TryParse<Key>(ShiftUpKeyConfig.Value, true, out var up) ? up : Key.E;
        ShiftDownKey = Enum.TryParse<Key>(ShiftDownKeyConfig.Value, true, out var down) ? down : Key.Q;
        TdaKey = TdaKeyConfig != null && Enum.TryParse<Key>(TdaKeyConfig.Value, true, out var tda) ? tda : Key.H;
        FssKey = FssKeyConfig != null && Enum.TryParse<Key>(FssKeyConfig.Value, true, out var fss) ? fss : Key.Digit6;
    }
}
