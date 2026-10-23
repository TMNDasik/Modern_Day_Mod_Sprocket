using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Engines;
using Sprocket.Powertrains.Engines;
using Sprocket.Vehicles.Engines;

namespace Powertrain;




internal static class EngineSoundChoice
{
    
    public static readonly (string Key, string Name)[] Options =
    {
        ("auto", "Auto (by displacement)"), ("t72", "T-72 (V12)"), ("t64", "T-64 (5-cylinder)"), ("leo", "Leopard (V10)"),
        ("t55", "T-55 (V12)"), ("m60", "M60 (V12)"), ("bmp1", "BMP-1 (V6)"), ("m2", "Bradley (V8)"), ("m113", "M113 (V8)"),
        ("marder", "Marder (V6)"), ("brdm", "BRDM-2 (petrol V8)"), ("m923", "Truck (inline 6)")
    };

    private static readonly Regex Token = new(@"\s?\[esnd:([a-z0-9]+)\]", RegexOptions.Compiled);
    private static readonly Dictionary<IntPtr, string> ByEngine = new();

    public static string Read(string description)
    {
        if (string.IsNullOrEmpty(description)) return "auto";
        var m = Token.Match(description);
        return m.Success ? m.Groups[1].Value : "auto";
    }

    public static string With(string description, string key)
    {
        var d = Token.Replace(description ?? "", "");
        if (string.IsNullOrEmpty(key) || key == "auto") return d;
        var token = $"[esnd:{key}]";
        return d.Length == 0 ? token : d + " " + token;
    }

    public static int IndexOf(string key)
    {
        for (var i = 0; i < Options.Length; i++) if (Options[i].Key == key) return i;
        return 0;
    }

    
    public static string For(IntPtr engineBehaviour) =>
        engineBehaviour != IntPtr.Zero && ByEngine.TryGetValue(engineBehaviour, out var k) ? k : "auto";

    [HarmonyPatch(typeof(EngineBehaviour), nameof(EngineBehaviour.SetBlueprint))]
    private static class SetBlueprintPatch
    {
        private static void Postfix(EngineBehaviour __instance, IEngineBlueprint blueprint)
        {
            try
            {
                var bp = blueprint?.TryCast<EngineBlueprint>();
                if (ByEngine.Count > 512) ByEngine.Clear();
                ByEngine[__instance.Pointer] = Read(bp?.Description);
            }
            catch { }
        }
    }
}
