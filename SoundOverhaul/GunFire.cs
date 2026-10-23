using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles.Fires;
using Sprocket.WeaponFramework;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SoundOverhaul;








[HarmonyPatch(typeof(MuzzleFlashEffect), nameof(MuzzleFlashEffect.Setup))]
internal static class GunFire
{
    private enum Kind { Mg, Hmg, Autocannon, Medium, Large }

    private sealed class Set
    {
        public Kind Kind;
        public string Ext, Int, ExtTail, IntTail; 
        public string Single, SingleInt;          
        public float LoopRpm;
        public float IntPitch = 1f;
        public bool Heavy;                         
        public float Pitch = 1f;                   
        public float Punch;                        
    }

    private static readonly Set Large = new() { Kind = Kind.Large, Ext = "gun.large.ext.body", ExtTail = "gun.large.ext.tail", Int = "gun.large.int.west" };
    private static readonly Set LargeEast = new() { Kind = Kind.Large, Ext = "gun.large.ext.body", ExtTail = "gun.large.ext.tail", Int = "gun.large.int.east" };
    private static readonly Set Medium = new() { Kind = Kind.Medium, Ext = "gun.med.ext.body", ExtTail = "gun.med.ext.tail", Int = "gun.large.int.west" };
    
    
    private static readonly Set Ac20 = new()
    {
        Kind = Kind.Autocannon, Ext = "ac.m242.ext", Int = "ac.m242.int", ExtTail = "ac.m242.ext.tail", IntTail = "ac.m242.int.end",
        Pitch = 1.07f, IntPitch = 1.07f, Punch = 0.35f
    };
    private static readonly Set M242 = new() { Kind = Kind.Autocannon, Ext = "ac.m242.ext", Int = "ac.m242.int", ExtTail = "ac.m242.ext.tail", IntTail = "ac.m242.int.end", Punch = 0.2f };
    private static readonly Set A42 = new() { Kind = Kind.Autocannon, Ext = "ac.2a42.ext", Int = "ac.2a42.int", ExtTail = "ac.2a42.ext.tail", IntTail = "ac.int.end" };
    
    private static readonly Set AcMedium = new()
    {
        Kind = Kind.Autocannon, Ext = "gun.med.ext.body", Int = "gun.large.int.west", ExtTail = "gun.med.ext.tail", IntPitch = 1.15f, Heavy = true
    };

    private static readonly Set[] Mgs =
    {
        new() { Kind = Kind.Mg, Ext = "mg.pkt.ext", Int = "mg.pkt.int", ExtTail = "mg.pkt.ext.tail", IntTail = "mg.pkt.int.end", Single = "mg.single.ext", SingleInt = "mg.single.int", LoopRpm = 750f },
        new() { Kind = Kind.Mg, Ext = "mg.m240.ext", Int = "mg.m240.int", ExtTail = "mg.m240.ext.tail", IntTail = "mg.m240.int.end", Single = "mg.single.ext", SingleInt = "mg.single.int", LoopRpm = 750f }
    };

    private static readonly Set[] Hmgs =
    {
        new() { Kind = Kind.Hmg, Ext = "hmg.m2.ext", Int = "hmg.m2.int", ExtTail = "hmg.m2.ext.tail", IntTail = "hmg.m2.int.end", LoopRpm = 500f },
        new() { Kind = Kind.Hmg, Ext = "hmg.dshk.ext", Int = "hmg.dshk.int", ExtTail = "hmg.m2.ext.tail", IntTail = "hmg.dshk.int.end", LoopRpm = 600f },
        new() { Kind = Kind.Hmg, Ext = "hmg.nsvt.ext", Int = "hmg.nsvt.int", ExtTail = "hmg.m85.ext.tail", IntTail = "hmg.nsvt.int.end", LoopRpm = 700f },
        new() { Kind = Kind.Hmg, Ext = "hmg.m85.ext", Int = "hmg.m85.int", ExtTail = "hmg.m85.ext.tail", IntTail = "hmg.m85.int.end", LoopRpm = 1050f }
    };

    private static readonly Set Kpvt = new() { Kind = Kind.Hmg, Ext = "hmg.kpvt.ext", Int = "hmg.kpvt.int", ExtTail = "hmg.kpvt.ext.tail", IntTail = "hmg.kpvt.int.end", LoopRpm = 600f };

    private sealed class Gun
    {
        public Set Set;
        public Transform Muzzle;
        public Vector3 Position;
        public float Last = -100f, Interval = 1f;
        public int Calibre;
        public bool Burst, TailDue, Inside;
        public float Volume;
        public Voice Loop, LoopOuter; 
    }

    
    
    private static float Outer => Mathf.Clamp01(Plugin.InsideOuter.Value);

    
    private static float Delay(Gun g, bool own) => own ? 0f : Voices.SoundDelay(g.Position);

    private const float BurstWindow = 0.3f; 

    private static readonly Dictionary<IntPtr, Gun> Guns = new();
    private static int _errors;

    private static void Postfix(MuzzleFlashEffect __instance, CannonMuzzleFlashEffectInfo info, Transform parent, Vector3 position)
    {
        if (!Plugin.Active) return;
        try
        {
            var cal = (int)info.Calibre;
            var key = parent != null ? parent.Pointer : IntPtr.Zero;
            var set = Choose(cal, key, MunitionsBridge.AutoType(parent));
            if (!Library.Has(set.Ext)) return; 
            
            var src = __instance.audioSource;
            var vanilla = 1f;
            if (src != null)
            {
                if (src.volume > 0.05f) vanilla = Mathf.Clamp01(src.volume);
                src.Stop();
            }
            var far = __instance.longRangeAudioSource;
            if (far != null) far.Stop();
            Voices.UseGroupOf(src);
            Shot(key, set, parent, position, cal, vanilla);
        }
        catch (Exception e)
        {
            if (_errors++ < 10) Plugin.Log.LogError("Gun sound failed: " + e);
        }
    }

    
    private static Set Choose(int cal, IntPtr key, int auto)
    {
        if (auto == MunitionsBridge.Autocannon && cal >= 40) return AcMedium;
        var h = (int)((key.ToInt64() >> 4) & 0x7FFFFFFF);
        if (cal < 10) return Mgs[h % Mgs.Length];
        if (cal < 14) return Hmgs[h % Hmgs.Length];
        if (cal < 16) return Kpvt;
        if (cal < 23) return Ac20;
        if (cal < 28) return M242;
        if (cal < 40) return A42;
        if (cal < 90) return Medium;
        return cal >= 115 ? LargeEast : Large;
    }

    private static void Shot(IntPtr key, Set set, Transform muzzle, Vector3 pos, int cal, float vanilla)
    {
        if (!Guns.TryGetValue(key, out var g) || g.Set != set)
        {
            if (g?.Loop != null) Voices.Stop(g.Loop, 0.03f);
            if (g?.LoopOuter != null) Voices.Stop(g.LoopOuter, 0.03f);
            g = new Gun { Set = set };
            Guns[key] = g;
            if (Guns.Count > 256) Cleanup();
        }
        var now = Time.time;
        var dt = now - g.Last;
        g.Last = now;
        g.Interval = dt < BurstWindow ? Mathf.Lerp(g.Interval, dt, 0.35f) : dt;
        g.Muzzle = muzzle;
        g.Position = pos;
        g.Calibre = cal;
        g.Inside = View.Inside(muzzle);
        g.Volume = vanilla * (g.Inside ? Plugin.InsideVolume.Value : Plugin.WeaponsVolume.Value);
        var own = View.OwnVehicle(muzzle);

        switch (set.Kind)
        {
            case Kind.Large:
            case Kind.Medium:
                Cannon(g, set, cal, own);
                break;
            case Kind.Autocannon:
                Autocannon(g, set, own);
                break;
            default:
                MachineGun(g, set, dt, own);
                break;
        }
    }

    private static float CannonPitch(Kind kind, int cal) => kind == Kind.Large
        ? Mathf.Clamp(1f + (120f - cal) / 400f, 0.92f, 1.08f) * Random.Range(0.97f, 1.03f)
        : Mathf.Clamp(1f + (65f - cal) / 300f, 0.92f, 1.1f) * Random.Range(0.97f, 1.03f);

    private static void Cannon(Gun g, Set set, int cal, bool own)
    {
        var pitch = CannonPitch(set.Kind, cal);
        var large = set.Kind == Kind.Large;
        if (g.Inside)
        {
            var pitchIn = set.Kind == Kind.Medium ? pitch * 1.12f : pitch;
            Play(set.Int, VoiceSpec.Flat(g.Volume), pitchIn, 0);
            
            Play(set.Ext, VoiceSpec.Flat(g.Volume * Outer), pitch, 2);
            
            Play(set.ExtTail, Ext(g, 0.3f + 0.3f * Outer, large ? 80f : 60f, 9000f, 0f), pitch, 8);
            return;
        }
        var delay = Delay(g, own);
        Play(set.Ext, Ext(g, 1f, large ? 25f : 15f, large ? 2500f : 2000f, delay), pitch, own ? 0 : 24);
        Play(set.ExtTail, Ext(g, 0.9f, large ? 90f : 60f, large ? 9000f : 7000f, delay), pitch, own ? 4 : 40);
    }

    private static void Autocannon(Gun g, Set set, bool own)
    {
        var pitch = Random.Range(0.97f, 1.03f) * set.Pitch;
        if (g.Inside)
        {
            Play(set.Int, VoiceSpec.Flat(g.Volume), pitch * set.IntPitch / set.Pitch, 8);
            Play(set.Ext, VoiceSpec.Flat(g.Volume * Outer * 0.8f), pitch, 10);
            if (set.Punch > 0f) Play("gun.large.int.west", VoiceSpec.Flat(g.Volume * set.Punch * 0.6f), pitch * 1.5f, 12);
        }
        else
        {
            var delay = Delay(g, own);
            Play(set.Ext, Ext(g, 1f, set.Heavy ? 15f : 8f, set.Heavy ? 2000f : 1500f, delay), pitch, own ? 8 : 48);
            if (set.Punch > 0f) Play("gun.med.ext.body", Ext(g, set.Punch, 10f, 1500f, delay), pitch * 1.4f, own ? 10 : 52);
        }
        g.TailDue = true; 
    }

    private static void MachineGun(Gun g, Set set, float dt, bool own)
    {
        var rapid = dt < BurstWindow;
        if (!rapid || g.Set.Ext == null)
        {
            
            if (set.Kind == Kind.Mg)
            {
                var p = Random.Range(0.97f, 1.03f);
                if (g.Inside)
                {
                    Play(set.SingleInt, VoiceSpec.Flat(g.Volume), p, 16);
                    Play(set.Single, VoiceSpec.Flat(g.Volume * Outer * 0.8f), p, 18);
                }
                else Play(set.Single, Ext(g, 1f, 5f, 900f, Delay(g, own)), p, own ? 16 : 64);
            }
            else
            {
                
                var cycle = 60f / Mathf.Max(100f, set.LoopRpm);
                var spec = g.Inside ? VoiceSpec.Flat(g.Volume) : Ext(g, 1f, 8f, 1200f, Delay(g, own));
                var v = Play(g.Inside ? set.Int : set.Ext, spec, 1f, own ? 16 : 56);
                if (v != null) Voices.Cut(v, spec.Delay + cycle, 0.04f);
                if (g.Inside)
                {
                    var outer = Play(set.Ext, VoiceSpec.Flat(g.Volume * Outer * 0.8f), 1f, 18);
                    if (outer != null) Voices.Cut(outer, cycle, 0.04f);
                }
            }
            g.TailDue = true;
            g.Burst = false;
            return;
        }
        
        var rpm = 60f / Mathf.Max(0.02f, g.Interval);
        var pitch = Mathf.Clamp(rpm / Mathf.Max(100f, set.LoopRpm), 0.85f, 1.2f);
        if (!Voices.Alive(g.Loop))
        {
            var spec = g.Inside ? VoiceSpec.Flat(g.Volume) : Ext(g, 1f, set.Kind == Kind.Mg ? 5f : 8f, set.Kind == Kind.Mg ? 900f : 1200f, 0f);
            spec.Loop = true;
            spec.Follow = g.Muzzle;
            var clip = Library.Pick(g.Inside ? set.Int : set.Ext);
            if (clip != null) spec.StartTime = Random.Range(0f, clip.length * 0.8f);
            g.Loop = Voices.Play(clip, WithGain(spec, g.Inside ? set.Int : set.Ext, pitch, own ? 8 : 48));
            if (g.Inside && Outer > 0.01f && !Voices.Alive(g.LoopOuter))
            {
                var outerSpec = VoiceSpec.Flat(g.Volume * Outer * 0.8f);
                outerSpec.Loop = true;
                outerSpec.Follow = g.Muzzle;
                var outerClip = Library.Pick(set.Ext);
                if (outerClip != null) outerSpec.StartTime = Random.Range(0f, outerClip.length * 0.8f);
                g.LoopOuter = Voices.Play(outerClip, WithGain(outerSpec, set.Ext, pitch, 10));
            }
        }
        else
        {
            Voices.SetPitch(g.Loop, pitch);
            Voices.SetPitch(g.LoopOuter, pitch);
        }
        g.Burst = true;
        g.TailDue = true;
    }

    
    public static void Tick()
    {
        if (Guns.Count == 0) return;
        var now = Time.time;
        foreach (var g in Guns.Values)
        {
            if (!g.TailDue) continue;
            var gap = g.Set.Kind == Kind.Autocannon || g.Burst ? Mathf.Max(0.12f, g.Interval * 1.6f) : BurstWindow;
            if (now - g.Last < gap) continue;
            g.TailDue = false;
            g.Burst = false;
            try
            {
                if (g.Loop != null) { Voices.Stop(g.Loop, 0.03f); g.Loop = null; }
                if (g.LoopOuter != null) { Voices.Stop(g.LoopOuter, 0.03f); g.LoopOuter = null; }
                if (!Plugin.Active) continue;
                var own = View.OwnVehicle(g.Muzzle);
                if (g.Inside)
                {
                    Play(g.Set.IntTail ?? g.Set.ExtTail, VoiceSpec.Flat(g.Volume * (g.Set.IntTail != null ? 1f : 0.35f)), 1f, 16);
                    if (g.Set.IntTail != null) Play(g.Set.ExtTail, VoiceSpec.Flat(g.Volume * Outer * 0.5f), 1f, 20);
                }
                else Play(g.Set.ExtTail, Ext(g, 0.85f, g.Set.Kind == Kind.Mg ? 25f : 40f, 5000f, Delay(g, own)), 1f, own ? 16 : 64);
            }
            catch { }
        }
    }

    public static void StopAll()
    {
        foreach (var g in Guns.Values)
        {
            if (g.Loop != null) Voices.Stop(g.Loop, 0.05f);
            if (g.LoopOuter != null) Voices.Stop(g.LoopOuter, 0.05f);
            g.Loop = null;
            g.LoopOuter = null;
            g.Burst = false;
            g.TailDue = false;
        }
    }

    private static void Cleanup()
    {
        var now = Time.time;
        var dead = new List<IntPtr>();
        foreach (var kv in Guns) if (now - kv.Value.Last > 30f && !kv.Value.TailDue) dead.Add(kv.Key);
        foreach (var k in dead) Guns.Remove(k);
    }

    private static VoiceSpec Ext(Gun g, float volume, float minDistance, float maxDistance, float delay)
    {
        var s = VoiceSpec.At(g.Position, g.Volume * volume, minDistance, maxDistance);
        s.Delay = delay < 0.03f ? 0f : delay;
        s.Spread = 40f;
        return s;
    }

    private static VoiceSpec WithGain(VoiceSpec s, string bank, float pitch, int priority)
    {
        s.Volume *= Library.Gain(bank);
        s.Pitch = pitch;
        s.Priority = priority;
        return s;
    }

    private static Voice Play(string bank, VoiceSpec s, float pitch, int priority)
    {
        if (bank == null) return null;
        var clip = Library.Pick(bank);
        if (clip == null) return null;
        return Voices.Play(clip, WithGain(s, bank, pitch, priority));
    }
}
