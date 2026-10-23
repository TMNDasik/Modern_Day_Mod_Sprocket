using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ModernDay;






[BepInPlugin(Guid, "Modern Day", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.modernday";
    public const string Version = "1.8.0";

    internal new static ManualLogSource Log;

    public override void Load()
    {
        Log = base.Log;
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Modern Day {Version} is turned off in the Mod menu — mod parts stay in Firepower → Cannons");
            return;
        }
        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");
        AddComponent<Ticker>();
        Api.Active = true;
        Log.LogInfo($"Modern Day {Version} loaded");
    }
}


public static class Api
{
    public const string TagEo = "modernEo";
    public const string TagAps = "modernAps";
    public const string TagThermal = "modernThermal";
    public const string TagOther = "modernOther";
    public const string TagGuns = "modernGuns";
    public const string TagEra = "modernEra";

    
    public const string TagActive = "modernActive";
    public const string TagPassive = "modernPassive";
    public const string TagWeapon = "modernWeapon";

    
    
    public const string TagWheels = "modWheel";

    public static bool Active { get; internal set; }

    
    
    public static bool PartsVisible => Visibility.EffectiveShown;

    
    
    
    public static bool HiddenInBattle(IntPtr vehicleObject) => HideInBattle.IsHiddenNow(vehicleObject);

    public static bool Hidden(IntPtr vehicleObject) => HideInBattle.IsHiddenNow(vehicleObject);

    internal static readonly string[] AllTags = { TagEo, TagAps, TagEra, TagThermal, TagGuns, TagOther };
}
