using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CombatEffects;



internal sealed class Instance
{
    public EffectInfo Info;
    public GameObject Go;
    public Transform Tr;
    public ParticleSystem[] Systems = Array.Empty<ParticleSystem>();
    public ParticleSystem[] Roots = Array.Empty<ParticleSystem>();
    public float[] BaseRate = Array.Empty<float>();
    public float[] BaseDistance = Array.Empty<float>();
    public Vector3[] BaseStart = Array.Empty<Vector3>();   
    public bool StartScaled;                               
    public Light[] Lights = Array.Empty<Light>();
    public float[] LightBase = Array.Empty<float>();
    public float Start, End;          
    public bool Active, Stopping;
    public float StopAt;
    public float Speed = 1f;          
    public bool NoDiminish;           
    public Transform Follow;          
    public readonly Dictionary<string, Transform> Paths = new();
}

internal static class Effects
{
    private static readonly Dictionary<string, Stack<Instance>> Pools = new();
    private static readonly List<Instance> Live = new();
    private static GameObject _container;
    private static int _errors;
    private static readonly System.Random Rng = new();

    public static int Count => Live.Count;

    private static Transform Container
    {
        get
        {
            if (_container != null && !_container.WasCollected) return _container.transform;
            _container = new GameObject("CombatEffects");
            Object.DontDestroyOnLoad(_container);
            return _container.transform;
        }
    }

    
    public static Instance Play(string key, Vector3 position, Quaternion rotation, Transform parent = null, float lifetime = -1f, bool cull = true)
    {
        try
        {
            if (cull && !Visible(position)) return null;
            var info = Library.Get(key);
            if (info == null) return null;
            var inst = Take(info);
            if (inst == null) return null;
            Place(inst, position, rotation, parent);
            Start(inst, position);
            inst.End = Time.time + (lifetime > 0 ? lifetime : info.PlayTime);
            return inst;
        }
        catch (Exception e)
        {
            if (_errors++ < 20) Plugin.Log.LogError($"Effect {key} failed: {e}");
            return null;
        }
    }

    
    public static Instance Loop(string key, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        var inst = Play(key, position, rotation, parent, 1f, false);
        if (inst != null) inst.End = -1f;
        return inst;
    }

    
    public static void Stop(Instance inst, float linger = -1f)
    {
        if (inst == null || !inst.Active || inst.Stopping) return;
        try
        {
            foreach (var ps in inst.Roots) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        catch { }
        inst.Stopping = true;
        inst.StopAt = Time.time;
        inst.End = Time.time + (linger > 0 ? linger : Mathf.Clamp(inst.Info.Duration, 2f, 30f));
    }

    
    public static void SetRate(Instance inst, float k)
    {
        if (inst == null || !inst.Active) return;
        for (var i = 0; i < inst.Systems.Length; i++) SetSystemRate(inst, i, k);
    }

    public static void SetSystemRate(Instance inst, int i, float k)
    {
        if (inst == null || !inst.Active || i < 0 || i >= inst.Systems.Length) return;
        try
        {
            var ps = inst.Systems[i];
            if (ps == null) return;
            k = Mathf.Max(0f, k);
            ParticleNative.SetRates(ps, inst.BaseRate[i] * k, inst.BaseDistance[i] * k);
        }
        catch (Exception e) { if (_errors++ < 20) Plugin.Log.LogWarning("Effect rate failed: " + e.Message); }
    }

    
    public static void SetSystemStart(Instance inst, int i, float size, float speed, float life)
    {
        if (inst == null || !inst.Active || i < 0 || i >= inst.Systems.Length) return;
        try
        {
            var b = inst.BaseStart[i];
            ParticleNative.SetStart(inst.Systems[i], new Vector3(b.x * size, b.y * speed, b.z * life));
            inst.StartScaled = true;
        }
        catch (Exception e) { if (_errors++ < 20) Plugin.Log.LogWarning("Effect size failed: " + e.Message); }
    }

    
    public static bool SetSpeed(Instance inst, float speed)
    {
        if (inst == null || !inst.Active || speed <= 0f) return false;
        var ok = true;
        try
        {
            foreach (var ps in inst.Systems) ok &= ParticleNative.SetSimulationSpeed(ps, speed);
        }
        catch (Exception e) { if (_errors++ < 20) Plugin.Log.LogWarning("Effect speed failed: " + e.Message); return false; }
        if (!ok) return false;
        if (inst.End >= 0f) inst.End = inst.Start + (inst.End - inst.Start) * inst.Speed / speed;
        inst.Speed = speed;
        return true;
    }

    public static bool Alive(Instance inst) => inst != null && inst.Active && inst.Go != null && !inst.Go.WasCollected;

    
    public static bool Visible(Vector3 position)
    {
        var cam = Lighting.Camera;
        if (cam == null) return true;
        var ct = cam.transform;
        var d = position - ct.position;
        var zoom = Mathf.Clamp(60f / Mathf.Max(1f, cam.fieldOfView), 1f, 8f);
        if (d.sqrMagnitude > Sq(Plugin.CullDistance.Value * zoom)) return false;
        return d.sqrMagnitude < 900f || Vector3.Dot(d, ct.forward) > -10f;
    }

    private static float Sq(float x) => x * x;

    private static Instance Take(EffectInfo info)
    {
        if (!Pools.TryGetValue(info.Key, out var pool)) Pools[info.Key] = pool = new Stack<Instance>();
        
        if (Live.Count >= Plugin.MaxEffects.Value)
        {
            for (var i = 0; i < Live.Count; i++)
                if (Live[i].End >= 0f) { Release(Live[i]); break; }
            if (Live.Count >= Plugin.MaxEffects.Value) return null;
        }
        Instance inst = null;
        while (pool.Count > 0 && inst == null)
        {
            var p = pool.Pop();
            if (p.Go != null && !p.Go.WasCollected) inst = p;
        }
        inst ??= Create(info);
        if (inst == null) return null;
        if (inst.Speed != 1f)
        {
            foreach (var ps in inst.Systems) ParticleNative.SetSimulationSpeed(ps, 1f);
            inst.Speed = 1f;
        }
        if (inst.StartScaled)
        {
            for (var i = 0; i < inst.Systems.Length; i++) ParticleNative.SetStart(inst.Systems[i], inst.BaseStart[i]);
            inst.StartScaled = false;
        }
        inst.NoDiminish = false;
        inst.Active = true;
        inst.Stopping = false;
        inst.Start = Time.time;
        Live.Add(inst);
        return inst;
    }

    private static Instance Create(EffectInfo info)
    {
        var go = Object.Instantiate(info.Prefab, Container, false);
        go.SetActive(false);
        go.name = "CFX " + info.Key;
        var inst = new Instance { Info = info, Go = go, Tr = go.transform };
        inst.Systems = go.GetComponentsInChildren<ParticleSystem>(true);
        inst.BaseRate = new float[inst.Systems.Length];
        inst.BaseDistance = new float[inst.Systems.Length];
        inst.BaseStart = new Vector3[inst.Systems.Length];
        
        var roots = new List<ParticleSystem>();
        for (var i = 0; i < inst.Systems.Length; i++)
        {
            var ps = inst.Systems[i];
            try
            {
                inst.BaseRate[i] = ParticleNative.GetRate(ps);
                inst.BaseDistance[i] = ParticleNative.GetDistanceRate(ps);
                inst.BaseStart[i] = ParticleNative.GetStart(ps);
            }
            catch { inst.BaseRate[i] = 1f; inst.BaseDistance[i] = 0f; inst.BaseStart[i] = Vector3.one; }
            if (!HasSystemAbove(ps.transform, go.transform)) roots.Add(ps);
        }
        inst.Roots = roots.ToArray();
        inst.Lights = go.GetComponentsInChildren<Light>(true);
        inst.LightBase = new float[inst.Lights.Length];
        for (var i = 0; i < inst.Lights.Length; i++) inst.LightBase[i] = inst.Lights[i].intensity;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) inst.Paths[PathOf(t, go.transform)] = t;
        HideUnsupported(go, info.Key);
        return inst;
    }

    
    private static readonly Dictionary<IntPtr, bool> ShaderOk = new();

    private static void HideUnsupported(GameObject go, string key)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                var s = m.shader;
                var p = s != null ? s.Pointer : IntPtr.Zero;
                if (!ShaderOk.TryGetValue(p, out var ok))
                {
                    ok = s != null && s.isSupported;
                    ShaderOk[p] = ok;
                    if (!ok) Plugin.Log.LogWarning($"Shader {(s != null ? s.name : "(missing)")} of material {m.name} ({key}) is not supported here — that part of the effect is hidden");
                }
                if (ok) continue;
                r.enabled = false;
                break;
            }
        }
    }

    private static bool HasSystemAbove(Transform t, Transform root)
    {
        if (t.Pointer == root.Pointer) return false;
        var p = t.parent;
        while (p != null)
        {
            if (p.GetComponent<ParticleSystem>() != null) return true;
            if (p.Pointer == root.Pointer) return false;
            p = p.parent;
        }
        return false;
    }

    private static string PathOf(Transform t, Transform root)
    {
        if (t.Pointer == root.Pointer) return "";
        var names = new List<string>();
        while (t != null && t.Pointer != root.Pointer)
        {
            names.Add(t.name);
            t = t.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static void Place(Instance inst, Vector3 position, Quaternion rotation, Transform parent)
    {
        var tr = inst.Tr;
        if (parent != null && !parent.WasCollected)
        {
            tr.SetParent(parent, true);
            inst.Follow = parent;
        }
        else
        {
            tr.SetParent(Container, true);
            inst.Follow = null;
        }
        tr.position = position;
        tr.rotation = rotation * inst.Info.Prefab.transform.localRotation;
    }

    private static void Start(Instance inst, Vector3 position)
    {
        var info = inst.Info;
        
        foreach (var g in info.Pickers) foreach (var p in g) SetActive(inst, p, true);
        foreach (var g in info.Mirrors) foreach (var p in g.Systems) SetActive(inst, p, true);
        foreach (var g in info.Cull) foreach (var p in g.Systems) SetActive(inst, p, true);
        foreach (var g in info.Pickers)
        {
            if (g.Length == 0) continue;
            var pick = Rng.Next(g.Length);
            for (var i = 0; i < g.Length; i++) SetActive(inst, g[i], i == pick);
        }
        foreach (var g in info.Mirrors)
        {
            if (g.Systems.Length < 3) continue;
            var a = Rng.Next(g.Systems.Length);
            int b;
            do b = Rng.Next(g.Systems.Length); while (b == a);
            for (var i = 0; i < g.Systems.Length; i++) SetActive(inst, g.Systems[i], i == a || i == b);
            Mirror(inst, g.Systems[a], g.Pivot, false);
            Mirror(inst, g.Systems[b], g.Pivot, true);
        }
        var cam = Lighting.Camera;
        if (cam != null)
            foreach (var g in info.Cull)
            {
                var fov = 1f + (1f - cam.fieldOfView / 59f);
                var d = Vector3.Distance(cam.transform.position, position);
                var vp = cam.WorldToViewportPoint(position);
                var onScreen = vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1 && vp.z > 0;
                if ((d > g.Max * fov || !onScreen) && d >= g.Min)
                    foreach (var p in g.Systems) SetActive(inst, p, false);
            }
        var k = Mathf.Max(0f, Plugin.LightBrightness.Value);
        for (var i = 0; i < inst.Lights.Length; i++)
            if (inst.Lights[i] != null) inst.Lights[i].intensity = inst.LightBase[i] * k;
        inst.Go.SetActive(true);
        foreach (var ps in inst.Roots)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) continue;
            ps.Clear(true);
            ps.Play(true);
        }
    }

    private static void SetActive(Instance inst, string path, bool on)
    {
        if (path != null && inst.Paths.TryGetValue(path, out var t) && t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    
    private static void Mirror(Instance inst, string path, Vector3 pivot, bool flip)
    {
        try
        {
            if (!inst.Paths.TryGetValue(path, out var t) || t == null) return;
            ParticleNative.SetFlip(t.GetComponent<ParticleSystem>(), t.GetComponent<ParticleSystemRenderer>(), flip, pivot);
        }
        catch (Exception e) { if (_errors++ < 20) Plugin.Log.LogWarning("Effect mirror failed: " + e.Message); }
    }

    public static void Tick()
    {
        var now = Time.time;
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            var inst = Live[i];
            if (inst.Go == null || inst.Go.WasCollected)
            {
                Live.RemoveAt(i);
                inst.Active = false;
                continue;
            }
            
            if (inst.Follow != null && inst.Follow.WasCollected) inst.Follow = null;
            Diminish(inst, now);
            if (inst.End >= 0f && now >= inst.End) Release(inst);
        }
    }

    
    private static void Diminish(Instance inst, float now)
    {
        if (inst.Info.Diminish.Count == 0 || inst.Stopping || inst.NoDiminish) return;
        var t = now - inst.Start;
        foreach (var d in inst.Info.Diminish)
        {
            if (t < d.Start || !inst.Paths.TryGetValue(d.At, out var tr) || tr == null) continue;
            var ps = tr.GetComponent<ParticleSystem>();
            if (ps == null) continue;
            var f = 1f - Mathf.Clamp01((t - d.Start) / Mathf.Max(0.1f, d.Seconds));
            try
            {
                var idx = Array.IndexOf(inst.Systems, ps);
                if (idx >= 0) SetSystemRate(inst, idx, f);
                if (f <= 0f) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            catch { }
        }
    }

    private static void Release(Instance inst)
    {
        Live.Remove(inst);
        inst.Active = false;
        try
        {
            if (inst.Go == null || inst.Go.WasCollected) return;
            foreach (var ps in inst.Roots) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            inst.Go.SetActive(false);
            inst.Tr.SetParent(Container, false);
            inst.Follow = null;
            for (var i = 0; i < inst.Systems.Length; i++)
                if (inst.Systems[i] != null) ParticleNative.SetRates(inst.Systems[i], inst.BaseRate[i], inst.BaseDistance[i]);
            if (Pools.TryGetValue(inst.Info.Key, out var pool) && pool.Count < 24) pool.Push(inst);
            else Object.Destroy(inst.Go);
        }
        catch { }
    }

    
    public static void Clear()
    {
        foreach (var inst in new List<Instance>(Live)) Release(inst);
        Live.Clear();
    }

    public static void OnSettingsChanged()
    {
        if (!Plugin.Enabled.Value) Clear();
        Hooks.SettingsChanged();
    }
}
