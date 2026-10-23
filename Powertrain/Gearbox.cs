using System;
using HarmonyLib;
using Sprocket.Vehicles.Powertrains;
using UnityEngine;

namespace Powertrain;












internal static unsafe class Gearbox
{
    
    private const int ThrottleTarget = 0x18, SteerTarget = 0x1C, EngineField = 0x60, TransmissionsField = 0x68;
    private const int AnyInput = 0x86, ForwardVelocity = 0x88, ThrottleInput = 0xB8, AppliedThrottle = 0xC8;
    
    private const int DriverFunctioning = 0x83;
    private const int InitialShiftRpmTarget = 0x9C, ShiftRpmTarget = 0xA0, ActionField = 0xAC, ShiftStateField = 0xC0;
    private const int ActionShifting = 5;
    
    private const int ClutchBrakingState = 0x1B8, ClutchBrakingIdealGear = 0x40;
    private const int TwinState = 0x228, TwinIdealGear = 0x30;
    
    public const int TransRequestedMode = 0x40, TransRequestedGear = 0x44, TransDriveCount = 0x28, TransReverseCount = 0x38;
    public const int TransActiveMode = 0x8C, TransActiveGear = 0x8D;
    private const byte ModeDrive = 1, ModeNeutral = 2, ModeReverse = 4;
    private const int ActionPivoting = 2;
    private const float StopSpeed = 1.05f;

    public static GearSelector ForPowertrain(byte* p)
    {
        if (!Plugin.GearboxEnabled.Value || !PlayerTracker.InControl) return null;
        var sel = PlayerTracker.Current;
        if (sel == null) return null;
        var engine = *(IntPtr*)(p + EngineField);
        return PlayerTracker.Engines.Contains(engine) ? sel : null;
    }

    
    [HarmonyPatch(typeof(PowertrainBehaviour), nameof(PowertrainBehaviour.SetInputs))]
    private static class InputCapture
    {
        private static void Postfix(PowertrainBehaviour __instance)
        {
            try
            {
                var p = (byte*)__instance.Pointer;
                var sel = ForPowertrain(p);
                if (sel != null) sel.RawThrottle = *(float*)(p + ThrottleTarget);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ClutchBraking), nameof(ClutchBraking.ExecuteDriverFixedUpdate))]
    private static class ClutchBrakingDriver
    {
        private static void Prefix(ClutchBraking __instance) => BeforeDriver((byte*)__instance.Pointer, ClutchBrakingState, ClutchBrakingIdealGear);
        private static void Postfix(ClutchBraking __instance, PowertrainBehaviour.DriveAction __result, float deltaTime) =>
            AfterDriver((byte*)__instance.Pointer, (int)__result, true, deltaTime);
    }

    
    [HarmonyPatch(typeof(TwinTransmission), nameof(TwinTransmission.ExecuteDriverFixedUpdate))]
    private static class TwinDriver
    {
        private static void Prefix(TwinTransmission __instance) => BeforeDriver((byte*)__instance.Pointer, TwinState, TwinIdealGear);
        private static void Postfix(TwinTransmission __instance, PowertrainBehaviour.DriveAction __result, float deltaTime) =>
            AfterDriver((byte*)__instance.Pointer, (int)__result, false, deltaTime);
    }

    
    
    
    [HarmonyPatch(typeof(PowertrainBehaviour), nameof(PowertrainBehaviour.FixedUpdate))]
    private static class NoDriver
    {
        private static void Prefix(PowertrainBehaviour __instance)
        {
            try
            {
                var p = (byte*)__instance.Pointer;
                var noDriver = NoDriverAt(p);
                
                var sel = ForPowertrain(p);
                if (sel != null && (sel.NoDriver != noDriver || !sel.DriverLogged))
                {
                    var changed = sel.NoDriver != noDriver;
                    sel.NoDriver = noDriver;
                    if (!sel.DriverLogged || _driverLogs++ < 10)
                        Plugin.Log.LogInfo((noDriver ? "Gearbox: no driver — the vehicle stops, shifting blocked" : changed ? "Gearbox: driver back — shifting allowed" : "Gearbox: driver present") +
                                           $" ({DriverTasks.Describe((IntPtr)p)}, functioning flag {p[DriverFunctioning]})");
                    sel.DriverLogged = true;
                }
                if (!noDriver) return;
                *(float*)(p + ThrottleTarget) = 0f;
                *(float*)(p + SteerTarget) = 0f;
                p[AnyInput] = 0;
                ForceNeutral(p);
            }
            catch (Exception e)
            {
                if (_noDriverErrors++ < 5) Plugin.Log.LogError("Gearbox (no driver) failed: " + e);
            }
        }
    }

    
    
    private static bool NoDriverAt(byte* p) =>
        p[DriverFunctioning] == 0 || DriverTasks.Unmanned((IntPtr)p) || (ForPowertrain(p) != null && DriverTasks.TaskMissing((IntPtr)p));

    private static void ForceNeutral(byte* p)
    {
        var arr = *(IntPtr*)(p + TransmissionsField);
        if (arr == IntPtr.Zero) return;
        var n = *(int*)((byte*)arr + 0x18);
        for (var i = 0; i < n; i++)
        {
            var t = (byte*)*(IntPtr*)((byte*)arr + 0x20 + i * 8);
            if (t != null) t[TransRequestedMode] = ModeNeutral;
        }
    }

    private static int _errors, _shiftErrors, _noDriverErrors, _driverLogs;

    
    
    
    private static void TurbineShift(byte* p)
    {
        try
        {
            var t = Turbines.Get(*(IntPtr*)(p + EngineField));
            if (t == null || *(int*)(p + ActionField) != ActionShifting) return;
            ref var stage = ref *(int*)(p + ShiftStateField);
            var was = stage;
            if (stage == 2 || stage == 3) stage = 1;
            else if (stage >= 4 && stage <= 6) stage = 7;
            t.ShiftUntil = Time.time + 0.15f;
            
            
            t.ShiftTargetRpm = was >= 7 ? *(float*)(p + ShiftRpmTarget) : was >= 1 ? *(float*)(p + InitialShiftRpmTarget) : 0f;
        }
        catch (Exception e)
        {
            if (_shiftErrors++ < 5) Plugin.Log.LogError("Gas turbine shift failed: " + e);
        }
    }

    
    private static bool EngineReady(byte* p)
    {
        var engine = *(IntPtr*)(p + EngineField);
        if (engine == IntPtr.Zero || ((byte*)engine)[Turbines.State] != Turbines.Running) return false;
        var t = Turbines.Get(engine);
        return t == null || t.Phase != TurbinePhase.Starting;
    }

    
    private static void SyncSelector(byte* p, GearSelector sel, float v)
    {
        sel.SyncPending = false;
        int mode = ModeNeutral, gear = 0;
        var arr = *(IntPtr*)(p + TransmissionsField);
        if (arr != IntPtr.Zero && *(int*)((byte*)arr + 0x18) > 0)
        {
            var t = (byte*)*(IntPtr*)((byte*)arr + 0x20);
            if (t != null)
            {
                mode = t[TransActiveMode];
                gear = t[TransActiveGear];
            }
        }
        sel.SyncTo(mode, gear, v);
    }

    private static void BeforeDriver(byte* p, int stateField, int idealGearOffset)
    {
        try
        {
            TurbineShift(p);
            if (NoDriverAt(p)) return; 
            var sel = ForPowertrain(p);
            if (sel == null) return;
            var v = *(float*)(p + ForwardVelocity);
            sel.Velocity = v;
            if (sel.SyncPending) SyncSelector(p, sel, v);
            var raw = sel.RawThrottle;
            var steer = Math.Abs(*(float*)(p + SteerTarget));
            var gas = Math.Max(0f, raw);
            var brake = Math.Max(0f, -raw);
            var dir = sel.Direction;

            float mapped;
            if (dir == 0)
                mapped = brake > 0.01f && Math.Abs(v) > StopSpeed ? -Math.Sign(v) * brake : 0f;
            else if (brake > 0.01f)
                mapped = dir * v > StopSpeed ? -dir * brake : 0f; 
            else if (!EngineReady(p))
                mapped = 0f; 
            else if (gas > 0.01f)
                mapped = dir * gas;
            else
                mapped = steer > 0.01f ? 0f : dir * Math.Max(0f, Plugin.CreepThrottle.Value); 

            *(float*)(p + ThrottleTarget) = mapped;
            *(float*)(p + ThrottleInput) = Math.Abs(mapped);
            p[AnyInput] = (byte)(Math.Abs(mapped) > 0.01f || steer > 0.01f ? 1 : 0);

            
            if (sel.Manual && dir != 0)
            {
                var state = *(IntPtr*)(p + stateField);
                if (state != IntPtr.Zero) *(int*)((byte*)state + idealGearOffset) = sel.GearIndex;
            }
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Gearbox input failed: " + e);
        }
    }

    private static void AfterDriver(byte* p, int action, bool forceGear, float deltaTime)
    {
        try
        {
            
            if (NoDriverAt(p))
            {
                ForceNeutral(p);
                return;
            }
            var sel = ForPowertrain(p);
            if (sel == null) return;
            var dir = sel.Direction;
            var arr = *(IntPtr*)(p + TransmissionsField);
            if (arr != IntPtr.Zero)
            {
                var n = *(int*)((byte*)arr + 0x18);
                for (var i = 0; i < n; i++)
                {
                    var t = (byte*)*(IntPtr*)((byte*)arr + 0x20 + i * 8);
                    if (t == null) continue;
                    ref var mode = ref t[TransRequestedMode];
                    if (action == ActionPivoting) { } 
                    else if (dir == 0) mode = ModeNeutral;
                    else if (sel.Manual && forceGear)
                    {
                        
                        if (mode != ModeNeutral)
                        {
                            mode = dir > 0 ? ModeDrive : ModeReverse;
                            var count = *(int*)(t + (dir > 0 ? TransDriveCount : TransReverseCount));
                            *(int*)(t + TransRequestedGear) = Math.Clamp(sel.GearIndex, 0, Math.Max(0, count - 1));
                        }
                    }
                    else if (dir > 0 && mode == ModeReverse || dir < 0 && mode == ModeDrive) mode = ModeNeutral;

                    if (i == 0)
                    {
                        sel.ActiveMode = t[TransActiveMode];
                        sel.ActiveGear = t[TransActiveGear];
                    }
                }
            }

            
            if (dir == 0 && action != ActionPivoting)
            {
                sel.Rev = Mathf.MoveTowards(sel.Rev, Math.Max(0f, sel.RawThrottle), deltaTime * 3f);
                var applied = (float*)(p + AppliedThrottle);
                *applied = Math.Max(*applied, sel.Rev);
            }
            else sel.Rev = 0f;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Gearbox control failed: " + e);
        }
    }
}
