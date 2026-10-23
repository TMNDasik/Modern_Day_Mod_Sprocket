using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Sprocket.Gameplay.VehicleControl;
using Sprocket.Vehicles;
using UnityEngine.InputSystem;

namespace ModMenu;





[BepInPlugin(Guid, "Mod Menu", Version)]
[BepInDependency(ConfigManagerGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.modsettings";
    public const string Version = "2.1.4";
    internal const string ConfigManagerGuid = "com.bepis.bepinex.configurationmanager";

    internal new static ManualLogSource Log;
    internal static ConfigEntry<string> OpenKeyConfig;
    internal static Key OpenKey = Key.F11;

    
    internal static HashSet<string> StartupDisabled = new(StringComparer.OrdinalIgnoreCase);

    public override void Load()
    {
        Log = base.Log;
        StartupDisabled = DisabledList.Read();
        OpenKeyConfig = Config.Bind("General", "Open key", "F11",
            "Key that opens and closes the Mod menu (F1-F10 select weapon slots in the game).");
        ParseKey();
        OpenKeyConfig.SettingChanged += (_, _) => ParseKey();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        ConfigManagerOff.Apply(harmony);

        AddComponent<MenuWindow>();
        Log.LogInfo($"Mod Menu {Version} loaded, {OpenKeyConfig.Value} opens the menu" +
                    (StartupDisabled.Count > 0 ? $"; turned off: {string.Join(", ", StartupDisabled)}" : ""));
    }

    private static void ParseKey() =>
        OpenKey = Enum.TryParse<Key>(OpenKeyConfig.Value, true, out var key) && key != Key.None ? key : Key.F11;
}



internal static class DisabledList
{
    private static string FilePath => Path.Combine(Paths.ConfigPath, "sprocket.modmenu.disabled.txt");

    public static HashSet<string> Read()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(FilePath)) return set;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var guid = line.Trim();
                if (guid.Length > 0 && !guid.StartsWith("#")) set.Add(guid);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Reading the list of turned off mods failed: " + e.Message); }
        return set;
    }

    public static void Write(HashSet<string> set)
    {
        try
        {
            var lines = new List<string> { "# Mods turned off in the Mod menu (one plugin GUID per line). Takes effect when the game starts." };
            lines.AddRange(set);
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Saving the list of turned off mods failed: " + e.Message); }
    }
}


[HarmonyPatch(typeof(VehicleControlUI), nameof(VehicleControlUI.ControlUpdate))]
internal static class BattleCursor
{
    private static void Postfix(ref VehicleControlUIOptions __result)
    {
        if (MenuWindow.IsOpen)
            __result |= VehicleControlUIOptions.ShowCursor | VehicleControlUIOptions.LockAim | VehicleControlUIOptions.LockZoom;
    }
}



internal static class ConfigManagerOff
{
    public static void Apply(Harmony harmony)
    {
        try
        {
            Type cm = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "ConfigurationManager")
                {
                    cm = asm.GetType("ConfigurationManager.ConfigurationManager");
                    break;
                }
            if (cm == null) return;
            var skip = new HarmonyMethod(typeof(ConfigManagerOff), nameof(Skip));
            var onGui = AccessTools.Method(cm, "OnGUI");
            var show = AccessTools.PropertySetter(cm, "DisplayingWindow");
            if (onGui != null) harmony.Patch(onGui, prefix: skip);
            if (show != null) harmony.Patch(show, prefix: skip);
            Plugin.Log.LogInfo("ConfigurationManager window turned off: Sprocket has no IMGUI, the settings are in the Mod menu");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not turn off the ConfigurationManager window: " + e.Message);
        }
    }

    private static bool Skip() => false;
}
