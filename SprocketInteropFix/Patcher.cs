using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using HarmonyLib;

namespace SprocketInteropFix;





[PatcherPluginInfo("sprocket.il2cppinterop.fix", "Sprocket Il2CppInterop Fix", "1.0.0")]
public class Patcher : BasePatcher
{
    internal static ManualLogSource Logger;

    
    
    
    private const string Pattern =
        "48 89 5C 24 08 57 48 81 EC ?? ?? ?? ?? 48 8B 41 10 48 8B D9 48 8B FA 48 2B 98 ?? ?? ?? ?? 48 C1 FB 05";

    public override void Initialize()
    {
        Logger = Log;
        var target = AccessTools.Method(
            "Il2CppInterop.Runtime.Injection.Hooks.Class_GetFieldDefaultValue_Hook:FindTargetMethod");
        if (target == null)
        {
            Log.LogError("Class_GetFieldDefaultValue_Hook.FindTargetMethod not found, fix not applied");
            return;
        }

        new Harmony("sprocket.il2cppinterop.fix").Patch(target,
            postfix: new HarmonyMethod(typeof(Patcher), nameof(FindTargetMethodPostfix)));
        Log.LogInfo("Class::GetDefaultFieldValue patch installed");
    }

    private static void FindTargetMethodPostfix(ref IntPtr __result)
    {
        try
        {
            var found = FindInGameAssembly();
            if (found == IntPtr.Zero)
            {
                Logger.LogWarning($"Pattern not found, keeping Il2CppInterop address 0x{__result.ToInt64():X}");
                return;
            }

            Logger.LogInfo($"Class::GetDefaultFieldValue: 0x{__result.ToInt64():X} -> 0x{found.ToInt64():X}");
            __result = found;
        }
        catch (Exception e)
        {
            Logger.LogError($"Class::GetDefaultFieldValue lookup failed: {e}");
        }
    }

    
    private static IntPtr FindInGameAssembly()
    {
        ProcessModule module = null;
        foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            if (string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                module = m;
        if (module == null) return IntPtr.Zero;

        var data = File.ReadAllBytes(module.FileName);
        var hits = Scan(data, Pattern);
        if (hits.Count != 1)
        {
            Logger.LogWarning($"Pattern matched {hits.Count} times, expected 1");
            return IntPtr.Zero;
        }

        var rva = FileOffsetToRva(data, hits[0]);
        return rva < 0 ? IntPtr.Zero : module.BaseAddress + (int)rva;
    }

    private static List<int> Scan(byte[] data, string pattern)
    {
        var parts = pattern.Split(' ');
        var bytes = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            bytes[i] = parts[i] == "??" ? -1 : Convert.ToInt32(parts[i], 16);

        var hits = new List<int>();
        for (var i = 0; i <= data.Length - bytes.Length; i++)
        {
            var j = 0;
            while (j < bytes.Length && (bytes[j] < 0 || data[i + j] == bytes[j])) j++;
            if (j == bytes.Length) hits.Add(i);
        }
        return hits;
    }

    private static long FileOffsetToRva(byte[] d, int offset)
    {
        var pe = BitConverter.ToInt32(d, 0x3C);
        int count = BitConverter.ToUInt16(d, pe + 6);
        int optSize = BitConverter.ToUInt16(d, pe + 20);
        var sections = pe + 24 + optSize;
        for (var i = 0; i < count; i++)
        {
            var h = sections + 40 * i;
            var va = BitConverter.ToUInt32(d, h + 12);
            var rawSize = BitConverter.ToUInt32(d, h + 16);
            var raw = BitConverter.ToUInt32(d, h + 20);
            if (offset >= raw && offset < raw + rawSize)
                return offset - raw + va;
        }
        return -1;
    }
}
