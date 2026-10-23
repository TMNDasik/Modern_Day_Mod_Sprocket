using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SoundOverhaul;





internal static class Loading
{
    private const int MinCalibre = 37;
    private const float MinTime = 1.5f;
    private const float InsertLead = 2.8f; 

    private sealed class Job
    {
        public Transform At;
        public float InsertAt, RackCloseAt;
        public bool Inserted, RackClosed, Rack;
    }

    private static readonly Dictionary<IntPtr, Job> Jobs = new();
    private static readonly HashSet<IntPtr> Fired = new();

    [HarmonyPatch(typeof(CannonBehaviour), nameof(CannonBehaviour.FireInternal))]
    private static class FirePatch
    {
        private static void Postfix(CannonBehaviour __instance)
        {
            try { if (__instance.caliber >= MinCalibre) Fired.Add(__instance.Pointer); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LoadTask), nameof(LoadTask.SetState))]
    private static class StatePatch
    {
        private static void Postfix(LoadTask __instance)
        {
            try
            {
                var target = __instance.target;
                if (target == null) return;
                var ptr = target.Pointer;
                var state = (int)__instance.state;
                if (state == (int)LoadState.Loading) Begin(ptr);
                else if (state == (int)LoadState.Loaded) Finish(ptr);
                else Jobs.Remove(ptr);
            }
            catch { }
        }
    }

    private static void Begin(IntPtr ptr)
    {
        if (!Plugin.Active) return;
        var wb = new WeaponBehaviour(ptr);
        var cannon = wb.TryCast<CannonBehaviour>();
        if (cannon == null || cannon.caliber < MinCalibre) return;
        var time = wb.RemainingLoadTime;
        if (time < MinTime || float.IsInfinity(time) || float.IsNaN(time)) return;
        var at = wb.transform;
        var now = Time.time;
        var job = new Job
        {
            At = at,
            InsertAt = now + Mathf.Max(0.6f, time - InsertLead),
            Rack = time > 4.5f
        };
        job.RackCloseAt = job.InsertAt - 0.6f;
        Jobs[ptr] = job;
        if (Fired.Remove(ptr)) Play("load.breech.open", at, 1f);
        if (job.Rack) Play("load.rack.open", at, 0.8f, 0.35f);
    }

    private static void Finish(IntPtr ptr)
    {
        if (!Jobs.TryGetValue(ptr, out var job)) return;
        Jobs.Remove(ptr);
        if (!Plugin.Active) return;
        if (!job.Inserted) Play("load.shell.insert", job.At, 0.7f); 
        Play("load.breech.close", job.At, 1f);
    }

    public static void Tick()
    {
        if (Jobs.Count == 0) return;
        var now = Time.time;
        List<IntPtr> dead = null;
        foreach (var kv in Jobs)
        {
            var job = kv.Value;
            try
            {
                if (job.At == null || job.At.WasCollected) { (dead ??= new List<IntPtr>()).Add(kv.Key); continue; }
                if (!Plugin.Active) continue;
                if (job.Rack && !job.RackClosed && now >= job.RackCloseAt)
                {
                    job.RackClosed = true;
                    Play("load.rack.close", job.At, 0.8f);
                }
                if (!job.Inserted && now >= job.InsertAt)
                {
                    job.Inserted = true;
                    Play("load.shell.insert", job.At, 1f);
                }
            }
            catch { (dead ??= new List<IntPtr>()).Add(kv.Key); }
        }
        if (dead != null) foreach (var k in dead) Jobs.Remove(k);
    }

    
    
    private static void Play(string bank, Transform at, float volume, float delay = 0f)
    {
        var clip = Library.Pick(bank);
        if (clip == null || at == null) return;
        var inside = View.Inside(at);
        var own = !inside && View.OwnVehicle(at);
        var vol = volume * Library.Gain(bank) * Plugin.LoadingVolume.Value;
        VoiceSpec s;
        if (inside) s = VoiceSpec.Flat(vol);
        else
        {
            s = own ? VoiceSpec.At(at.position, vol * 0.8f, 7f, 80f) : VoiceSpec.At(at.position, vol * 0.45f, 3f, 60f);
            s.Follow = at;
        }
        s.Delay = delay;
        s.Pitch = Random.Range(0.97f, 1.03f);
        s.Priority = inside ? 20 : own ? 36 : 80;
        Voices.Play(clip, s);
    }
}
