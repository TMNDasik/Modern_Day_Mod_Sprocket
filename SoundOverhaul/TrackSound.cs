using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.ContinuousTracks;
using Sprocket.Vehicles.Tracks;
using UnityEngine;

namespace SoundOverhaul;



[HarmonyPatch(typeof(TrackAudioController), nameof(TrackAudioController.UpdateAudio))]
internal static class TrackSound
{
    private sealed class Side
    {
        public LayerSet Set;
        public AudioSource[] Vanilla;
        public string Class;
    }

    
    private static readonly float[] StageSpeed = { 2.5f, 6f, 11f };
    private static readonly string[] StageId = { "33", "66", "99" };

    private const int SourcesField = 0x18, MinPriorityField = 0x10;
    private static readonly Dictionary<IntPtr, Side> Sides = new();
    private static int _errors;

    private static unsafe void Prefix(TrackAudioController __instance, float volumeMultiplier, float distanceFromListener)
    {
        try
        {
            var self = (byte*)__instance.Pointer;
            var arr = *(IntPtr*)(self + SourcesField);
            if (arr == IntPtr.Zero) return;
            var len = *(int*)((byte*)arr + 0x18);
            if (len <= 0 || len > 16) return;
            var minPriority = *(float*)(self + MinPriorityField);
            for (var i = 0; i < len; i++)
            {
                var elem = (byte*)arr + 0x20 + i * 16;   
                var audio = *(IntPtr*)elem;
                var source = *(IntPtr*)(elem + 8);
                if (audio == IntPtr.Zero || source == IntPtr.Zero) continue;
                if (!Plugin.Active) { Release(audio); continue; }
                UpdateSide(audio, source, i == 0, volumeMultiplier, distanceFromListener, minPriority);
            }
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Track sound failed: " + e);
        }
    }

    private static void Postfix() => VanillaMute.Demote();

    private static void UpdateSide(IntPtr audioPtr, IntPtr sourcePtr, bool primary, float volumeMultiplier, float distance, float minPriority)
    {
        if (!Sides.TryGetValue(audioPtr, out var side) || side.Set == null || !side.Set.Alive)
        {
            var blender = new MultiClipAudioSourceBlender(audioPtr);
            var a = blender.aChannel;
            if (a == null) return;
            var created = LayerSet.Create(a, "track", 4f);
            if (created == null) return;
            var vanilla = new List<AudioSource> { a };
            if (blender.bChannel != null) vanilla.Add(blender.bChannel);
            side = new Side { Set = created, Vanilla = vanilla.ToArray(), Class = ClassOf(a) };
            Sides[audioPtr] = side;
            if (Sides.Count > 512) Cleanup();
        }
        if (!side.Set.HasAny($"trk.{side.Class}.33", "trk.heavy.33"))
        {
            foreach (var v in side.Vanilla) VanillaMute.Unmute(v);
            return;
        }
        foreach (var v in side.Vanilla) VanillaMute.Mute(v);

        var track = new ITrackEffectsSource(sourcePtr);
        var speed = Mathf.Abs(track.TrackVelocity);
        var broken = track.Broken;
        var contact = track.ContactState;
        var set = side.Set;
        set.Flat = View.Inside(set.Template);
        set.Priority = LayerSet.PriorityFor(distance, minPriority);

        var level = Mathf.Max(0f, Plugin.TrackVolume.Value) * Mathf.Max(0f, volumeMultiplier);
        
        if (broken || WheelsBridge.HiddenTrack(sourcePtr)) level = 0f;
        else if (contact == ContactState.NoContact) level *= 0.35f;
        else if (contact == ContactState.PartialContact) level *= 0.75f;
        if (set.Flat) level *= 0.6f;
        
        level *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1.6f, speed));

        if (level > 0.0005f)
        {
            var cls = side.Class;
            var k = 0;
            while (k < StageSpeed.Length - 2 && speed > StageSpeed[k + 1]) k++;
            var t = Mathf.Clamp01((speed - StageSpeed[k]) / (StageSpeed[k + 1] - StageSpeed[k]));
            if (speed < StageSpeed[0]) t = 0f;
            Layer(set, $"trk.{cls}.{StageId[k]}", level * Mathf.Cos(t * Mathf.PI * 0.5f), speed / StageSpeed[k]);
            Layer(set, $"trk.{cls}.{StageId[k + 1]}", level * Mathf.Sin(t * Mathf.PI * 0.5f), speed / StageSpeed[k + 1]);
            
            set.Want("trk.squeak", level * 0.3f * (1f - 0.5f * Mathf.InverseLerp(3f, 10f, speed)), Mathf.Clamp(0.85f + speed * 0.03f, 0.85f, 1.2f));
            
            if (set.Flat && primary)
            {
                var inner = Mathf.Max(0f, Plugin.TrackVolume.Value) * Mathf.Max(0f, volumeMultiplier) *
                            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 8f, speed));
                set.Want("veh.int.rattle", inner * 0.5f, 1f);
                set.Want("veh.int.rumble", inner * 0.45f, Mathf.Clamp(0.9f + speed * 0.02f, 0.9f, 1.15f));
            }
        }
        set.Apply(3f, 3f);
    }

    private static void Layer(LayerSet set, string bank, float volume, float pitchRatio)
    {
        if (volume < 0.0005f) return;
        set.Want(bank, volume, Mathf.Clamp(0.85f + 0.15f * pitchRatio, 0.8f, 1.2f));
    }

    
    private static string ClassOf(AudioSource src)
    {
        try
        {
            var rb = src.GetComponentInParent<Rigidbody>();
            var mass = rb != null ? rb.mass : 40000f;
            return mass >= 30000f ? "heavy" : mass >= 14000f ? "med" : "light";
        }
        catch { return "heavy"; }
    }

    private static void Release(IntPtr key)
    {
        if (!Sides.TryGetValue(key, out var side)) return;
        foreach (var v in side.Vanilla) VanillaMute.Unmute(v);
        side.Set?.Destroy();
        Sides.Remove(key);
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in Sides) if (kv.Value.Set == null || !kv.Value.Set.Alive) dead.Add(kv.Key);
        foreach (var k in dead) Sides.Remove(k);
    }

    public static void ReleaseAll()
    {
        foreach (var k in new List<IntPtr>(Sides.Keys)) Release(k);
    }
}
