using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Unity.IL2CPP.Hook;
using Il2CppInterop.Common;

namespace SoundOverhaul;








internal static unsafe class NativeHook
{
    private static readonly List<object> Keep = new(); 

    [DllImport("kernel32.dll")] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    private const uint ExecuteReadWrite = 0x40;

    
    public static T Apply<T>(MethodInfo generated, T hook, out bool chained) where T : Delegate
    {
        if (generated == null) throw new ArgumentNullException(nameof(generated));
        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(generated)
                    ?? throw new InvalidOperationException("no native method for " + generated.Name);
        var methodInfo = (IntPtr)field.GetValue(null);
        if (methodInfo == IntPtr.Zero) throw new InvalidOperationException("native method not resolved: " + generated.Name);
        var code = *(IntPtr*)methodInfo; 
        if (code == IntPtr.Zero) throw new InvalidOperationException("native code missing: " + generated.Name);

        var slot = JumpSlot(code);
        if (slot != IntPtr.Zero)
        {
            var next = *(IntPtr*)slot;
            if (next == IntPtr.Zero) throw new InvalidOperationException("another mod's hook on " + generated.Name + " has no target");
            var previous = Marshal.GetDelegateForFunctionPointer<T>(next);
            var mine = Marshal.GetFunctionPointerForDelegate(hook);
            Keep.Add(hook);
            Keep.Add(previous);
            WriteSlot(slot, mine);
            chained = true;
            return previous;
        }
        if (StartsWithJump(code))
            throw new InvalidOperationException("another mod already hooks " + generated.Name + " in a way that cannot be shared");

        chained = false;
        var detour = INativeDetour.CreateAndApply(code, hook, out T original);
        Keep.Add(hook);
        Keep.Add(original);
        Keep.Add(detour);
        return original;
    }

    
    
    private static IntPtr JumpSlot(IntPtr code)
    {
        var p = (byte*)code;
        for (var hop = 0; hop < 3; hop++)
        {
            if (p[0] == 0xFF && p[1] == 0x25) return (IntPtr)(p + 6 + *(int*)(p + 2));
            if (p[0] == 0x48 && p[1] == 0xB8 && p[10] == 0xFF && p[11] == 0xE0) return (IntPtr)(p + 2);
            if (p[0] == 0xE9) { p = p + 5 + *(int*)(p + 1); continue; }
            if (p[0] == 0xEB) { p = p + 2 + (sbyte)p[1]; continue; }
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    
    private static bool StartsWithJump(IntPtr code)
    {
        var p = (byte*)code;
        return p[0] is 0xE9 or 0xEB || (p[0] == 0xFF && (p[1] & 0x30) == 0x20) || (p[0] == 0x68 && p[5] == 0xC3);
    }

    private static void WriteSlot(IntPtr slot, IntPtr value)
    {
        if (!VirtualProtect(slot, (UIntPtr)8, ExecuteReadWrite, out var old))
            throw new InvalidOperationException("cannot write the other mod's hook address");
        Interlocked.Exchange(ref *(long*)slot, (long)value);
        VirtualProtect(slot, (UIntPtr)8, old, out _);
        FlushInstructionCache(GetCurrentProcess(), slot, (UIntPtr)8);
    }
}
