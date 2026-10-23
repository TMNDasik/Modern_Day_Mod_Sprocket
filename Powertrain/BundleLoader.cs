using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Powertrain;




internal static unsafe class BundleLoader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ManagedSpanWrapper
    {
        public IntPtr Begin;
        public int Length;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadFromFileFn(ref ManagedSpanWrapper path, uint crc, ulong offset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadAssetFn(IntPtr self, ref ManagedSpanWrapper name, IntPtr type);

    private static LoadFromFileFn _loadFromFile;
    private static LoadAssetFn _loadAsset;

    private static T Resolve<T>(string name) where T : Delegate
    {
        var ptr = IL2CPP.il2cpp_resolve_icall(name);
        if (ptr == IntPtr.Zero) throw new MissingMethodException("icall not found: " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    
    
    private static IntPtr HandleToObject(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return IntPtr.Zero;
        if (((long)handle & 1) != 0) return IL2CPP.il2cpp_gchandle_get_target(handle);
        return *(IntPtr*)handle;
    }

    public static AssetBundle LoadFromFile(string path)
    {
        _loadFromFile ??= Resolve<LoadFromFileFn>("UnityEngine.AssetBundle::LoadFromFile_Internal_Injected");
        IntPtr ret;
        fixed (char* p = path)
        {
            var span = new ManagedSpanWrapper { Begin = (IntPtr)p, Length = path.Length };
            ret = _loadFromFile(ref span, 0, 0);
        }
        var obj = HandleToObject(ret);
        return obj == IntPtr.Zero ? null : new AssetBundle(obj);
    }

    public static T LoadAsset<T>(AssetBundle bundle, string name) where T : UnityEngine.Object
    {
        try
        {
            var asset = bundle.LoadAsset(name, Il2CppType.Of<T>());
            if (asset != null) return asset.TryCast<T>();
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"LoadAsset wrapper unavailable ({e.GetType().Name}), using icall");
        }

        _loadAsset ??= Resolve<LoadAssetFn>("UnityEngine.AssetBundle::LoadAsset_Internal_Injected");
        IntPtr ret;
        fixed (char* p = name)
        {
            var span = new ManagedSpanWrapper { Begin = (IntPtr)p, Length = name.Length };
            ret = _loadAsset(bundle.m_CachedPtr, ref span, Il2CppType.Of<T>().Pointer);
        }
        var obj = HandleToObject(ret);
        return obj == IntPtr.Zero ? null : new UnityEngine.Object(obj).TryCast<T>();
    }
}
