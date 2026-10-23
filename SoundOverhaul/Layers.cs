using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SoundOverhaul;





internal sealed class LayerSet
{
    private sealed class Layer
    {
        public AudioSource Src;
        public float Volume;
        public float Pitch = 1f;
        public float SilentSince = -1f;
    }

    public GameObject Host;
    public AudioSource Template;
    public Object Group;
    public bool Flat;                     
    public int Priority = 128;
    public float LastUpdate;
    public float MinDistance = 3f;

    private readonly Dictionary<string, Layer> _layers = new();
    private readonly Dictionary<string, (float Volume, float Pitch)> _want = new();
    private AudioSource _oneShot;
    private bool _flatApplied;

    public static readonly List<LayerSet> All = new();

    
    
    public const int LoopPriorityMin = 100;

    public static int PriorityFor(float distance, float minPriorityDistance)
    {
        var pr = minPriorityDistance > 0f ? Mathf.Clamp01(distance / minPriorityDistance) : 0f;
        return LoopPriorityMin + (int)(pr * pr * (255 - LoopPriorityMin));
    }

    public static LayerSet Create(AudioSource template, string name, float minDistance)
    {
        if (template == null || template.WasCollected) return null;
        var go = new GameObject("Sound Overhaul " + name);
        go.transform.SetParent(template.transform, false);
        var set = new LayerSet { Host = go, Template = template, MinDistance = minDistance, LastUpdate = Time.time };
        try { set.Group = Mixer.Get(template); } catch { }
        All.Add(set);
        return set;
    }

    public bool Alive
    {
        get
        {
            try { return Host != null && !Host.WasCollected && Host && Template != null && !Template.WasCollected && Template; }
            catch { return false; }
        }
    }

    
    public void Want(string bank, float volume, float pitch)
    {
        if (bank == null || volume <= 0.0001f || !Library.Has(bank)) return;
        volume *= Library.Gain(bank);
        if (_want.TryGetValue(bank, out var w)) _want[bank] = (w.Volume + volume, pitch);
        else _want[bank] = (volume, pitch);
    }

    public bool HasAny(params string[] banks)
    {
        foreach (var b in banks) if (b != null && Library.Has(b)) return true;
        return false;
    }

    
    public void Apply(float up = 4f, float down = 4f)
    {
        LastUpdate = Time.time;
        if (!Alive) { _want.Clear(); return; }
        var dt = Mathf.Max(0f, Time.deltaTime);
        foreach (var kv in _want)
            if (!_layers.ContainsKey(kv.Key))
            {
                var l = NewLayer(kv.Key);
                if (l != null) _layers[kv.Key] = l;
            }
        var now = Time.time;
        foreach (var kv in _layers)
        {
            var l = kv.Value;
            if (l.Src == null || l.Src.WasCollected || !l.Src.isActiveAndEnabled) continue;
            var target = 0f;
            if (_want.TryGetValue(kv.Key, out var w))
            {
                target = Mathf.Clamp01(w.Volume);
                l.Pitch = Mathf.Clamp(w.Pitch, 0.3f, 3f);
            }
            l.Volume = Mathf.MoveTowards(l.Volume, target, (target > l.Volume ? up : down) * dt);
            try
            {
                if (l.Volume > 0.0005f)
                {
                    l.SilentSince = -1f;
                    l.Src.volume = l.Volume;
                    l.Src.pitch = l.Pitch;
                    l.Src.priority = Priority;
                    if (!l.Src.isPlaying)
                    {
                        var clip = l.Src.clip;
                        if (clip != null && clip.length > 0.5f) l.Src.time = Random.Range(0f, clip.length * 0.9f);
                        l.Src.Play();
                    }
                }
                else
                {
                    l.Src.volume = 0f;
                    if (l.SilentSince < 0f) l.SilentSince = now;
                    else if (now - l.SilentSince > 0.3f && l.Src.isPlaying) l.Src.Stop();
                }
            }
            catch { }
        }
        if (_flatApplied != Flat) ApplySpace();
        _want.Clear();
    }

    
    public void OneShot(string bank, float volume, float pitch = 1f)
    {
        if (!Alive || !Host.activeInHierarchy || volume <= 0.0001f) return;
        var clip = Library.Pick(bank);
        if (clip == null) return;
        try
        {
            if (_oneShot == null || _oneShot.WasCollected)
            {
                _oneShot = Host.AddComponent<AudioSource>();
                Configure(_oneShot, false);
            }
            _oneShot.spatialBlend = Flat ? 0f : SpatialOf(Template);
            _oneShot.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
            _oneShot.priority = Priority;
            if(_oneShot.isActiveAndEnabled)_oneShot.PlayOneShot(clip, Mathf.Clamp01(volume * Library.Gain(bank)));
        }
        catch { }
    }

    public void StopAll()
    {
        foreach (var l in _layers.Values)
        {
            l.Volume = 0f;
            try { if (l.Src != null && !l.Src.WasCollected) { l.Src.volume = 0f; l.Src.Stop(); } } catch { }
        }
        try { if (_oneShot != null && !_oneShot.WasCollected) _oneShot.Stop(); } catch { }
        _want.Clear();
    }

    public void Destroy()
    {
        StopAll();
        try { if (Host != null && !Host.WasCollected) Object.Destroy(Host); } catch { }
        All.Remove(this);
    }

    private Layer NewLayer(string bank)
    {
        var clip = Library.Pick(bank);
        if (clip == null) return null;
        try
        {
            var src = Host.AddComponent<AudioSource>();
            Configure(src, true);
            src.clip = clip;
            return new Layer { Src = src };
        }
        catch { return null; }
    }

    private static float SpatialOf(AudioSource s)
    {
        try { return s != null && !s.WasCollected ? s.spatialBlend : 1f; } catch { return 1f; }
    }

    private void Configure(AudioSource src, bool loop)
    {
        src.playOnAwake = false;
        src.loop = loop;
        src.volume = 0f;
        src.dopplerLevel = 0f;
        src.spatialBlend = Flat ? 0f : SpatialOf(Template);
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        var min = MinDistance;
        var max = 1500f;
        try
        {
            min = Mathf.Max(MinDistance, Template.minDistance);
            max = Mathf.Max(min + 50f, Template.maxDistance);
        }
        catch { }
        src.minDistance = min;
        src.maxDistance = max;
        src.priority = Priority;
        try { Mixer.Set(src, Group); } catch { }
    }

    private void ApplySpace()
    {
        _flatApplied = Flat;
        var blend = Flat ? 0f : SpatialOf(Template);
        foreach (var l in _layers.Values)
            try { if (l.Src != null && !l.Src.WasCollected) l.Src.spatialBlend = blend; } catch { }
    }

    
    public static void Tick()
    {
        var now = Time.time;
        for (var i = All.Count - 1; i >= 0; i--)
        {
            var s = All[i];
            if (!s.Alive) { s.StopAll(); All.RemoveAt(i); continue; }
            if (now - s.LastUpdate > 0.4f)
            {
                s.Apply(4f, 2f);
                s.LastUpdate = now - 0.41f; 
            }
        }
    }

    public static void StopEverything()
    {
        foreach (var s in All.ToArray()) s.StopAll();
    }
}





internal static class VanillaMute
{
    private const int Lowest = 256;
    private static readonly Dictionary<IntPtr, (AudioSource Src, int Priority)> Muted = new();
    private static readonly List<AudioSource> Pending = new();

    public static void Mute(AudioSource s)
    {
        if (s == null) return;
        try
        {
            if (s.WasCollected) return;
            if (!s.mute) s.mute = true;
            if (!Muted.ContainsKey(s.Pointer)) Muted[s.Pointer] = (s, s.priority);
            if (Pending.Count > 512) Pending.Clear();
            Pending.Add(s);
        }
        catch { }
    }

    
    public static void Demote()
    {
        if (Pending.Count == 0) return;
        foreach (var s in Pending)
            try { if (s != null && !s.WasCollected) s.priority = Lowest; } catch { }
        Pending.Clear();
    }

    public static void Unmute(AudioSource s)
    {
        if (s == null) return;
        try
        {
            if (!s.WasCollected)
            {
                if (s.mute) s.mute = false;
                if (Muted.TryGetValue(s.Pointer, out var m) && s.priority == Lowest) s.priority = m.Priority;
            }
            Muted.Remove(s.Pointer);
        }
        catch { }
    }

    public static void UnmuteAll()
    {
        foreach (var m in Muted.Values)
            try
            {
                var s = m.Src;
                if (s == null || s.WasCollected || !s) continue;
                s.mute = false;
                if (s.priority == Lowest) s.priority = m.Priority;
            }
            catch { }
        Muted.Clear();
        Pending.Clear();
    }
}
