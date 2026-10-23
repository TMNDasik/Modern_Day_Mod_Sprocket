using System;
using HarmonyLib;
using Sprocket.Vehicles.Weapons;
using UnityEngine;

namespace GunStabilizer;








internal static class Stabilization
{
    private static int _errors;

    public static void Fail(Exception e)
    {
        if (_errors++ < 5) Plugin.Log.LogError("Stabilizer update failed: " + e);
    }

    private static bool Realism(DriveState s) => s.VehicleKey == ScopeCarry.PlayerVehicleKey || Plugin.AffectAI.Value;

    

    public static void Turret(TurretBehaviour tb, float dt, ref TurretBoost boost)
    {
        if (!tb.move) return;
        var tr = tb.transform;
        var f = tb.foundation;
        if (tr == null || f == null) return;
        var parent = tr.parent;
        if (parent == null || parent.Pointer != f.Pointer) return; 

        var s = Registry.Get(tb);
        s.LastUpdate = Time.time;
        var unit = s.Unit;
        unit.Touch();

        var fq = f.rotation;
        var axis = tb.axis;
        s.UpdateRates(fq, f.Pointer, dt, true, axis);

        var restF = tb.restForwards;
        var raw = s.TrueAim(tb.targetDirection);
        s.PrevRaw = raw;
        s.HasRaw = true;
        
        var d = FireControl.Raise(s, raw, out var raised);
        s.HasOutput = false;
        var fqInv = Quaternion.Inverse(fq);
        if (!TurretAngle(fqInv * d, axis, restF, out var target)) return;
        
        var command = 0f;
        if (s.HasPrevAim && TurretAngle(fqInv * s.PrevAim, axis, restF, out var prevInNow))
            command = Mathf.DeltaAngle(prevInNow, target);
        s.PrevAim = d;
        s.HasPrevAim = true;

        var scoped = s.VehicleKey == ScopeCarry.ScopedVehicleKey;
        var stab = Registry.Stabilized(s);
        s.StabActive = stab;
        
        
        
        if (stab) Boost(tb, s.IsPitch ? StabSpec.VerticalMaxRate : 0f, s.IsPitch ? StabSpec.VerticalMaxAccel : StabSpec.HorizontalMaxAccel, ref boost);
        var cmd = target;
        if (stab)
        {
            
            cmd = target + s.StepResidual(dt, s.IsPitch ? StabSpec.VerticalLag : StabSpec.HorizontalLag);
            s.Main.Sync(target);
        }
        else
        {
            s.Residual = 0f;
            if (scoped || !Realism(s)) s.Main.Sync(target);
            else cmd = s.Main.Step(target, command, dt, true);
        }
        s.CommandError = Mathf.Abs(Mathf.DeltaAngle(target, cmd)) - Mathf.Abs(s.Residual);

        var delta = Mathf.DeltaAngle(target, cmd);
        if (Mathf.Abs(delta) < 1e-5f && !raised) return;
        var output = Quaternion.AngleAxis(delta, fq * axis) * d;
        tb.targetDirection = output;
        s.LastOutput = output;
        s.HasOutput = true;
    }

    
    
    
    
    
    private static void Boost(TurretBehaviour tb, float minRate, float minAccel, ref TurretBoost boost)
    {
        var rate = tb.maxTraverseRate;
        var accel = tb.maxAcceleration;
        var brake = tb.maxBrakeAcceleration;
        var need = minAccel / Mathf.Max(0.05f, Multiplier(tb));
        if (rate >= minRate && accel >= need && brake >= need) return;
        boost.Boosted = true;
        boost.Rate = rate;
        boost.Accel = accel;
        boost.Brake = brake;
        if (rate < minRate) tb.maxTraverseRate = minRate;
        if (accel < need) tb.maxAcceleration = need;
        if (brake < need) tb.maxBrakeAcceleration = need;
    }

    
    internal static float Multiplier(TurretBehaviour tb)
    {
        try
        {
            var m = tb.MotorTorqueMultiplier;
            return float.IsNaN(m) || m <= 0f ? 1f : m;
        }
        catch { return 1f; }
    }

    
    private static bool TurretAngle(Vector3 local, Vector3 axis, Vector3 restForwards, out float angle)
    {
        var p = local - axis * Vector3.Dot(local, axis);
        if (p.sqrMagnitude < 1e-8f) { angle = 0f; return false; }
        angle = Vector3.SignedAngle(restForwards, p, axis);
        return true;
    }

    

    private const float MaxAngle = 85f;

    public static void Gun(LayingDriveBehaviour ldb, float dt, ref RotationAxisInfo saved, ref bool boosted)
    {
        if (!ldb.moving) return;
        var f = ldb.foundation;
        if (f == null || ldb.trunnions == null) return;

        var s = Registry.Get(ldb);
        s.LastUpdate = Time.time;
        var unit = s.Unit;
        unit.Touch();

        var fq = f.rotation * ldb.zeroLocalRot;
        s.UpdateRates(fq, f.Pointer, dt, false);

        var raw = s.TrueAim(ldb.targetDirection);
        s.PrevRaw = raw;
        s.HasRaw = true;
        
        var d = FireControl.Raise(s, raw, out var raised);
        s.HasOutput = false;
        var fqInv = Quaternion.Inverse(fq);
        var local = fqInv * d;
        if (local.z < 1e-3f) return;
        var el = Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
        var az = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        
        float cmdChangeEl = 0f, cmdChangeAz = 0f;
        if (s.HasPrevAim)
        {
            var prev = fqInv * s.PrevAim;
            if (prev.z > 1e-3f)
            {
                cmdChangeEl = el - Mathf.Atan2(prev.y, prev.z) * Mathf.Rad2Deg;
                cmdChangeAz = az - Mathf.Atan2(prev.x, prev.z) * Mathf.Rad2Deg;
            }
        }
        s.PrevAim = d;
        s.HasPrevAim = true;

        var scoped = s.VehicleKey == ScopeCarry.ScopedVehicleKey;
        var realism = Realism(s);
        var stab = Registry.Stabilized(s);
        s.StabActive = stab;

        var cmdEl = el;
        if (stab)
        {
            cmdEl = el + s.StepResidual(dt, StabSpec.VerticalLag);
            s.Main.Sync(el);
        }
        else
        {
            s.Residual = 0f;
            if (scoped || !realism) s.Main.Sync(el);
            else cmdEl = s.Main.Step(el, cmdChangeEl, dt, false);
        }

        
        var cmdAz = az;
        var azInfo = ldb.azimuthInfo;
        var hasAz = azInfo.Max - azInfo.Min > 0.01f;
        var azStab = s.Peer != null && s.Peer.StabActive;
        if (!hasAz || azStab || scoped || !realism) s.Az.Sync(az);
        else cmdAz = s.Az.Step(az, cmdChangeAz, dt, false);

        s.CommandError = Mathf.Max(Mathf.Abs(cmdEl - el) - Mathf.Abs(s.Residual), Mathf.Abs(cmdAz - az));

        if (raised || Mathf.Abs(cmdEl - el) > 1e-5f || Mathf.Abs(cmdAz - az) > 1e-5f)
        {
            cmdEl = Mathf.Clamp(cmdEl, -MaxAngle, MaxAngle);
            cmdAz = Mathf.Clamp(cmdAz, -MaxAngle, MaxAngle);
            var dir = new Vector3(Mathf.Tan(cmdAz * Mathf.Deg2Rad), Mathf.Tan(cmdEl * Mathf.Deg2Rad), 1f).normalized;
            var output = fq * dir;
            ldb.targetDirection = output;
            s.LastOutput = output;
            s.HasOutput = true;
        }

        
        if (stab)
        {
            var info = ldb.elevationInfo;
            saved = info;
            var torque = info.MotorTorque;
            var inertia = ldb.momentOfInertia;
            if (inertia > 0f && !float.IsNaN(inertia))
            {
                var gravity = Mathf.Abs(ldb.mass * ldb.centreOfMass * 9.81f);
                torque = Mathf.Max(torque, StabSpec.VerticalMaxAccel * inertia + gravity);
            }
            SetDrive(ref info, torque, Mathf.Max(info.MaxSpeed, StabSpec.VerticalMaxRate));
            ldb.elevationInfo = info;
            boosted = true;
        }
    }

    
    private static unsafe void SetDrive(ref RotationAxisInfo info, float torque, float maxSpeed)
    {
        fixed (RotationAxisInfo* p = &info)
        {
            var f = (float*)p;
            f[0] = torque;
            f[1] = maxSpeed;
        }
    }
}


internal struct TurretBoost
{
    public bool Boosted;
    public float Rate, Accel, Brake;
}

[HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.MoveToTarget))]
internal static class TurretMovePatch
{
    private static void Prefix(TurretBehaviour __instance, float deltaTime, out TurretBoost __state)
    {
        __state = default;
        if (deltaTime <= 0f) return;
        try { Stabilization.Turret(__instance, deltaTime, ref __state); }
        catch (Exception e) { Stabilization.Fail(e); }
    }

    private static void Postfix(TurretBehaviour __instance, TurretBoost __state)
    {
        if (!__state.Boosted) return;
        try
        {
            __instance.maxTraverseRate = __state.Rate;
            __instance.maxAcceleration = __state.Accel;
            __instance.maxBrakeAcceleration = __state.Brake;
        }
        catch (Exception e) { Stabilization.Fail(e); }
    }
}

internal struct GunPatchState
{
    public bool Boosted;
    public RotationAxisInfo Saved;
}

[HarmonyPatch(typeof(LayingDriveBehaviour), nameof(LayingDriveBehaviour.MoveToTarget))]
internal static class GunMovePatch
{
    private static void Prefix(LayingDriveBehaviour __instance, float deltaTime, out GunPatchState __state)
    {
        __state = default;
        if (deltaTime <= 0f) return;
        try { Stabilization.Gun(__instance, deltaTime, ref __state.Saved, ref __state.Boosted); }
        catch (Exception e) { Stabilization.Fail(e); }
    }

    
    private static void Postfix(LayingDriveBehaviour __instance, GunPatchState __state)
    {
        if (!__state.Boosted) return;
        try { __instance.elevationInfo = __state.Saved; }
        catch (Exception e) { Stabilization.Fail(e); }
    }
}



[HarmonyPatch(typeof(TurretBehaviour), nameof(TurretBehaviour.OnTarget))]
internal static class TurretOnTargetPatch
{
    private static void Postfix(TurretBehaviour __instance, float __0, ref bool __result)
    {
        if (!__result) return;
        var s = Registry.Peek(__instance.Pointer);
        if (s != null && s.CommandError > __0) __result = false;
    }
}

[HarmonyPatch(typeof(LayingDriveBehaviour), nameof(LayingDriveBehaviour.OnTarget))]
internal static class GunOnTargetPatch
{
    private static void Postfix(LayingDriveBehaviour __instance, float __0, ref bool __result)
    {
        if (!__result) return;
        var s = Registry.Peek(__instance.Pointer);
        if (s != null && s.CommandError > __0) __result = false;
    }
}
