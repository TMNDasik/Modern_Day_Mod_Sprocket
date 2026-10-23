using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace SoundOverhaul;



internal sealed class Voice
{
    public GameObject Go;
    public AudioSource Src;
    public bool Busy, Loop;
    public float End;
    public float FadeStart = -1f, FadeLength, FadeFrom;
    public float BaseVolume;
    public int Priority;
}

internal struct VoiceSpec
{
    public float Volume, Pitch, MinDistance, MaxDistance, Delay, Spread;
    public bool Spatial, Loop;
    public int Priority;
    public Vector3 Position;
    public Transform Follow;
    public float StartTime; 

    public static VoiceSpec At(Vector3 position, float volume, float minDistance, float maxDistance) => new()
    {
        Position = position, Volume = volume, Pitch = 1f, MinDistance = minDistance, MaxDistance = maxDistance,
        Spatial = true, Priority = 128
    };

    public static VoiceSpec Flat(float volume) => new() { Volume = volume, Pitch = 1f, Spatial = false, Priority = 64 };
}

internal static class Voices
{
    private const int MaxVoices = 128;
    private const float SpeedOfSound = 343f;

    private static GameObject _root;
    private static readonly List<Voice> Pool = new();
    private static Object _group; 
    private static bool _groupLogged, _fallbackLogged;
    private static float _nextSearch;

    
    public static void UseGroupOf(AudioSource vanilla)
    {
        if (Mixer.Alive(_group)) return;
        try
        {
            var g = Mixer.Get(vanilla);
            if (!Mixer.Alive(g)) return;
            if (Excluded((g.name ?? "").ToLowerInvariant()))
            {
                if (!_groupLogged)
                {
                    _groupLogged = true;
                    Plugin.Log.LogInfo($"The game's gun sound uses the mixer group '{g.name}' — not used (filtered group)");
                }
                return;
            }
            _group = g;
            if (!_groupLogged)
            {
                _groupLogged = true;
                Plugin.Log.LogInfo($"Sounds go to the game's mixer group '{g.name}'");
            }
        }
        catch (Exception e)
        {
            if (!_groupLogged)
            {
                _groupLogged = true;
                Plugin.Log.LogWarning("Mixer group not readable: " + e.Message);
            }
        }
    }

    public static float SoundDelay(Vector3 position)
    {
        var listener = View.Listener;
        return View.HasListener ? Mathf.Min(Vector3.Distance(position, listener) / SpeedOfSound, 30f) : 0f;
    }

    public static Voice Play(AudioClip clip, VoiceSpec s)
    {
        if (clip == null || s.Volume <= 0.0001f) return null;
        var v = Take() ?? Steal(Mathf.Clamp(s.Priority, 0, 256));
        if (v == null)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning($"No free voice for '{clip.name}'");
            return null;
        }
        try
        {
            var src = v.Src;
            src.clip = clip;
            src.loop = s.Loop;
            src.volume = Mathf.Clamp01(s.Volume);
            src.pitch = s.Pitch <= 0f ? 1f : s.Pitch;
            src.spatialBlend = s.Spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = Mathf.Max(0.5f, s.MinDistance);
            src.maxDistance = Mathf.Max(src.minDistance + 1f, s.MaxDistance <= 0f ? 2000f : s.MaxDistance);
            src.dopplerLevel = 0f;
            src.spread = s.Spread;
            src.priority = Mathf.Clamp(s.Priority, 0, 256);
            src.bypassReverbZones = false;
            try { Mixer.Set(src, Group()); }
            catch { }
            var t = v.Go.transform;
            if (s.Follow != null && !s.Follow.WasCollected)
            {
                t.SetParent(s.Follow, false);
                t.position = s.Spatial ? s.Position : s.Follow.position;
            }
            else
            {
                t.SetParent(_root.transform, false);
                t.position = s.Position;
            }
            if (s.StartTime > 0f && s.StartTime < clip.length) src.time = s.StartTime;
            if (s.Delay > 0.001f) src.PlayDelayed(s.Delay);
            else src.Play();
            v.Busy = true;
            v.Loop = s.Loop;
            v.Priority = src.priority;
            v.BaseVolume = src.volume;
            v.FadeStart = -1f;
            v.End = s.Loop ? float.MaxValue : Time.unscaledTime + s.Delay + (clip.length - Mathf.Max(0f, s.StartTime)) / src.pitch + 0.15f;
            return v;
        }
        catch (Exception e)
        {
            Release(v);
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Voice failed: " + e.Message);
            return null;
        }
    }

    public static void Stop(Voice v, float fade)
    {
        if (v == null || !v.Busy) return;
        if (fade <= 0.001f) { Release(v); return; }
        try
        {
            v.FadeStart = Time.unscaledTime;
            v.FadeLength = fade;
            v.FadeFrom = v.Src.volume;
            v.End = Time.unscaledTime + fade + 0.02f;
            v.Loop = false;
        }
        catch { Release(v); }
    }

    
    public static void Cut(Voice v, float after, float fade)
    {
        if (v == null || !v.Busy) return;
        try
        {
            v.FadeStart = Time.unscaledTime + Mathf.Max(0f, after);
            v.FadeLength = Mathf.Max(0.005f, fade);
            v.FadeFrom = v.Src.volume;
            v.End = v.FadeStart + v.FadeLength + 0.02f;
            v.Loop = false;
        }
        catch { Release(v); }
    }

    public static bool Alive(Voice v)
    {
        if (v == null || !v.Busy) return false;
        try { return v.Go != null && !v.Go.WasCollected && v.Go; }
        catch { return false; }
    }

    public static void SetVolume(Voice v, float volume)
    {
        if (!Alive(v) || v.FadeStart >= 0f) return;
        v.BaseVolume = Mathf.Clamp01(volume);
        v.Src.volume = v.BaseVolume;
    }

    public static void SetPitch(Voice v, float pitch)
    {
        if (!Alive(v)) return;
        v.Src.pitch = Mathf.Clamp(pitch, 0.3f, 3f);
    }

    public static void Tick()
    {
        var now = Time.unscaledTime;
        for (var i = Pool.Count - 1; i >= 0; i--)
        {
            var v = Pool[i];
            bool alive;
            try { alive = v.Go != null && !v.Go.WasCollected && v.Go; }
            catch { alive = false; }
            if (!alive) { Pool.RemoveAt(i); continue; } 
            if (!v.Busy) continue;
            try
            {
                if (v.FadeStart >= 0f)
                {
                    var k = Mathf.Clamp01((now - v.FadeStart) / Mathf.Max(0.001f, v.FadeLength));
                    v.Src.volume = v.FadeFrom * (1f - k);
                }
                if (now >= v.End) Release(v);
            }
            catch { Release(v); }
        }
    }

    
    public static void StopAll()
    {
        foreach (var v in Pool) if (v.Busy) Release(v);
    }

    private static Voice Take()
    {
        EnsureRoot();
        foreach (var v in Pool)
            if (!v.Busy) return v;
        if (Pool.Count >= MaxVoices) return null;
        try
        {
            var go = new GameObject("Sound Overhaul voice");
            go.transform.SetParent(_root.transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            var v = new Voice { Go = go, Src = src };
            Pool.Add(v);
            return v;
        }
        catch { return null; }
    }

    
    
    private static Voice Steal(int priority)
    {
        var best = -1;
        for (var i = 0; i < Pool.Count; i++)
        {
            var v = Pool[i];
            if (!v.Busy || v.Loop || v.Priority < priority) continue;
            if (best < 0) { best = i; continue; }
            var b = Pool[best];
            if (v.Priority > b.Priority || (v.Priority == b.Priority && v.End < b.End)) best = i;
        }
        if (best < 0) return null;
        var old = Pool[best];
        Release(old);
        var fresh = new Voice { Go = old.Go, Src = old.Src };
        Pool[best] = fresh;
        return fresh;
    }

    private static void Release(Voice v)
    {
        v.Busy = false;
        v.Loop = false;
        v.FadeStart = -1f;
        try
        {
            if (v.Src != null && !v.Src.WasCollected)
            {
                v.Src.Stop();
                v.Src.clip = null;
            }
            if (v.Go != null && !v.Go.WasCollected && _root != null) v.Go.transform.SetParent(_root.transform, false);
        }
        catch { }
    }

    private static void EnsureRoot()
    {
        if (_root != null && !_root.WasCollected && _root) return;
        _root = new GameObject("Sound Overhaul");
        Object.DontDestroyOnLoad(_root);
        Pool.Clear();
    }

    
    private static Object Group()
    {
        if (Mixer.Alive(_group)) return _group;
        if (Time.unscaledTime < _nextSearch) return null;
        _nextSearch = Time.unscaledTime + 5f;
        try
        {
            var counts = new Dictionary<IntPtr, (Object Group, int Count)>();
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<AudioSource>()))
            {
                var s = o?.TryCast<AudioSource>();
                var g = s != null ? Mixer.Get(s) : null;
                var ptr = Mixer.Native(g);
                if (ptr == IntPtr.Zero) continue;
                var n = (g.name ?? "").ToLowerInvariant();
                if (Excluded(n)) continue;
                counts[ptr] = counts.TryGetValue(ptr, out var c) ? (c.Group, c.Count + 1) : (g, 1);
            }
            var best = 0;
            foreach (var c in counts.Values)
                if (c.Count > best) { best = c.Count; _group = c.Group; }
            if (Mixer.Alive(_group) && !_fallbackLogged)
            {
                _fallbackLogged = true;
                Plugin.Log.LogInfo($"Sounds go to the game's mixer group '{_group.name}' (most used by the game's sound sources)");
            }
        }
        catch { }
        return Mixer.Alive(_group) ? _group : null;
    }

    
    
    private static bool Excluded(string n) =>
        n.Contains("music") || n.Contains("ui") || n.Contains("voice") || n.Contains("ambien") || n.Contains("menu") ||
        n.Contains("interior") || n.Contains("inside") || n.Contains("muffl") || n.Contains("distant") || n.Contains("far") ||
        n.Contains("low") || n.Contains("filter") || n.Contains("occlu");
}
