using System;
using System.Collections.Generic;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace GunStabilizer;

internal static class Rot
{
    
    
    public static Vector3 Vector(Quaternion q)
    {
        if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
        var v = new Vector3(q.x, q.y, q.z);
        var s = v.magnitude;
        if (s < 1e-12f) return Vector3.zero;
        var angle = 2f * Mathf.Atan2(s, q.w) * Mathf.Rad2Deg;
        return v * (angle / s);
    }
}


internal sealed class Ou
{
    private static readonly System.Random Rng = new();
    private readonly float _time, _sigma;
    public float Value;

    public Ou(float time, float sigma) { _time = time; _sigma = sigma; }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        var a = Math.Exp(-dt / _time);
        Value = (float)(Value * a + _sigma * Math.Sqrt(1 - a * a) * Gauss());
    }

    private static double Gauss()
    {
        var u1 = 1.0 - Rng.NextDouble();
        var u2 = Rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}





internal sealed class GunnerTracker
{
    private const float MaxPerceivedRate = 40f; 
    private const float MaxHandRate = 25f;      
    private const float LostAim = 45f;          
    private const float Deadband = 0.02f;       

    private bool _init;
    private float _prevTarget, _rate;
    public float Cmd;

    public void Sync(float target)
    {
        _init = true;
        Cmd = target;
        _prevTarget = target;
        _rate = 0f;
    }

    
    public float Step(float target, float command, float dt, bool wrap)
    {
        if (!_init) { Sync(target); return Cmd; }
        var tau = Mathf.Max(0.05f, Plugin.GunnerReaction.Value);
        Cmd += command;

        var total = wrap ? Mathf.DeltaAngle(_prevTarget, target) : target - _prevTarget;
        _prevTarget = target;
        var seenRate = Mathf.Clamp((total - command) / dt, -MaxPerceivedRate, MaxPerceivedRate);
        _rate += (seenRate - _rate) * (1f - Mathf.Exp(-dt / (0.7f * tau)));

        var err = wrap ? Mathf.DeltaAngle(Cmd, target) : target - Cmd;
        if (Mathf.Abs(err) > LostAim) { Sync(target); return Cmd; }
        var step = _rate * dt;
        if (Mathf.Abs(err) > Deadband) step += err * (1f - Mathf.Exp(-dt / tau));
        Cmd += Mathf.Clamp(step, -MaxHandRate * dt, MaxHandRate * dt);
        if (wrap) Cmd = Mathf.DeltaAngle(0f, Cmd);
        return Cmd;
    }
}


internal sealed class StabUnit
{
    public readonly int Key;
    public float PowerOnTime, LastSeen, EngagedAt;
    public bool Engaged = true;
    public readonly Ou DriftV = new(StabSpec.DriftTime, StabSpec.VerticalDrift);
    public readonly Ou DriftH = new(StabSpec.DriftTime, StabSpec.HorizontalDrift);
    private float _driftTime;

    public StabUnit(int key)
    {
        Key = key;
        PowerOn();
    }

    public void PowerOn()
    {
        PowerOnTime = Time.time;
        LastSeen = Time.time;
        EngagedAt = Time.time - StabSpec.EngageDelay;
        _driftTime = Time.time;
    }

    
    public bool Engaging => Engaged && Time.time - EngagedAt < StabSpec.EngageDelay;
    public bool Ready => Engaged && Time.time - EngagedAt >= StabSpec.EngageDelay;

    public void Touch()
    {
        var now = Time.time;
        
        if (now - LastSeen > 3f) PowerOn();
        LastSeen = now;
        var dt = now - _driftTime;
        if (dt <= 0f) return;
        _driftTime = now;
        DriftV.Step(dt);
        DriftH.Step(dt);
    }

    public void Toggle()
    {
        Engaged = !Engaged;
        if (Engaged) EngagedAt = Time.time;
    }
}


internal sealed class DriveState
{
    public IntPtr Ptr;
    public bool IsTurret;
    public TurretBehaviour Tb;
    public LayingDriveBehaviour Ldb;
    public Cannon Cannon;          
    public TraverseMotor Motor;    
    public LayingDrive Drive;      
    public bool CasemateAzimuth;   
    public DriveState Peer;        
    public int VehicleKey;
    public StabUnit Unit;

    
    public bool FlagStab, PowerOk = true, Healthy = true;
    public float FlagsCheckedAt = -100f;
    public float LastUpdate;       
    public float LastSeen;         

    
    public Quaternion PrevFoundation;
    public IntPtr PrevFoundationPtr;
    public bool HasPrev;
    public float RateMain, RateAz;

    public readonly GunnerTracker Main = new(), Az = new();
    public Vector3 PrevAim;        
    public bool HasPrevAim;
    public Vector3 LastOutput;     
    public bool HasOutput;

    
    
    public bool IsPitch;

    
    
    public Vector3 TrueAim(Vector3 current) => HasOutput && HasRaw && current == LastOutput ? PrevRaw : current;
    public Vector3 PrevRaw;
    public bool HasRaw;
    public readonly Ou Noise = new(StabSpec.NoiseTime, 1f);
    public float Residual;         

    
    public float StepResidual(float dt, float lag)
    {
        Noise.Step(dt);
        var sigma = Mathf.Min(StabSpec.NoiseMax, StabSpec.NoiseBase + StabSpec.NoisePerRate * Mathf.Abs(RateMain));
        Residual = lag * RateMain + Noise.Value * sigma;
        return Residual;
    }
    public bool StabActive;        
    public float CommandError;     

    public bool ChannelAvailable => FlagStab && PowerOk && Healthy;

    
    
    public void UpdateRates(Quaternion foundation, IntPtr foundationPtr, float dt, bool turret, Vector3 turretAxis = default)
    {
        if (!HasPrev || PrevFoundationPtr != foundationPtr)
        {
            PrevFoundation = foundation;
            PrevFoundationPtr = foundationPtr;
            HasPrev = true;
            RateMain = RateAz = 0f;
            return;
        }
        var rot = Rot.Vector(Quaternion.Inverse(PrevFoundation) * foundation);
        PrevFoundation = foundation;
        if (float.IsNaN(rot.x) || rot.magnitude > 30f) return;
        var v = rot / dt; 
        var k = 1f - Mathf.Exp(-dt / 0.03f);
        if (turret) RateMain += ((turretAxis == default ? v.y : Vector3.Dot(v, turretAxis)) - RateMain) * k; 
        else
        {
            RateMain += (-v.x - RateMain) * k;                 
            RateAz += (v.y - RateAz) * k;
        }
    }
}








internal sealed class AimChain
{
    public DriveState Yaw, Pitch;
    public Transform Hull;
    public bool FreeYaw;
    public float ResolvedAt;
}

internal static class Registry
{
    private static readonly Dictionary<IntPtr, DriveState> Drives = new();
    private static readonly Dictionary<IntPtr, AimChain> Chains = new();
    private static readonly Dictionary<int, StabUnit> Units = new();
    private static readonly Dictionary<IntPtr, TraverseMotor> MotorByTurret = new();
    private static readonly Dictionary<IntPtr, TurretBehaviour> Turrets = new();
    private static readonly Dictionary<IntPtr, TurretBehaviour> TurretAboveCache = new();
    private static readonly Dictionary<IntPtr, LayingDrive> DriveByBehaviour = new();
    private static readonly Dictionary<IntPtr, Cannon> CannonByVertical = new();
    private static readonly Dictionary<IntPtr, Cannon> CannonByHorizontal = new();
    private static float _lastScan = -100f;

    public static DriveState Peek(IntPtr ptr) => ptr != IntPtr.Zero && Drives.TryGetValue(ptr, out var s) ? s : null;

    public static StabUnit PeekUnit(int key) => Units.TryGetValue(key, out var u) ? u : null;

    public static StabUnit GetUnit(int key)
    {
        if (!Units.TryGetValue(key, out var u))
        {
            u = new StabUnit(key);
            Units[key] = u;
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Stabilizer unit powered up for vehicle {key}");
        }
        return u;
    }

    public static int VehicleKeyOf(Transform t)
    {
        if (t == null) return 0;
        var v = t.GetComponentInParent<Vehicle>();
        return v != null ? v.gameObject.GetInstanceID() : t.root.gameObject.GetInstanceID();
    }

    public static DriveState Get(TurretBehaviour tb)
    {
        var ptr = tb.Pointer;
        if (Drives.TryGetValue(ptr, out var s)) { s.LastSeen = Time.time; Refresh(s); return s; }
        s = new DriveState { Ptr = ptr, IsTurret = true, Tb = tb, LastSeen = Time.time };
        s.VehicleKey = VehicleKeyOf(tb.transform);
        s.Unit = GetUnit(s.VehicleKey);
        Drives[ptr] = s;
        Refresh(s);
        return s;
    }

    public static DriveState Get(LayingDriveBehaviour ldb)
    {
        var ptr = ldb.Pointer;
        if (Drives.TryGetValue(ptr, out var s)) { s.LastSeen = Time.time; Refresh(s); return s; }
        s = new DriveState { Ptr = ptr, IsTurret = false, Ldb = ldb, LastSeen = Time.time };
        s.VehicleKey = VehicleKeyOf(ldb.trunnions);
        s.Unit = GetUnit(s.VehicleKey);
        Drives[ptr] = s;
        Refresh(s);
        return s;
    }

    
    
    public static TurretBehaviour TurretAbove(Transform t)
    {
        if (t == null) return null;
        if (TurretAboveCache.TryGetValue(t.Pointer, out var cached)) return cached;
        var found = FindTurretAbove(t);
        if (found == null && Scan()) found = FindTurretAbove(t);
        if (found != null) TurretAboveCache[t.Pointer] = found;
        return found;
    }

    private static TurretBehaviour FindTurretAbove(Transform t)
    {
        TurretBehaviour best = null;
        Transform bestTr = null;
        foreach (var tb in Turrets.Values)
        {
            try
            {
                var tr = tb.transform;
                if (tr == null || !t.IsChildOf(tr)) continue;
                if (bestTr == null || tr.IsChildOf(bestTr)) { best = tb; bestTr = tr; }
            }
            catch { }
        }
        return best;
    }

    
    public static TurretBehaviour YawTurretAbove(Transform t)
    {
        TurretBehaviour best = null;
        Transform bestTr = null;
        foreach (var tb in TurretsAbove(t))
        {
            if (Get(tb).IsPitch) continue;
            var tr = tb.transform;
            if (bestTr == null || tr.IsChildOf(bestTr)) { best = tb; bestTr = tr; }
        }
        return best;
    }

    
    private static List<TurretBehaviour> TurretsAbove(Transform t)
    {
        var list = new List<TurretBehaviour>();
        if (t == null) return list;
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var tb in Turrets.Values)
            {
                try
                {
                    var tr = tb.transform;
                    if (tr != null && t.IsChildOf(tr)) list.Add(tb);
                }
                catch { }
            }
            if (list.Count > 0 || !Scan()) break;
        }
        return list;
    }

    
    public static AimChain ChainFor(Cannon cannon)
    {
        if (cannon == null) return null;
        var now = Time.time;
        if (Chains.TryGetValue(cannon.Pointer, out var chain) && now - chain.ResolvedAt < 2f && now >= chain.ResolvedAt) return chain;
        chain = new AimChain { ResolvedAt = now };
        Chains[cannon.Pointer] = chain;
        try
        {
            var weapon = cannon.Behaviour;
            var anchor = weapon != null && weapon.transform != null ? weapon.transform : cannon.transform;

            
            var turrets = TurretsAbove(anchor);
            turrets.Sort((a, b) => a.transform.IsChildOf(b.transform) ? 1 : b.transform.IsChildOf(a.transform) ? -1 : 0);
            TurretBehaviour yaw = null, pitch = null;
            foreach (var tb in turrets)
            {
                var st = Get(tb);
                if (st.IsPitch) pitch = tb;            
                else if (yaw == null) yaw = tb;       
            }

            
            var v = cannon.VerticalAimingMechanism;
            var ldb = v != null ? v.TryCast<LayingDriveBehaviour>() : null;
            var vTurret = v != null ? v.TryCast<TurretBehaviour>() : null;
            if (ldb != null) chain.Pitch = Get(ldb);
            else if (vTurret != null && Get(vTurret).IsPitch) chain.Pitch = Get(vTurret);
            else if (pitch != null) chain.Pitch = Get(pitch);

            
            if (yaw != null) chain.Yaw = Get(yaw);
            else chain.FreeYaw = anchor.GetComponentInParent<TurretRing>() != null;

            chain.Hull = HullOf(cannon);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Aim chain failed: " + e.Message);
        }
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Aim chain for {cannon.name}: yaw={(chain.Yaw == null ? "none" : chain.Yaw.IsTurret ? "turret" : "mantlet")}, " +
                               $"pitch={(chain.Pitch == null ? "none" : chain.Pitch.IsTurret ? "sideways ring" : "laying drive")}, hull={(chain.Hull != null ? chain.Hull.name : "unknown")}");
        return chain;
    }

    
    public static Transform HullOf(Component c)
    {
        if (c == null) return null;
        var v = c.GetComponentInParent<Vehicle>();
        if (v == null) return null;
        try
        {
            var rb = v.Rigidbody;
            if (rb != null) return rb.transform;
        }
        catch { }
        return v.transform;
    }

    
    
    public static bool Stabilized(DriveState s) => s != null && s.ChannelAvailable && s.Unit != null && s.Unit.Ready;

    
    private static bool IsSidewaysRing(TurretBehaviour tb)
    {
        var f = tb.foundation;
        if (f == null) return false;
        var axis = f.rotation * tb.axis;
        if (axis.sqrMagnitude < 1e-6f) return false;
        var vehicle = tb.transform.GetComponentInParent<Vehicle>();
        var up = vehicle != null ? vehicle.transform.up : Vector3.up;
        return Mathf.Abs(Vector3.Dot(axis.normalized, up)) < 0.7f;
    }

    
    private static void Refresh(DriveState s)
    {
        var now = Time.time;
        if (now - s.FlagsCheckedAt < 1f) return;
        s.FlagsCheckedAt = now;
        try
        {
            if (s.IsTurret)
            {
                s.IsPitch = IsSidewaysRing(s.Tb);
                if (s.Motor == null && !MotorByTurret.TryGetValue(s.Ptr, out s.Motor) && Scan()) MotorByTurret.TryGetValue(s.Ptr, out s.Motor);
                s.FlagStab = StabSettings.Horizontal(s.Motor);
                s.PowerOk = StabSettings.PoweredTraverse(s.Motor);
                s.Healthy = s.Motor == null || s.Motor.HealthFraction >= StabSpec.MinHealth;
            }
            else
            {
                if (s.Drive == null && !DriveByBehaviour.TryGetValue(s.Ptr, out s.Drive) && Scan()) DriveByBehaviour.TryGetValue(s.Ptr, out s.Drive);
                if (s.Cannon == null)
                {
                    if (!CannonByVertical.TryGetValue(s.Ptr, out s.Cannon) && Scan()) CannonByVertical.TryGetValue(s.Ptr, out s.Cannon);
                    if (s.Cannon == null && CannonByHorizontal.TryGetValue(s.Ptr, out var hc)) { s.Cannon = hc; s.CasemateAzimuth = true; }
                }
                
                if (s.Peer == null)
                {
                    var tb = YawTurretAbove(s.Ldb.foundation ?? s.Ldb.trunnions);
                    if (tb != null) s.Peer = Get(tb);
                }
                s.CasemateAzimuth = s.Peer == null;
                s.FlagStab = StabSettings.Vertical(s.Drive);
                s.PowerOk = true;
                s.Healthy = s.Ldb.HealthFraction >= StabSpec.MinHealth;
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Drive refresh failed: " + e.Message);
        }
    }

    
    private static bool Scan()
    {
        var now = Time.time;
        if (now - _lastScan < 1f) return false;
        _lastScan = now;
        try
        {
            foreach (var motor in UnityEngine.Object.FindObjectsOfType<TraverseMotor>())
            {
                var b = motor.Behaviour;
                var tb = b != null ? b.TryCast<TurretBehaviour>() : null;
                if (tb == null) continue;
                MotorByTurret[tb.Pointer] = motor;
                Turrets[tb.Pointer] = tb;
            }
            foreach (var drive in UnityEngine.Object.FindObjectsOfType<LayingDrive>())
            {
                var b = drive.Behaviour;
                var ldb = b != null ? b.TryCast<LayingDriveBehaviour>() : null;
                if (ldb != null) DriveByBehaviour[ldb.Pointer] = drive;
            }
            foreach (var cannon in UnityEngine.Object.FindObjectsOfType<Cannon>())
            {
                var v = cannon.VerticalAimingMechanism;
                var vl = v != null ? v.TryCast<LayingDriveBehaviour>() : null;
                if (vl != null) CannonByVertical[vl.Pointer] = cannon;
                var h = cannon.HorizontalAimingMechanism;
                var hl = h != null ? h.TryCast<LayingDriveBehaviour>() : null;
                if (hl != null) CannonByHorizontal[hl.Pointer] = cannon;
            }
            if (Plugin.DebugLogging.Value)
                Plugin.Log.LogInfo($"Drive scan: {MotorByTurret.Count} traverse motors, {DriveByBehaviour.Count} laying drives, {CannonByVertical.Count} cannons");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Drive scan failed: " + e.Message); }
        return true;
    }

    
    
    
    private static bool IsGone(DriveState s)
    {
        try { return s.IsTurret ? s.Tb == null || s.Tb.transform == null : s.Ldb == null || s.Ldb.trunnions == null; }
        catch { return true; }
    }

    public static void Cleanup()
    {
        var now = Time.time;
        List<IntPtr> dead = null;
        foreach (var kv in Drives)
            if (IsGone(kv.Value) || now - kv.Value.LastSeen > 60f || now < kv.Value.LastSeen) (dead ??= new List<IntPtr>()).Add(kv.Key);
        if (dead != null)
        {
            foreach (var p in dead) Drives.Remove(p);
            Chains.Clear();
            MotorByTurret.Clear();
            Turrets.Clear();
            TurretAboveCache.Clear();
            DriveByBehaviour.Clear();
            CannonByVertical.Clear();
            CannonByHorizontal.Clear();
        }

        List<int> deadUnits = null;
        foreach (var kv in Units)
            if (now - kv.Value.LastSeen > 60f || now < kv.Value.LastSeen) (deadUnits ??= new List<int>()).Add(kv.Key);
        if (deadUnits != null) foreach (var k in deadUnits) Units.Remove(k);
    }
}
