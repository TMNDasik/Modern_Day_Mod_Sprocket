using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VehicleMaterials;



internal static class Assets
{
    private const string Root = "assets/vehiclematerials/";
    private static readonly string[] Files = { null, "glass", "nickel", "chrome", "steel", "fabric", "rubber" };

    private static AssetBundle _bundle;
    private static bool _tried;
    private static readonly Material[] Mats = new Material[Kinds.Count];
    private static float _appliedOpacity = -1f;

    public static Material Get(MaterialKind kind)
    {
        if (kind == MaterialKind.Default) return null;
        Ensure();
        var m = Mats[(int)kind];
        if (m != null && !m.WasCollected)
        {
            if (kind == MaterialKind.Glass) ApplyGlassOpacity(m);
            return m;
        }
        m = Fallback(kind);
        Mats[(int)kind] = m;
        return m;
    }

    private static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            string path = null;
            foreach (var p in new[] { Path.Combine(Plugin.PluginDir, "materials.bundle"), Path.Combine(Plugin.PluginDir, "MDVehicleMaterials", "materials.bundle"), Path.Combine(Plugin.PluginDir, "VehicleMaterials", "materials.bundle") })
                if (File.Exists(p)) { path = p; break; }
            if (path == null)
            {
                Plugin.Log.LogWarning("materials.bundle not found — using simple fallback materials (no clear glass)");
                return;
            }
            _bundle = BundleLoader.LoadFromFile(path);
            if (_bundle == null) throw new InvalidOperationException("bundle load returned null");
            var loaded = 0;
            for (var k = 1; k < Kinds.Count; k++)
            {
                var m = BundleLoader.LoadAsset<Material>(_bundle, Root + Files[k] + ".mat");
                if (m == null) continue;
                m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Mats[k] = m;
                loaded++;
            }
            Plugin.Log.LogInfo($"Vehicle materials loaded: {loaded} of {Kinds.Count - 1} (shader supported: {Mats[1]?.shader?.isSupported})");
        }
        catch (Exception e) { Plugin.Log.LogError("materials.bundle failed: " + e); }
    }

    private static void ApplyGlassOpacity(Material m)
    {
        var a = Mathf.Clamp01(Plugin.GlassOpacity.Value);
        if (Mathf.Abs(a - _appliedOpacity) < 0.001f) return;
        _appliedOpacity = a;
        try
        {
            var c = m.GetColor("_BaseColor");
            c.a = a;
            m.SetColor("_BaseColor", c);
        }
        catch { }
    }

    private static Material Fallback(MaterialKind kind)
    {
        try
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) return null;
            var m = new Material(shader) { name = "Vehicle material " + kind };
            var (color, metal, smooth) = kind switch
            {
                MaterialKind.Glass => (new Color(0.08f, 0.12f, 0.13f), 0.2f, 0.97f),
                MaterialKind.Nickel => (new Color(0.7f, 0.68f, 0.63f), 1f, 0.8f),
                MaterialKind.Chrome => (new Color(0.93f, 0.93f, 0.95f), 1f, 0.97f),
                MaterialKind.Steel => (new Color(0.53f, 0.54f, 0.55f), 1f, 0.55f),
                MaterialKind.Fabric => (new Color(0.36f, 0.34f, 0.24f), 0f, 0.12f),
                _ => (new Color(0.06f, 0.06f, 0.06f), 0f, 0.3f)
            };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return m;
        }
        catch { return null; }
    }
}
