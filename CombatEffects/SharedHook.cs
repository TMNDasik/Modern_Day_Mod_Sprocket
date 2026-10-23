using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CombatEffects;







internal static class SharedHook
{
    public const string MarkKey = "SprocketMods.Hook.ProjectileImpactMark.PlayEffect";
    public const string EffectKey = "SprocketMods.Hook.ProjectileImpactEffect.PlayEffect";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PlayEffectFn(IntPtr self, IntPtr info, IntPtr method);

    private sealed class Host
    {
        public List<Action<IntPtr, IntPtr, bool>> Listeners;
        public PlayEffectFn Original;

        public void Detour(IntPtr self, IntPtr info, IntPtr method)
        {
            var list = Listeners;
            for (var i = 0; i < list.Count; i++)
                try { list[i](self, info, false); } catch { }
            Original(self, info, method);
            for (var i = 0; i < list.Count; i++)
                try { list[i](self, info, true); } catch { }
        }
    }

    
    public static bool Register(MethodInfo generated, string key, Action<IntPtr, IntPtr, bool> listener)
    {
        var domain = AppDomain.CurrentDomain;
        if (domain.GetData(key) is List<Action<IntPtr, IntPtr, bool>> existing)
        {
            existing.Add(listener);
            return false;
        }
        var host = new Host { Listeners = new List<Action<IntPtr, IntPtr, bool>> { listener } };
        PlayEffectFn hook = host.Detour;
        host.Original = NativeHook.Apply(generated, hook, out var chained);
        domain.SetData(key, host.Listeners);
        
        if (chained) Plugin.Log.LogInfo($"{generated.DeclaringType?.Name}.{generated.Name}: another mod already hooks it — chained in front of its hook");
        return true;
    }
}
