using System;
using System.Collections.Generic;
using Sprocket.Vehicles;
using UnityEngine;

namespace ActiveProtection;


internal sealed class Module
{
    
    public static readonly Vector3 DetectorSize = new(0.26f, 0.26f, 0.26f);
    public static readonly Vector3 LauncherSize = new(0.11f, 0.11f, 0.32f);
    
    public static readonly Vector3 WarnerSize = new(0.12f, 0.1f, 0.07f);
    public static readonly Vector3 SmokeSize = new(0.26f, 0.16f, 0.16f);
    public static readonly Vector3 JammerSize = new(0.2f, 0.18f, 0.16f);

    public static Vector3 BaseSize(ModuleKind k) => k switch
    {
        ModuleKind.Detector or ModuleKind.RadioJammer => DetectorSize,
        ModuleKind.Launcher => LauncherSize,
        ModuleKind.Warner => WarnerSize,
        ModuleKind.Smoke => SmokeSize,
        ModuleKind.Era => new Vector3(0.6f, 0.4f, 0.1f),
        ModuleKind.Panel => new Vector3(1.2f, 0.6f, 0.02f),
        _ => JammerSize
    };

    public VehicleObject Vo;
    public IntPtr Ptr;
    public ModuleKind Kind;
    public ApsSettings S;
    public bool FromFile;          
    public bool AutoLinkDone;
    public int Version;            

    
    public bool InBattle;
    public int ChargesLeft;
    public float LastShot = -100f;
    public bool Destroyed;
    public float LastFlash = -100f;
    public float JamUntil = -100f;        
    public bool JamOn;                    
    public readonly HashSet<int> SpentTiles = new();   
    public bool Vented;                   
    public float VentedAt = -100f;

    public ModuleVisual Visual;
    public Vehicle VehicleCached;
    public float VehicleAt = -100f;

    public bool Alive
    {
        get
        {
            try { return Vo != null && !Vo.WasCollected && Vo.transform != null; }
            catch { return false; }
        }
    }

    public Transform T => Vo.transform;

    public int Vuid
    {
        get
        {
            try { return Vo.VUID.Value; }
            catch { return 0; }
        }
    }

    public Vector3 PartScale
    {
        get
        {
            try
            {
                var vt = Vo.VehicleTransform;
                if (vt != null)
                {
                    var s = vt.scale;
                    s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)); 
                    if (s.x > 0.01f && s.y > 0.01f && s.z > 0.01f) return s;
                }
            }
            catch { }
            return Vector3.one;
        }
    }

    
    public Vector3 Size
    {
        get
        {
            return Kind switch
            {
                ModuleKind.Era => SyncScale().EraSize,
                ModuleKind.Panel => SyncScale().PanelSize,
                _ => Vector3.Scale(BaseSize(Kind), PartScale)
            };
        }
    }

    
    private float _scaleAt = -100f;

    public ApsSettings SyncScale()
    {
        if (Kind is not (ModuleKind.Era or ModuleKind.Panel) || S == null) return S;
        var now = Time.unscaledTime;
        if (InBattle && now - _scaleAt < 2f && now >= _scaleAt) return S;
        _scaleAt = now;
        S.Scale = PartScale;
        return S;
    }

    public Quaternion Rotation => T.rotation;

    
    public Vector3 Center => T.position + T.rotation * new Vector3(0f, 0f, Size.z * 0.5f);

    
    public Vector3 Muzzle => T.position + T.rotation * new Vector3(0f, 0f, Size.z);

    public Vector3 ToLocalDir(Vector3 world) => Quaternion.Inverse(T.rotation) * world;

    public void SetSettings(ApsSettings s, bool bumpVersion = true)
    {
        s.Sanitize();
        if (S != null) s.Scale = S.Scale;
        var massChanged = Kind is ModuleKind.Era or ModuleKind.Panel && (S == null || Mathf.Abs(S.PartMass - s.PartMass) > 0.01f);
        S = s;
        if (bumpVersion) Version++;
        if (!InBattle) ChargesLeft = S.Charges;
        if (massChanged) Era.ApplyMass(this, null);
    }
}

internal static class Modules
{
    private static readonly Dictionary<IntPtr, Module> All = new();
    private static readonly List<Module> Buffer = new();

    public static int Count => All.Count;

    public static Module Get(VehicleObject vo)
    {
        if (vo == null) return null;
        try
        {
            if (All.TryGetValue(vo.Pointer, out var m) && m.Alive && m.Vo.Pointer == vo.Pointer) return m;
        }
        catch { }
        return null;
    }

    public static Module Get(IntPtr ptr) => All.TryGetValue(ptr, out var m) && m.Alive ? m : null;

    
    public static Module Ensure(VehicleObject vo)
    {
        var kind = ApsParts.KindOf(vo);
        if (kind == null) return null;
        var existing = Get(vo);
        if (existing != null && existing.Kind == kind.Value) return existing;
        var m = new Module { Vo = vo, Ptr = vo.Pointer, Kind = kind.Value };
        m.SetSettings(ApsSettings.Default(kind.Value));
        All[vo.Pointer] = m;
        return m;
    }

    
    public static void Load(VehicleObject vo, string token)
    {
        var m = Ensure(vo);
        if (m == null) return;
        if (string.IsNullOrEmpty(token)) return;
        var tokenKind = ApsSettings.KindOfToken(token);
        if (tokenKind != null && tokenKind.Value != m.Kind) return;
        m.SetSettings(ApsSettings.FromToken(token, m.Kind));
        m.FromFile = true;
        m.AutoLinkDone = true;
    }

    
    public static List<Module> Snapshot()
    {
        Buffer.Clear();
        List<IntPtr> dead = null;
        foreach (var kv in All)
        {
            if (kv.Value.Alive) Buffer.Add(kv.Value);
            else (dead ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (dead != null)
            foreach (var k in dead)
            {
                if (All.TryGetValue(k, out var m)) m.Visual?.Destroy();
                All.Remove(k);
            }
        return Buffer;
    }

    
    public static Vehicle VehicleOf(Module m)
    {
        var now = Time.unscaledTime;
        try
        {
            if (m.VehicleCached != null && now - m.VehicleAt < 1f && now >= m.VehicleAt && !m.VehicleCached.WasCollected && m.VehicleCached)
                return m.VehicleCached;
        }
        catch { }
        try { m.VehicleCached = m.T.GetComponentInParent<Vehicle>(); }
        catch { m.VehicleCached = null; }
        m.VehicleAt = now;
        return m.VehicleCached;
    }

    
    public static List<Module> OnVehicle(Vehicle v, ModuleKind? kind = null)
    {
        var list = new List<Module>();
        if (v == null) return list;
        foreach (var m in Snapshot())
        {
            if (kind != null && m.Kind != kind.Value) continue;
            var mv = VehicleOf(m);
            if (mv != null && mv.Pointer == v.Pointer) list.Add(m);
        }
        list.Sort((a, b) => a.Vuid.CompareTo(b.Vuid));
        return list;
    }

    
    public static Module DetectorOf(Module m, List<Module> sameVehicle)
    {
        var target = EoInfo.LinkTarget(m.Kind);
        if (target == null || m.S.Detector == 0) return null;
        foreach (var x in sameVehicle)
            if (x.Kind == target.Value && x.Vuid == m.S.Detector) return x;
        return null;
    }

    
    public static Module MirrorOf(Module m)
    {
        try
        {
            var vt = m.Vo.VehicleTransform;
            var mirror = vt != null ? vt.Mirror : null;
            var vo = mirror != null ? mirror.VehicleObject : null;
            return vo != null ? Get(vo) : null;
        }
        catch { return null; }
    }

    public static bool IsMirroredCopy(Module m)
    {
        try { return m.Vo.VehicleTransform != null && m.Vo.VehicleTransform.IsMirrored; }
        catch { return false; }
    }
}
