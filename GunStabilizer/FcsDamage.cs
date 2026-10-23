using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using UnityEngine;

namespace GunStabilizer;






internal static unsafe class FcsDamage
{
    internal sealed class Health
    {
        public float Value = 1f;
        public float BiasMil;           
        public float LeadScale = 1f;    
    }

    private static readonly Dictionary<int, Health> ById = new();
    private static readonly System.Random Rng = new();
    private static int _errors, _logged;

    public static Health Of(int id) => ById.TryGetValue(id, out var h) ? h : null;

    public static void Forget()
    {
        ById.Clear();
        Tracked.Clear();
        _logged = 0;
    }

    private static void Hit(Vector3 point, Vector3 velocity, bool blast, bool penetrated)
    {
        var parts = FcsRegistry.Snapshot();
        if (parts.Count == 0) return;
        var speed = velocity.magnitude;
        var dir = speed > 1f ? velocity / speed : Vector3.zero;
        foreach (var e in parts)
        {
            if (!e.S.Damageable) continue;
            Vector3 box;
            try { box = e.Vo.transform.position; }
            catch { continue; }
            var dmg = Damage(point, box, dir, blast, penetrated);
            if (dmg < 0.02f) continue;
            if (!ById.TryGetValue(e.Id, out var h)) ById[e.Id] = h = new Health();
            if (h.Value <= 0f) continue;
            h.Value = Mathf.Max(0f, h.Value - dmg);
            var broken = 1f - h.Value;
            h.BiasMil = (float)(Rng.NextDouble() * 2.0 - 1.0) * broken * 2.5f;
            h.LeadScale = 1f + (float)(Rng.NextDouble() * 2.0 - 1.0) * broken * 0.6f;
            if (_logged++ < 30)
            {
                string vehicle;
                try { vehicle = e.Vo.GetComponentInParent<Sprocket.Vehicles.Vehicle>()?.name ?? "?"; }
                catch { vehicle = "?"; }
                Plugin.Log.LogInfo(h.Value <= 0f
                    ? $"Fire control computer on '{vehicle}' destroyed ({(blast ? "explosion" : penetrated ? "spall" : "hit")} {Vector3.Distance(point, box):0.0} m away)"
                    : $"Fire control computer on '{vehicle}' damaged ({(blast ? "explosion" : penetrated ? "spall" : "hit")} {Vector3.Distance(point, box):0.0} m away) — {h.Value * 100f:0}% left");
            }
        }
    }

    private static float Damage(Vector3 point, Vector3 box, Vector3 dir, bool blast, bool penetrated)
    {
        var v = box - point;
        var dist = v.magnitude;
        if (blast) return dist < 3f ? 0.6f * (1f - dist / 3f) : 0f;
        if (!penetrated || dir == Vector3.zero) return dist < 0.3f ? 0.1f : 0f;
        if (dist > 3f) return 0f;
        if (dist < 0.05f) return 1f;
        var along = Vector3.Dot(v, dir);
        if (along > -0.2f && (v - dir * along).magnitude < 0.35f) return 0.9f;
        var cone = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.92f, along / dist));
        return 0.8f * cone * Mathf.Pow(1f - dist / 3f, 1.3f);
    }

    

    private const int ElemSize = 0x50, ElemBase = 0x20, PoolOffset = 0x38;
    private const int EEvent = 0x10, EId = 0x18, EVel = 0x34, EFlags = 0x48;
    private static readonly Dictionary<int, (IntPtr Event, Vector3 Velocity)> Tracked = new();
    private static readonly HashSet<int> Alive = new();
    private static readonly List<int> Gone = new();

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class HitScan
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (FcsRegistry.Snapshot().Count == 0) return;
            try { Scan(__instance); }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Fire control computer hit scan: " + e.Message); }
        }
    }

    private static void Scan(ProjectileRegister register)
    {
        var pool = *(IntPtr*)(register.Pointer + PoolOffset);
        if (pool == IntPtr.Zero) return;
        var len = *(int*)(pool + 0x18);
        Alive.Clear();
        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            if ((*(int*)(e + EFlags) & 1) == 0) continue;
            var id = *(int*)(e + EId);
            Alive.Add(id);
            var ev = *(IntPtr*)(e + EEvent);
            var vel = *(Vector3*)(e + EVel);
            if (!Tracked.TryGetValue(id, out var t)) { Tracked[id] = (ev, vel); if (ev != IntPtr.Zero) OnEvent(ev, vel); continue; }
            
            if (ev != IntPtr.Zero && ev != t.Event) OnEvent(ev, t.Velocity);
            Tracked[id] = (ev, vel);
        }
        Gone.Clear();
        foreach (var kv in Tracked) if (!Alive.Contains(kv.Key)) Gone.Add(kv.Key);
        if (Gone.Count == 0) return;
        var history = register.projectileHistory;
        foreach (var id in Gone)
        {
            var t = Tracked[id];
            Tracked.Remove(id);
            try
            {
                if (history != null && history.TryGetValue(id, out var last) && last != null && last.Pointer != t.Event) OnEvent(last.Pointer, t.Velocity);
            }
            catch { }
        }
        if (Tracked.Count > 4096) Tracked.Clear();
    }

    private static void OnEvent(IntPtr ptr, Vector3 velocity)
    {
        var dm = new ProjectileEvent(ptr).TryCast<HitDamageModelProjectileEvent>();
        if (dm == null) return;
        Hit(dm.Position, velocity, false, ((int)dm.Type & (int)HitDamageModelProjectileEvent.HitType.Penetrated) != 0);
    }

    
    [HarmonyPatch(typeof(Sprocket.Gameplay.ExplosionEffects), nameof(Sprocket.Gameplay.ExplosionEffects.Play), new[] { typeof(float) })]
    private static class ExplosionPatch
    {
        private static void Postfix(Sprocket.Gameplay.ExplosionEffects __instance)
        {
            try { Hit(__instance.transform.position, Vector3.zero, true, false); }
            catch { }
        }
    }
}
