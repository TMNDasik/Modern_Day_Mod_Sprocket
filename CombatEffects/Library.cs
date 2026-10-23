using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CombatEffects;



internal sealed class EffectInfo
{
    public string Key, Path;
    public float Duration;          
    public bool Looping;
    public float Lifetime = -1;     
    public readonly List<string[]> Pickers = new();                 
    public readonly List<(string[] Systems, Vector3 Pivot)> Mirrors = new();   
    public readonly List<(float Min, float Max, string[] Systems)> Cull = new();
    public readonly List<(string At, float Start, float Seconds)> Diminish = new();
    public GameObject Prefab;
    public bool Missing;

    
    public float PlayTime => Lifetime > 0 ? Lifetime : Looping ? 10f : Mathf.Clamp(Duration + 0.5f, 1f, 60f);
}


internal sealed class ImpactEntry
{
    public string Name, Effect;
    public int Category, Size, Fuzed, Surface, Ricochet, Flags;
}

internal static class Library
{
    private static AssetBundle _bundle;
    private static bool _failed;
    private static readonly Dictionary<string, EffectInfo> Effects = new(StringComparer.OrdinalIgnoreCase);
    public static readonly List<ImpactEntry> Impacts = new();

    public static bool Ready => Load();

    private static string Folder => Plugin.ModFolder(Plugin.PluginDir, "CombatEffects");

    private static bool Load()
    {
        if (_bundle != null) return true;
        if (_failed) return false;
        try
        {
            var bundlePath = System.IO.Path.Combine(Folder, "combateffects.bundle");
            if (!File.Exists(bundlePath))
            {
                _failed = true;
                Plugin.Log.LogError($"{bundlePath} not found — the game's own effects stay");
                return false;
            }
            ReadManifest(System.IO.Path.Combine(Folder, "manifest.json"), System.IO.Path.Combine(Folder, "durations.json"));
            _bundle = BundleLoader.LoadFromFile(bundlePath);
            if (_bundle == null) throw new InvalidOperationException("bundle load returned null");
            Plugin.Log.LogInfo($"Effect library loaded: {Effects.Count} effects, {Impacts.Count} impact entries");
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogError("Effect library failed — the game's own effects stay: " + e);
            return false;
        }
    }

    private static void ReadManifest(string manifestPath, string durationsPath)
    {
        using (var doc = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            foreach (var p in doc.RootElement.GetProperty("effects").EnumerateObject())
            {
                var e = new EffectInfo { Key = p.Name, Path = p.Value.GetProperty("path").GetString() };
                var v = p.Value;
                if (v.TryGetProperty("lifetime", out var lt) && lt.ValueKind == JsonValueKind.Number) e.Lifetime = lt.GetSingle();
                foreach (var g in v.GetProperty("pickers").EnumerateArray()) e.Pickers.Add(Strings(g.GetProperty("systems")));
                foreach (var g in v.GetProperty("vrandomizers").EnumerateArray())
                {
                    var pv = g.GetProperty("pivot");
                    e.Mirrors.Add((Strings(g.GetProperty("systems")), new Vector3(pv[0].GetSingle(), pv[1].GetSingle(), pv[2].GetSingle())));
                }
                foreach (var g in v.GetProperty("cull").EnumerateArray())
                    e.Cull.Add((g.GetProperty("min").GetSingle(), g.GetProperty("max").GetSingle(), Strings(g.GetProperty("systems"))));
                foreach (var g in v.GetProperty("diminishers").EnumerateArray())
                    e.Diminish.Add((g.GetProperty("at").GetString(), g.GetProperty("start").GetSingle(), g.GetProperty("seconds").GetSingle()));
                Effects[e.Key] = e;
            }
            foreach (var i in doc.RootElement.GetProperty("impacts").EnumerateArray())
            {
                var eff = i.GetProperty("effect");
                Impacts.Add(new ImpactEntry
                {
                    Name = i.GetProperty("name").GetString(),
                    Effect = eff.ValueKind == JsonValueKind.String ? eff.GetString() : null,
                    Category = i.GetProperty("category").GetInt32(),
                    Size = i.GetProperty("size").GetInt32(),
                    Fuzed = i.GetProperty("fuzed").GetInt32(),
                    Surface = i.GetProperty("surface").GetInt32(),
                    Ricochet = i.GetProperty("ricochet").GetInt32(),
                    Flags = i.GetProperty("flags").GetInt32()
                });
            }
        }
        if (!File.Exists(durationsPath)) return;
        using var dur = JsonDocument.Parse(File.ReadAllText(durationsPath));
        foreach (var p in dur.RootElement.EnumerateObject())
            if (Effects.TryGetValue(p.Name, out var e))
            {
                e.Duration = p.Value.GetProperty("duration").GetSingle();
                e.Looping = p.Value.GetProperty("looping").GetBoolean();
            }
    }

    private static string[] Strings(JsonElement a)
    {
        var list = new List<string>();
        foreach (var s in a.EnumerateArray()) list.Add(s.GetString() ?? "");
        return list.ToArray();
    }

    public static EffectInfo Get(string key)
    {
        if (!Load() || key == null || !Effects.TryGetValue(key, out var e)) return null;
        if (e.Prefab != null && !e.Prefab.WasCollected) return e;
        if (e.Missing) return null;
        try
        {
            e.Prefab = BundleLoader.LoadAsset<GameObject>(_bundle, e.Path.ToLowerInvariant());
            if (e.Prefab == null) throw new InvalidOperationException("not in the bundle");
            e.Prefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Effect loaded: {key} ({e.PlayTime:0.#} s{(e.Looping ? ", looping" : "")})");
        }
        catch (Exception ex)
        {
            e.Missing = true;
            Plugin.Log.LogWarning($"Effect {key} unavailable: {ex.Message}");
            return null;
        }
        return e;
    }

    public static bool Has(string key) => key != null && Effects.ContainsKey(key);
}
