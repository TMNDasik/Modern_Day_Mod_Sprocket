using System;
using System.Reflection;
using UnityEngine;

namespace GunStabilizer;




internal static class RangefinderLink
{
    private static FieldInfo _count, _range, _note;
    private static float _nextTry;
    private static bool _logged;

    
    public static string Note()
    {
        try { return _note?.GetValue(null) as string ?? ""; }
        catch { return ""; }
    }

    private static FieldInfo _refusedCount, _refusedNote;

    
    public static bool Refused(out int count, out string note)
    {
        count = 0;
        note = "";
        if (_refusedCount == null) return false;
        try
        {
            count = (int)_refusedCount.GetValue(null);
            note = _refusedNote?.GetValue(null) as string ?? "";
            return true;
        }
        catch { return false; }
    }

    public static bool Read(out int count, out float range)
    {
        count = 0;
        range = 0f;
        if (_count == null && !Find()) return false;
        try
        {
            count = (int)_count.GetValue(null);
            range = (float)_range.GetValue(null);
            return true;
        }
        catch
        {
            _count = _range = null;
            return false;
        }
    }

    private static bool Find()
    {
        if (Time.unscaledTime < _nextTry) return false;
        _nextTry = Time.unscaledTime + 10f;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name is not ("ThermalSight" or "MDThermalSight")) continue;
                var api = a.GetType("ThermalSight.Api");
                var count = api?.GetField("LaserCount", BindingFlags.Public | BindingFlags.Static);
                
                var range = api?.GetField("LastRange", BindingFlags.Public | BindingFlags.Static) ??
                            api?.GetField("LaserRange", BindingFlags.Public | BindingFlags.Static);
                if (count == null || range == null) break;
                _count = count;
                _range = range;
                _note = api.GetField("LastRangeNote", BindingFlags.Public | BindingFlags.Static);
                _refusedCount = api.GetField("LaserRefusedCount", BindingFlags.Public | BindingFlags.Static);
                _refusedNote = api.GetField("LaserRefusedNote", BindingFlags.Public | BindingFlags.Static);
                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo("Linked to Thermal Sight: its laser rangefinder feeds the fire control");
                }
                return true;
            }
        }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Rangefinder link failed: " + e.Message); }
        return false;
    }
}
