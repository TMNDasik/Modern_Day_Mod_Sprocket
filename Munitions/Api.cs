using System;
using Sprocket.Vehicles.Weapons;
using Sprocket.WeaponFramework;

namespace Munitions;


public static class Api
{
    
    
    
    public static int DisplayKind(IntPtr weaponBehaviour)
    {
        try
        {
            if (weaponBehaviour == IntPtr.Zero) return -1;
            
            if (Launchers.IsLauncher(weaponBehaviour)) return Launchers.IsRocket(weaponBehaviour) ? Kind.He : Kind.Atgm;
            var weapon = new WeaponBehaviour(weaponBehaviour);
            var task = weapon.loadTask?.TryCast<LoadTask>();
            if (task != null && PlayerTracker.InControl)
            {
                var key = Racks.VehicleKeyOf(weapon.transform);
                if (key != 0 && key == PlayerTracker.VehicleKey && Racks.Of(key, task.Request.SizeID.Value).Count > 0)
                    return Kind.External(Controller.Selected);
            }
            return TypeRegistry.TryGetKind(weapon._LoadedProjectileTypeID_k__BackingField.Value, out _, out var kind) ? Kind.External(kind) : -1;
        }
        catch { return -1; }
    }

    
    public static bool GunLocked(IntPtr weaponBehaviour)
    {
        try { return weaponBehaviour != IntPtr.Zero && Autoloader.IsLocked(weaponBehaviour); }
        catch { return false; }
    }

    
    
    public static int ProjectileKind(int typeId)
    {
        try { return TypeRegistry.TryGetKind(typeId, out _, out var kind) ? Kind.External(kind) : -1; }
        catch { return -1; }
    }

    
    private static readonly System.Collections.Generic.Dictionary<IntPtr, (int Type, float Caliber)> AutoCache = new();

    public static int AutoWeaponType(UnityEngine.Transform t) => AutoInfo(t).Type;

    
    internal static void ForgetAutoInfo() => AutoCache.Clear();

    
    public static float AutoWeaponCaliber(UnityEngine.Transform t) => AutoInfo(t).Caliber;

    
    public static bool IsAutoWeapon(IntPtr weaponBehaviour) => AutoFeed.IsAuto(weaponBehaviour);

    
    
    public static int LoaderKind(IntPtr weaponBehaviour)
    {
        try
        {
            if (weaponBehaviour == IntPtr.Zero) return 0;
            if (AutoFeed.IsAuto(weaponBehaviour) || Launchers.IsLauncher(weaponBehaviour)) return 4;
            if (!Autoloader.Enabled || !Autoloader.ForWeapon(weaponBehaviour, out var c)) return 0;
            return c.Spec.Type switch { LoaderType.Mz => 2, LoaderType.Belt => 3, _ => 1 };
        }
        catch { return 0; }
    }

    
    
    public static bool PlayerRounds(out int selected, out int total)
    {
        selected = total = 0;
        try
        {
            var v = Controller.LastView;
            if (!v.Valid || v.Launcher) return false;
            if (v.AutoVanilla)
            {
                selected = total = v.VanillaRounds;
                return true;
            }
            if (v.Counts == null) return false;
            selected = Controller.Selected >= 0 && Controller.Selected < v.Counts.Length ? v.Counts[Controller.Selected] : 0;
            total = Kind.Sum(v.Counts);
            return true;
        }
        catch { return false; }
    }

    
    public static bool LoaderCycling(IntPtr weaponBehaviour)
    {
        try { return weaponBehaviour != IntPtr.Zero && Autoloader.ForWeapon(weaponBehaviour, out var c) && c.Busy; }
        catch { return false; }
    }

    private static (int Type, float Caliber) AutoInfo(UnityEngine.Transform t)
    {
        try
        {
            if (t == null) return (0, 0f);
            var key = t.Pointer;
            if (AutoCache.TryGetValue(key, out var info)) return info;
            if (AutoCache.Count > 512) AutoCache.Clear();
            var cannon = t.GetComponentInParent<Sprocket.Vehicles.Cannons.Cannon>();
            var spec = AutoWeaponPart.SpecOf(cannon);
            info = spec == null ? (0, 0f) : ((int)spec.Type, AutoWeaponPart.NominalCaliber(spec, cannon.Blueprint));
            AutoCache[key] = info;
            return info;
        }
        catch { return (0, 0f); }
    }

    
    
    
    public static float LaunchSpeed(IntPtr weaponBehaviour, int typeId, float natural)
    {
        try
        {
            if (weaponBehaviour == IntPtr.Zero || !TypeRegistry.TryGetKind(typeId, out _, out var kind) ||
                (kind != Kind.Apfsds && kind != Kind.Heat)) return natural;
            var bp = ApfsdsSettings.BlueprintOf(new Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour(weaponBehaviour).transform);
            if (bp == null) return natural;
            if (kind == Kind.Heat)
            {
                HeatSettings.Read(bp, out _, out var heatSpeed);
                if (heatSpeed > 0f) return UnityEngine.Mathf.Clamp(heatSpeed, HeatSettings.MinSpeed, HeatSettings.MaxSpeed);
                var heatAuto = HeatSettings.AutoSpeed(bp, out var heatMeasured);
                return heatMeasured ? heatAuto : natural;
            }
            ApfsdsSettings.Read(bp, out _, out var speedSet, out _);
            if (speedSet > 0f) return UnityEngine.Mathf.Clamp(speedSet, ApfsdsSettings.MinSpeed, ApfsdsSettings.MaxSpeed);
            
            var auto = ApfsdsSettings.AutoSpeed(bp, out var measured);
            return measured ? auto : natural;
        }
        catch { return natural; }
    }

    
    public static int ProjectileCaliber(int typeId)
    {
        try { return TypeRegistry.TryGetKind(typeId, out var types, out _) && types != null ? types.Caliber : 0; }
        catch { return 0; }
    }

    
    
    public static void SetGuidanceFilter(Func<int, UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.Vector3, int, int> filter) =>
        Guidance.SetFilter(filter);

    
    public static void SmokeLaunch(UnityEngine.Vector3 muzzle, UnityEngine.Vector3 velocity, float flightTime)
    {
        try { Effects.SmokeLaunch(muzzle, velocity, flightTime); }
        catch { }
    }

    
    public static UnityEngine.Vector3 SmokeDrift() => Particles.Wind;

    
    public static void ExhaustSmoke(UnityEngine.Vector3 pos, UnityEngine.Vector3 dir, UnityEngine.Vector3 vehicleVelocity, float dt, float intensity)
    {
        try { Effects.ExhaustSmoke(pos, dir, vehicleVelocity, dt, intensity); }
        catch { }
    }

    
    public static void SmokeCloud(UnityEngine.Vector3 center, float radius, float buildTime, float duration)
    {
        try { Effects.SmokeCloud(center, radius, buildTime, duration); }
        catch { }
    }

    
    
    
    public static bool ScaleImpact(int projectileId, float speedFactor)
    {
        try { return Guidance.ScaleImpact(projectileId, speedFactor); }
        catch { return false; }
    }

    
    public static void EraEffect(UnityEngine.Vector3 point, UnityEngine.Vector3 normal, float size)
    {
        try { Effects.EraBurst(point, normal, size); }
        catch { }
    }

    
    
    
    public static void VentFire(UnityEngine.Transform anchor, UnityEngine.Vector3 localPos, UnityEngine.Vector3 localUp, float size, float intensity, float duration)
    {
        try { Effects.VentFire(anchor, localPos, localUp, size, intensity, duration); }
        catch { }
    }

    
    
    public static int DetonationNear(UnityEngine.Vector3 pos, float radius)
    {
        try { return Effects.ClaimDetonation(pos, radius); }
        catch { return -1; }
    }

    
    
    public static int DetonationKindNear(UnityEngine.Vector3 pos, float radius)
    {
        try { return Effects.PeekDetonation(pos, radius); }
        catch { return -1; }
    }

    
    
    public static UnityEngine.Vector4 UnclaimedDetonation(float minAge)
    {
        try { return Effects.TakeUnclaimed(minAge); }
        catch { return new UnityEngine.Vector4(0f, 0f, 0f, -1f); }
    }

    
    
    
    
    public static bool Warhead(UnityEngine.Vector3 position, UnityEngine.Vector3 direction, int caliber, float penetration, bool ground)
    {
        try { return Warheads.Fire(position, direction, caliber, penetration, ground); }
        catch { return false; }
    }

    public static bool Warhead(UnityEngine.Vector3 position, UnityEngine.Vector3 direction, int caliber, float penetration) =>
        Warhead(position, direction, caliber, penetration, false);

    
    
    public static bool Warhead(UnityEngine.Vector3 position, UnityEngine.Vector3 direction, int caliber, float penetration, float blast, int burstKind, bool ground)
    {
        try { return Warheads.Fire(position, direction, caliber, penetration, blast, burstKind, ground); }
        catch { return false; }
    }

    
    public static void ApsEffect(UnityEngine.Vector3 launcher, UnityEngine.Vector3 point, UnityEngine.Vector3 threatDir, bool kinetic, bool success)
    {
        try { Effects.ApsBurst(launcher, point, threatDir, kinetic, success); }
        catch { }
    }
}
