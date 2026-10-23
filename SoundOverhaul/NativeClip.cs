using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Bindings;

namespace SoundOverhaul;







internal static unsafe class NativeClip
{
    
    private static readonly List<AudioClip> Broken = new();

    public static AudioClip Create(string name, float[] samples, int frames, int channels, int frequency)
    {
        if (samples == null || frames <= 0 || channels <= 0 || frequency <= 0 || samples.Length < frames * channels)
            throw new ArgumentException("bad sound data");
        if (string.IsNullOrEmpty(name)) name = "Sound Overhaul clip";

        var clip = AudioClip.Construct_Internal();
        if (clip == null) throw new InvalidOperationException("Unity did not create the clip");
        clip.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        var self = clip.m_CachedPtr;
        if (self == IntPtr.Zero)
        {
            Broken.Add(clip);
            throw new InvalidOperationException("the clip has no native object");
        }

        var span = default(ManagedSpanWrapper);
        try
        {
            fixed (char* p = name)
            {
                Fill(&span, (IntPtr)p, name.Length);
                AudioClip.CreateUserSound_Injected(self, ref span, frames, channels, frequency, false);
            }
            bool ok;
            fixed (float* s = samples)
            {
                Fill(&span, (IntPtr)s, frames * channels);
                ok = AudioClip.SetData_Injected(self, ref span, 0);
            }
            if (!ok) throw new InvalidOperationException("Unity refused the sound data");
            return clip;
        }
        catch
        {
            Broken.Add(clip);
            throw;
        }
    }

    private static void Fill(ManagedSpanWrapper* w, IntPtr begin, int length)
    {
        *(IntPtr*)w = begin;
        *(int*)((byte*)w + 8) = length;
    }
}
