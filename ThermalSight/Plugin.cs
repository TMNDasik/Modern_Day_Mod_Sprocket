using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace ThermalSight;

[BepInPlugin(Guid, "Thermal Sight", Version)]
public class Plugin : BasePlugin
{
    
    
    public const string Guid = "sprocket.thermalsight.modernday";
    internal const string OldGuid = "sprocket.thermalsight";
    public const string Version = "0.19.7";

    internal static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<string> ToggleKeyConfig;
    internal static ConfigEntry<float> HeatOverlayScale;
    internal static ConfigEntry<float> BaseFieldOfView;
    internal static ConfigEntry<float> Visibility;
    internal static ConfigEntry<bool> SmoothZoom;
    internal static ConfigEntry<string> NightVisionKeyConfig;
    internal static ConfigEntry<bool> OpticsDamage;
    internal static Key NightVisionKey = Key.M;
    internal static ConfigEntry<float> HeatDecodeGain;
    internal static ConfigEntry<float> ResolutionScale;
    internal static ConfigEntry<int> CanvasSortingOrder;
    internal static ConfigEntry<bool> ShowReticle;
    internal static ConfigEntry<bool> HudZoomText, HudStatusLine;
    internal static ConfigEntry<bool> RealisticFrameRate;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<bool> DebugSnapshots;
    internal static ConfigEntry<float> DebugAutoView;

    internal static ConfigEntry<string> RangefinderKeyConfig;
    internal static ConfigEntry<float> RangefinderMax;
    internal static ConfigEntry<bool> RangefinderSetsConvergence;
    internal static Key RangefinderKey = Key.None;
    internal static int RangefinderMouse = 3;    

    internal static Key ToggleKey = Key.N;
    internal static string ToggleKeyName => ToggleKey.ToString();


    
    
    internal static string ModFolder(string root, string name)
    {
        var md = Path.Combine(root, "MD" + name);
        var old = Path.Combine(root, name);
        return Directory.Exists(md) || !Directory.Exists(old) ? md : old;
    }
    public override void Load()
    {
        Log = base.Log;
        PluginDir = ModFolder(Paths.PluginPath, "ThermalSight");
        if (ModSwitch.IsDisabled(Guid) || ModSwitch.IsDisabled(OldGuid))
        {
            Log.LogInfo($"Thermal Sight {Version} is turned off in the Mod menu");
            return;
        }
        MigrateConfig();

        ToggleKeyConfig = Config.Bind("Controls", "Toggle key", "N", "Key that switches the camera to the thermal sight and back (Unity Input System key name).");
        HeatOverlayScale = Config.Bind("Tuning", "Vehicle temperature scale", 1f, "How warm vehicles appear in the thermal channel (F6/F7 in debug mode).");
        BaseFieldOfView = Config.Bind("Zoom", "Field of view at x1", 40f, "Vertical field of view (degrees) of the thermal sight at x1 magnification.");
        Visibility = Config.Bind("Tuning", "Atmospheric visibility (m)", 6000f, "Distance over which thermal contrast fades into the atmosphere.");
        SmoothZoom = Config.Bind("Zoom", "Smooth zoom", false,
            "Off: the mouse wheel switches the magnification of every sight in whole steps (x1, x2, x3 ...). " +
            "On: fine steps of 0.1 (x1.0, x1.1, x1.2 ...). Applies to the gunner sights, the thermal sight and the night vision sight.");
        NightVisionKeyConfig = Config.Bind("Controls", "Night vision key", "M",
            "Key that switches the camera to the night vision sight and back (Unity Input System key name).");
        OpticsDamage = Config.Bind("Damage", "Optics damage", true,
            "Hits and explosions near a sight head crack its glass, spall from a penetration near the eyepiece breaks it from inside. " +
            "A thermal sight has a head (cracked window, dead sensor lines, freezes, NO SIGNAL) and a display (broken screen, new " +
            "pattern every hit). Damage is kept for every vehicle in the battle, so it is there when you switch into a damaged vehicle.");
        HeatDecodeGain = Config.Bind("Tuning", "Vehicle temperature gain", 1f, "Multiplier for the decoded vehicle temperature.");
        ResolutionScale = Config.Bind("Tuning", "Sensor resolution scale", 1f, "Multiplier for the internal sensor resolution of every generation.");
        ShowReticle = Config.Bind("Display", "Own reticle", false, "Draw a simple reticle of the mod (the game's own reticle with range is shown anyway).");
        
        HudZoomText = Config.Bind("HUD elements", "Zoom and mode text", true,
            "The magnification / generation text in the corner of the thermal and night vision sight.");
        HudStatusLine = Config.Bind("HUD elements", "Sight status line", true,
            "The status line at the bottom of the thermal sight frame: ready, ammo type, laser range, mode.");
        RealisticFrameRate = Config.Bind("Display", "Realistic frame rate", false,
            "The thermal image refreshes at the device's own rate (25-50 Hz depending on the generation), like a real sight. " +
            "Off = the image refreshes every game frame (smooth).");
        CanvasSortingOrder = Config.Bind("Tuning", "Canvas sorting order", -100, "Sorting order of the thermal image overlay (game HUD should stay above it).");
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output and debug keys (F10 snapshot, F6/F7 heat overlay scale).");
        DebugAutoView = Config.Bind("Debug", "Auto view seconds", 0f, "Debug: switch the thermal view on for the main camera N seconds after start (0 = off).");
        DebugSnapshots = Config.Bind("Debug", "Auto snapshots", false, "Save a snapshot of the thermal image a few seconds after it is switched on.");

        
        RangefinderKeyConfig = Config.Bind("Controls", "Laser rangefinder button", "MiddleMouse",
            "Measures the range to the target under the reticle while in the sight: MiddleMouse (pressing the mouse wheel), " +
            "Mouse4 / Mouse5 (side buttons) or a keyboard key name (Unity Input System, e.g. E).");
        RangefinderMax = Config.Bind("Rangefinder", "Max range (m)", 7500f, "Beyond this there is no laser return (0000).");
        RangefinderSetsConvergence = Config.Bind("Rangefinder", "Enter range into the sight", true,
            "The measured range sets the gun convergence of the sight (like a fire control computer).");
        ParseKeys();
        
        RangefinderKeyConfig.SettingChanged += (_, _) => ParseKeys();
        ToggleKeyConfig.SettingChanged += (_, _) => ParseKeys();
        NightVisionKeyConfig.SettingChanged += (_, _) => ParseKeys();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }

        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<ThermalController>();
        Log.LogInfo($"Thermal Sight {Version} loaded, toggle key {ToggleKeyName}");
    }

    
    
    private void MigrateConfig()
    {
        try
        {
            var path = Config.ConfigFilePath;
            var old = Path.Combine(Paths.ConfigPath, OldGuid + ".cfg");
            if (File.Exists(path) || !File.Exists(old)) return;
            var text = File.ReadAllText(old);
            if (!text.Contains("Field of view at x1") || !text.Contains("Laser rangefinder key")) return;
            File.Copy(old, path);
            Config.Reload();
            Log.LogInfo($"Thermal Sight settings taken over from {OldGuid}.cfg");
        }
        catch (Exception e) { Log.LogWarning("Thermal Sight settings not taken over: " + e.Message); }
    }

    
    internal static bool RangefinderPressed()
    {
        try
        {
            if (RangefinderMouse != 0)
            {
                var m = Mouse.current;
                if (m == null) return false;
                return RangefinderMouse switch
                {
                    4 => m.backButton.wasPressedThisFrame,
                    5 => m.forwardButton.wasPressedThisFrame,
                    _ => m.middleButton.wasPressedThisFrame,
                };
            }
            var kb = Keyboard.current;
            return kb != null && RangefinderKey != Key.None && kb[RangefinderKey].wasPressedThisFrame;
        }
        catch { return false; }
    }

    
    internal static string RangefinderButtonName => RangefinderMouse switch
    {
        3 => "middle mouse button",
        4 => "mouse button 4",
        5 => "mouse button 5",
        _ => RangefinderKey.ToString(),
    };

    private static void ParseKeys()
    {
        var button = (RangefinderKeyConfig.Value ?? "").Trim().Replace(" ", "").ToLowerInvariant();
        RangefinderKey = Key.None;
        RangefinderMouse = button switch
        {
            "middlemouse" or "mousemiddle" or "middle" or "mmb" or "mouse3" or "wheel" => 3,
            "mouse4" or "backmouse" => 4,
            "mouse5" or "forwardmouse" => 5,
            _ => 0,
        };
        if (RangefinderMouse == 0)
        {
            if (Enum.TryParse<Key>(RangefinderKeyConfig.Value, true, out var rkey) && rkey != Key.None) RangefinderKey = rkey;
            else
            {
                RangefinderMouse = 3;
                Log.LogWarning($"Unknown rangefinder button '{RangefinderKeyConfig.Value}', using the middle mouse button");
            }
        }

        if (Enum.TryParse<Key>(ToggleKeyConfig.Value, true, out var key)) ToggleKey = key;
        else
        {
            ToggleKey = Key.N;
            Log.LogWarning($"Unknown toggle key '{ToggleKeyConfig.Value}', using N");
        }

        if (Enum.TryParse<Key>(NightVisionKeyConfig.Value, true, out var nkey)) NightVisionKey = nkey;
        else
        {
            NightVisionKey = Key.M;
            Log.LogWarning($"Unknown night vision key '{NightVisionKeyConfig.Value}', using M");
        }
    }
}
