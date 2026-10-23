using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using Sprocket;
using Sprocket.ContinuousTracks;
using Sprocket.Landmines;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Exhausts;
using Sprocket.Vehicles.Fires;
using Sprocket.Vehicles.Flameouts;
using Sprocket.Vehicles.Tracks;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;
using UnityEngine;
using UnityEngine.VFX;
using Random = UnityEngine.Random;

namespace CombatEffects;



internal sealed class HiddenSet
{
    private readonly Dictionary<IntPtr, VisualEffect> _hidden = new();

    public void Hide(VisualEffect v)
    {
        if (v == null || v.WasCollected) return;
        if (v.enabled) v.enabled = false;
        _hidden[v.Pointer] = v;
    }

    public void Show(VisualEffect v)
    {
        if (v == null || v.WasCollected || !_hidden.Remove(v.Pointer)) return;
        v.enabled = true;
    }

    public void ShowAll()
    {
        foreach (var v in _hidden.Values)
            try { if (v != null && !v.WasCollected) v.enabled = true; } catch { }
        _hidden.Clear();
    }
}

internal static class Hooks
{
    public static void SettingsChanged()
    {
        if (!Plugin.On(Plugin.MuzzleFlashes)) MuzzleFlashes.Hidden.ShowAll();
        if (!Plugin.On(Plugin.Impacts)) ImpactHooks.Hidden.ShowAll();
        if (!Plugin.On(Plugin.Fires)) { FireHooks.StopAll(); Smoke.StopAll(); }
        if (!Plugin.On(Plugin.Exhaust)) ExhaustHooks.StopAll();
        if (!Plugin.On(Plugin.TrackDust)) TrackDustHooks.StopAll();
        if (!Plugin.On(Plugin.Environment)) MineHooks.Hidden.ShowAll();
    }

    
    public static void StopAll()
    {
        FireHooks.StopAll();
        Smoke.StopAll();
        ExhaustHooks.StopAll();
        TrackDustHooks.StopAll();
        MuzzleFlashes.Hidden.ShowAll();
        ImpactHooks.Hidden.ShowAll();
        MineHooks.Hidden.ShowAll();
    }

    public static void Tick()
    {
        FireHooks.Tick();
        Smoke.Tick();
        ExhaustHooks.Tick();
        TrackDustHooks.Tick();
    }

    public static Quaternion SurfaceRotation(Vector3 normal)
    {
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        normal.Normalize();
        return Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.FromToRotation(Vector3.up, normal);
    }

    public static Transform Part(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var t = new Transform(ptr);
            return t != null && !t.WasCollected ? t : null;
        }
        catch { return null; }
    }
}





[HarmonyPatch(typeof(MuzzleFlashEffect), nameof(MuzzleFlashEffect.Setup))]
internal static class MuzzleFlashes
{
    public static readonly HiddenSet Hidden = new();
    private static int _logged;

    public const string M240 = "M240 Muzzle Flash 00731", M2 = "M2 Browning Muzzle Flash Standard 00797", Kpvt = "KPVT Muzzle Flash 00786",
        Mk20 = "Mk20 Autocannon Muzzle Flash 00784", M242 = "M242 Bushmaster Muzzle Flash 00777", Shipunov = "2A42 Shipunov 00774",
        Grom = "Grom Muzzle Flash 1 00767", D56 = "D-56T Muzzle Flash 00742", Gun105 = "105mm Muzzle Flash 00730", Gun125 = "105mm Muzzle Flash 00754",
        BoreLarge = "Bore Evac FX Large 00729", BoreSmall = "Bore Evac FX Small 00743", BlastDust = "MuzzleBlastFx dust only 00160";

    public static string FlashFor(int cal, int auto)
    {
        if (cal < 10) return M240;
        if (cal < 14) return M2;
        if (cal < 17) return Kpvt;
        if (cal < 23) return Mk20;
        if (cal < 27) return M242;
        if (cal < 45 || (auto == 2 && cal < 70)) return Shipunov;
        if (cal < 75) return Grom;
        if (cal < 90) return D56;
        return cal < 112 ? Gun105 : Gun125;
    }

    [HarmonyPriority(Priority.First)]
    private static void Prefix(MuzzleFlashEffect __instance)
    {
        try { if (!Plugin.On(Plugin.MuzzleFlashes)) Hidden.Show(__instance.visualEffect); }
        catch { }
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(MuzzleFlashEffect __instance, CannonMuzzleFlashEffectInfo info, Transform parent, Vector3 position, Quaternion rotation)
    {
        try
        {
            if (!Plugin.On(Plugin.MuzzleFlashes)) return;
            Hidden.Hide(__instance.visualEffect);
            int cal = info.Calibre;
            var auto = MunitionsBridge.AutoType(parent);
            var key = FlashFor(cal, auto);
            Effects.Play(key, position, rotation, parent);
            if (auto == 0 && cal >= 70) Effects.Play(cal >= 100 ? BoreLarge : BoreSmall, position, rotation, parent);
            if (auto == 0 && cal >= 75) Dust(position, rotation);
            if (Plugin.DebugLogging.Value && _logged++ < 20)
                Plugin.Log.LogInfo($"Muzzle flash {cal} mm{(auto == 1 ? " MG" : auto == 2 ? " autocannon" : "")} → {key}" +
                                   (parent != null ? $" (flash axis {Vector3.Angle(rotation * Vector3.forward, parent.forward):0}° from the barrel)" : ""));
        }
        catch (Exception e) { Plugin.Log.LogWarning("Muzzle flash failed: " + e.Message); }
    }

    
    private static void Dust(Vector3 muzzle, Quaternion rotation)
    {
        var best = float.MaxValue;
        RaycastHit ground = default;
        foreach (var h in Physics.RaycastAll(muzzle, Vector3.down, 4f))
        {
            if (h.rigidbody != null || h.distance >= best) continue;
            best = h.distance;
            ground = h;
        }
        if (best > 4f) return;
        var fwd = Vector3.ProjectOnPlane(rotation * Vector3.forward, ground.normal);
        var rot = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, ground.normal) : Quaternion.FromToRotation(Vector3.up, ground.normal);
        Effects.Play(BlastDust, ground.point, rot);
    }
}





internal static class Launches
{
    public const string TowFront = "TOW Front FX 00779", TowBack = "TOW Backblast FX 00778", RocketLarge = "Rocket Backblast Large 00179",
        RocketSmall = "Rocket Backblast Small 00175", Rpg = "RPG7 Backblast 00205";

    
    public static bool Play(Vector3 muzzle, Vector3 dir, float tubeLength, int caliber, int kind)
    {
        if (dir.sqrMagnitude < 1e-4f) return false;
        var rot = Quaternion.LookRotation(dir.normalized);
        var rear = muzzle - dir.normalized * Mathf.Max(tubeLength, 0.3f);
        bool played;
        if (kind == 5 || kind == 6)
            played = Effects.Play(caliber >= 120 ? RocketLarge : caliber >= 80 ? RocketSmall : Rpg, rear, rot) != null;
        else
        {
            played = Effects.Play(TowBack, rear, rot) != null;
            played |= Effects.Play(TowFront, muzzle, rot) != null;
        }
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Launch {caliber} mm kind {kind}, tube {tubeLength:0.00} m → {(played ? "played" : "not available")}");
        return played;
    }
}








internal static unsafe class ImpactHooks
{
    private static bool _installed;
    private static int _errors, _logged;
    public static readonly HiddenSet Hidden = new();

    private const int IType = 0x0, IPos = 0x4, INormal = 0x10, IVel = 0x1C, IParent = 0x28, ICalibre = 0x30;
    private const int Explosion = 1, Ground = 2, Penetration = 4, NonPenetration = 8, Ricochet = 16;

    private struct Hit
    {
        public int Type, Calibre;
        public Vector3 Pos, Normal, Vel;
        public IntPtr Parent;
        public bool Mark;
    }

    private static readonly List<Hit> Pending = new();
    private static readonly List<float> BulletTimes = new();

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var own = SharedHook.Register(typeof(ProjectileImpactMark).GetMethod(nameof(ProjectileImpactMark.PlayEffect)), SharedHook.MarkKey,
                (self, info, after) => OnCall(self, info, after, true));
            own &= SharedHook.Register(typeof(ProjectileImpactEffect).GetMethod(nameof(ProjectileImpactEffect.PlayEffect)), SharedHook.EffectKey,
                (self, info, after) => OnCall(self, info, after, false));
            Plugin.Log.LogInfo("Hooked projectile hits and explosions" + (own ? "" : " (sharing Sound Overhaul's hook)"));
        }
        catch (Exception e) { Plugin.Log.LogError("Hit effect hooks failed — the game's own hit effects stay: " + e.Message); }
    }

    private static Hit Read(IntPtr p, bool mark) => new()
    {
        Type = *(byte*)(p + IType),
        Pos = *(Vector3*)(p + IPos),
        Normal = *(Vector3*)(p + INormal),
        Vel = *(Vector3*)(p + IVel),
        Parent = *(IntPtr*)(p + IParent),
        Calibre = *(ushort*)(p + ICalibre),
        Mark = mark
    };

    private static VisualEffect Vfx(IntPtr self, bool mark)
    {
        try { return mark ? new ProjectileImpactMark(self).visualEffect : new ProjectileImpactEffect(self).visualEffect; }
        catch { return null; }
    }

    
    
    private static readonly Hit[] Call = new Hit[2];
    private static readonly bool[] CallOk = new bool[2];

    private static void OnCall(IntPtr self, IntPtr info, bool after, bool mark)
    {
        var k = mark ? 0 : 1;
        if (!after)
        {
            CallOk[k] = false;
            try
            {
                var on = Plugin.On(Plugin.Impacts);
                if (on && info != IntPtr.Zero) { Call[k] = Read(info, mark); CallOk[k] = true; }
                if (!on) Hidden.Show(Vfx(self, mark));
            }
            catch { }
            return;
        }
        if (!CallOk[k]) return;
        CallOk[k] = false;
        try
        {
            Hidden.Hide(Vfx(self, mark));
            if (Pending.Count < 256) Pending.Add(Call[k]);
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Hit effect failed: " + e); }
    }

    public static void Resolve()
    {
        if (Pending.Count == 0) return;
        try
        {
            foreach (var h in Pending)
            {
                if (h.Mark && (h.Type & (Penetration | NonPenetration)) != 0 && NearExplosion(h.Pos)) continue;
                Spawn(h);
            }
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Hit effects failed: " + e); }
        Pending.Clear();
    }

    private static bool NearExplosion(Vector3 p)
    {
        foreach (var o in Pending)
            if (!o.Mark && (o.Type & Explosion) != 0 && (o.Pos - p).sqrMagnitude < 6.25f) return true;
        return false;
    }

    private static void Spawn(Hit h)
    {
        var explosion = (h.Type & Explosion) != 0;
        var ricochet = !explosion && (h.Type & Ricochet) != 0;
        var parent = Hooks.Part(h.Parent);
        var surface = h.Mark || (explosion && parent != null) ? ImpactSelector.Steel : ImpactSelector.Dirt;
        var cal = (int)h.Calibre;
        var kind = -1;
        var automatic = false;
        if (explosion)
        {
            kind = MunitionsBridge.KindNear(h.Pos, 4f, out var mcal, out automatic);
            if (cal <= 0 && mcal > 0) cal = mcal;
        }
        if (cal <= 0) cal = 20;
        var d = ImpactSelector.Describe(cal, kind, explosion, automatic);
        if (d.Size == ImpactSelector.Bullet && !BulletAllowed()) return;
        var e = ImpactSelector.Pick(d, surface, ricochet);
        if (e == null)
        {
            if (Plugin.DebugLogging.Value && _logged++ < 40) Plugin.Log.LogInfo($"Hit {cal} mm type {h.Type}: no effect for {d}, surface {surface}");
            return;
        }
        Quaternion rot;
        if (ricochet)
        {
            var dir = h.Vel.sqrMagnitude > 1f ? Vector3.Reflect(h.Vel.normalized, h.Normal.normalized) : h.Normal;
            rot = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        }
        else rot = Hooks.SurfaceRotation(h.Normal);
        
        var follow = surface == ImpactSelector.Steel && !explosion ? parent : null;
        Effects.Play(e.Effect, h.Pos + h.Normal.normalized * 0.02f, rot, follow);
        if (Plugin.DebugLogging.Value && _logged++ < 40)
            Plugin.Log.LogInfo($"Hit {cal} mm type {h.Type}{(kind >= 0 ? $" (Munitions kind {kind})" : "")} on {(surface == ImpactSelector.Steel ? "armour" : "ground")} → {e.Name} ({e.Effect})");
    }

    
    public static bool Detonation(Vector3 pos, Vector3 normal, int cal, int kind, bool ground, bool automatic)
    {
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        var d = ImpactSelector.Describe(Mathf.Max(cal, 1), kind, true, automatic);
        var e = ImpactSelector.Pick(d, ground ? ImpactSelector.Dirt : ImpactSelector.Steel, false);
        if (e == null) return false;
        var played = Effects.Play(e.Effect, pos + normal.normalized * 0.02f, Hooks.SurfaceRotation(normal)) != null;
        if (Plugin.DebugLogging.Value && _logged++ < 40)
            Plugin.Log.LogInfo($"Burst without a game effect {cal} mm (Munitions kind {kind}) → {e.Name} ({e.Effect}){(played ? "" : " not available")}");
        return played;
    }

    
    private static bool BulletAllowed()
    {
        var now = Time.time;
        BulletTimes.RemoveAll(t => now - t > 1f);
        if (BulletTimes.Count >= 30) return false;
        BulletTimes.Add(now);
        return true;
    }
}






[HarmonyPatch]
internal static class FireHooks
{
    private enum FireKind { Fire, Flameout, Explosion, LongTerm }

    private static readonly Dictionary<IntPtr, FireKind> Prototypes = new();

    private sealed class Burning
    {
        public FireEffect Effect;
        public IFireIntensitySource Source;
        public Instance Fire, Smoke, Flames;
        public float FlamesScale = 1f;      
        public FireKind Kind;
        
        public Transform Parent;
        public Vector3 Pos;
        public Quaternion Rot;
        public float NextSurge, SurgeUntil;
        
        public bool Detached;
    }

    
    
    
    private const string Jet = "Cookoff Jet 90001", Surge = "Flame Spout Huge 00089", SmallSurge = "Flame Spout Large 1 00090";

    private static readonly Dictionary<IntPtr, Burning> Active = new();
    private static readonly HashSet<string> LoggedNames = new();
    public static readonly HiddenSet Hidden = new();

    [HarmonyPatch(typeof(FireControlAssemblyStage), nameof(FireControlAssemblyStage.Execute))]
    [HarmonyPostfix]
    private static void FireStage(FireControlAssemblyStage __instance)
    {
        try { Remember(__instance.fireEffect, FireKind.Fire); }
        catch { }
    }

    [HarmonyPatch(typeof(FlameoutAssemblyStage), nameof(FlameoutAssemblyStage.Execute))]
    [HarmonyPostfix]
    private static void FlameoutStage(FlameoutAssemblyStage __instance)
    {
        try
        {
            Remember(__instance.flameoutEffect, FireKind.Flameout);
            Remember(__instance.explosionEffect, FireKind.Explosion);
            Remember(__instance.explosionLongTermEffect, FireKind.LongTerm);
        }
        catch { }
    }

    private static void Remember(FireEffect proto, FireKind kind)
    {
        if (proto != null && !Prototypes.ContainsKey(proto.Pointer)) Prototypes[proto.Pointer] = kind;
    }

    private static FireKind KindOf(FireEffect fe)
    {
        try
        {
            var proto = fe.prototype;
            if (proto != null && Prototypes.TryGetValue(proto.Pointer, out var k)) return k;
        }
        catch { }
        var n = (fe.gameObject.name ?? "").ToLowerInvariant();
        if (n.Contains("long")) return FireKind.LongTerm;
        if (n.Contains("explosion") || n.Contains("explode")) return FireKind.Explosion;
        if (n.Contains("flameout") || n.Contains("cookoff") || n.Contains("cook")) return FireKind.Flameout;
        return FireKind.Fire;
    }

    [HarmonyPatch(typeof(FireEffect), nameof(FireEffect.PlayEffect))]
    [HarmonyPrefix]
    private static void PlayPrefix(FireEffect __instance)
    {
        try
        {
            if (Plugin.On(Plugin.Fires)) return;
            Hidden.Show(__instance.visualEffect);
            foreach (var v in __instance.GetComponentsInChildren<VisualEffect>(true)) Hidden.Show(v);
        }
        catch { }
    }

    private static readonly HashSet<string> LoggedParts = new();

    
    private static void HideGame(FireEffect fe)
    {
        Hidden.Hide(fe.visualEffect);
        var parts = fe.GetComponentsInChildren<VisualEffect>(true);
        foreach (var v in parts) Hidden.Hide(v);
        var name = fe.gameObject.name;
        if (LoggedParts.Add(name)) Plugin.Log.LogInfo($"Fire effect '{name}': {parts.Length} VFX part(s) of the game hidden");
    }

    [HarmonyPatch(typeof(FireEffect), nameof(FireEffect.PlayEffect))]
    [HarmonyPostfix]
    private static void PlayPostfix(FireEffect __instance, Transform parent, Vector3 position, Vector3 direction, IFireIntensitySource intensitySource)
    {
        try
        {
            if (!Plugin.On(Plugin.Fires)) return;
            var kind = KindOf(__instance);
            var name = __instance.gameObject.name;
            if (LoggedNames.Add(name)) Plugin.Log.LogInfo($"Fire effect '{name}' shown as {kind}");
            HideGame(__instance);
            Stop(__instance.Pointer);
            var up = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.up;
            var rot = Quaternion.FromToRotation(Vector3.up, up);
            var intensity = Intensity(intensitySource);
            if (kind == FireKind.Explosion)
            {
                Effects.Play("Ammo Detonation 00021", position, Quaternion.identity);
                return;
            }
            var b = new Burning
            {
                Effect = __instance, Source = intensitySource, Kind = kind, Parent = parent,
                Pos = parent != null ? parent.InverseTransformPoint(position) : position,
                Rot = parent != null ? Quaternion.Inverse(parent.rotation) * rot : rot,
                NextSurge = Time.time + Random.Range(1.5f, 4f),
                Detached = parent != null && (parent.lossyScale - Vector3.one).sqrMagnitude > 1e-3f
            };
            if (b.Detached) parent = null;
            switch (kind)
            {
                case FireKind.Flameout:
                    
                    b.Fire = Effects.Loop(Jet, position, rot, parent);
                    break;
                case FireKind.LongTerm:
                    
                    b.Fire = Effects.Loop("PoppedTurretFire 00169", position, Quaternion.identity, parent);
                    b.Smoke = Effects.Loop("Plume Heavy 00164", position, Quaternion.identity, parent);
                    b.Flames = Effects.Loop(Jet, position, Quaternion.identity, parent);
                    b.FlamesScale = 0.45f;
                    b.NextSurge = Time.time + Random.Range(5f, 10f);
                    break;
                default:
                    b.Fire = Effects.Loop(intensity > 0.45f ? "Hatch Fire Medium 1 00104" : "Hatch Fire Small 1 00107", position, rot, parent);
                    b.Smoke = Effects.Loop("Hole Smoke Medium 1 00141", position, rot, parent);
                    break;
            }
            foreach (var inst in new[] { b.Fire, b.Smoke, b.Flames })
            {
                if (inst == null) continue;
                
                foreach (var l in inst.Lights) if (l != null) l.intensity = 0f;
                
                inst.NoDiminish = true;
                if (inst.Info.Key == Jet) Drift(inst);
            }
            Active[__instance.Pointer] = b;
            Rates(b);
            if (kind != FireKind.Fire)
                Plugin.Log.LogInfo($"{kind} at intensity {intensity:0.00}: {(b.Fire != null ? b.Fire.Info.Key : "-")}" +
                                   $"{(b.Flames != null ? " + " + b.Flames.Info.Key : "")}{(b.Smoke != null ? " + " + b.Smoke.Info.Key : "")}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Fire effect failed: " + e.Message); }
    }

    private static float Intensity(IFireIntensitySource s)
    {
        try { return s != null ? Mathf.Clamp01(s.Intensity) : 1f; }
        catch { return 1f; }
    }

    private static void Stop(IntPtr key)
    {
        if (!Active.TryGetValue(key, out var b)) return;
        Active.Remove(key);
        Effects.Stop(b.Fire);
        Effects.Stop(b.Flames);
        Effects.Stop(b.Smoke, 20f);
    }

    
    
    private static void Rates(Burning b)
    {
        var intensity = Intensity(b.Source);
        var k = Mathf.Lerp(0.25f, 1f, intensity);
        var jet = Mathf.Lerp(0.75f, 1.15f, intensity);
        var surge = Time.time < b.SurgeUntil ? 2.2f : 1f;
        Effects.SetRate(b.Fire, b.Kind == FireKind.Flameout ? jet * surge : k);
        Effects.SetRate(b.Flames, jet * b.FlamesScale * surge);
        Effects.SetRate(b.Smoke, Mathf.Lerp(0.5f, 1f, k));
    }

    
    private static void Surges(Burning b)
    {
        if (b.Kind != FireKind.Flameout && b.Kind != FireKind.LongTerm) return;
        var now = Time.time;
        if (now < b.NextSurge) return;
        var flameout = b.Kind == FireKind.Flameout;
        b.SurgeUntil = now + Random.Range(0.5f, 1.1f);
        b.NextSurge = now + (flameout ? Random.Range(2.5f, 7f) : Random.Range(6f, 14f));
        var parent = b.Parent != null && !b.Parent.WasCollected ? b.Parent : null;
        var pos = parent != null ? parent.TransformPoint(b.Pos) : b.Pos;
        var rot = parent != null ? parent.rotation * b.Rot : b.Rot;
        var burst = Effects.Play(flameout && Intensity(b.Source) > 0.4f ? Surge : SmallSurge, pos, rot, b.Detached ? null : parent);
        if (burst != null) foreach (var l in burst.Lights) if (l != null) l.intensity = 0f;
    }

    public static void StopAll()
    {
        foreach (var k in new List<IntPtr>(Active.Keys)) Stop(k);
        foreach (var v in Vents) Effects.Stop(v.Jet);
        Vents.Clear();
        Hidden.ShowAll();
    }

    
    
    
    
    
    
    
    
    private sealed class VentJet
    {
        public Instance Jet;
        public Transform Anchor;                 
        public Vector3 LocalPos, LocalUp, WorldPos;
        public Quaternion WorldRot;
        public bool Fixed;
        public float Intensity, FadeStart, EndAt, NextSurge, SurgeUntil;
    }

    private const float VentSmokeAfter = 12f;   
    private const float FlameTail = 3f;         
    private const float FadeShare = 0.45f;      

    
    
    
    private static Vector3? _wind;

    private static Vector3 Wind()
    {
        var w = MunitionsBridge.Wind();
        if (w.HasValue)
        {
            var flat = new Vector3(w.Value.x, 0f, w.Value.z);
            if (flat.sqrMagnitude > 1e-6f) return flat.normalized * Mathf.Clamp(flat.magnitude * 4f, 1f, 2.5f);
        }
        if (!_wind.HasValue)
        {
            var a = Random.Range(0f, Mathf.PI * 2f);
            _wind = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f;
        }
        return _wind.Value;
    }

    private static void Drift(Instance jet)
    {
        if (jet == null) return;
        var w = Wind();
        foreach (var ps in jet.Systems)
            if (ps != null && ps.name == "Jet Smoke") ParticleNative.SetVelocity(ps, new Vector3(w.x, 1.2f, w.z));
    }

    private static readonly List<VentJet> Vents = new();

    private static bool Opening(VentJet v, out Vector3 pos, out Quaternion rot)
    {
        pos = v.WorldPos;
        rot = v.WorldRot;
        if (v.Anchor == null) return v.Fixed;
        if (v.Anchor.WasCollected) return false;
        pos = v.Anchor.TransformPoint(v.LocalPos);
        rot = Quaternion.FromToRotation(Vector3.up, (v.Anchor.rotation * v.LocalUp).normalized);
        return true;
    }

    public static bool Vent(Transform anchor, Vector3 localPos, Vector3 localUp, float intensity, float duration)
    {
        if (anchor == null || anchor.WasCollected) return false;
        var now = Time.time;
        duration = Mathf.Clamp(duration, 2f, 60f);
        var v = new VentJet
        {
            Anchor = anchor, LocalPos = localPos, LocalUp = localUp.sqrMagnitude > 1e-4f ? localUp.normalized : Vector3.up,
            Intensity = Mathf.Clamp01(intensity), NextSurge = now, EndAt = now + duration,
            FadeStart = now + duration - Mathf.Max(4f, FadeShare * duration)
        };
        if (!Opening(v, out var pos, out var rot)) return false;
        v.Jet = Effects.Loop(Jet, pos, rot);
        if (v.Jet == null) return false;
        v.Jet.NoDiminish = true;
        foreach (var l in v.Jet.Lights) if (l != null) l.intensity = 0f;
        Drift(v.Jet);
        Vents.Add(v);
        Plugin.Log.LogInfo($"Cook-off vented through a blow-off panel: {Jet} for {duration:0} s at intensity {intensity:0.00} " +
                           $"(panel scale {anchor.lossyScale.x:0.00} × {anchor.lossyScale.y:0.00} × {anchor.lossyScale.z:0.00})");
        return true;
    }

    
    private static void FadeOut(Burning b)
    {
        var now = Time.time;
        var parent = b.Parent != null && !b.Parent.WasCollected ? b.Parent : null;
        var v = new VentJet
        {
            Jet = b.Fire, Anchor = parent, LocalPos = b.Pos, LocalUp = b.Rot * Vector3.up,
            Intensity = Intensity(b.Source), FadeStart = now, EndAt = now + 5f, NextSurge = float.MaxValue
        };
        if (parent == null)
        {
            v.Fixed = true;
            v.WorldPos = b.Fire.Tr.position;
            v.WorldRot = b.Fire.Tr.rotation;
        }
        Effects.Stop(b.Flames);
        Effects.Stop(b.Smoke, 20f);
        Vents.Add(v);
    }

    private static void TickVents()
    {
        if (Vents.Count == 0) return;
        var now = Time.time;
        for (var i = Vents.Count - 1; i >= 0; i--)
        {
            var v = Vents[i];
            var open = Opening(v, out var pos, out var rot);
            var flamesOut = v.EndAt + FlameTail;
            if (!Effects.Alive(v.Jet) || !open || now >= flamesOut + VentSmokeAfter)
            {
                Effects.Stop(v.Jet, 25f);   
                Vents.RemoveAt(i);
                continue;
            }
            v.Jet.Tr.SetPositionAndRotation(pos, rot);
            var strength = Mathf.Lerp(0.75f, 1.15f, v.Intensity);
            float rate, size, speed, life, smoke = 1f;
            if (now < v.FadeStart)
            {
                
                rate = strength;
                size = speed = life = 1f;
                if (now >= v.NextSurge)
                {
                    v.SurgeUntil = now + Random.Range(0.5f, 1.1f);
                    v.NextSurge = now + Random.Range(2.5f, 7f);
                    var burst = Effects.Play(Surge, pos, rot);
                    if (burst != null) foreach (var l in burst.Lights) if (l != null) l.intensity = 0f;
                }
                if (now < v.SurgeUntil) rate *= 2.2f;
            }
            else if (now < v.EndAt)
            {
                
                var e = 1f - Mathf.SmoothStep(0f, 1f, (now - v.FadeStart) / Mathf.Max(0.1f, v.EndAt - v.FadeStart));
                size = Mathf.Lerp(0.22f, 1f, e);
                speed = Mathf.Lerp(0.25f, 1f, e);
                life = Mathf.Lerp(0.45f, 1f, e);
                rate = strength * Mathf.Lerp(0.55f, 1f, e);
            }
            else if (now < flamesOut)
            {
                
                var t = (now - v.EndAt) / FlameTail;
                size = Mathf.Lerp(0.22f, 0.12f, t);
                speed = 0.25f;
                life = Mathf.Lerp(0.45f, 0.3f, t);
                rate = strength * 0.55f * (1f - t);
            }
            else
            {
                
                rate = 0f;
                size = 0.12f;
                speed = 0.25f;
                life = 0.3f;
                smoke = Mathf.Lerp(1f, 0.15f, (now - flamesOut) / VentSmokeAfter);
            }
            for (var s = 0; s < v.Jet.Systems.Length; s++)
            {
                var ps = v.Jet.Systems[s];
                if (ps != null && ps.name == "Jet Smoke")
                {
                    Effects.SetSystemRate(v.Jet, s, smoke);
                    continue;
                }
                Effects.SetSystemRate(v.Jet, s, rate);
                Effects.SetSystemStart(v.Jet, s, size, speed, life);
            }
        }
    }

    public static void Tick()
    {
        TickVents();
        if (Active.Count == 0) return;
        foreach (var kv in new List<KeyValuePair<IntPtr, Burning>>(Active))
        {
            var b = kv.Value;
            bool running;
            try { running = b.Effect != null && !b.Effect.WasCollected && b.Effect.gameObject.activeInHierarchy && !b.Effect.StopRequested; }
            catch { running = false; }
            if (!running && b.Kind == FireKind.Flameout && Effects.Alive(b.Fire) && b.Fire.Info.Key == Jet)
            {
                
                Active.Remove(kv.Key);
                FadeOut(b);
                continue;
            }
            if (!running || (!Effects.Alive(b.Fire) && !Effects.Alive(b.Smoke))) { Stop(kv.Key); continue; }
            if (b.Detached && b.Parent != null && !b.Parent.WasCollected)
            {
                var pos = b.Parent.TransformPoint(b.Pos);
                var rot = b.Parent.rotation * b.Rot;
                if (Effects.Alive(b.Fire)) b.Fire.Tr.SetPositionAndRotation(pos, b.Kind == FireKind.LongTerm ? Quaternion.identity : rot);
                if (Effects.Alive(b.Smoke)) b.Smoke.Tr.position = pos;
                if (Effects.Alive(b.Flames)) b.Flames.Tr.position = pos;
            }
            Surges(b);
            Rates(b);
        }
    }
}




[HarmonyPatch(typeof(ExhaustEffect), nameof(ExhaustEffect.EffectUpdate))]
internal static class ExhaustHooks
{
    private const string Key = "Exhaust T72 00758";
    private static readonly Vector3 PlumeAxis = Quaternion.Euler(70f, 0f, 0f) * Vector3.forward; 

    private sealed class Pipe
    {
        public Instance Plume;
        public float LastSeen;
    }

    private static readonly Dictionary<IntPtr, Pipe> Pipes = new();
    public static readonly HiddenSet Hidden = new();

    private static void Postfix(ExhaustEffect __instance)
    {
        try
        {
            var key = __instance.Pointer;
            if (!Plugin.On(Plugin.Exhaust))
            {
                Hidden.Show(__instance.visualEffect);
                return;
            }
            Hidden.Hide(__instance.visualEffect);
            var tr = __instance.transform;
            if (!Pipes.TryGetValue(key, out var p) || !Effects.Alive(p.Plume))
            {
                var rot = Quaternion.FromToRotation(PlumeAxis, tr.forward);
                p = new Pipe { Plume = Effects.Loop(Key, tr.position, rot, tr) };
                if (p.Plume != null) p.Plume.Tr.localRotation = Quaternion.FromToRotation(PlumeAxis, Vector3.forward);
                Pipes[key] = p;
            }
            p.LastSeen = Time.time;
            var info = __instance.info;
            var k = 1f;
            if (info != null)
                k = Mathf.Clamp(info.Rate / Mathf.Max(Sprocket.Powertrains.Exhausts.ExhaustOutputInfo.IdleRate, 1e-3f), 0.3f, 4f) *
                    Mathf.Lerp(0.8f, 1.6f, Mathf.Clamp01(info.Richness));
            Effects.SetRate(p.Plume, k);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Exhaust effect failed: " + e.Message); }
    }

    public static void Tick()
    {
        if (Pipes.Count == 0) return;
        var now = Time.time;
        foreach (var kv in new List<KeyValuePair<IntPtr, Pipe>>(Pipes))
            if (now - kv.Value.LastSeen > 0.5f || !Effects.Alive(kv.Value.Plume))
            {
                Effects.Stop(kv.Value.Plume);
                Pipes.Remove(kv.Key);
            }
    }

    public static void StopAll()
    {
        foreach (var p in Pipes.Values) Effects.Stop(p.Plume);
        Pipes.Clear();
        Hidden.ShowAll();
    }
}





[HarmonyPatch(typeof(TrackVisualEffectController), nameof(TrackVisualEffectController.Update))]
internal static class TrackDustHooks
{
    private const string Key = "Track Dust T72 00759";

    private sealed class Rig
    {
        public Instance Dust;
        public Transform Vehicle;
        public float LastSeen;
        public bool Placed;
        public readonly List<int> Left = new(), Right = new();
    }

    
    private const float T72Center = -0.35f, T72Length = 6.1f, T72Height = 1f;

    
    private struct TrackBox
    {
        public bool Valid;
        public float MinY, MaxY, MinZ, MaxZ, LeftX, RightX;
    }

    private static void Grow(ref TrackBox box, Transform vehicle, GameObject track, bool left)
    {
        if (track == null) return;
        foreach (var r in track.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled) continue;
            var b = r.bounds;
            if (b.size.sqrMagnitude < 0.01f) continue;
            for (var c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                var p = vehicle.InverseTransformPoint(corner);
                if (!box.Valid)
                {
                    box.Valid = true;
                    box.MinY = box.MaxY = p.y;
                    box.MinZ = box.MaxZ = p.z;
                }
                box.MinY = Mathf.Min(box.MinY, p.y);
                box.MaxY = Mathf.Max(box.MaxY, p.y);
                box.MinZ = Mathf.Min(box.MinZ, p.z);
                box.MaxZ = Mathf.Max(box.MaxZ, p.z);
            }
            var cx = vehicle.InverseTransformPoint(b.center).x;
            if (left) box.LeftX = cx; else box.RightX = cx;
        }
    }

    private static readonly Dictionary<IntPtr, Rig> Rigs = new();
    public static readonly HiddenSet Hidden = new();

    private static void Postfix(TrackVisualEffectController __instance)
    {
        try
        {
            var tracks = __instance.tracks;
            if (tracks == null || tracks.Length == 0) return;
            if (!Plugin.On(Plugin.TrackDust))
            {
                for (var i = 0; i < tracks.Length; i++) Hidden.Show(tracks[i].vfx);
                return;
            }
            float leftK = 0f, rightK = 0f;
            Transform vehicle = null;
            for (var i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                Hidden.Hide(t.vfx);
                var src = t.track;
                if (src == null) continue;
                var go = src.gameObject;
                if (vehicle == null && go != null)
                {
                    var v = go.GetComponentInParent<Vehicle>();
                    vehicle = v != null ? v.transform : go.transform.root;
                }
                var contact = (int)src.ContactState;
                var k = src.Broken || contact == 0 ? 0f : Mathf.Clamp(Mathf.Abs(src.TrackVelocity) / 8f, 0f, 1.5f) * (contact == 1 ? 0.5f : 1f);
                if (k > 0f && WheelsBridge.HiddenTrack(src.Pointer)) k = 0f;
                if (src.LeftSide) leftK = Mathf.Max(leftK, k); else rightK = Mathf.Max(rightK, k);
            }
            if (vehicle == null) return;
            var key = __instance.Pointer;
            if (!Rigs.TryGetValue(key, out var rig) || !Effects.Alive(rig.Dust))
            {
                rig = new Rig { Vehicle = vehicle, Dust = Effects.Loop(Key, vehicle.position, vehicle.rotation, vehicle) };
                Rigs[key] = rig;
            }
            if (rig.Dust == null) return;
            if (!rig.Placed)
            {
                var box = new TrackBox { LeftX = -1.36f, RightX = 1.36f };
                for (var i = 0; i < tracks.Length; i++)
                {
                    var src = tracks[i].track;
                    if (src != null) Grow(ref box, vehicle, src.gameObject, src.LeftSide);
                }
                if (!box.Valid || box.MaxZ - box.MinZ < 1f)
                {
                    Effects.SetRate(rig.Dust, 0f);   
                    rig.LastSeen = Time.time;
                    return;
                }
                Place(rig, box);
            }
            rig.LastSeen = Time.time;
            for (var i = 0; i < rig.Dust.Systems.Length; i++)
            {
                var k = rig.Left.Contains(i) ? leftK : rig.Right.Contains(i) ? rightK : Mathf.Max(leftK, rightK);
                Effects.SetSystemRate(rig.Dust, i, k);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Track dust failed: " + e.Message); }
    }

    
    
    private static void Place(Rig rig, TrackBox box)
    {
        rig.Placed = true;
        var length = box.MaxZ - box.MinZ;
        var zScale = length / T72Length;
        var yScale = Mathf.Clamp((box.MaxY - box.MinY) / T72Height, 0.4f, 2f);
        rig.Dust.Tr.localPosition = new Vector3(0f, box.MinY, (box.MinZ + box.MaxZ) * 0.5f);
        rig.Dust.Tr.localRotation = Quaternion.identity;
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Track dust fitted: tracks {length:0.0} m long, {box.MaxY - box.MinY:0.00} m high, bottom {box.MinY:0.00} m, sides {box.LeftX:0.00} / {box.RightX:0.00} m");
        var systems = rig.Dust.Systems;
        for (var i = 0; i < systems.Length; i++)
        {
            var ps = systems[i];
            if (ps == null) continue;
            var t = ps.transform;
            var name = t.name ?? "";
            if (name.Contains("Grass")) { t.gameObject.SetActive(false); continue; }
            var left = name.StartsWith("L ");
            var right = name.StartsWith("R ");
            if (left) rig.Left.Add(i);
            if (right) rig.Right.Add(i);
            if ((left || right) && t.parent != null && t.parent.Pointer == rig.Dust.Tr.Pointer && Mathf.Abs(t.localPosition.x) > 0.5f)
            {
                var p = t.localPosition;
                t.localPosition = new Vector3(left ? box.LeftX : box.RightX, p.y * yScale, (p.z - T72Center) * zScale);
            }
        }
    }

    public static void Tick()
    {
        if (Rigs.Count == 0) return;
        var now = Time.time;
        foreach (var kv in new List<KeyValuePair<IntPtr, Rig>>(Rigs))
            if (now - kv.Value.LastSeen > 1f || !Effects.Alive(kv.Value.Dust))
            {
                Effects.Stop(kv.Value.Dust);
                Rigs.Remove(kv.Key);
            }
    }

    public static void StopAll()
    {
        foreach (var r in Rigs.Values) Effects.Stop(r.Dust);
        Rigs.Clear();
        Hidden.ShowAll();
    }
}


[HarmonyPatch]
internal static class MineHooks
{
    public static readonly HiddenSet Hidden = new();

    [HarmonyPatch(typeof(LandmineDetonationEffect), nameof(LandmineDetonationEffect.Setup))]
    [HarmonyPostfix]
    private static void Mine(LandmineDetonationEffect __instance, Vector3 pos)
    {
        try
        {
            var vfx = __instance.explosionEffect?.TryCast<VisualEffect>();
            if (!Plugin.On(Plugin.Environment)) { Hidden.Show(vfx); return; }
            Hidden.Hide(vfx);
            Effects.Play("Artillery 120mm Terrain 00026", pos, Hooks.SurfaceRotation(Vector3.up));
        }
        catch (Exception e) { Plugin.Log.LogWarning("Mine effect failed: " + e.Message); }
    }

    
    [HarmonyPatch(typeof(TreeLeafFallEffect), nameof(TreeLeafFallEffect.OnEffectStart))]
    [HarmonyPostfix]
    private static void Tree(TreeLeafFallEffect __instance)
    {
        try
        {
            if (!Plugin.On(Plugin.Environment)) return;
            var p = __instance.transform.position;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 40f))
            {
                if (h.rigidbody != null) continue;
                p = h.point;
                break;
            }
            Effects.Play("Tree Fall Dust 00219", p, Quaternion.identity);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Tree effect failed: " + e.Message); }
    }
}
