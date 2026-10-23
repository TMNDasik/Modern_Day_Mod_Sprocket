using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ShowroomMenu;





[BepInPlugin(Guid, "Showroom Menu", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.showroommenu";
    public const string Version = "1.0.9";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<string> Subtitle, VehicleName;
    internal static ConfigEntry<bool> ShowVehicle, LightFromCamera, TemporalAA, DebugLogging, WatchLighting;
    internal static ConfigEntry<float> TurntableSpeed, ViewAngle, CameraTilt, CameraDistance;


    
    
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
            Log.LogInfo($"Showroom Menu {Version} is turned off in the Mod menu");
            return;
        }

        Subtitle = Config.Bind("Menu", "Subtitle", "MODERN DAY MOD", "Text under the SPROCKET title. Empty = no subtitle.");
        ShowVehicle = Config.Bind("Showroom", "Show vehicle", true, "Show a vehicle on the turntable.");
        VehicleName = Config.Bind("Showroom", "Vehicle", "",
            "Name of the blueprint to show (current faction). Empty = the most recently saved vehicle.");
        TurntableSpeed = Config.Bind("Showroom", "Turntable speed (deg/s)", 3f, Range(
            "How fast the turntable rotates. 0 = it stands still.", 0f, 30f));
        ViewAngle = Config.Bind("Showroom", "View angle (deg)", 35f, Range(
            "From which side the camera looks at the vehicle: 0 = the front, 35 = front-left, 90 = the left side, -35 = front-right.", -180f, 180f));
        CameraTilt = Config.Bind("Showroom", "Camera tilt (deg)", 15f, Range(
            "How steeply the camera looks down at the turntable.", 0f, 40f));
        CameraDistance = Config.Bind("Showroom", "Camera distance (%)", 100f, Range(
            "100 = the camera fits the turntable and the vehicle into the left part of the screen; less = closer, more = further away.", 40f, 250f));
        LightFromCamera = Config.Bind("Showroom", "Light from the camera side", true,
            "Turn the showroom lights around so the vehicle is lit from the front. Off = the lighting of the vehicle designer.");
        TemporalAA = Config.Bind("Showroom", "Temporal anti-aliasing", true,
            "Use TAA in the main menu: it removes the flicker of thin parts (slat armour, rails, grilles) and their shadows on the turning vehicle. Off = the anti-aliasing from the game settings.");
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output.");
        WatchLighting = Config.Bind("Debug", "Lighting watch", true,
            "Log every change of lights, post-processing volumes and the camera in the main menu (to find the cause of light flicker). F8 puts a mark into the log.");

        ShowVehicle.SettingChanged += (_, _) => Director.RequestVehicleRebuild();
        VehicleName.SettingChanged += (_, _) => Director.RequestVehicleRebuild();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Runner>();
        Log.LogInfo($"Showroom Menu {Version} loaded");
    }

    private static ConfigDescription Range(string description, float min, float max) =>
        new(description, new AcceptableValueRange<float>(min, max));

    internal static void Debug(string message)
    {
        if (DebugLogging != null && DebugLogging.Value) Log.LogInfo(message);
    }
}
