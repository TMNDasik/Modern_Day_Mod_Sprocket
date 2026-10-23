using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SoundOverhaul;




internal static class Mixer
{
    
    public static Object Get(AudioSource src)
    {
        if (src == null || src.WasCollected) return null;
        var self = src.m_CachedPtr;
        if (self == IntPtr.Zero) return null;
        var handle = AudioSource.get_outputAudioMixerGroup_Injected(self);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var obj = IL2CPP.il2cpp_gchandle_get_target(handle);
            return obj == IntPtr.Zero ? null : new Object(obj);
        }
        finally
        {
            IL2CPP.il2cpp_gchandle_free(handle);
        }
    }

    public static void Set(AudioSource src, Object group)
    {
        if (src == null || src.WasCollected) return;
        var self = src.m_CachedPtr;
        if (self == IntPtr.Zero) return;
        AudioSource.set_outputAudioMixerGroup_Injected(self, Alive(group) ? group.m_CachedPtr : IntPtr.Zero);
    }

    public static bool Alive(Object group)
    {
        try { return group != null && !group.WasCollected && group && group.m_CachedPtr != IntPtr.Zero; }
        catch { return false; }
    }

    public static IntPtr Native(Object group) => Alive(group) ? group.m_CachedPtr : IntPtr.Zero;
}
