using UnityEngine;

namespace ActiveProtection;


public static class Api
{
    public static float RadioInterference(Vector3 position)
    {
        float strength=0;
        foreach(var m in Modules.Snapshot())
        {
            if(m.Kind!=ModuleKind.RadioJammer || !m.Alive || !m.InBattle || m.Destroyed || m.S.Mode==JammerMode.Off)continue;
            var v=Modules.VehicleOf(m);if(v==null || v.behaviour==null || v.releasing || v.released || !v.BehaviourEnabled)continue;
            var d=position-m.T.position;var distance=d.magnitude;
            if(distance>=m.S.Range || !m.S.InSector(m.ToLocalDir(d)))continue;
            strength=1-(1-strength)*(1-m.S.JamChance*Mathf.SmoothStep(0,1,1-distance/m.S.Range));
        }
        return Mathf.Clamp01(strength);
    }

    
    public static float SmokeDistance(Vector3 origin, Vector3 direction, float max)
    {
        try { return EoSystem.SmokeDistance(origin, direction, max); }
        catch { return -1f; }
    }

    
    public static bool Obscured(Vector3 a, Vector3 b)
    {
        try { return EoSystem.Blocks(a, b); }
        catch { return false; }
    }

    
    
    public static Vector4 SmokeStatus()
    {
        try { return EoSystem.PlayerSmokeStatus(); }
        catch { return Vector4.zero; }
    }

    
    public static string SmokeKeyName() => Plugin.SmokeKey.ToString();

    
    
    
    
    public static float[] ApsSegments()
    {
        try { return Segments(); }
        catch { return System.Array.Empty<float>(); }
    }

    
    private static void Sector(Module l, Transform hull, out float center, out float width)
    {
        var s = l.S;
        if (s.Full360 || s.HalfH >= 179.5f)
        {
            center = 0f;
            width = 360f;
            return;
        }
        
        var rot = l.Rotation;
        float Az(float a)
        {
            var local = hull.InverseTransformDirection(rot * ApsSettings.Dir(a, s.Elevation));
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }
        var left = Az(s.Azimuth - s.HalfH);
        var mid = Az(s.Azimuth);
        var right = Az(s.Azimuth + s.HalfH);
        width = Mathf.Min(360f, Mathf.Repeat(mid - left, 360f) + Mathf.Repeat(right - mid, 360f));
        center = Mathf.Repeat(left + width * 0.5f, 360f);
    }

    private static float[] Segments()
    {
        var v = EoPlayer.InControl ? EoPlayer.Vehicle : null;
        if (v == null || v.WasCollected) return System.Array.Empty<float>();
        var all = Modules.OnVehicle(v);
        var hull = v.transform;
        var result = new System.Collections.Generic.List<float>();
        foreach (var l in all)
        {
            if (l.Kind != ModuleKind.Launcher || l.S == null) continue;
            var s = l.S;
            Sector(l, hull, out var center, out var width);
            var det = Modules.DetectorOf(l, all);
            var left0 = l.InBattle ? l.ChargesLeft : s.Charges;
            var working = left0 > 0 && !(l.InBattle && l.Destroyed) && det != null && !(det.InBattle && det.Destroyed);
            result.Add(center);
            result.Add(width);
            result.Add(left0);
            result.Add(s.Charges);
            result.Add(working ? 1f : 0f);
        }
        return result.ToArray();
    }

    
    
    
    
    
    
    
    
    
    
    
    public static float[] ApsSegmentFrames()
    {
        try { return Frames(); }
        catch { return System.Array.Empty<float>(); }
    }

    private static System.IntPtr _familiesOf;
    private static readonly System.Collections.Generic.Dictionary<System.IntPtr, int> YawAxisOf = new();   
    private static string _framesLogged;

    private static float[] Frames()
    {
        var v = EoPlayer.InControl ? EoPlayer.Vehicle : null;
        if (v == null || v.WasCollected) return System.Array.Empty<float>();
        var hull = v.transform;
        if (_familiesOf != v.Pointer) { _familiesOf = v.Pointer; YawAxisOf.Clear(); }
        var all = Modules.OnVehicle(v);
        var result = new System.Collections.Generic.List<float>();
        var tops = new System.Collections.Generic.List<System.IntPtr>();
        var names = new System.Collections.Generic.List<string>();
        foreach (var l in all)
        {
            if (l.Kind != ModuleKind.Launcher || l.S == null) continue;
            Sector(l, hull, out var center, out _);
            var family = 0;
            var angle = center;
            var top = TopBelow(l.T, hull);
            if (top != null && top.Pointer != l.T.Pointer)
            {
                var index = tops.IndexOf(top.Pointer);
                if (index < 0) { tops.Add(top.Pointer); names.Add(top.name); index = tops.Count - 1; }
                family = index + 1;
                angle = Mathf.Repeat(center - Yaw(top, hull), 360f);
            }
            result.Add(family);
            result.Add(angle);
        }
        if (result.Count > 0)
        {
            var onHull = 0;
            for (var i = 0; i < result.Count; i += 2) if (result[i] == 0f) onHull++;
            var summary = $"APS panel: {onHull} launcher(s) on the hull, {result.Count / 2 - onHull} on moving parts" +
                          (names.Count > 0 ? $" ({string.Join(", ", names)})" : "");
            if (summary != _framesLogged)
            {
                _framesLogged = summary;
                Plugin.Log.LogInfo(summary);
            }
        }
        return result.ToArray();
    }

    
    private static Transform TopBelow(Transform t, Transform hull)
    {
        for (var d = 0; t != null && d < 64; d++)
        {
            var p = t.parent;
            if (p == null) return null;
            if (p.Pointer == hull.Pointer) return t;
            t = p;
        }
        return null;
    }

    
    
    
    private static float Yaw(Transform tr, Transform hull)
    {
        if (!YawAxisOf.TryGetValue(tr.Pointer, out var axis) ||
            Mathf.Abs(hull.InverseTransformDirection(axis == 0 ? tr.forward : axis == 1 ? tr.right : tr.up).y) > 0.7f)
        {
            var best = 2f;
            axis = 0;
            var axes = new[] { tr.forward, tr.right, tr.up };
            for (var i = 0; i < 3; i++)
            {
                var vertical = Mathf.Abs(hull.InverseTransformDirection(axes[i]).y);
                if (vertical < best) { best = vertical; axis = i; }
            }
            YawAxisOf[tr.Pointer] = axis;
        }
        var a = hull.InverseTransformDirection(axis == 0 ? tr.forward : axis == 1 ? tr.right : tr.up);
        return Mathf.Atan2(a.x, a.z) * Mathf.Rad2Deg;
    }

    
    
    public static int DroneThreat(int id, Vector3 position, Vector3 velocity, float killChance)
    {
        try { return Interceptor.Drone(id, position, velocity, killChance); }
        catch { return 0; }
    }

    
    
    public static void AddSmokeCloud(Vector3 center, float radius, float halfHeight, float build, float duration)
    {
        try { EoSystem.AddCloud(center, radius, halfHeight, build, duration); }
        catch { }
    }
}
