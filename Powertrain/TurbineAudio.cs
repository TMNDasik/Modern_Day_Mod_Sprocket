using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles.Powertrains;
using UnityEngine;

namespace Powertrain;







[HarmonyPatch(typeof(EngineAudioController), nameof(EngineAudioController.UpdateAudio))]
internal static class TurbineAudio
{
    private sealed class Voice
    {
        public AudioSource Loop, StartSrc, StopSrc;
        public float LoopGain, StartGain, StopGain;
        public int SeenVersion = -1;
        public float LoopPitch = 1f, StopPitchFrom = 1f;
        public float LoopFade = 0.15f;
        public int LastFrame;
    }

    private const float IdleLevel = 0.62f;
    
    private const float Loudness = 1.3f;
    private const float StopCrossfade = 0.35f;
    private const float OneShotSlew = 0.08f;

    
    private const int MinPriorityField = 0x10, EngineField = 0x18, ChannelA = 0x30, SourceField = 0x10;

    private static readonly Dictionary<IntPtr, Voice> Voices = new();
    private static readonly System.Random Rng = new();
    private static int _errors;

    private static unsafe bool Prefix(EngineAudioController __instance, float volumeMultiplier, float distanceFromListener)
    {
        try
        {
            
            var c = (byte*)__instance.Pointer;
            var t = Turbines.Get(*(IntPtr*)(c + EngineField));
            if (t == null) return true;
            if (!Sounds.Ensure()) return true;

            var key = __instance.Pointer;
            if (!Voices.TryGetValue(key, out var v) || !Valid(v, c))
            {
                if (Voices.Count > 128) Voices.Clear();
                v = Create(__instance);
                if (v == null) return true;
                Voices[key] = v;
            }
            v.LastFrame = Time.frameCount;
            Update(v, t, volumeMultiplier, distanceFromListener, *(float*)(c + MinPriorityField));
            return false;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Gas turbine sound failed: " + e);
            return true;
        }
    }

    private static unsafe bool Valid(Voice v, byte* controller)
    {
        if (v.Loop == null || v.Loop.WasCollected || v.StartSrc == null || v.StartSrc.WasCollected || v.StopSrc == null || v.StopSrc.WasCollected)
            return false;
        var channel = *(IntPtr*)(controller + ChannelA);
        return channel != IntPtr.Zero && *(IntPtr*)((byte*)channel + SourceField) == v.Loop.Pointer;
    }

    private static Voice Create(EngineAudioController c)
    {
        var a = c.aChannel?.source;
        var b = c.bChannel?.source;
        var cc = c.cChannel?.source;
        if (a == null || b == null || cc == null) return null;
        foreach (var s in new[] { a, b, cc })
        {
            s.Stop();
            s.volume = 0f;
        }
        a.clip = Sounds.Idle;
        a.loop = true;
        cc.clip = Sounds.Start;
        cc.loop = false;
        b.clip = Sounds.Stop;
        b.loop = false;
        return new Voice { Loop = a, StartSrc = cc, StopSrc = b };
    }

    private static float SmoothStep(float a, float b, float x)
    {
        var u = Mathf.Clamp01((x - a) / (b - a));
        return u * u * (3f - 2f * u);
    }

    private static void Update(Voice v, Turbine t, float volumeMultiplier, float distance, float minPriorityDistance)
    {
        var now = Time.time;
        var dt = Mathf.Max(0f, Time.deltaTime);
        var s = t.Spec;

        
        if (t.Version != v.SeenVersion)
        {
            var first = v.SeenVersion < 0;
            v.SeenVersion = t.Version;
            switch (t.Phase)
            {
                case TurbinePhase.Starting:
                    PlayFromStart(v.StartSrc, Sounds.Start);
                    v.LoopFade = 0.15f;
                    break;
                case TurbinePhase.Stopping:
                    
                    v.StopPitchFrom = v.LoopPitch * Sounds.StopHeadPitch;
                    PlayFromStart(v.StopSrc, Sounds.Stop);
                    v.LoopFade = 0.15f;
                    break;
                case TurbinePhase.Running:
                    
                    v.LoopFade = first ? 0.8f : 0.3f;
                    break;
            }
        }

        var tau = now - t.PhaseStart;
        float loopT = 0f, startT = 0f, stopT = 0f;
        switch (t.Phase)
        {
            case TurbinePhase.Starting:
            {
                var xs = Sounds.StartLength - Sounds.StartCrossfade;
                var u = Mathf.Clamp01((tau - xs) / Sounds.StartCrossfade);
                loopT = Mathf.Sin(u * Mathf.PI * 0.5f);
                startT = Mathf.Cos(u * Mathf.PI * 0.5f);
                break;
            }
            case TurbinePhase.Running:
                loopT = 1f;
                break;
            case TurbinePhase.Stopping:
                loopT = Mathf.Cos(Mathf.Clamp01(tau / StopCrossfade) * Mathf.PI * 0.5f);
                stopT = 1f;
                break;
        }

        
        var loopSlew = t.Phase == TurbinePhase.Starting ? 1f / 0.05f : 1f / Mathf.Max(0.05f, v.LoopFade);
        v.LoopGain = Mathf.MoveTowards(v.LoopGain, loopT, loopSlew * dt);
        if (t.Phase == TurbinePhase.Starting) v.StartGain = startT;
        else v.StartGain = Mathf.MoveTowards(v.StartGain, startT, dt / OneShotSlew);
        v.StopGain = Mathf.MoveTowards(v.StopGain, stopT, dt / OneShotSlew);

        
        var rpmFrac = Mathf.Clamp01((t.Rpm - s.Idle) / Mathf.Max(100f, s.Safe - s.Idle));
        var load = t.Phase == TurbinePhase.Running || t.Phase == TurbinePhase.Starting ? 0.75f * t.N1 + 0.25f * rpmFrac : 0f;
        var pitch = 1f + (Mathf.Max(0.5f, Plugin.FullPowerPitch.Value) - 1f) * load;
        
        
        pitch *= Sounds.StartSettle(now - (t.StartedAt + Sounds.StartLength));
        v.LoopPitch = pitch;

        var level = Mathf.Max(0f, Plugin.Volume.Value) * Mathf.Max(0f, volumeMultiplier) * Loudness;
        v.Loop.volume = Mathf.Clamp01(level * (IdleLevel + (1f - IdleLevel) * load) * v.LoopGain);
        v.Loop.pitch = pitch;
        v.StartSrc.volume = Mathf.Clamp01(level * IdleLevel * v.StartGain);
        v.StartSrc.pitch = 1f;
        v.StopSrc.volume = Mathf.Clamp01(level * IdleLevel * v.StopGain);
        v.StopSrc.pitch = Mathf.Lerp(v.StopPitchFrom, 1f, SmoothStep(0f, Sounds.StopPitchGlide, t.Phase == TurbinePhase.Stopping ? tau : 100f));

        
        if (v.LoopGain > 0.0005f || loopT > 0f)
        {
            if (v.Loop.isActiveAndEnabled && !v.Loop.isPlaying)
            {
                v.Loop.clip = Sounds.Idle;
                try
                {
                    var n = v.Loop.clip.samples;
                    if (n > 0) v.Loop.timeSamples = Rng.Next(0, n);
                }
                catch { }
                v.Loop.Play();
            }
        }
        else if (v.Loop.isPlaying) v.Loop.Stop();
        if (v.StartGain <= 0.0005f && startT <= 0f && v.StartSrc.isPlaying) v.StartSrc.Stop();
        if (v.StopGain <= 0.0005f && stopT <= 0f && v.StopSrc.isPlaying) v.StopSrc.Stop();

        
        var pr = minPriorityDistance > 0f ? Mathf.Clamp01(distance / minPriorityDistance) : 0f;
        var priority = (int)(pr * pr * 255f);
        v.Loop.priority = priority;
        v.StartSrc.priority = priority;
        v.StopSrc.priority = priority;
    }

    private static void PlayFromStart(AudioSource src, AudioClip clip)
    {
        if (src == null || clip == null || !src.isActiveAndEnabled) return;
        src.Stop();
        src.clip = clip;
        src.loop = false;
        src.timeSamples = 0;
        src.Play();
    }
}
