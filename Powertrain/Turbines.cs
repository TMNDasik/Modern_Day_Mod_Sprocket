using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Engines;
using Sprocket.Powertrains.Engines;
using Sprocket.Powertrains.FuelSystems;
using Sprocket.Vehicles.Engines;
using UnityEngine;

namespace Powertrain;

internal enum TurbinePhase { Off, Starting, Running, Stopping }


internal sealed class Turbine
{
    public IntPtr Ptr;
    public TurbineSpec Spec;
    public TurbinePhase Phase = TurbinePhase.Off;
    public float PhaseStart;
    public bool RunRequested;
    public float N1;             
    public float Demand;         
    public float Throttle = TurbineModel.IdleFloor;
    public float SavedFuelPerRot;
    public bool OutOfFuel;
    public float StartedAt = -100f;
    public float Rpm;
    public int Version;          
    public float ShiftUntil = -100f; 
    public float ShiftTargetRpm;     

    public void SetPhase(TurbinePhase p)
    {
        Phase = p;
        PhaseStart = Time.time;
        Version++;
    }
}

internal static unsafe class Turbines
{
    
    public const int FuelSystem = 0x10, State = 0x1C, RequestedState = 0x1D, ShutdownReason = 0x1E, IgnitionDelta = 0x24;
    public const int Speed = 0x28, TargetMaxRpm = 0x44, TargetMinRpm = 0x48, FuelPerRotation = 0x50;
    public const int JobMaxTorque = 0x60, JobInertia = 0x64, JobHealth = 0x68, JobMaxSpeed = 0x6C, JobThrottle = 0x70;
    public const int JobRevLimit = 0x74, JobFriction = 0x78, JobOperatingSpeed = 0x7C, JobIdleSpeed = 0x80;
    public const int TorqueTable = 0x98, TorqueTableLength = 0xA0;
    public const byte Off = 1, Starting = 2, Running = 4, ShuttingDown = 8;
    public const byte ReasonManual = 1, ReasonOutOfFuel = 2, ReasonStalled = 4, ReasonDestroyed = 8;

    public static readonly Dictionary<IntPtr, Turbine> Map = new();

    public static Turbine Get(IntPtr behaviour) =>
        behaviour != IntPtr.Zero && Map.TryGetValue(behaviour, out var t) ? t : null;

    
    public static float StartLength => Sounds.StartLength;
    public static float RunDelay => Math.Max(0.5f, Sounds.StartLength);

    
    private const float ShiftSyncTime = 0.25f;

    

    public static bool BeginStart(Turbine t)
    {
        if (t.Phase == TurbinePhase.Starting || t.Phase == TurbinePhase.Running) return false;
        var b = (byte*)t.Ptr;
        if (*(float*)(b + JobHealth) <= 0f) return false; 
        try
        {
            var fuel = new EngineBehaviour(t.Ptr).FuelSource;
            if (fuel != null && fuel.Remaining <= 0f) return false;
        }
        catch { }
        t.RunRequested = false;
        t.N1 = 0f;
        t.StartedAt = Time.time;
        t.SetPhase(TurbinePhase.Starting);
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("Gas turbine start");
        return true;
    }

    public static void BeginStop(Turbine t, byte reason)
    {
        if (t.Phase == TurbinePhase.Off || t.Phase == TurbinePhase.Stopping) return;
        var b = (byte*)t.Ptr;
        try
        {
            if (b[State] != Off) new EngineBehaviour(t.Ptr).ShutdownEngine((EngineShutdownReason)reason);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Engine shutdown failed: " + e.Message); }
        t.SetPhase(TurbinePhase.Stopping);
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("Gas turbine shutdown, reason " + reason);
    }

    

    [HarmonyPatch(typeof(EngineBehaviour), nameof(EngineBehaviour.SetBlueprint))]
    private static class SetBlueprintPatch
    {
        private static void Postfix(EngineBehaviour __instance, IEngineBlueprint blueprint)
        {
            try
            {
                var ptr = __instance.Pointer;
                var bp = blueprint?.TryCast<EngineBlueprint>();
                var spec = SpecCache.Get(bp);
                if (spec == null)
                {
                    Map.Remove(ptr);
                    return;
                }
                if (!Map.TryGetValue(ptr, out var t))
                {
                    t = new Turbine { Ptr = ptr };
                    Map[ptr] = t;
                }
                t.Spec = spec.Clone();
                Apply(ptr, t.Spec);
                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo($"Gas turbine: {spec.Power:0} kW, peak {TurbineModel.PeakRpm(spec):0} rpm, stall torque {TurbineModel.StallTorque(spec):0} N·m");
            }
            catch (Exception e) { Plugin.Log.LogError("Gas turbine setup failed: " + e); }
        }
    }

    private static void Apply(IntPtr ptr, TurbineSpec s)
    {
        var b = (byte*)ptr;
        var k = TurbineModel.RpmToRad;
        *(float*)(b + JobMaxTorque) = TurbineModel.StallTorque(s);
        *(float*)(b + JobInertia) = s.Inertia;
        *(float*)(b + JobMaxSpeed) = s.Safe * k;
        *(float*)(b + JobRevLimit) = TurbineModel.RevLimit(s) * k;
        *(float*)(b + JobOperatingSpeed) = s.Operating * k;
        *(float*)(b + JobIdleSpeed) = s.Idle * k;
        *(float*)(b + JobFriction) = TurbineModel.DragPerRad(s);
        *(float*)(b + JobThrottle) = TurbineModel.IdleFloor;
        *(int*)(b + TargetMaxRpm) = s.Upshift;
        *(int*)(b + TargetMinRpm) = s.Operating;
        
        *(float*)(b + FuelPerRotation) = TurbineModel.FuelFullPerSecond(s) / Math.Max(1f, s.Safe / 60f);

        
        var table = *(IntPtr*)(b + TorqueTable);
        var n = *(int*)(b + TorqueTableLength);
        if (table == IntPtr.Zero || n < 2)
        {
            Plugin.Log.LogWarning("Engine torque table missing — gas turbine curve not applied");
            return;
        }
        var top = 1.35f * s.Safe;
        var f = (float*)table;
        for (var i = 0; i < n; i++)
        {
            var rpm = top * i / (n - 1);
            f[i * 2] = rpm * k;
            f[i * 2 + 1] = TurbineModel.TorqueAt(s, rpm);
        }
    }

    [HarmonyPatch(typeof(EngineBehaviour), nameof(EngineBehaviour.Dispose))]
    private static class DisposePatch
    {
        private static void Postfix(EngineBehaviour __instance)
        {
            try { Map.Remove(__instance.Pointer); }
            catch { }
        }
    }

    
    [HarmonyPatch(typeof(EngineBehaviour), nameof(EngineBehaviour.Throttle), MethodType.Setter)]
    private static class ThrottlePatch
    {
        private static void Prefix(EngineBehaviour __instance, ref float value)
        {
            var t = Get(__instance.Pointer);
            if (t == null) return;
            t.Demand = Math.Clamp(value, 0f, 1f);
            value = t.Throttle;
        }
    }

    [HarmonyPatch(typeof(EngineBehaviour), nameof(EngineBehaviour.Update))]
    private static class UpdatePatch
    {
        private static int _errors;

        private static void Prefix(EngineBehaviour __instance, float deltaTime)
        {
            try
            {
                var t = Get(__instance.Pointer);
                if (t == null) return;
                var b = (byte*)t.Ptr;
                var now = Time.time;

                
                var delta = *(float*)(b + IgnitionDelta);
                if (delta != 0f)
                {
                    if (delta > 0f && b[State] == Off) BeginStart(t);
                    else if (delta < 0f && b[State] == Running) BeginStop(t, ReasonManual);
                    *(float*)(b + IgnitionDelta) = 0f;
                }

                
                if (b[State] == Running && t.Phase == TurbinePhase.Off) t.SetPhase(TurbinePhase.Running);

                
                if (t.Phase == TurbinePhase.Starting)
                {
                    if (!t.RunRequested && now - t.PhaseStart >= RunDelay)
                    {
                        t.RunRequested = true;
                        b[RequestedState] = Running;
                    }
                    if (now - t.PhaseStart >= StartLength) t.SetPhaseQuiet(TurbinePhase.Running);
                }
                if (t.Phase == TurbinePhase.Stopping && now - t.PhaseStart >= Sounds.StopLength) t.SetPhaseQuiet(TurbinePhase.Off);

                
                var running = b[State] == Running;
                var shifting = running && now < t.ShiftUntil;
                if (running)
                {
                    
                    
                    var target = shifting ? Math.Max(t.Demand, t.N1) : t.Demand;
                    var err = target - t.N1;
                    var rate = err * 3f;
                    var up = (TurbineModel.SpoolA + TurbineModel.SpoolB * t.N1) / TurbineModel.SpoolBase();
                    var down = 1f / Math.Max(0.2f, Plugin.SpoolDownTime.Value);
                    t.N1 = Math.Clamp(t.N1 + Math.Clamp(rate, -down, up) * deltaTime, 0f, 1f);
                }
                else t.N1 = 0f;
                var output = shifting ? Math.Min(t.N1, t.Demand) : t.N1;
                t.Throttle = TurbineModel.IdleFloor + (1f - TurbineModel.IdleFloor) * output;
                *(float*)(b + JobThrottle) = t.Throttle;

                
                var w = *(float*)(b + Speed);
                var idleW = *(float*)(b + JobIdleSpeed);
                t.Rpm = w * TurbineModel.RadToRpm;
                if (w > idleW)
                {
                    var brake = TurbineModel.IdleFloor * TurbineModel.TorqueAt(t.Spec, t.Rpm) * Math.Clamp((w - idleW) / 4f, 0f, 1f)
                                + TurbineModel.DragPerRad(t.Spec) * (w - idleW);
                    
                    if (shifting)
                    {
                        var tw = t.ShiftTargetRpm * TurbineModel.RpmToRad;
                        if (tw > idleW && w > tw)
                            brake += Math.Min(TurbineModel.RatedTorque(t.Spec), t.Spec.Inertia * (w - tw) / ShiftSyncTime);
                    }
                    var free = 1f - t.Throttle;
                    var gameFactor = (w - idleW + 5.2359877f) / 6.2359877f * free * free * free * w;
                    *(float*)(b + JobFriction) = gameFactor > 1e-3f ? Math.Clamp(brake / gameFactor, 0f, 200f) : 200f;
                }

                
                t.SavedFuelPerRot = *(float*)(b + FuelPerRotation);
                *(float*)(b + FuelPerRotation) = 0f;
                if (running && deltaTime > 0f)
                {
                    var need = TurbineModel.FuelPerSecond(t.Spec, t.N1) * deltaTime;
                    if (need > 0f)
                    {
                        var fuel = __instance.FuelSource;
                        if (fuel != null)
                        {
                            var got = fuel.Pull(need);
                            if (got < need * 0.5f) t.OutOfFuel = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (_errors++ < 5) Plugin.Log.LogError("Gas turbine update failed: " + e);
            }
        }

        private static void Postfix(EngineBehaviour __instance)
        {
            try
            {
                var t = Get(__instance.Pointer);
                if (t == null) return;
                var b = (byte*)t.Ptr;
                *(float*)(b + FuelPerRotation) = t.SavedFuelPerRot;

                if (t.OutOfFuel)
                {
                    t.OutOfFuel = false;
                    BeginStop(t, ReasonOutOfFuel);
                    return;
                }
                if (b[State] != Off) return;
                if (t.Phase == TurbinePhase.Running || (t.Phase == TurbinePhase.Starting && t.RunRequested))
                {
                    
                    if (b[ShutdownReason] == ReasonStalled) b[RequestedState] = Running;
                    else t.SetPhase(TurbinePhase.Stopping); 
                }
            }
            catch { }
        }
    }
}

internal static class TurbineExtensions
{
    
    public static void SetPhaseQuiet(this Turbine t, TurbinePhase p)
    {
        t.Phase = p;
        t.PhaseStart = Time.time;
    }
}
