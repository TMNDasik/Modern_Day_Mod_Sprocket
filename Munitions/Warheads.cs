using System;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Sprocket.DamageModelling;
using UnityEngine;

namespace Munitions;








internal static unsafe class Warheads
{
    private const int PoolOffset = 0x38, NextIdOffset = 0x44;
    private const int ElemSize = 0x50, ElemBase = 0x20;
    private const int EDef = 0x08, EId = 0x18, ESpawn = 0x1C, EPos = 0x28, EVel = 0x34, EFlags = 0x48;
    private const int FlagActive = 1;

    private static ProjectileRegister _register;
    private static IntPtr _launchMethod;
    private static bool _failed;
    private static int _errors;

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class RegisterPatch
    {
        private static void Prefix(ProjectileRegister __instance) => _register = __instance;
    }

    
    private const int BurstKind = 3;

    
    
    public static bool Fire(Vector3 position, Vector3 direction, int caliber, float penetration, bool ground) =>
        Fire(position, direction, caliber, penetration, 0f, BurstKind, ground);

    
    
    public static bool Fire(Vector3 position, Vector3 direction, int caliber, float penetration, float blast, int burstKind, bool ground)
    {
        if (_failed) return false;
        var register = _register;
        if (register == null || register.WasCollected) return false;
        if (direction.sqrMagnitude < 1e-6f) return false;
        direction.Normalize();
        try
        {
            var reg = register.Pointer;
            var jet = 0f;
            var jetOk = penetration > 0f && Jet(reg, position, direction, caliber, penetration, out jet);
            var blastOk = blast > 0f && Blast(reg, position, direction, caliber, blast);
            if (!jetOk && !blastOk) return false;
            try
            {
                
                if (blastOk) Effects.BlastDetonation(position, -direction, burstKind, caliber, ground);
                else Effects.ExternalDetonation(register, position, -direction, direction * 40f, burstKind, caliber, ground);
            }
            catch (Exception fx) { if (_errors++ < 5) Plugin.Log.LogWarning("Warhead effect failed: " + fx.Message); }
            Plugin.Log.LogInfo($"Warhead: {caliber} mm" + (jetOk ? $", shaped charge {penetration:0} mm (jet {jet:0} m/s)" : "") +
                               (blastOk ? $", blast {blast:0.#} (radius {Mathf.Sqrt(blast / 10f):0.0} m)" : "") + (ground ? ", into the ground" : ""));
            return true;
        }
        catch (Exception ex)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Warhead failed: " + ex);
            return false;
        }
    }

    
    
    public static bool Fuse(Vector3 position, Vector3 velocity, int caliber, float power)
    {
        if (_failed) return false;
        var register = _register;
        if (register == null || register.WasCollected || velocity.sqrMagnitude < 1e-4f) return false;
        try
        {
            var types = TypeRegistry.EnsureBlast(caliber, power, Kind.Fuse);
            if (types == null) return false;
            var typeId = types.TypeIds[Kind.Fuse];
            var def = types.Defs[Kind.Fuse];
            if (typeId == 0 || def == IntPtr.Zero) return false;
            var dir = velocity.normalized;
            if (!Place(register.Pointer, position, dir, velocity, typeId, def)) return false;
            try { Effects.BlastDetonation(position + dir * 0.5f, -dir, 1, caliber, false); }
            catch (Exception fx) { if (_errors++ < 5) Plugin.Log.LogWarning("APHE burst effect failed: " + fx.Message); }
            return true;
        }
        catch (Exception ex)
        {
            if (_errors++ < 5) Plugin.Log.LogError("APHE burst failed: " + ex);
            return false;
        }
    }

    
    private static bool Jet(IntPtr reg, Vector3 position, Vector3 direction, int caliber, float penetration, out float jet)
    {
        jet = 0f;
        var types = TypeRegistry.EnsureWarhead(caliber);
        if (types == null) return false;
        var typeId = types.TypeIds[Kind.Warhead];
        var def = types.Defs[Kind.Warhead];
        if (typeId == 0 || def == IntPtr.Zero) return false;
        var spec = types.Spec;
        var basePen = ShellSpec.Penetration(spec.JetDiameter, spec.JetMass, ShellSpec.JetSpeed, spec.LauncherConstant);
        jet = basePen > 1f ? Mathf.Clamp(ShellSpec.JetSpeed * Mathf.Pow(penetration / basePen, 1f / 1.43f), 150f, 20000f) : ShellSpec.JetSpeed;
        return Place(reg, position, direction, direction * jet, typeId, def);
    }

    
    private static bool Blast(IntPtr reg, Vector3 position, Vector3 direction, int caliber, float power)
    {
        var types = TypeRegistry.EnsureBlast(caliber, power);
        if (types == null) return false;
        var typeId = types.TypeIds[Kind.Blast];
        var def = types.Defs[Kind.Blast];
        if (typeId == 0 || def == IntPtr.Zero) return false;
        return Place(reg, position, direction, direction * 60f, typeId, def);
    }

    private static bool Place(IntPtr reg, Vector3 position, Vector3 direction, Vector3 velocity, int typeId, IntPtr def)
    {
        if (!Launch(reg, position, velocity, typeId)) return false;
        var next = *(int*)(reg + NextIdOffset);
        var id = next == 0 ? 0x7FFFFFFE : next - 1;
        var e = Find(reg, id, def);
        if (e == IntPtr.Zero)
        {
            if (_errors++ < 5) Plugin.Log.LogWarning("Warhead: launched projectile not found in the pool");
            return false;
        }
        *(Vector3*)(e + EPos) = position;
        *(Vector3*)(e + EVel) = velocity;
        *(Vector3*)(e + ESpawn) = position - direction * 50f;
        return true;
    }

    
    
    private static bool Launch(IntPtr reg, Vector3 position, Vector3 velocity, int typeId)
    {
        if (_launchMethod == IntPtr.Zero)
        {
            foreach (var f in typeof(ProjectileRegister).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                if (f.Name.StartsWith("NativeMethodInfoPtr_Launch_", StringComparison.Ordinal))
                {
                    _launchMethod = (IntPtr)f.GetValue(null);
                    break;
                }
            if (_launchMethod == IntPtr.Zero)
            {
                _failed = true;
                Plugin.Log.LogError("Warhead: ProjectileRegister.Launch not found");
                return false;
            }
        }
        var state = stackalloc float[9];
        var dir = velocity.normalized;
        state[0] = position.x; state[1] = position.y; state[2] = position.z;
        state[3] = dir.x; state[4] = dir.y; state[5] = dir.z;
        state[6] = velocity.x; state[7] = velocity.y; state[8] = velocity.z;
        var launcher = stackalloc uint[2];
        *(float*)launcher = 0.5f; 
        launcher[1] = 1;          
        var type = typeId;
        var fire = stackalloc byte[16];
        for (var i = 0; i < 16; i++) fire[i] = 0;
        var args = stackalloc void*[5];
        args[0] = (void*)reg;     
        args[1] = state;
        args[2] = launcher;
        args[3] = &type;
        args[4] = fire;
        var exc = IntPtr.Zero;
        IL2CPP.il2cpp_runtime_invoke(_launchMethod, reg, args, ref exc);
        if (exc != IntPtr.Zero)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Warhead: Launch threw an exception");
            return false;
        }
        return true;
    }

    private static IntPtr Find(IntPtr reg, int id, IntPtr def)
    {
        var pool = *(IntPtr*)(reg + PoolOffset);
        if (pool == IntPtr.Zero) return IntPtr.Zero;
        var len = *(int*)(pool + 0x18);
        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            if (*(int*)(e + EId) == id && (*(int*)(e + EFlags) & FlagActive) != 0 && *(IntPtr*)(e + EDef) == def) return e;
        }
        return IntPtr.Zero;
    }
}
