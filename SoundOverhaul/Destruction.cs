using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Gameplay;
using Sprocket.Landmines;
using Sprocket.TreeDestruction;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.PartDecoupling;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SoundOverhaul;








internal static class Destruction
{
    

    private sealed class Fire
    {
        public FireEffect Fx;
        public AudioSource Vanilla;
        public LayerSet Set;
        public float Level, NextFlare;
    }

    private static readonly Dictionary<IntPtr, Fire> Fires = new();
    private static int _errors;

    [HarmonyPatch(typeof(FireEffect), nameof(FireEffect.PlayEffect))]
    private static class FirePatch
    {
        private static void Postfix(FireEffect __instance)
        {
            if (!Plugin.Active || !Library.Has("fire.big")) return;
            try
            {
                var src = __instance.audioSource;
                if (src == null) return;
                var key = __instance.Pointer;
                if (!Fires.TryGetValue(key, out var f) || f.Set == null || !f.Set.Alive)
                {
                    var set = LayerSet.Create(src, "fire", 6f);
                    if (set == null) return;
                    f = new Fire { Fx = __instance, Vanilla = src, Set = set };
                    Fires[key] = f;
                }
                f.Level = 0f;
                f.NextFlare = Time.time + Random.Range(1f, 2.5f);
                VanillaMute.Mute(src);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Fire sound failed: " + e); }
        }
    }

    private static void TickFires()
    {
        if (Fires.Count == 0) return;
        var now = Time.time;
        var dt = Mathf.Max(0f, Time.deltaTime);
        List<IntPtr> dead = null;
        foreach (var kv in Fires)
        {
            var f = kv.Value;
            try
            {
                if (f.Set == null || !f.Set.Alive || f.Fx == null || f.Fx.WasCollected) { (dead ??= new List<IntPtr>()).Add(kv.Key); continue; }
                var burning = f.Fx.gameObject.activeInHierarchy && !f.Fx.StopRequested;
                if (!burning) continue; 
                VanillaMute.Mute(f.Vanilla);
                var intensity = 0.6f;
                try
                {
                    var source = f.Fx.intensitySource;
                    if (source != null) intensity = Mathf.Clamp01(source.Intensity);
                }
                catch { }
                f.Level = Mathf.Lerp(f.Level, intensity, 1f - Mathf.Exp(-dt * 3f));
                var i = f.Level;
                var lvl = Mathf.Max(0f, Plugin.ImpactVolume.Value);
                var jet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.9f, i));
                var big = Mathf.Clamp01(i * 1.6f) * (1f - 0.5f * jet);
                var small = 0.35f + 0.45f * (1f - i);
                var set = f.Set;
                set.Flat = View.Inside(f.Vanilla);
                set.Priority = 80;
                set.Want("fire.jet", lvl * jet, 1f);
                set.Want("fire.big", lvl * big * 0.9f, 1f);
                set.Want("fire.small", lvl * small * 0.7f, 1f);
                set.Apply(1.5f, 0.6f);
                if (i > 0.25f && now >= f.NextFlare)
                {
                    f.NextFlare = now + Random.Range(1.5f, 4f);
                    set.OneShot("fire.flare", lvl * 0.6f * i, Random.Range(0.9f, 1.1f));
                }
            }
            catch { (dead ??= new List<IntPtr>()).Add(kv.Key); }
        }
        if (dead != null)
            foreach (var k in dead)
            {
                if (Fires.TryGetValue(k, out var f)) { VanillaMute.Unmute(f.Vanilla); f.Set?.Destroy(); }
                Fires.Remove(k);
            }
    }

    

    [HarmonyPatch(typeof(ExplosionEffects), nameof(ExplosionEffects.Play), new[] { typeof(float) })]
    private static class ExplosionPatch
    {
        private static void Postfix(ExplosionEffects __instance)
        {
            if (!Plugin.Active || !Library.Has("expl.vehicle")) return;
            try
            {
                StopGameSounds(__instance.gameObject, 10f);
                var pos = __instance.transform.position;
                var mine = __instance.GetComponentInParent<LandmineDetonationEffect>() != null;
                if (mine)
                {
                    Sfx.At("expl.mine", pos, null, 1f, Random.Range(0.95f, 1.05f), 30f, 40, false);
                    Sfx.At("expl.debris.small", pos, null, 0.6f, 1f, 10f, 70, false);
                }
                else
                {
                    Sfx.At("expl.vehicle", pos, null, 1f, Random.Range(0.92f, 1.04f), 35f, 40, false);
                    Sfx.At("expl.vehicle.debris", pos, null, 0.8f, 1f, 12f, 65, false);
                }
                if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo(mine ? "Landmine explosion sound" : "Explosion sound (vehicle)");
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Explosion sound failed: " + e); }
        }
    }

    
    private static void StopGameSounds(GameObject go, float maxAge)
    {
        if (go == null) return;
        foreach (var s in go.GetComponentsInChildren<AudioSource>(true))
        {
            try
            {
                if (s == null || !s.isPlaying || s.loop || s.time > maxAge) continue;
                var n = s.gameObject.name;
                if (n != null && n.StartsWith("Sound Overhaul")) continue;
                s.Stop();
            }
            catch { }
        }
    }

    

    [HarmonyPatch(typeof(DestructableTree), nameof(DestructableTree.PlayBendStartEffect))]
    private static class TreeStartPatch
    {
        private static void Postfix(DestructableTree __instance) => Tree(__instance, false);
    }

    [HarmonyPatch(typeof(DestructableTree), nameof(DestructableTree.PlayBendFinishEffect))]
    private static class TreeFinishPatch
    {
        private static void Postfix(DestructableTree __instance) => Tree(__instance, true);
    }

    
    [HarmonyPatch(typeof(TreeLeafFallEffect), nameof(TreeLeafFallEffect.OnEffectStart))]
    private static class LeafPatch
    {
        private static void Postfix(TreeLeafFallEffect __instance)
        {
            if (!Plugin.Active || !Library.Has("tree.fall")) return;
            try
            {
                foreach (var s in __instance.GetComponentsInChildren<AudioSource>(true))
                {
                    if (s == null) continue;
                    s.Stop();
                    VanillaMute.Mute(s);
                }
            }
            catch { }
        }
    }

    private static int _treeLogs;

    private static void Tree(DestructableTree tree, bool landed)
    {
        if (!Plugin.Active || !Library.Has("tree.fall")) return;
        try
        {
            var t = tree.transform;
            var pos = t.TransformPoint(tree.effectOffset);
            var mass = tree.mass;
            
            var k = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, mass) / 1500f), 0.45f, 1.3f);
            var pitch = Mathf.Clamp(1.2f - 0.25f * k, 0.85f, 1.12f) * Random.Range(0.96f, 1.04f);
            if (!landed)
            {
                Sfx.At(k < 0.7f ? "tree.crack.light" : "tree.crack", pos, null, 0.75f * k, pitch, 10f, 75);
                Sfx.At("tree.leaves", pos + Vector3.up * 3f, null, 0.7f * k, pitch, 8f, 80);
            }
            else
            {
                Sfx.At("tree.fall", pos, null, 0.85f * k, pitch, 12f, 70);
                if (k >= 0.8f) Sfx.At("tree.fall.bass", pos, null, 0.7f * k, 1f, 15f, 72);
                Sfx.At("tree.leaves", pos, null, 0.45f * k, pitch, 8f, 85);
            }
            if (Plugin.DebugLogging.Value && _treeLogs++ < 20) Plugin.Log.LogInfo($"Tree {(landed ? "fell" : "breaks")}: mass {mass:0}, size {k:0.00}");
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Tree sound failed: " + e); }
    }

    

    private static readonly Dictionary<IntPtr, float> LastLanding = new();
    private static readonly HashSet<IntPtr> Landed = new();

    [HarmonyPatch(typeof(PartDecoupler), nameof(PartDecoupler.Decouple), new[] { typeof(Vector3) })]
    private static class DecouplePatch
    {
        private static void Prefix(PartDecoupler __instance, out bool __state)
        {
            __state = false;
            try { __state = __instance.decoupled; } catch { }
        }

        private static void Postfix(PartDecoupler __instance, bool __state)
        {
            if (__state || !Plugin.Active || !Library.Has("debris.detach")) return;
            try
            {
                if (!__instance.decoupled) return;
                var heavy = __instance.Mass > 1500f;
                Sfx.At(heavy ? "debris.detach" : "debris.detach.small", __instance.transform.position, null, 1f, Random.Range(0.94f, 1.04f),
                    heavy ? 15f : 8f, 55);
                Landed.Remove(__instance.Pointer);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Detach sound failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(PartDecoupler), nameof(PartDecoupler.OnCollisionEnter))]
    private static class LandingPatch
    {
        private static void Postfix(PartDecoupler __instance)
        {
            if (!Plugin.Active || !Library.Has("debris.land")) return;
            try
            {
                if (!__instance.decoupled) return;
                var key = __instance.Pointer;
                var now = Time.time;
                if (LastLanding.TryGetValue(key, out var last) && now - last < 0.6f && now >= last) return;
                var speed = 5f;
                try
                {
                    var rb = __instance.partRigidbody;
                    if (rb != null) speed = rb.linearVelocity.magnitude;
                }
                catch { }
                var first = Landed.Add(key);
                if (!first && speed < 1.5f) return;
                LastLanding[key] = now;
                if (LastLanding.Count > 256) LastLanding.Clear();
                StopGameSounds(__instance.gameObject, 0.25f);
                var heavy = __instance.Mass > 1500f;
                var vol = first ? 1f : Mathf.Clamp01(speed / 8f);
                Sfx.At(heavy ? "debris.land" : "debris.land.small", __instance.transform.position, null, vol, Random.Range(0.92f, 1.04f),
                    heavy ? 12f : 6f, 60);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Landing sound failed: " + e); }
        }
    }

    

    [HarmonyPatch(typeof(VehicleDesigner.FX.LoaderAudioController), nameof(VehicleDesigner.FX.LoaderAudioController.Update))]
    private static class LoaderPatch
    {
        private const int Controllers = 0x20, ArrayLength = 0x18, ArrayData = 0x20, ElemSize = 0x18, ElemSource = 0x8;

        private static unsafe void Prefix(VehicleDesigner.FX.LoaderAudioController __instance)
        {
            if (!Plugin.Active || !Library.Has("load.breech.close")) return;
            try
            {
                var arr = *(IntPtr*)((byte*)__instance.Pointer + Controllers);
                if (arr == IntPtr.Zero) return;
                var n = *(int*)((byte*)arr + ArrayLength);
                for (var i = 0; i < n && i < 32; i++)
                {
                    var src = *(IntPtr*)((byte*)arr + ArrayData + i * ElemSize + ElemSource);
                    if (src != IntPtr.Zero) VanillaMute.Mute(new AudioSource(src));
                }
            }
            catch { }
        }

        private static void Postfix() => VanillaMute.Demote();
    }

    

    public static void Tick()
    {
        try
        {
            TickFires();
            VanillaMute.Demote();
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Destruction sounds failed: " + e); }
    }

    
    public static void ReleaseAll()
    {
        foreach (var f in Fires.Values)
        {
            VanillaMute.Unmute(f.Vanilla);
            f.Set?.Destroy();
        }
        Fires.Clear();
    }
}
