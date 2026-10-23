using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace DriveTuning;




[BepInPlugin(Guid, "Drive Tuning", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.drivetuning";
    public const string Version = "1.0.2";

    internal new static ManualLogSource Log;
    internal static ConfigEntry<float> MaxSpeedLimit, MaxAccelLimit;
    internal static ConfigEntry<bool> DebugLogging;

    public override void Load()
    {
        Log = base.Log;
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Drive Tuning {Version} is turned off in the Mod menu");
            return;
        }
        MaxSpeedLimit = Config.Bind("Limits", "Slider max speed (deg/s)", 180f, new ConfigDescription(
            "Upper end of the speed sliders in the designer.", new AcceptableValueRange<float>(30f, 1000f)));
        MaxAccelLimit = Config.Bind("Limits", "Slider max acceleration (deg/s²)", 720f, new ConfigDescription(
            "Upper end of the acceleration sliders in the designer.", new AcceptableValueRange<float>(60f, 5000f)));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output.");

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");
        Log.LogInfo($"Drive Tuning {Version} loaded");
    }
}
