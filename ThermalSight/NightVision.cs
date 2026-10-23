using System;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.Events;

namespace ThermalSight;











internal sealed class NightVisionProfile
{
    public int Gen;
    public string Label, Summary, Spectral;
    public float Range;            
    public float[] Magnifications; 
    public string[] ModeNames;
    public float DigitalMax = 1f;  
    public int Lines;              
    public float Blur;             
    public float SensitivityEv;    
    public float Noise;            
    public float Halo;             
    public float Persistence;      
    public float FrameRate;        
    public float NirBoost;         
    public bool Illuminator;       
    public Color Phosphor;
    public float FloorGlow;        

    public string ModeName(int i) => ModeNames != null && i >= 0 && i < ModeNames.Length ? ModeNames[i] : "";

    
    public static float FieldOfView(float m)
    {
        var baseFov = Math.Clamp(Plugin.BaseFieldOfView.Value, 5f, 120f) * Mathf.Deg2Rad;
        return 2f * Mathf.Atan(Mathf.Tan(baseFov * 0.5f) / Math.Max(1f, m)) * Mathf.Rad2Deg;
    }

    public string Characteristics()
    {
        var modes = "";
        for (var i = 0; i < Magnifications.Length; i++)
            modes += (i > 0 ? ", " : "") + $"{ModeName(i)} x{Magnifications[i]:0.#} ({FieldOfView(Magnifications[i]):0.#}°)";
        return $"Range: {Range:0} m{(Illuminator ? " (own IR searchlight)" : "")}\n" +
               $"Fields of view: {modes}{(DigitalMax > 1f ? $", + digital up to x{DigitalMax:0}" : "")}\n" +
               $"Resolution: {Lines} lines\n" +
               $"Low-light sensitivity: up to x{Mathf.Pow(2f, SensitivityEv):0} brightness ({SensitivityEv:0.#} EV)\n" +
               $"Spectral range: {Spectral}\n" +
               $"Refresh: {FrameRate:0} Hz{(Persistence > 0.2f ? ", long afterglow" : Persistence > 0.08f ? ", slight afterglow" : "")}";
    }

    public static readonly NightVisionProfile[] All =
    {
        new()
        {
            Gen = 1, Label = "Gen 1 - active infrared",
            Summary = "Image converter tube with its own infrared searchlight. Sees well only inside the searchlight beam; " +
                      "grainy, soft picture with long afterglow and big halos around lights.",
            Spectral = "0.4-1.2 um, visible + near IR (S-1 photocathode)",
            Range = 600f, Magnifications = new[] { 5.5f }, ModeNames = new[] { "fixed" },
            Lines = 260, Blur = 1.2f, SensitivityEv = 3f, Noise = 0.05f, Halo = 0.9f, Persistence = 0.5f, FrameRate = 25f,
            NirBoost = 0.35f, Illuminator = true, Phosphor = new Color(0.55f, 1f, 0.32f), FloorGlow = 0.012f,
        },
        new()
        {
            Gen = 2, Label = "Gen 2 - passive microchannel plate",
            Summary = "Passive intensifier: works by starlight and moonlight without a searchlight. Clearer picture, " +
                      "moderate grain in deep darkness, noticeable smear when the turret swings.",
            Spectral = "0.4-0.9 um, visible + near IR (S-25 photocathode)",
            Range = 1300f, Magnifications = new[] { 3f, 7f }, ModeNames = new[] { "wide", "narrow" },
            Lines = 420, Blur = 0.8f, SensitivityEv = 8f, Noise = 0.04f, Halo = 0.55f, Persistence = 0.28f, FrameRate = 30f,
            NirBoost = 0.5f, Phosphor = new Color(0.38f, 1f, 0.36f), FloorGlow = 0.008f,
        },
        new()
        {
            Gen = 3, Label = "Gen 3 - GaAs intensifier",
            Summary = "Gallium arsenide photocathode: very sensitive in the near infrared, fine detail and fast response. " +
                      "Wide field for searching, narrow for identification.",
            Spectral = "0.5-0.9 um, near IR (GaAs photocathode)",
            Range = 2600f, Magnifications = new[] { 2.5f, 8f }, ModeNames = new[] { "wide", "narrow" },
            Lines = 620, Blur = 0.5f, SensitivityEv = 10.5f, Noise = 0.026f, Halo = 0.3f, Persistence = 0.12f, FrameRate = 50f,
            NirBoost = 0.7f, Phosphor = new Color(0.42f, 1f, 0.5f), FloorGlow = 0.006f,
        },
        new()
        {
            Gen = 4, Label = "Gen 4 - filmless / digital",
            Summary = "Modern filmless intensifier with a digital channel: white phosphor, high resolution, three fields " +
                      "of view and digital zoom; little grain and almost no smear.",
            Spectral = "0.4-1.1 um, visible + near IR (digital)",
            Range = 4200f, Magnifications = new[] { 2f, 6f, 12f }, ModeNames = new[] { "wide", "medium", "narrow" }, DigitalMax = 2f,
            Lines = 900, Blur = 0.3f, SensitivityEv = 12.5f, Noise = 0.016f, Halo = 0.15f, Persistence = 0.04f, FrameRate = 60f,
            NirBoost = 0.5f, Phosphor = new Color(0.88f, 0.93f, 1f), FloorGlow = 0.005f,
        },
    };

    public static NightVisionProfile Get(int gen) => All[Math.Clamp(gen, 1, All.Length) - 1];
}


internal static class NightVisionParts
{
    public static readonly string[] Guids =
    {
        "7b1e4c2a-9d3f-4e8a-b6c3-5f2a8e9d0c41",
        "7b1e4c2a-9d3f-4e8a-b6c3-5f2a8e9d0c42",
        "7b1e4c2a-9d3f-4e8a-b6c3-5f2a8e9d0c43",
        "7b1e4c2a-9d3f-4e8a-b6c3-5f2a8e9d0c44",
    };
    public static readonly string[] Names = { "mdNightVisionSight", "mdNightVisionSightGen2", "mdNightVisionSightGen3", "mdNightVisionSightGen4" };
    public const string AnyGuidPattern = "7b1e4c2a-9d3f-4e8a-b6c3-5f2a8e9d0c4[1-4]";
    public const string DisplayName = "Night Vision Sight";

    public static string Description =>
        "An image-intensifier night sight for the gunner. Inside the vehicle press " + Plugin.NightVisionKey +
        " to switch to night vision and back; the mouse wheel switches the field of view. The generation sets range, " +
        "resolution, sensitivity and refresh rate.";

    public static int GenFromGuid(string guid)
    {
        if (guid == null) return 0;
        for (var i = 0; i < Guids.Length; i++)
            if (string.Equals(Guids[i], guid, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return 0;
    }

    public static int GenFromName(string name)
    {
        if (name == null) return 0;
        for (var i = 0; i < Names.Length; i++)
            if (name == Names[i]) return i + 1;
        return 0;
    }

    
    public static int GenOf(Sprocket.Vehicles.AttachedBehaviours.GunnerSight s)
    {
        try
        {
            var obj = s.GetComponent<VehicleObject>();
            return obj != null && !string.IsNullOrEmpty(obj.guid) ? GenFromGuid(obj.guid) : 0;
        }
        catch { return 0; }
    }

    public static string EnsureDefinition(int gen)
    {
        gen = Math.Clamp(gen, 1, Guids.Length);
        var guid = Guids[gen - 1];
        if (ThermalParts.Database == null) ThermalParts.EnsureInjected(force: true);
        
        var tags = gen == 1
            ? new[] { "gunner", "scope", ModernDayTags.Or(ModernDayTags.Thermal), "nightvision" }
            : new[] { "gunner", "scope", "nightvision" };
        ThermalParts.AddDefinition(guid, Names[gen - 1], tags);
        return guid;
    }

    public static void Apply(VehicleObject target, int gen)
    {
        target.guid = EnsureDefinition(gen);
        target.gameObject.name = Names[Math.Clamp(gen, 1, Names.Length) - 1];
    }

    
    private const string GenKey = "nightVisionGen";
    private static readonly Regex SaveRegex = new("\"guid\"\\s*:\\s*\"(" + AnyGuidPattern + ")\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LoadRegex = new(
        "\"guid\"\\s*:\\s*\"" + ThermalParts.GunnerSightGuid + "\"\\s*,\\s*\"" + GenKey + "\"\\s*:\\s*(\\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ToFile(string json, out int count)
    {
        var n = 0;
        var result = SaveRegex.Replace(json, m =>
        {
            var gen = GenFromGuid(m.Groups[1].Value);
            if (gen == 0) return m.Value;
            n++;
            return $"\"guid\": \"{ThermalParts.GunnerSightGuid}\", \"{GenKey}\": {gen}";
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
            return $"\"guid\": \"{EnsureDefinition(m.Groups[1].Value[0] - '0')}\"";
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
                if (n > 0) Plugin.Log.LogInfo($"Blueprint save: {n} night vision sight(s) stored as Gunner Sight + generation");
            }
            catch (Exception e) { Plugin.Log.LogError("Night vision blueprint save failed: " + e); }
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
                if (n > 0) Plugin.Log.LogInfo($"Blueprint load: restored {n} night vision sight(s)");
            }
            catch (Exception e) { Plugin.Log.LogError("Night vision blueprint load failed: " + e); }
        }
    }

    
    private static Il2CppSystem.Collections.Generic.List<string> _options;

    [HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
    private static class PanelPatch
    {
        private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
        {
            try
            {
                var target = __instance.target;
                var gen = target != null ? GenFromGuid(target.guid) : 0;
                if (gen == 0) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer == null) return;
                if (_options == null)
                {
                    _options = new Il2CppSystem.Collections.Generic.List<string>();
                    foreach (var p in NightVisionProfile.All) _options.Add(p.Label);
                }
                var editor = __instance;
                UnityAction<int> onGen = (Action<int>)(index =>
                {
                    try
                    {
                        Apply(target, index + 1);
                        editor.RequestRedraw();
                    }
                    catch (Exception e) { Plugin.Log.LogError("Night vision generation change failed: " + e); }
                });
                var profile = NightVisionProfile.Get(gen);
                drawer.Header("Night Vision");
                drawer.Dropdown("Generation", _options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), gen - 1, onGen,
                    "Image intensifier generation of the night vision sight");
                drawer.InfoField(profile.Summary, 3);
                drawer.InfoField(profile.Characteristics(), 6);
                drawer.InfoField($"In the vehicle: {Plugin.NightVisionKey} switches to night vision and back, the mouse wheel " +
                                 "switches the field of view. Sees reflected light only (moon, stars, lights, its own IR searchlight), not heat.", 3);
            }
            catch (Exception e) { Plugin.Log.LogError("Night vision panel failed: " + e); }
        }
    }
}
