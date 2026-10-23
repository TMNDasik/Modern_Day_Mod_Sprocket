using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Turrets;
using Sprocket.Vehicles.Weapons;
using Sprocket.Vehicles.Weapons.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;

namespace Munitions;



internal enum LoaderType { Az, Mz, Belt }





internal struct CarouselSpec
{
    public const float DefaultSpeed = 70f, DefaultAccel = 90f, DefaultAngle = 3f, DefaultRam = 3f;
    public const float MinSpeed = 10f, MaxSpeed = 180f, MinAccel = 10f, MaxAccel = 720f;
    public const float BeltDefaultSpeed = 4f, BeltDefaultAccel = 10f, BeltDefaultAngle = 0f, BeltDefaultRam = 2f;
    public const float BeltMinSpeed = 0.5f, BeltMaxSpeed = 12f, BeltMinAccel = 1f, BeltMaxAccel = 60f;
    public const float MinAngle = -10f, MaxAngle = 20f, MinRam = 0.5f, MaxRam = 10f;

    public float Speed, Accel, LoadAngle, RamTime;
    public LoaderType Type;

    public bool Mz => Type == LoaderType.Mz;
    public bool Belt => Type == LoaderType.Belt;

    public string LoaderName => Type switch { LoaderType.Mz => "Mechanized loader", LoaderType.Belt => "Belt loader", _ => "Autoloader" };
    
    public string Magazine => Belt ? "belt" : "carousel";

    public static CarouselSpec Default => new() { Speed = DefaultSpeed, Accel = DefaultAccel, LoadAngle = DefaultAngle, RamTime = DefaultRam };

    public static CarouselSpec DefaultFor(LoaderType type) => type == LoaderType.Belt
        ? new CarouselSpec { Speed = BeltDefaultSpeed, Accel = BeltDefaultAccel, LoadAngle = BeltDefaultAngle, RamTime = BeltDefaultRam, Type = type }
        : new CarouselSpec { Speed = DefaultSpeed, Accel = DefaultAccel, LoadAngle = DefaultAngle, RamTime = DefaultRam, Type = type };

    private static readonly Regex Token = new(@"\s?\[az:([0-9.\-]+),([0-9.\-]+),([0-9.\-]+),([0-9.\-]+)(?:,(az|mz|belt))?\]", RegexOptions.Compiled);

    public static bool TryRead(string description, out CarouselSpec s)
    {
        s = Default;
        if (string.IsNullOrEmpty(description)) return false;
        var m = Token.Match(description);
        if (!m.Success) return false;
        float Get(int i, float def) => float.TryParse(m.Groups[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        var type = m.Groups[5].Success ? m.Groups[5].Value switch { "mz" => LoaderType.Mz, "belt" => LoaderType.Belt, _ => LoaderType.Az } : LoaderType.Az;
        s = DefaultFor(type);
        if (type == LoaderType.Belt)
        {
            s.Speed = Mathf.Clamp(Get(1, BeltDefaultSpeed), BeltMinSpeed, BeltMaxSpeed);
            s.Accel = Mathf.Clamp(Get(2, BeltDefaultAccel), BeltMinAccel, BeltMaxAccel);
        }
        else
        {
            s.Speed = Mathf.Clamp(Get(1, DefaultSpeed), MinSpeed, MaxSpeed);
            s.Accel = Mathf.Clamp(Get(2, DefaultAccel), MinAccel, MaxAccel);
        }
        s.LoadAngle = Mathf.Clamp(Get(3, s.LoadAngle), MinAngle, MaxAngle);
        s.RamTime = Mathf.Clamp(Get(4, s.RamTime), MinRam, MaxRam);
        return true;
    }

    
    public static string With(string description, CarouselSpec? s)
    {
        var d = Token.Replace(description ?? "", "");
        if (s == null) return d.Length == 0 ? null : d;
        var v = s.Value;
        var token = string.Format(CultureInfo.InvariantCulture, "[az:{0:0.#},{1:0.#},{2:0.#},{3:0.##}{4}]", v.Speed, v.Accel, v.LoadAngle, v.RamTime,
            v.Type switch { LoaderType.Mz => ",mz", LoaderType.Belt => ",belt", _ => "" });
        return d.Length == 0 ? token : d + " " + token;
    }
}

internal enum AutoloaderPhase { Idle, Rotating, Ramming }


internal sealed class Carousel
{
    public RackState Rack;
    public CarouselSpec Spec;
    public int[] Cells;                 
    public float Angle, Velocity;       
    public int TargetCell = -1;
    public AutoloaderPhase Phase;
    public float PhaseStart;
    public float CycleEnd;              
    public float CycleTotal;            
    public float CycleStart;
    public int LoadingKind = -1;
    public LoaderVoice Voice;           
    public bool RotationHeard;          

    public Cannon Gun;
    public CannonBehaviour Weapon;
    public IntPtr WeaponPtr;
    public LayingDriveBehaviour Ldb;    
    public TurretBehaviour Ring;
    public float NextGunSearch;
    public bool Preloaded;

    public float Step => 360f / Math.Max(1, Cells.Length);
    public bool Busy => Phase != AutoloaderPhase.Idle;

    
    
    public float DegSpeed => Spec.Belt ? Spec.Speed * Step : Spec.Speed;
    public float DegAccel => Spec.Belt ? Spec.Accel * Step : Spec.Accel;

    public int Count(int kind)
    {
        var n = 0;
        foreach (var c in Cells) if (c == kind) n++;
        return n;
    }

    public int Total
    {
        get
        {
            var n = 0;
            foreach (var c in Cells) if (c >= 0) n++;
            return n;
        }
    }
}





internal static class Autoloader
{
    private static readonly Dictionary<IntPtr, Carousel> ByRack = new();
    private static readonly Dictionary<IntPtr, Carousel> ByWeapon = new();
    private static readonly Dictionary<IntPtr, Carousel> LockedDrives = new();
    private static readonly System.Random Rng = new();

    public static bool Enabled => Plugin.AutoloaderEnabled.Value;

    public static bool IsCarouselRack(RackState s) => s != null && ByRack.ContainsKey(s.Ptr);

    public static bool ForWeapon(IntPtr weapon, out Carousel c) => ByWeapon.TryGetValue(weapon, out c);

    
    public static bool IsLocked(IntPtr weapon) => ByWeapon.TryGetValue(weapon, out var c) && c.Busy;

    

    
    public static int[] Layout(int[] counts, int cells)
    {
        var result = new int[Math.Max(1, cells)];
        for (var i = 0; i < result.Length; i++) result[i] = -1;
        var list = new List<(double Key, int Kind)>();
        for (var k = 0; k < counts.Length; k++)
            for (var j = 0; j < counts[k]; j++)
                list.Add(((j + 0.5) / counts[k], k));
        list.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Kind.CompareTo(b.Kind));
        var total = Math.Min(list.Count, result.Length);
        for (var i = 0; i < total; i++)
        {
            var cell = (int)Math.Floor((i + 0.5) * result.Length / total);
            result[Math.Min(cell, result.Length - 1)] = list[i].Kind;
        }
        return result;
    }

    

    public static void Setup(RackState s, CarouselSpec spec)
    {
        var cells = Math.Max(1, s.Total);
        try { cells = Math.Max(cells, s.Beh.Capacity); } catch { }
        var c = new Carousel { Rack = s, Spec = spec, Cells = Layout(s.Count, cells) };
        Forget(s.Ptr);
        ByRack[s.Ptr] = c;
        if (Plugin.DebugLogging.Value || spec.Belt)
            Plugin.Log.LogInfo($"{spec.LoaderName} {spec.Magazine}: {c.Cells.Length} cells, {Racks.Describe(s.Count)}" +
                               (spec.Belt ? $", {spec.Speed:0.#} cells/s, accel {spec.Accel:0.#} cells/s², loading angle {spec.LoadAngle:0.#}°" : ""));
    }

    private static void Forget(IntPtr rack)
    {
        if (!ByRack.TryGetValue(rack, out var old)) return;
        Abort(old, null);
        old.Voice?.Destroy();
        old.Voice = null;
        ByRack.Remove(rack);
        if (old.WeaponPtr != IntPtr.Zero && ByWeapon.TryGetValue(old.WeaponPtr, out var w) && w == old) ByWeapon.Remove(old.WeaponPtr);
    }

    
    private static void FindGun(Carousel c)
    {
        var rack = c.Rack.Rack;
        var vehicle = rack.GetComponentInParent<Vehicle>();
        var root = vehicle != null ? vehicle.transform : rack.transform.root;
        Cannon best = null;
        var bestDist = float.MaxValue;
        foreach (var cannon in root.GetComponentsInChildren<Cannon>(true))
        {
            if (cannon == null || LauncherPart.IsLauncher(cannon)) continue;
            var weapon = cannon.Behaviour;
            var task = weapon?.loadTask?.TryCast<LoadTask>();
            if (task == null || task.Request.SizeID.Value != c.Rack.SizeValue) continue;
            if (ByWeapon.TryGetValue(weapon.Pointer, out var other) && other != c) continue; 
            var d = (cannon.transform.position - rack.transform.position).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = cannon; }
        }
        if (best == null) return;
        c.Gun = best;
        c.Weapon = best.Behaviour;
        c.WeaponPtr = c.Weapon.Pointer;
        ByWeapon[c.WeaponPtr] = c;
        FindElevationDrive(c, root);
        Plugin.Log.LogInfo($"{c.Spec.LoaderName}: {c.Spec.Magazine} of {c.Cells.Length} cells serves {best.name} " +
                           $"({(c.Ldb != null ? "laying drive" : c.Ring != null ? "sideways turret ring" : "fixed gun")})");
    }

    
    private static void FindElevationDrive(Carousel c, Transform root)
    {
        c.Ldb = null;
        c.Ring = null;
        var v = c.Gun.VerticalAimingMechanism;
        c.Ldb = v?.TryCast<LayingDriveBehaviour>();
        if (c.Ldb != null) return;
        var ring = v?.TryCast<TurretBehaviour>();
        if (ring != null && Sideways(ring, root)) { c.Ring = ring; return; }

        var weaponT = c.Weapon.transform;
        Transform bestTr = null;
        foreach (var motor in root.GetComponentsInChildren<TraverseMotor>(true))
        {
            var tb = motor?.Behaviour?.TryCast<TurretBehaviour>();
            if (tb == null || tb.transform == null || !weaponT.IsChildOf(tb.transform) || !Sideways(tb, root)) continue;
            if (bestTr == null || tb.transform.IsChildOf(bestTr)) { c.Ring = tb; bestTr = tb.transform; }
        }
    }

    private static bool Sideways(TurretBehaviour tb, Transform root)
    {
        var f = tb.foundation;
        if (f == null) return false;
        var axis = f.rotation * tb.axis;
        return axis.sqrMagnitude > 1e-6f && Mathf.Abs(Vector3.Dot(axis.normalized, root.up)) < 0.7f;
    }

    

    
    public static bool HandleRequest(LoadTask task)
    {
        if (!Enabled) return false;
        var target = task.target;
        if (target == null || !ByWeapon.TryGetValue(target.Pointer, out _)) return false;
        try
        {
            if ((int)task.state == (int)LoadState.Loaded && (int)task.currentRequest == (int)LoadRequest.Consumed)
                task.SetState(LoadState.Unloaded); 
            task.currentRequest = LoadRequest.None;
        }
        catch (Exception e) { Plugin.Log.LogError("Autoloader request failed: " + e.Message); }
        return true;
    }

    

    
    public static string Request(Carousel c, int kind)
    {
        if (c.Busy) return c.Spec.LoaderName.ToUpperInvariant() + " BUSY";
        if (c.Weapon == null || c.Weapon.WasCollected) return null;
        if (c.Weapon.LoadState == LoadState.Loaded) return "ROUND CHAMBERED";
        if (c.Total == 0) return c.Spec.Magazine.ToUpperInvariant() + " EMPTY";
        var cell = NearestCell(c, kind);
        if (cell < 0) return $"NO {Kind.Names[kind]} {MagazineWhere(c)}";
        Start(c, cell, kind);
        return null;
    }

    private static int NearestCell(Carousel c, int kind)
    {
        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < c.Cells.Length; i++)
        {
            if (c.Cells[i] != kind) continue;
            var d = Mathf.Abs(Mathf.DeltaAngle(c.Angle, i * c.Step));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static void Start(Carousel c, int cell, int kind)
    {
        c.TargetCell = cell;
        c.LoadingKind = kind;
        c.Phase = AutoloaderPhase.Rotating;
        c.PhaseStart = Time.time;
        if (c.Ldb != null) LockedDrives[c.Ldb.Pointer] = c;
        if (c.Ring != null) LockedDrives[c.Ring.Pointer] = c;
        
        if (c.Voice == null || c.Voice.Type != c.Spec.Type)
        {
            c.Voice?.Destroy();
            c.Voice = new LoaderVoice(c.Spec.Type);
        }
        c.RotationHeard = false;
        c.CycleEnd = 0f;
        c.CycleStart = Time.time;
        c.CycleTotal = RotationTime(Mathf.Abs(Mathf.DeltaAngle(c.Angle, cell * c.Step)), 0f, c.DegSpeed, c.DegAccel) + RamDuration(c);
        
        Casings.OnLoadingStarted(c.Gun, c.WeaponPtr, true, Mathf.Max(0f, Plugin.AutoloaderEjectTime.Value));
        if (c.Spec.Belt)
            Plugin.Log.LogInfo($"Belt loader: {Kind.Names[kind]} from cell {cell + 1}, belt runs {Mathf.DeltaAngle(c.Angle, cell * c.Step) / c.Step:+0;-0;0} cell(s), " +
                               $"expected cycle {c.CycleTotal:0.0} s");
        else if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"{c.Spec.LoaderName}: {Kind.Names[kind]} from cell {cell + 1}, turn {Mathf.DeltaAngle(c.Angle, cell * c.Step):0}°");
    }

    private static void Abort(Carousel c, string why)
    {
        if (c.Busy && why != null) Plugin.Log.LogInfo($"{c.Spec.LoaderName} cycle aborted: " + why);
        c.Phase = AutoloaderPhase.Idle;
        c.TargetCell = -1;
        c.LoadingKind = -1;
        c.Velocity = 0f;
        Unlock(c);
        try { c.Voice?.OnAbort(); } catch { }
    }

    private static void Unlock(Carousel c)
    {
        try { if (c.Ldb != null) LockedDrives.Remove(c.Ldb.Pointer); } catch { }
        try { if (c.Ring != null) LockedDrives.Remove(c.Ring.Pointer); } catch { }
    }

    

    private static float _nextCleanup;

    public static void Tick(float dt)
    {
        if (ByRack.Count == 0) return;
        var now = Time.time;
        if (Time.unscaledTime > _nextCleanup) { _nextCleanup = Time.unscaledTime + 5f; Cleanup(); }
        foreach (var c in new List<Carousel>(ByRack.Values))
        {
            try { Update(c, dt, now); }
            catch (Exception e)
            {
                Abort(c, e.Message);
                Plugin.Log.LogError("Autoloader update failed: " + e);
            }
            try { c.Voice?.Update(c, dt); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Loader sound update failed: " + e.Message);
                c.Voice?.Destroy();
                c.Voice = null;
            }
        }
    }

    private static void Update(Carousel c, float dt, float now)
    {
        if (!c.Rack.Alive) { Abort(c, "carousel destroyed"); return; }
        if (c.Gun == null || c.Gun.WasCollected || c.Weapon == null || c.Weapon.WasCollected || c.Gun.Behaviour == null ||
            c.Gun.Behaviour.Pointer != c.WeaponPtr)
        {
            if (c.Busy) Abort(c, "gun gone");
            if (c.WeaponPtr != IntPtr.Zero) { ByWeapon.Remove(c.WeaponPtr); c.WeaponPtr = IntPtr.Zero; c.Gun = null; c.Weapon = null; }
            if (now < c.NextGunSearch) return;
            c.NextGunSearch = now + 1f;
            FindGun(c);
            if (c.Gun == null) return;
        }

        SyncCells(c);
        var task = c.Weapon.loadTask?.TryCast<LoadTask>();
        if (task == null) return;
        var loaded = (int)task.state == (int)LoadState.Loaded;

        
        if (!c.Preloaded)
        {
            c.Preloaded = true;
            if (!loaded && c.Total > 0)
            {
                var kind = PreferredKind(c);
                var cell = NearestCell(c, kind);
                if (cell >= 0)
                {
                    c.Angle = cell * c.Step;
                    LoadRound(c, task, cell, kind);
                    return;
                }
            }
        }

        switch (c.Phase)
        {
            case AutoloaderPhase.Idle:
                
                if (!loaded && c.Total > 0 && !IsPlayer(c) && Time.timeScale > 0f) Request(c, PreferredKind(c));
                break;

            case AutoloaderPhase.Rotating:
            {
                var target = c.TargetCell * c.Step;
                var delta = Mathf.DeltaAngle(c.Angle, target);
                var vTarget = Mathf.Sign(delta) * Mathf.Min(c.DegSpeed, Mathf.Sqrt(2f * c.DegAccel * Mathf.Abs(delta)));
                c.Velocity = Mathf.MoveTowards(c.Velocity, vTarget, c.DegAccel * dt);
                c.Angle = Mathf.Repeat(c.Angle + c.Velocity * dt, 360f);
                var aligned = Mathf.Abs(Mathf.DeltaAngle(c.Angle, target)) < 0.3f && Mathf.Abs(c.Velocity) < 3f;
                if (aligned) { c.Angle = target; c.Velocity = 0f; }
                
                if (!c.RotationHeard && !aligned && Mathf.Abs(c.Velocity) > 0.05f)
                {
                    c.RotationHeard = true;
                    c.Voice?.OnRotationStart(c.Gun != null ? c.Gun.transform : null);
                }
                
                var gunReady = GunAtLoadAngle(c) || now - c.PhaseStart > 8f;
                if (aligned && gunReady)
                {
                    c.Phase = AutoloaderPhase.Ramming;
                    c.PhaseStart = now;
                    
                    c.Voice?.OnLoadStart(c.Gun != null ? c.Gun.transform : null);
                    c.CycleEnd = now + (c.Voice?.LoadLength ?? 0f);
                    if (c.Spec.Belt)
                        Plugin.Log.LogInfo($"Belt loader: cell {c.TargetCell + 1} at the rammer after {now - c.CycleStart:0.0} s" +
                                           (GunAtLoadAngle(c) ? "" : " (gun did not reach the loading angle in 8 s)") +
                                           $", ramming {Mathf.Max(c.Spec.RamTime, c.CycleEnd - now):0.0} s");
                }
                break;
            }

            case AutoloaderPhase.Ramming:
                
                if (now - c.PhaseStart < c.Spec.RamTime || now < c.CycleEnd) break;
                var kindNow = c.TargetCell >= 0 && c.TargetCell < c.Cells.Length ? c.Cells[c.TargetCell] : -1;
                if (kindNow != c.LoadingKind) { Abort(c, "the round was lost"); break; }
                LoadRound(c, task, c.TargetCell, c.LoadingKind);
                c.Phase = AutoloaderPhase.Idle;
                c.TargetCell = -1;
                c.LoadingKind = -1;
                Unlock(c);
                break;
        }
    }

    

    
    public static bool CycleProgress(IntPtr loadTask, out float remaining, out float fraction)
    {
        remaining = 0f;
        fraction = 1f;
        if (loadTask == IntPtr.Zero || ByWeapon.Count == 0) return false;
        foreach (var c in ByWeapon.Values)
        {
            if (!c.Busy || c.Weapon == null || c.Weapon.WasCollected) continue;
            try
            {
                var task = c.Weapon.loadTask;
                if (task == null || task.Pointer != loadTask) continue;
            }
            catch { continue; }
            return Progress(c, out remaining, out fraction);
        }
        return false;
    }

    
    public static bool CycleProgress(Cannon cannon, out float remaining, out float fraction)
    {
        remaining = 0f;
        fraction = 1f;
        if (cannon == null || ByWeapon.Count == 0) return false;
        var ptr = cannon.Pointer;
        foreach (var c in ByWeapon.Values)
            if (c.Busy && c.Gun != null && !c.Gun.WasCollected && c.Gun.Pointer == ptr) return Progress(c, out remaining, out fraction);
        return false;
    }

    private static bool Progress(Carousel c, out float remaining, out float fraction)
    {
        var now = Time.time;
        if (c.Phase == AutoloaderPhase.Ramming)
            remaining = Mathf.Max(c.PhaseStart + c.Spec.RamTime, c.CycleEnd) - now;
        else
        {
            var target = c.TargetCell * c.Step;
            var delta = Mathf.DeltaAngle(c.Angle, target);
            var towards = Mathf.Sign(delta) == Mathf.Sign(c.Velocity) ? Mathf.Abs(c.Velocity) : 0f;
            remaining = RotationTime(Mathf.Abs(delta), towards, c.DegSpeed, c.DegAccel) + RamDuration(c);
        }
        remaining = Mathf.Max(0f, remaining);
        
        var elapsed = now - c.CycleStart;
        if (elapsed + remaining > c.CycleTotal) c.CycleTotal = elapsed + remaining;
        fraction = c.CycleTotal > 0.01f ? Mathf.Clamp01(elapsed / c.CycleTotal) : 1f;
        return true;
    }

    
    private static float RotationTime(float angle, float v0, float speed, float accel)
    {
        if (angle < 0.3f && v0 < 3f) return 0f;
        var a = Mathf.Max(1f, accel);
        var vmax = Mathf.Max(1f, speed);
        v0 = Mathf.Min(v0, vmax);
        var stop = v0 * v0 / (2f * a);
        if (stop >= angle) return v0 / a;
        var peak = Mathf.Min(vmax, Mathf.Sqrt(a * angle + v0 * v0 * 0.5f));
        var accelDist = (peak * peak - v0 * v0) / (2f * a);
        var brakeDist = peak * peak / (2f * a);
        var cruise = Mathf.Max(0f, angle - accelDist - brakeDist);
        return (peak - v0) / a + peak / a + cruise / peak;
    }

    
    private static float RamDuration(Carousel c) => Mathf.Max(c.Spec.RamTime, c.Voice?.LoadLength ?? 0f);

    
    private static bool IsPlayer(Carousel c) => c.Rack.VehicleKey != 0 && c.Rack.VehicleKey == PlayerTracker.VehicleKey;

    private static int PreferredKind(Carousel c)
    {
        if (IsPlayer(c) && c.Count(Controller.Selected) > 0) return Controller.Selected;
        foreach (var k in Kind.AutoOrder) if (c.Count(k) > 0) return k;
        return Kind.Apfsds;
    }

    
    private static void LoadRound(Carousel c, LoadTask task, int cell, int kind)
    {
        var s = c.Rack;
        var typeId = s.Types.TypeIds[kind];
        if (typeId == 0) return;
        c.Cells[cell] = -1;
        if (s.Count[kind] > 0) s.Count[kind]--;
        var total = s.Total;
        try { s.Beh.amountStored = (ushort)total; } catch { }
        s.LastTotal = total;
        Racks.FireStorageChanged(s);

        task.currentRequest = LoadRequest.None;
        task.SetState(LoadState.Loaded);
        c.Weapon._LoadedProjectileTypeID_k__BackingField = new ProjectileTypeID(typeId);
        c.Weapon._NeedsReload_k__BackingField = false;
        if (Plugin.DebugLogging.Value || (c.Spec.Belt && c.Busy))
            Plugin.Log.LogInfo($"{c.Spec.LoaderName}: {Kind.Names[kind]} loaded, {c.Total} rounds left in the {c.Spec.Magazine}" +
                               (c.Busy ? $" (cycle {Time.time - c.CycleStart:0.0} s)" : ""));
    }

    
    private static void SyncCells(Carousel c)
    {
        var s = c.Rack;
        Racks.Reconcile(s);
        for (var k = 0; k < Kind.Count; k++)
        {
            var n = c.Count(k);
            while (n > s.Count[k])
            {
                
                var candidates = new List<int>();
                for (var i = 0; i < c.Cells.Length; i++)
                    if (c.Cells[i] == k && !(c.Busy && i == c.TargetCell)) candidates.Add(i);
                if (candidates.Count == 0)
                    for (var i = 0; i < c.Cells.Length; i++) if (c.Cells[i] == k) candidates.Add(i);
                if (candidates.Count == 0) break;
                c.Cells[candidates[Rng.Next(candidates.Count)]] = -1;
                n--;
            }
            while (n < s.Count[k])
            {
                var free = Array.IndexOf(c.Cells, -1);
                if (free < 0) break;
                c.Cells[free] = k;
                n++;
            }
        }
    }

    private static void Cleanup()
    {
        List<IntPtr> dead = null;
        foreach (var kv in ByRack) if (!kv.Value.Rack.Alive) (dead ??= new List<IntPtr>()).Add(kv.Key);
        if (dead != null) foreach (var p in dead) Forget(p);
    }

    

    
    private static bool LockDirection(Carousel c, out Vector3 dir)
    {
        dir = default;
        var wt = c.Weapon?.transform;
        if (wt == null) return false;
        var fwd = wt.forward;
        var e = c.Spec.LoadAngle * Mathf.Deg2Rad;
        if (c.Ldb != null)
        {
            var f = c.Ldb.foundation;
            if (f == null) return false;
            var fq = f.rotation * c.Ldb.zeroLocalRot;
            var local = Quaternion.Inverse(fq) * fwd;
            var az = Mathf.Atan2(local.x, local.z);
            dir = fq * new Vector3(Mathf.Sin(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(az) * Mathf.Cos(e));
            return true;
        }
        if (c.Ring != null)
        {
            var f = c.Ring.foundation;
            if (f == null) return false;
            var up = f.up;
            var h = fwd - up * Vector3.Dot(fwd, up);
            h = h.sqrMagnitude > 1e-6f ? h.normalized : f.forward;
            dir = h * Mathf.Cos(e) + up * Mathf.Sin(e);
            return true;
        }
        return false;
    }

    private static bool GunAtLoadAngle(Carousel c)
    {
        var wt = c.Weapon?.transform;
        if (wt == null || (c.Ldb == null && c.Ring == null)) return true; 
        var fwd = wt.forward;
        float el;
        if (c.Ldb != null)
        {
            var f = c.Ldb.foundation;
            if (f == null) return true;
            var local = Quaternion.Inverse(f.rotation * c.Ldb.zeroLocalRot) * fwd;
            el = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
        }
        else
        {
            var f = c.Ring.foundation;
            if (f == null) return true;
            el = Mathf.Asin(Mathf.Clamp(Vector3.Dot(fwd, f.up), -1f, 1f)) * Mathf.Rad2Deg;
        }
        
        return Mathf.Abs(el - c.Spec.LoadAngle) < 1f;
    }

    
    
    
    
    private const Sprocket.AimFlags HoldFlag = (Sprocket.AimFlags)2;

    [HarmonyPatch(typeof(LayingDriveBehaviour), nameof(LayingDriveBehaviour.MoveToTarget))]
    private static class LayingLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(LayingDriveBehaviour __instance, float speedMultiplier, ref Sprocket.AimFlags flags)
        {
            if (LockedDrives.Count == 0) return;
            try
            {
                if (!LockedDrives.TryGetValue(__instance.Pointer, out var c) || !LockDirection(c, out var dir)) return;
                __instance.targetDirection = dir;
                __instance.moving = true;
                if (speedMultiplier > 0f) flags &= ~HoldFlag;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
    private static class RingLockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(TurretBehaviour __instance, float speedMultiplier, ref Sprocket.AimFlags flags)
        {
            if (LockedDrives.Count == 0) return;
            try
            {
                if (!LockedDrives.TryGetValue(__instance.Pointer, out var c) || !LockDirection(c, out var dir)) return;
                __instance.targetDirection = dir;
                __instance.move = true;
                if (speedMultiplier > 0f) flags &= ~HoldFlag;
            }
            catch { }
        }
    }

    
    public static string StateText(Carousel c, bool loaded)
    {
        switch (c.Phase)
        {
            case AutoloaderPhase.Rotating: return (c.Spec.Belt ? "BELT MOVING  " : "ROTATING  ") + Kind.Names[Math.Max(0, c.LoadingKind)];
            case AutoloaderPhase.Ramming: return "RAMMING  " + Kind.Names[Math.Max(0, c.LoadingKind)];
        }
        if (loaded) return null;
        return c.Total == 0 ? c.Spec.Magazine.ToUpperInvariant() + " EMPTY" : $"EMPTY  [{KeyName}] TO LOAD";
    }

    
    public static string MagazineWhere(Carousel c) => c.Spec.Belt ? "ON BELT" : "IN CAROUSEL";

    public static string KeyName
    {
        get
        {
            var s = Plugin.AutoloaderKey.ToString();
            return s.StartsWith("Digit") ? s.Substring(5) : s;
        }
    }
}
