using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Vehicles;

namespace ThermalSight;




internal static class BlueprintCompat
{
    private const string GenKey = "thermalGen";
    private const string OpticalKey = "thermalOptical";
    private const string DigitalKey = "thermalDigital";

    private static readonly Regex SaveRegex = new(
        "\"guid\"\\s*:\\s*\"(" + ThermalParts.AnyGuidPattern + ")\"",
        RegexOptions.Compiled);

    private static readonly Regex LoadRegex = new(
        "\"guid\"\\s*:\\s*\"" + ThermalParts.GunnerSightGuid + "\"\\s*,\\s*\"" + GenKey + "\"\\s*:\\s*(\\d)" +
        "(?:\\s*,\\s*\"" + OpticalKey + "\"\\s*:\\s*(\\d+)\\s*,\\s*\"" + DigitalKey + "\"\\s*:\\s*(\\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ToFile(string json, out int count)
    {
        var n = 0;
        var result = SaveRegex.Replace(json, m =>
        {
            if (!ThermalParts.TryDecode(m.Groups[1].Value, out var c)) return m.Value;
            n++;
            return $"\"guid\": \"{ThermalParts.GunnerSightGuid}\", \"{GenKey}\": {c.Gen}, \"{OpticalKey}\": {c.Optical}, \"{DigitalKey}\": {c.Digital}";
        });
        count = n;
        return result;
    }

    public static string FromFile(string json, out int count)
    {
        var n = 0;
        var result = LoadRegex.Replace(json, m =>
        {
            n++;
            var gen = m.Groups[1].Value[0] - '0';
            var opt = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : ThermalParts.DefaultOptical;
            var dig = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : ThermalParts.DefaultDigital;
            var guid = ThermalParts.EnsureDefinition(new SightConfig(gen, opt, dig));
            return $"\"guid\": \"{guid}\"";
        });
        count = n;
        return result;
    }

    [HarmonyPatch(typeof(VehicleBlueprintSerializer), nameof(VehicleBlueprintSerializer.SerializeToJSON))]
    private static class SavePatch
    {
        private static void Postfix(ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result)) return;
                __result = ToFile(__result, out var n);
                if (n > 0) Plugin.Log.LogInfo($"Blueprint save: {n} thermal sight(s) stored as Gunner Sight + settings");
            }
            catch (Exception e) { Plugin.Log.LogError("Blueprint save patch failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(VehicleBlueprintSerializer), nameof(VehicleBlueprintSerializer.DeserializeJSON))]
    private static class LoadPatch
    {
        private static void Prefix(ref string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return;
                json = FromFile(json, out var n);
                if (n > 0) Plugin.Log.LogInfo($"Blueprint load: restored {n} thermal sight(s)");
                
                foreach (Match m in Regex.Matches(json, ThermalParts.AnyGuidPattern))
                    if (ThermalParts.TryDecode(m.Value, out var c)) ThermalParts.EnsureDefinition(c);
            }
            catch (Exception e) { Plugin.Log.LogError("Blueprint load patch failed: " + e); }
        }
    }
}
