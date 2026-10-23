using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using Sprocket.Vehicles.CrewSystems;
using UnityEngine;

namespace Munitions;














internal static unsafe class CrewDamage
{
    
    
    private const int PoolRegister = 0x18, PoolCurrent = 0x20, PoolMax = 0x24, PoolChanged = 0x28, PoolMods = 0x48, RegisterChanged = 0x20;
    
    private const int SimApplier = 0x18, SimObjects = 0x28, SimFragments = 0x80, SimFragCount = 0x88;
    
    private const int FragSize = 0x50, FragMass = 4, FragSpeed = 8, FragHits = 0x38, FragFlags = 0x48;
    private const int ArrLength = 0x18, ArrData = 0x20;
    private const int StructureApplier = 0x78;    
    private const int SeatVuid = 0x20;            
    public const float CrewHealth = 100f;

    private sealed class Seat
    {
        public IntPtr Pool;
        public int Id;
        public Transform T;
        public int Index;
    }

    private sealed class VehicleCrew
    {
        public string Name;
        public readonly List<Seat> Seats = new();
        public readonly Dictionary<int, Seat> ById = new();
    }

    private sealed class Entry
    {
        public IntPtr Pool;
        public float Before, Ours;
        public VehicleCrew Vehicle;
        public Seat Seat;
        public string Why;
    }

    private static readonly Dictionary<IntPtr, VehicleCrew> ByApplier = new();
    private static readonly Dictionary<IntPtr, Entry> Ledger = new();
    private static readonly Dictionary<IntPtr, float> SpallSum = new();
    private static readonly Dictionary<IntPtr, float> PenetratorSum = new();
    private static float _nextRefresh, _nextOnDemand;
    private static int _context, _errors;
    private static bool _announced, _firstLogged;

    private static bool Enabled => Plugin.CrewDamageEnabled != null && Plugin.CrewDamageEnabled.Value;
    private static float Toughness => Mathf.Clamp(Plugin.CrewToughness.Value, 0.25f, 4f);

    

    private static void Refresh(bool force)
    {
        var now = Time.unscaledTime;
        if (!force && now < _nextRefresh) return;
        if (force && now < _nextOnDemand) return;
        _nextRefresh = now + 2f;
        _nextOnDemand = now + 0.5f;
        ByApplier.Clear();
        foreach (var v in UnityEngine.Object.FindObjectsOfType<Vehicle>())
        {
            try
            {
                if (v == null || v.releasing || v.released) continue;
                var reg = v.behaviour?.healthRegister;
                if (reg == null) continue;
                var crew = new VehicleCrew { Name = v.name };
                var i = 0;
                foreach (var seat in v.GetComponentsInChildren<CrewSeat>(true))
                {
                    var pool = seat?.healthPool;
                    if (pool == null) continue;
                    var s = new Seat { Pool = pool.Pointer, Id = *(int*)(seat.Pointer + SeatVuid), T = seat.transform, Index = i++ };
                    crew.Seats.Add(s);
                    crew.ById[s.Id] = s;
                }
                if (crew.Seats.Count > 0) ByApplier[reg.Pointer] = crew;
            }
            catch { }
        }
        if (!_announced && ByApplier.Count > 0)
        {
            _announced = true;
            Plugin.Log.LogInfo($"Crew damage model active (toughness {Toughness:0.##}, blast to crew {Plugin.CrewBlast.Value:0.##}): " +
                               $"{ByApplier.Count} vehicles with crew");
        }
    }

    private static VehicleCrew CrewOf(IntPtr applier)
    {
        if (applier == IntPtr.Zero) return null;
        Refresh(false);
        if (ByApplier.TryGetValue(applier, out var c)) return c;
        Refresh(true);
        return ByApplier.TryGetValue(applier, out c) ? c : null;
    }

    private static Entry Touch(VehicleCrew v, Seat s)
    {
        if (Ledger.TryGetValue(s.Pool, out var e)) return e;
        e = new Entry { Pool = s.Pool, Before = *(float*)(s.Pool + PoolCurrent), Vehicle = v, Seat = s };
        Ledger[s.Pool] = e;
        return e;
    }

    
    private static void Commit(bool clear)
    {
        if (Ledger.Count == 0) return;
        foreach (var e in Ledger.Values)
        {
            try
            {
                var p = e.Pool;
                var max = *(float*)(p + PoolMax);
                var now = *(float*)(p + PoolCurrent);
                var target = Mathf.Clamp(e.Before - e.Ours, 0f, Mathf.Max(max, e.Before));
                if (Math.Abs(target - now) < 0.001f) continue;
                if (!_firstLogged)
                {
                    _firstLogged = true;
                    Plugin.Log.LogInfo($"Crew damage: first correction — '{e.Vehicle.Name}' crew #{e.Seat.Index} would have lost {e.Before - now:0} " +
                                       $"of {e.Before:0} health ({e.Why}), loses {e.Before - target:0}");
                }
                if (Plugin.DebugLogging.Value)
                    Plugin.Log.LogInfo($"Crew damage: '{e.Vehicle.Name}' crew #{e.Seat.Index}: game {e.Before - now:0} → {e.Ours:0} ({e.Why}), " +
                                       $"health {target:0}/{max:0}");
                *(float*)(p + PoolCurrent) = target;
                *(bool*)(p + PoolChanged) = true;
                var reg = *(IntPtr*)(p + PoolRegister);
                if (reg != IntPtr.Zero) *(bool*)(reg + RegisterChanged) = true;
            }
            catch { }
        }
        if (clear) Ledger.Clear();
    }

    

    [HarmonyPatch(typeof(PenetrationSimulation), nameof(PenetrationSimulation.ApplyFragmentHits))]
    private static class FragmentPatch
    {
        private static void Prefix(PenetrationSimulation __instance)
        {
            if (!Enabled) return;
            try { Fragments(__instance.Pointer); }
            catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Crew damage (fragments): " + e.Message); }
        }

        private static void Postfix()
        {
            if (!Enabled || Ledger.Count == 0) return;
            try { Commit(_context == 0); }
            catch { }
        }
    }

    private static void Fragments(IntPtr sim)
    {
        var crew = CrewOf(*(IntPtr*)(sim + SimApplier));
        if (crew == null) return;
        var objects = *(IntPtr*)(sim + SimObjects);
        var frags = *(IntPtr*)(sim + SimFragments);
        if (objects == IntPtr.Zero || frags == IntPtr.Zero) return;
        var objCount = *(int*)(objects + ArrLength);
        var count = Math.Min(*(int*)(sim + SimFragCount), *(int*)(frags + ArrLength));
        SpallSum.Clear();
        PenetratorSum.Clear();
        for (var i = 0; i < count; i++)
        {
            var f = frags + ArrData + i * FragSize;
            var mass = *(float*)(f + FragMass);
            if (mass <= 0f) continue;
            var hits = *(IntPtr*)(f + FragHits);
            if (hits == IntPtr.Zero) continue;
            var n = *(int*)(hits + ArrLength);
            if (n <= 0) continue;
            var speed = *(float*)(f + FragSpeed);
            var energy = 0.5f * mass * speed * speed;
            var penetrator = (*(ushort*)(f + FragFlags) & 1) != 0;
            for (var k = 0; k < n; k++)
            {
                int idx = *(short*)(hits + ArrData + k * 2);
                if (idx < 0 || idx >= objCount) continue;
                var id = *(int*)(objects + ArrData + idx * 8);
                if (!crew.ById.TryGetValue(id, out var seat)) continue;
                var e = Touch(crew, seat);
                var piercing = *(float*)(seat.Pool + PoolMods);
                if (penetrator)
                {
                    PenetratorSum.TryGetValue(seat.Pool, out var ps);
                    PenetratorSum[seat.Pool] = ps + piercing * (35f + energy / 60f);
                }
                else if (energy > 20f)
                {
                    SpallSum.TryGetValue(seat.Pool, out var ss);
                    SpallSum[seat.Pool] = ss + piercing * 16f * Mathf.Log(1f + energy / 250f, 2f);
                }
                e.Why = penetrator ? "hit by the round" : "spall";
            }
        }
        var t = Toughness;
        foreach (var kv in SpallSum)
            if (Ledger.TryGetValue(kv.Key, out var e)) e.Ours += 120f * (1f - Mathf.Exp(-kv.Value / 110f)) / t;
        foreach (var kv in PenetratorSum)
            if (Ledger.TryGetValue(kv.Key, out var e)) e.Ours += kv.Value / t;
    }

    

    [HarmonyPatch(typeof(DamageModel), nameof(DamageModel.SphereCast))]
    private static class BlastPatch
    {
        private static void Postfix(Vector3 position, float radius, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DamageModelSphereCastHit> __result)
        {
            if (!Enabled || _context == 0 || __result == null) return;
            try { Blast(position, radius, __result.Pointer); }
            catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Crew damage (blast): " + e.Message); }
        }
    }

    private static void Blast(Vector3 position, float radius, IntPtr hits)
    {
        var n = *(int*)(hits + ArrLength);
        if (n <= 0 || radius <= 0f) return;
        var power = radius * radius * 10f;       
        var share = Mathf.Clamp01(Plugin.CrewBlast.Value);
        var t = Toughness;
        for (var i = 0; i < n; i++)
        {
            var model = *(IntPtr*)(hits + ArrData + i * 0x10);
            if (model == IntPtr.Zero) continue;
            var structure = new Il2CppSystem.Object(model).TryCast<CompoundStructure>();
            if (structure == null) continue;
            var crew = CrewOf(*(IntPtr*)(model + StructureApplier));
            if (crew == null) continue;
            foreach (var seat in crew.Seats)
            {
                if (seat.T == null || seat.T.WasCollected) continue;
                var e = Touch(crew, seat);
                var d = Vector3.Distance(seat.T.position + seat.T.up * 0.55f, position);
                var pressure = *(float*)(seat.Pool + PoolMods + 8);
                var dmg = d > radius * 1.5f ? 0f : share * Mathf.Min(power, power / Mathf.Max(0.5f, d * d)) * pressure / t;
                e.Ours += dmg;
                if (dmg > 0.5f) e.Why = $"blast {power:0} at {d:0.0} m";
                else e.Why ??= "blast out of reach";
            }
        }
    }

    

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class StepPatch
    {
        private static void Prefix() => _context++;

        private static void Postfix()
        {
            _context = Math.Max(0, _context - 1);
            if (_context != 0 || Ledger.Count == 0) return;
            try { Commit(true); }
            catch { Ledger.Clear(); }
        }
    }

    [HarmonyPatch(typeof(Sprocket.Landmines.ATMine), nameof(Sprocket.Landmines.ATMine.Trigger))]
    private static class MinePatch
    {
        private static void Prefix() => _context++;

        private static void Postfix()
        {
            _context = Math.Max(0, _context - 1);
            if (_context != 0 || Ledger.Count == 0) return;
            try { Commit(true); }
            catch { Ledger.Clear(); }
        }
    }
}
