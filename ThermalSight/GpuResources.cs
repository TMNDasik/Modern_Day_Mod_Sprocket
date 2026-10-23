using System;
using System.IO;
using UnityEngine;

namespace ThermalSight;


internal static class GpuResources
{
    public static Shader ObjectShader, RadianceShader, SensorShader, GasShader, NightVisionShader, DisplayDamageShader;
    public static Texture2D SightFrame;

    
    
    
    public const float FramePixelsW = 2048f, FramePixelsH = 857f;
    public const float FrameAspect = FramePixelsW / FramePixelsH;
    public const float WindowLeft = 265.5f / FramePixelsW, WindowRight = 1781.5f / FramePixelsW;
    public const float WindowBottom = 1f - 805f / FramePixelsH, WindowTop = 1f - 51f / FramePixelsH;

    
    public static readonly System.Collections.Generic.Dictionary<string, Texture2D> Glyphs = new();

    private static readonly string[] GlyphNames =
        { "got", "osn", "b", "o", "k", "u", "p", "d0", "d1", "d2", "d3", "d4", "d5", "d6", "d7", "d8", "d9" };
    private static bool _tried;
    private static AssetBundle _bundle;

    public static bool Available => ObjectShader != null && RadianceShader != null && SensorShader != null;

    public static bool TryLoad()
    {
        if (_tried) return Available;
        _tried = true;
        try
        {
            var path = Path.Combine(Plugin.PluginDir, "thermalsight.bundle");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("thermalsight.bundle not found — using CPU thermal pipeline");
                return false;
            }
            _bundle = BundleLoader.LoadFromFile(path);
            if (_bundle == null) throw new InvalidOperationException("bundle load returned null");

            ObjectShader = Load("assets/thermal/thermalobject.shader");
            RadianceShader = Load("assets/thermal/thermalradiance.shader");
            SensorShader = Load("assets/thermal/thermalsensor.shader");
            GasShader = Load("assets/thermal/thermalgas.shader");
            NightVisionShader = Load("assets/thermal/nightvision.shader");
            if (NightVisionShader == null) Plugin.Log.LogWarning("Night vision shader missing: update thermalsight.bundle together with ThermalSight.dll");
            DisplayDamageShader = Load("assets/thermal/displaydamage.shader");
            if (DisplayDamageShader == null) Plugin.Log.LogWarning("Display damage shader missing: a damaged thermal sight shows the simple damage picture (update thermalsight.bundle)");
            try
            {
                SightFrame = BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/thermal/textures/sightframe.png");
                if (SightFrame != null) SightFrame.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sight frame not loaded: " + e.Message); }
            foreach (var n in GlyphNames)
            {
                try
                {
                    var t = BundleLoader.LoadAsset<Texture2D>(_bundle, $"assets/thermal/textures/labels/{n}.png");
                    if (t == null) continue;
                    t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    Glyphs[n] = t;
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Label '{n}' not loaded: {e.Message}"); }
            }
            if (Glyphs.Count < GlyphNames.Length) Plugin.Log.LogWarning($"Sight labels: {Glyphs.Count} of {GlyphNames.Length} loaded");
            Plugin.Log.LogInfo($"GPU thermal shaders: object={Ok(ObjectShader)} radiance={Ok(RadianceShader)} sensor={Ok(SensorShader)} gas={Ok(GasShader)} frame={(SightFrame == null ? "missing" : "ok")}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("GPU thermal resources failed: " + e);
        }
        return Available;
    }

    public static AssetBundle Bundle => _bundle;

    private static Shader Load(string name)
    {
        var s = BundleLoader.LoadAsset<Shader>(_bundle, name);
        if (s == null || !s.isSupported) return null;
        
        s.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return s;
    }

    public static string Describe() =>
        $"object={(ObjectShader == null ? "null" : "ok")} radiance={(RadianceShader == null ? "null" : "ok")} sensor={(SensorShader == null ? "null" : "ok")}";

    private static string Ok(Shader s) => s == null ? "missing" : "ok";
}
