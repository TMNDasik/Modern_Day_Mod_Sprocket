using System;
using UnityEngine;

namespace Munitions;



internal static class SoundBridge
{
    private static bool _found;
    private static float _nextTry;
    private static Func<bool> _active;
    private static Func<string, Vector3, Transform, float, float, bool> _play;
    private static Func<string, Transform, float, int> _loop;
    private static Action<int, float> _stopLoop;
    private static Func<int, int, Transform, bool, float, bool> _carousel;
    private static Func<bool, float> _loadLength;

    private static bool Find()
    {
        if (_found) return true;
        if (Time.unscaledTime < _nextTry) return false;
        _nextTry = Time.unscaledTime + 5f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("SoundOverhaul" or "MDSoundOverhaul")) continue;
                var t = a.GetType("SoundOverhaul.Api");
                if (t == null) return false;
                _active = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), t.GetProperty("Active").GetGetMethod());
                _play = (Func<string, Vector3, Transform, float, float, bool>)Delegate.CreateDelegate(
                    typeof(Func<string, Vector3, Transform, float, float, bool>), t.GetMethod("Play"));
                _loop = (Func<string, Transform, float, int>)Delegate.CreateDelegate(typeof(Func<string, Transform, float, int>), t.GetMethod("Loop"));
                _stopLoop = (Action<int, float>)Delegate.CreateDelegate(typeof(Action<int, float>), t.GetMethod("StopLoop"));
                _carousel = (Func<int, int, Transform, bool, float, bool>)Delegate.CreateDelegate(
                    typeof(Func<int, int, Transform, bool, float, bool>), t.GetMethod("Carousel"));
                _loadLength = (Func<bool, float>)Delegate.CreateDelegate(typeof(Func<bool, float>), t.GetMethod("CarouselLoadLength"));
                _found = true;
                return true;
            }
        }
        catch (Exception e)
        {
            _active = null;
            Plugin.Log.LogWarning("Sound Overhaul link failed: " + e.Message);
            _nextTry = float.MaxValue;
        }
        return false;
    }

    
    public static bool Active
    {
        get
        {
            if (!Find()) return false;
            try { return _active(); }
            catch { return false; }
        }
    }

    
    public static bool Play(string bank, Vector3 position, Transform part, float volume, float pitch = 1f)
    {
        if (!Active) return false;
        try { return _play(bank, position, part, volume, pitch); }
        catch { return false; }
    }

    
    public static int Loop(string bank, Transform follow, float volume)
    {
        if (!Active) return 0;
        try { return _loop(bank, follow, volume); }
        catch { return 0; }
    }

    public static void StopLoop(int id, float fade)
    {
        if (id == 0 || !_found) return;
        try { _stopLoop(id, fade); }
        catch { }
    }

    
    public static bool Carousel(int id, int evt, Transform gun, bool mz, float value)
    {
        if (!_found && !Find()) return false;
        try { return _carousel(id, evt, gun, mz, value); }
        catch { return false; }
    }

    public static float CarouselLoadLength(bool mz)
    {
        if (!Active) return 0f;
        try { return _loadLength(mz); }
        catch { return 0f; }
    }
}
