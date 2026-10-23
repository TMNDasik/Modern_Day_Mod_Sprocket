using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace VehicleMaterials;





[BepInPlugin(Guid, "Vehicle Materials", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.vehiclematerials";
    public const string Version = "1.2.1";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<float> GlassOpacity;
    internal static ConfigEntry<bool> DebugLogging;

    public override void Load()
    {
        Log = base.Log;
        PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Vehicle Materials {Version} is turned off in the Mod menu");
            return;
        }

        GlassOpacity = Config.Bind("Materials", "Glass opacity", 0.12f, new ConfigDescription(
            "0 = perfectly clear glass, 1 = opaque. Reflections stay at full strength.", new AcceptableValueRange<float>(0f, 1f)));
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output (proxy meshes, materials).");

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Controller>();
        Log.LogInfo($"Vehicle Materials {Version} loaded");
    }
}
