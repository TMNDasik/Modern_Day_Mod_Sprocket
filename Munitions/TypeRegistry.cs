using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.DamageModelling;

namespace Munitions;


internal sealed class SizeTypes
{
    public int SizeValue;          
    public int Caliber, ShellLength, PropellantLength;
    public ShellSpec Spec;
    public float Power;            
    public readonly int[] TypeIds = new int[Kind.Total];
    public readonly IntPtr[] Defs = new IntPtr[Kind.Total];
    public readonly Il2CppSystem.Guid[] Guids = new Il2CppSystem.Guid[Kind.Total];
    public bool AtgmAllowed;

    public ProjectileDefinition Def(int kind) => Defs[kind] == IntPtr.Zero ? null : new ProjectileDefinition(Defs[kind]);
}


internal static class TypeRegistry
{
    private static readonly Dictionary<int, SizeTypes> BySize = new();
    private static readonly Dictionary<int, (SizeTypes Types, int Kind)> ByType = new();

    public static bool TryGetKind(int typeId, out SizeTypes types, out int kind)
    {
        if (ByType.TryGetValue(typeId, out var e))
        {
            types = e.Types;
            kind = e.Kind;
            return true;
        }
        types = null;
        kind = -1;
        return false;
    }

    public static SizeTypes Peek(int sizeValue) => BySize.TryGetValue(sizeValue, out var t) ? t : null;

    private static readonly Dictionary<int, SizeTypes> ByLauncher = new();

    
    private static bool Register(IProjectileTypeRegister register, SizeTypes t, int kind)
    {
        IProjectileType type;
        var guid = t.Guids[kind];
        if (register.IsRegistered(guid))
        {
            if (!register.TryGet(guid, out type) || type == null) return false;
        }
        else
        {
            type = Create(register, t, kind);
            if (type == null) return false;
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Registered {type.Name} (id {type.ID.Value})");
        }
        var id = type.ID.Value;
        if (t.TypeIds[kind] != id && t.TypeIds[kind] != 0) ByType.Remove(t.TypeIds[kind]);
        t.TypeIds[kind] = id;
        t.Defs[kind] = type.Pointer;
        ByType[id] = (t, kind);
        return true;
    }

    private static readonly int[] LauncherKinds = { Kind.Launcher, Kind.LauncherHe, Kind.LauncherTb };
    private static readonly int[] RocketKinds = { Kind.RocketHe, Kind.RocketTb, Kind.RocketHeat };

    
    
    public static SizeTypes EnsureLauncher(int caliber)
    {
        caliber = Math.Clamp(caliber, LauncherPart.MinCaliber, LauncherPart.MaxCaliber);
        var register = IProjectileTypeRegister.Instance;
        if (register == null) return null;
        if (!ByLauncher.TryGetValue(caliber, out var t))
        {
            t = new SizeTypes { SizeValue = -caliber, Caliber = caliber, Spec = ShellSpec.For(caliber), AtgmAllowed = true };
            foreach (var k in LauncherKinds) t.Guids[k] = MakeGuid(k, caliber, 0);
            ByLauncher[caliber] = t;
        }
        foreach (var k in LauncherKinds)
        {
            try
            {
                if (!Register(register, t, k) && k == Kind.Launcher) return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not register {caliber} mm {Kind.Names[k]}: {e.Message}");
                if (k == Kind.Launcher) return null;
            }
        }
        return t;
    }

    private static readonly Dictionary<int, SizeTypes> ByWarhead = new();

    
    public static SizeTypes EnsureWarhead(int caliber)
    {
        caliber = Math.Clamp(caliber, 20, 300);
        var register = IProjectileTypeRegister.Instance;
        if (register == null) return null;
        if (!ByWarhead.TryGetValue(caliber, out var t))
        {
            t = new SizeTypes { SizeValue = -(2000 + caliber), Caliber = caliber, Spec = ShellSpec.For(caliber) };
            t.Guids[Kind.Warhead] = MakeGuid(Kind.Warhead, caliber, 0);
            ByWarhead[caliber] = t;
        }
        try { return Register(register, t, Kind.Warhead) ? t : null; }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not register {caliber} mm shaped charge: {e.Message}");
            return null;
        }
    }

    private static readonly Dictionary<long, SizeTypes> ByBlast = new();

    
    
    public static SizeTypes EnsureBlast(int caliber, float power, int kind = Kind.Blast)
    {
        caliber = Math.Clamp(caliber, 20, 300);
        var tenths = Math.Clamp((int)MathF.Round(power * 10f), 5, 100000);
        var register = IProjectileTypeRegister.Instance;
        if (register == null) return null;
        var key = ((long)caliber * 1000000L + tenths) * 2 + (kind == Kind.Fuse ? 1 : 0);
        if (!ByBlast.TryGetValue(key, out var t))
        {
            t = new SizeTypes { SizeValue = -(3000 + caliber), Caliber = caliber, Spec = ShellSpec.For(caliber), Power = tenths / 10f };
            t.Guids[kind] = MakeGuid(kind, caliber, tenths);
            ByBlast[key] = t;
        }
        try { return Register(register, t, kind) ? t : null; }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Could not register {caliber} mm {Kind.Names[kind]}: {e.Message}");
            return null;
        }
    }

    private static readonly Dictionary<int, SizeTypes> ByRocket = new();

    
    public static SizeTypes EnsureRocket(int caliber)
    {
        caliber = Math.Clamp(caliber, RocketPart.MinCaliber, RocketPart.MaxCaliber);
        var register = IProjectileTypeRegister.Instance;
        if (register == null) return null;
        if (!ByRocket.TryGetValue(caliber, out var t))
        {
            t = new SizeTypes { SizeValue = -(1000 + caliber), Caliber = caliber, Spec = ShellSpec.For(caliber) };
            foreach (var k in RocketKinds) t.Guids[k] = MakeGuid(k, caliber, 0);
            ByRocket[caliber] = t;
        }
        foreach (var kind in RocketKinds)
        {
            try
            {
                if (!Register(register, t, kind)) return null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not register {caliber} mm {Kind.Names[kind]}: {e.Message}");
                return null;
            }
        }
        return t;
    }

    
    private static readonly int[] SizeKinds =
        { Kind.Apfsds, Kind.He, Kind.Atgm, Kind.Heat, Kind.Ap, Kind.Aphe, Kind.Hesh, Kind.AtgmHe, Kind.AtgmTb };

    private static bool IsAtgmKind(int kind) => kind is Kind.Atgm or Kind.AtgmHe or Kind.AtgmTb;

    
    public static SizeTypes Ensure(int sizeValue)
    {
        var caliber = (sizeValue >> 16) & 0xFFFF;
        var shellLength = sizeValue & 0xFFFF;
        var propellant = shellLength - 3 * caliber;
        if (caliber <= 0 || propellant <= 0 || !Design.Managed(caliber)) return null;

        var register = IProjectileTypeRegister.Instance;
        if (register == null) return null;

        if (!BySize.TryGetValue(sizeValue, out var t))
        {
            t = new SizeTypes
            {
                SizeValue = sizeValue,
                Caliber = caliber,
                ShellLength = shellLength,
                PropellantLength = propellant,
                Spec = ShellSpec.For(caliber),
                AtgmAllowed = Design.AtgmAllowed(caliber)
            };
            foreach (var k in SizeKinds) t.Guids[k] = MakeGuid(k, caliber, shellLength);
            BySize[sizeValue] = t;
        }

        foreach (var k in SizeKinds)
        {
            if (IsAtgmKind(k) && !t.AtgmAllowed) continue;
            try { Register(register, t, k); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not register {caliber} mm {Kind.Names[k]}: {e.Message}");
            }
        }
        return t;
    }

    private static IProjectileType Create(IProjectileTypeRegister register, SizeTypes t, int kind)
    {
        var s = t.Spec;
        var cal = t.Caliber;
        var name = kind is Kind.Blast or Kind.Fuse
            ? $"{cal}mm {Kind.Names[kind]} {t.Power.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{cal}mm {Kind.Names[kind]}";
        var propellant = (ushort)Math.Clamp(t.PropellantLength, 1, ushort.MaxValue);
        ProjectileFunctionDefinition[] functions;
        float mass, diameter, length, lifetime;
        switch (kind)
        {
            case Kind.Apfsds:
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.RodConstant),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.RodMass;
                diameter = s.RodDiameter / 1000f;
                length = s.RodLength / 1000f;
                lifetime = 10f;
                break;
            case Kind.He:
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.HeConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.HePower),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.HeMass;
                diameter = cal / 1000f;
                length = 2.46f * cal / 1000f;
                lifetime = 10f;
                break;
            case Kind.Heat:
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.HeatShellConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.HeatShellBlast),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.HeatShellMass;
                diameter = cal / 1000f;
                length = s.HeatShellLength;
                lifetime = 10f;
                break;
            case Kind.Ap:
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.ApConstant),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.ApMass;
                diameter = cal / 1000f;
                length = s.ApLength;
                lifetime = 10f;
                break;
            case Kind.Aphe:
                
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.ApheConstant),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.ApheMass;
                diameter = cal / 1000f;
                length = s.ApheLength;
                lifetime = 10f;
                break;
            case Kind.Hesh:
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.HeshConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.HeshPower),
                    new DetonationPropellantFunctionDefinition(propellant)
                };
                mass = s.HeshMass;
                diameter = cal / 1000f;
                length = s.HeshLength;
                lifetime = 10f;
                break;
            case Kind.Launcher:
            case Kind.LauncherHe:
            case Kind.LauncherTb:
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(kind == Kind.LauncherHe ? s.LauncherHeConstant
                        : kind == Kind.LauncherTb ? s.LauncherTbConstant : s.LauncherConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.MissileBlast(kind))
                };
                mass = s.JetMass;
                diameter = s.JetDiameter / 1000f;
                length = 7f * cal / 1000f;
                lifetime = AtgmSettings.MaxFlightCap + 3f; 

                break;
            case Kind.Warhead:
                
                
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(s.LauncherConstant)
                };
                mass = s.JetMass;
                diameter = s.JetDiameter / 1000f;
                length = 7f * cal / 1000f;
                lifetime = 0.1f; 
                break;
            case Kind.Blast:
            case Kind.Fuse:
                
                
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new HighExplosiveProjectileFunctionDefinition(0f, Math.Max(0.5f, t.Power))
                };
                mass = 1f;
                diameter = cal / 1000f;
                length = 3f * cal / 1000f;
                lifetime = kind == Kind.Fuse ? ApheFuse.Lifetime : 1f;
                break;
            case Kind.RocketHe:
            case Kind.RocketTb:
            case Kind.RocketHeat:
                
                
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(kind == Kind.RocketTb ? s.RocketTbConstant
                        : kind == Kind.RocketHeat ? s.RocketHeatConstant : s.RocketHeConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.MissileBlast(kind))
                };
                mass = s.RocketMass;
                diameter = cal / 1000f;
                length = RocketPart.BodyCalibers * cal / 1000f;
                lifetime = RocketPart.Lifetime;
                break;
            default:
                
                
                functions = new ProjectileFunctionDefinition[]
                {
                    new ArmouredPiercingProjectileFunctionDefinition(kind == Kind.AtgmHe ? s.AtgmHeConstant
                        : kind == Kind.AtgmTb ? s.AtgmTbConstant : s.AtgmConstant),
                    new HighExplosiveProjectileFunctionDefinition(0f, s.MissileBlast(kind))
                };
                mass = s.JetMass;
                diameter = s.JetDiameter / 1000f;
                length = 5f * cal / 1000f;
                lifetime = AtgmSettings.MaxFlightCap + 3f;
                break;
        }
        var array = new Il2CppReferenceArray<ProjectileFunctionDefinition>(functions);
        var list = new Il2CppSystem.Collections.Generic.IReadOnlyList<ProjectileFunctionDefinition>(array.Pointer);
        return register.Create(name, t.Guids[kind], mass, diameter, length, 1f, lifetime, list);
    }

    private static Il2CppSystem.Guid MakeGuid(int kind, int caliber, int shellLength)
    {
        using var md5 = MD5.Create();
        var b = md5.ComputeHash(Encoding.UTF8.GetBytes($"sprocket.munitions/{Kind.GuidKey(kind)}/{caliber}/{shellLength}"));
        return new Il2CppSystem.Guid(BitConverter.ToInt32(b, 0), BitConverter.ToInt16(b, 4), BitConverter.ToInt16(b, 6),
            b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
    }
}
