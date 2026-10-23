using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SoundOverhaul;










internal static unsafe class Impacts
{
    private static bool _installed;
    private static int _errors;

    
    private const int IType = 0x0, IPos = 0x4, INormal = 0x10, IVel = 0x1C, IParent = 0x28, ICalibre = 0x30;
    private const int Explosion = 1, Ground = 2, Penetration = 4, NonPenetration = 8, Ricochet = 16;

    private struct Hit
    {
        public int Type, Calibre;
        public Vector3 Pos, Normal, Vel;
        public IntPtr Parent;
    }

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
            Plugin.Log.LogInfo("Hooked projectile impact effects (armour hits, explosions, ground impacts)" + (own ? "" : " — sharing Combat Effects' hook"));
        }
        catch (Exception e) { Plugin.Log.LogError("Impact sound hooks failed — the game's impact sounds stay: " + e.Message); }
    }

    private static Hit Read(IntPtr p) => new()
    {
        Type = *(byte*)(p + IType),
        Pos = *(Vector3*)(p + IPos),
        Normal = *(Vector3*)(p + INormal),
        Vel = *(Vector3*)(p + IVel),
        Parent = *(IntPtr*)(p + IParent),
        Calibre = *(ushort*)(p + ICalibre)
    };

    
    
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
                if (info != IntPtr.Zero) { Call[k] = Read(info); CallOk[k] = true; }
            }
            catch { }
            return;
        }
        if (!CallOk[k] || !Plugin.Active) return;
        CallOk[k] = false;
        try
        {
            if (mark) OnMark(self, Call[k]);
            else OnEffect(self, Call[k]);
        }
        catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError((mark ? "Hit" : "Impact") + " sound failed: " + e); }
    }

    private static Transform Part(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var t = new Transform(ptr);
            return t != null && t ? t : null;
        }
        catch { return null; }
    }

    

    private static float _bulletTokens = 8f, _bulletRefill;

    
    private static bool BulletAllowed(Vector3 pos, float range)
    {
        var now = Time.time;
        _bulletTokens = Mathf.Min(8f, _bulletTokens + (now - _bulletRefill) * 30f);
        _bulletRefill = now;
        if (_bulletTokens < 1f) return false;
        if (Vector3.Distance(pos, View.Listener) > range) return false;
        _bulletTokens -= 1f;
        return true;
    }

    private static void OnMark(IntPtr self, Hit h)
    {
        var type = h.Type & (Penetration | NonPenetration | Ricochet);
        if (type == 0 || !Library.Has("hit.shell")) return;
        var mark = new ProjectileImpactMark(self);
        var src = mark.audioSource;
        if (src != null) src.Stop();

        var cal = h.Calibre > 0 ? h.Calibre : 20;
        var size = cal < 15 ? 0 : cal < 45 ? 1 : 2;
        if (size < 2 && !BulletAllowed(h.Pos, size == 0 ? 250f : 600f)) return;
        var part = Part(h.Parent);
        var inside = part != null && View.Inside(part);
        var own = part != null && View.OwnVehicle(part);
        var pos = h.Pos;
        
        var pitch = size == 2 ? Mathf.Clamp(1.12f - cal / 500f, 0.82f, 1.08f) : Mathf.Clamp(1.1f - cal / 150f, 0.85f, 1.1f);
        var outside = inside ? 0.3f : 1f; 
        var pr = own ? 34 : size == 2 ? 55 : size == 1 ? 75 : 95;

        switch (type)
        {
            case Penetration:
                if (size == 0) Sfx.At("hit.bullet", pos, part, outside, pitch, 2.5f, pr);
                else if (size == 1)
                {
                    Sfx.At("hit.ac", pos, part, outside, pitch, 5f, pr);
                    Sfx.At("hit.debris", pos, part, 0.5f * outside, pitch, 4f, pr + 5);
                }
                else
                {
                    Sfx.At("hit.shell", pos, part, outside, pitch, 12f, pr);
                    Sfx.At("hit.debris", pos, part, 0.8f * outside, pitch, 6f, pr + 5);
                }
                if (inside)
                {
                    if (size == 0) Sfx.Flat("hit.bullet.int", 0.8f, pitch, 30);
                    else
                    {
                        
                        Sfx.Flat("hit.pen.int", 1f, pitch, 30);
                        Sfx.Flat("hit.pen.int.tear", size == 2 ? 0.85f : 0.6f, pitch, 31);
                        Sfx.Flat("hit.pen.int.ring", size == 2 ? 0.7f : 0.45f, pitch, 32);
                    }
                    if (size == 2) Sfx.Flat("hit.pen.bell", 0.7f, 1f, 33);
                }
                break;

            case NonPenetration:
                if (size == 0) Sfx.At("hit.bullet", pos, part, outside, pitch, 2.5f, pr);
                else if (size == 1)
                {
                    Sfx.At("hit.ac", pos, part, outside, pitch, 5f, pr);
                    Sfx.At("hit.ac.tonal", pos, part, 0.7f * outside, pitch, 5f, pr + 5);
                }
                else
                {
                    Sfx.At("hit.shell", pos, part, outside, pitch, 12f, pr);
                    Sfx.At("hit.shell.tonal", pos, part, 0.8f * outside, pitch, 10f, pr + 5);
                }
                if (inside) Sfx.Flat(size == 0 ? "hit.bullet.int" : size == 1 ? "hit.ac.int" : "hit.shell.int", 0.9f, pitch, 30);
                break;

            default: 
                if (size == 0) Sfx.At("hit.ricochet", pos, null, outside, Random.Range(0.92f, 1.08f), 3f, pr);
                else if (size == 1)
                {
                    Sfx.At("hit.ac.tonal", pos, part, outside, pitch, 5f, pr);
                    Sfx.At("hit.ricochet", pos, null, 0.8f * outside, 0.85f * pitch, 5f, pr + 5);
                }
                else
                {
                    Sfx.At("hit.shell.tonal", pos, part, outside, pitch, 10f, pr);
                    Sfx.At("hit.ricochet", pos, null, 0.6f * outside, 0.65f * pitch, 10f, pr + 5);
                }
                if (inside) Sfx.Flat(size == 0 ? "hit.bullet.int" : size == 1 ? "hit.ac.int" : "hit.shell.int", 0.6f, pitch, 30);
                break;
        }
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Hit sound: {(type == Penetration ? "penetration" : type == NonPenetration ? "no penetration" : "ricochet")}, {cal} mm" +
                               $"{(inside ? ", inside" : "")}");
    }

    

    private struct Pending
    {
        public Vector3 Pos;
        public int Calibre;
        public Transform Part;
    }

    private static readonly List<Pending> Explosions = new();

    private static void OnEffect(IntPtr self, Hit h)
    {
        var type = h.Type;
        if (type != Explosion && type != Ground) return;
        if (!Library.Has(type == Explosion ? "expl.he" : "hit.ground")) return;
        var fx = new ProjectileImpactEffect(self);
        var src = fx.audioSource;
        if (src != null) src.Stop();
        var cal = h.Calibre > 0 ? h.Calibre : 20;

        if (type == Explosion)
        {
            
            if (Explosions.Count < 64) Explosions.Add(new Pending { Pos = h.Pos, Calibre = cal, Part = Part(h.Parent) });
            return;
        }

        var size = cal < 15 ? 0 : cal < 45 ? 1 : 2;
        if (size < 2 && !BulletAllowed(h.Pos, size == 0 ? 200f : 500f)) return;
        var pitch = Mathf.Clamp(1.1f - cal / 400f, 0.85f, 1.1f);
        if (size == 0) Sfx.At("hit.ground.small", h.Pos, null, 1f, Random.Range(0.92f, 1.08f), 2.5f, 100);
        else
        {
            Sfx.At("hit.ground", h.Pos, null, size == 1 ? 0.8f : 1f, pitch, size == 1 ? 5f : 8f, size == 1 ? 80 : 60);
            if (size == 2) Sfx.At("hit.ground.large", h.Pos, null, Mathf.Clamp(cal / 120f, 0.5f, 1f), pitch, 12f, 62);
        }
    }

    
    public static void Tick()
    {
        if (Explosions.Count > 0)
        {
            foreach (var p in Explosions)
            {
                try
                {
                    var kind = MunitionsBridge.KindHe;
                    var cal = p.Calibre;
                    if (MunitionsBridge.DetonationNear(p.Pos, 4f, out var k, out var c))
                    {
                        kind = k;
                        if (c > 0) cal = c;
                    }
                    Explode(p.Pos, kind, cal, p.Part);
                }
                catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("Explosion sound failed: " + e); }
            }
            Explosions.Clear();
        }
        if (!Plugin.Active) return;
        for (var n = 0; n < 4 && MunitionsBridge.Unclaimed(0.15f, out var pos, out var kind, out var cal); n++)
            Explode(pos, kind, cal, null);
    }

    public static void Explode(Vector3 pos, int kind, int cal, Transform part)
    {
        string bank;
        float min;
        if (kind == MunitionsBridge.KindGunAtgm || kind == MunitionsBridge.KindTubeAtgm) { bank = "expl.missile"; min = 30f; }
        else if (kind == MunitionsBridge.KindHeat) { bank = "expl.heat"; min = 25f; }
        else if (kind == MunitionsBridge.KindRocketHe) { bank = cal >= 150 ? "expl.large" : "expl.rocket"; min = 30f; }
        else if (kind == MunitionsBridge.KindRocketTb)
        {
            
            bank = "expl.large";
            min = 45f;
            Sfx.At("expl.missile", pos, null, 0.85f, 0.8f * Random.Range(0.96f, 1.04f), 40f, 46);
        }
        else if (cal < 45) { bank = "expl.small"; min = 12f; }
        else if (cal < 100) { bank = "expl.he"; min = 25f; }
        else { bank = "expl.large"; min = 35f; }
        var pitch = Mathf.Clamp(1.08f - cal / 1000f, 0.9f, 1.08f) * Random.Range(0.96f, 1.04f);
        Sfx.At(bank, pos, null, 1f, pitch, min, 45);
        
        if (cal >= 75 && kind != MunitionsBridge.KindHeat)
            Sfx.At(cal >= 120 ? "expl.debris.medium" : "expl.debris.small", pos, null, 0.6f, 1f, 10f, 70);
        if (part != null && View.Inside(part)) Sfx.Flat("hit.shell.int", 0.8f, 0.9f, 30);
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Explosion sound: {bank}, {cal} mm");
    }

    public static void Clear() => Explosions.Clear();
}
