using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace SoundOverhaul;




internal static class DriveSound
{
    private sealed class Drive
    {
        public LayerSet Set;
        public AudioSource[] Vanilla;
        public bool Heavy;
        public bool Moving;
        public float Speed;          
        public float PeakSpeed;      
    }

    private static readonly Dictionary<IntPtr, Drive> Drives = new();
    private static int _errors;

    

    [HarmonyPatch(typeof(TurretAudioController), nameof(TurretAudioController.UpdateAudio))]
    private static class TurretPatch
    {
        private static void Prefix(TurretAudioController __instance, float volumeMultiplier, float distanceFromListener)
        {
            var key = __instance.Pointer;
            try
            {
                if (!Plugin.Active) { Release(key); return; }
                var src = __instance.source;
                if (src == null) return;
                var d = Get(key, src, "turret", 5f, src);
                if (d == null) return;
                var bankOk = d.Set.HasAny(d.Heavy ? "tur.mbt.motor" : "tur.ifv.lo");
                if (!bankOk) { foreach (var v in d.Vanilla) VanillaMute.Unmute(v); return; }
                foreach (var v in d.Vanilla) VanillaMute.Mute(v);

                __instance.turret.GetAudioInfo(out var info);
                var max = Mathf.Max(0.0001f, info.MaxTraverseRate);
                var frac = Mathf.Clamp01(Mathf.Abs(info.AngularVelocity) / max);
                Update(d, frac, 0.04f, volumeMultiplier, distanceFromListener, __instance.MinPriorityDistance, true);
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Plugin.Log.LogError("Turret sound failed: " + e);
            }
        }

        private static void Postfix() => VanillaMute.Demote();
    }

    

    [HarmonyPatch(typeof(LayingDriveAudioController), nameof(LayingDriveAudioController.UpdateAudio))]
    private static class LayingPatch
    {
        private static void Prefix(LayingDriveAudioController __instance, float volumeMultiplier, float distanceFromListener)
        {
            try
            {
                var drive = __instance.drive;
                if (drive == null) return;
                var elev = __instance.elevation;
                if (elev?.source != null)
                {
                    drive.GetElevationAudioInfo(out var info);
                    Axis(elev.Pointer, elev.source, info, volumeMultiplier, distanceFromListener, __instance.MinPriorityDistance);
                }
                var az = __instance.azimuth;
                if (az?.source != null)
                {
                    drive.GetAzimuthAudioInfo(out var info);
                    Axis(az.Pointer, az.source, info, volumeMultiplier, distanceFromListener, __instance.MinPriorityDistance);
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Plugin.Log.LogError("Gun drive sound failed: " + e);
            }
        }

        private static void Postfix() => VanillaMute.Demote();
    }

    private static void Axis(IntPtr key, AudioSource src, RotationAxisAudioInfo info, float volumeMultiplier, float distance, float minPriority)
    {
        if (!Plugin.Active) { Release(key); return; }
        var d = Get(key, src, "gun drive", 3f, src);
        if (d == null) return;
        if (!d.Set.HasAny("drv.loop")) { foreach (var v in d.Vanilla) VanillaMute.Unmute(v); return; }
        foreach (var v in d.Vanilla) VanillaMute.Mute(v);
        var max = Mathf.Max(0.0001f, info.MaxSpeed);
        var frac = Mathf.Clamp01(Mathf.Abs(info.AngularVelocity) / max);
        Update(d, frac, 0.05f, volumeMultiplier, distance, minPriority, false);
    }

    private static Drive Get(IntPtr key, AudioSource template, string name, float minDistance, params AudioSource[] vanilla)
    {
        if (Drives.TryGetValue(key, out var d) && d.Set != null && d.Set.Alive) return d;
        var set = LayerSet.Create(template, name, minDistance);
        if (set == null) return null;
        var heavy = true;
        try
        {
            var rb = template.GetComponentInParent<Rigidbody>();
            heavy = rb == null || rb.mass >= 30000f;
        }
        catch { }
        d = new Drive { Set = set, Vanilla = vanilla, Heavy = heavy };
        Drives[key] = d;
        if (Drives.Count > 512) Cleanup();
        return d;
    }

    private static void Update(Drive d, float frac, float threshold, float volumeMultiplier, float distance, float minPriority, bool turret)
    {
        var dt = Mathf.Max(0f, Time.deltaTime);
        var set = d.Set;
        set.Flat = View.Inside(set.Template);
        set.Priority = LayerSet.PriorityFor(distance, minPriority);
        d.Speed = Mathf.Lerp(d.Speed, frac, 1f - Mathf.Exp(-dt * 12f));
        var level = Mathf.Max(0f, Plugin.DriveVolume.Value) * Mathf.Max(0f, volumeMultiplier) * (set.Flat ? 1f : 0.7f);

        var moving = frac > threshold;
        if (moving && !d.Moving)
        {
            set.OneShot(turret ? (d.Heavy ? "tur.mbt.release" : "tur.ifv.release") : "drv.release", level * 0.8f);
            d.PeakSpeed = 0f;
        }
        else if (!moving && d.Moving && d.PeakSpeed > 0.15f)
        {
            var hard = d.PeakSpeed > 0.6f;
            var bank = turret ? (d.Heavy ? (hard ? "tur.mbt.brake.heavy" : "tur.mbt.brake") : "tur.ifv.brake") : null;
            if (bank != null) set.OneShot(bank, level * Mathf.Lerp(0.5f, 1f, d.PeakSpeed));
        }
        d.Moving = moving;
        if (moving) d.PeakSpeed = Mathf.Max(d.PeakSpeed, frac);

        if (d.Speed > 0.01f || moving)
        {
            var s = d.Speed;
            if (!turret)
                set.Want("drv.loop", level * (0.35f + 0.65f * s), 0.85f + 0.35f * s);
            else if (d.Heavy)
            {
                set.Want("tur.mbt.motor", level * (0.4f + 0.6f * s), 0.8f + 0.35f * s);
                set.Want("tur.teeth", level * 0.3f * s, 0.9f + 0.2f * s);
            }
            else
            {
                set.Want("tur.ifv.lo", level * (0.45f + 0.55f * s) * (1f - s), 0.9f + 0.2f * s);
                set.Want("tur.ifv.hi", level * (0.45f + 0.55f * s) * s, 0.9f + 0.15f * s);
            }
        }
        set.Apply(8f, 5f);
    }

    private static void Release(IntPtr key)
    {
        if (!Drives.TryGetValue(key, out var d)) return;
        foreach (var v in d.Vanilla) VanillaMute.Unmute(v);
        d.Set?.Destroy();
        Drives.Remove(key);
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in Drives) if (kv.Value.Set == null || !kv.Value.Set.Alive) dead.Add(kv.Key);
        foreach (var k in dead) Drives.Remove(k);
    }

    public static void ReleaseAll()
    {
        foreach (var k in new List<IntPtr>(Drives.Keys)) Release(k);
    }
}
