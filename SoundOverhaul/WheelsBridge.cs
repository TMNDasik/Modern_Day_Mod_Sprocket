using System;
using UnityEngine;

namespace SoundOverhaul;



internal static class WheelsBridge
{
    private static bool _searched;
    private static float _retry;
    private static Func<IntPtr, bool> _hidden;

    private static void Find()
    {
        if (_searched && (_hidden != null || Time.unscaledTime < _retry)) return;
        _searched = true;
        _retry = Time.unscaledTime + 10f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("Wheels" or "MDWheels")) continue;
                var m = a.GetType("Wheels.Api")?.GetMethod("IsHiddenTrack", new[] { typeof(IntPtr) });
                if (m != null) _hidden = (Func<IntPtr, bool>)Delegate.CreateDelegate(typeof(Func<IntPtr, bool>), m);
                break;
            }
        }
        catch { }
    }

    public static bool HiddenTrack(IntPtr track)
    {
        Find();
        if (_hidden == null) return false;
        try { return _hidden(track); }
        catch { return false; }
    }
}
