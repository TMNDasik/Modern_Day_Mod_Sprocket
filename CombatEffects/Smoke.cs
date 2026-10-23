using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CombatEffects;








internal static class Smoke
{
    public const string Launch = "Smoke Discharger Muzzle Flash 00190", Cloud = "Smoke Screen 90002",
        Machine = "Smoke Machine Long 00760", MachineUp = "Smoke Machine Upward Facing Variant 00769";

    private sealed class Screen
    {
        public Instance Smoke;
        public float StopAt;
    }

    private static readonly List<Screen> Screens = new();
    
    private static readonly Vector3 MachineAxis = Quaternion.Euler(45f, 90f, 0f) * Vector3.forward;

    public static bool GrenadeLaunch(Vector3 muzzle, Vector3 velocity)
    {
        var dir = velocity.sqrMagnitude > 1e-4f ? velocity.normalized : Vector3.forward;
        return Effects.Play(Launch, muzzle, Quaternion.LookRotation(dir)) != null;
    }

    public static bool GrenadeCloud(Vector3 center, float buildTime, float duration)
    {
        
        var at = center;
        var best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(center + Vector3.up, Vector3.down, 30f))
            if (h.rigidbody == null && h.distance < best) { best = h.distance; at = h.point; }
        
        var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var cam = Lighting.Camera;
        if (cam != null)
        {
            var view = at - cam.transform.position;
            view.y = 0f;
            if (view.sqrMagnitude > 1f) rot = Quaternion.LookRotation(view.normalized) * Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
        }
        var inst = Effects.Loop(Cloud, at, rot);
        if (inst == null) return false;
        Screens.Add(new Screen { Smoke = inst, StopAt = Time.time + Mathf.Max(buildTime, 0f) + Mathf.Clamp(duration, 3f, 300f) });
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Smoke screen at {at} for {duration:0} s (burst {center.y - at.y:0.0} m above the ground)");
        return true;
    }

    private sealed class Outlet
    {
        public Instance Smoke;
        public Vector3 Pos;
        public float Seen;
        public int Frame;
        public bool Up;
    }

    private static readonly List<Outlet> Outlets = new();

    
    public static bool EngineSmoke(Vector3 pos, Vector3 dir, float intensity)
    {
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.back;
        var up = dir.y > 0.6f;
        var frame = Time.frameCount;
        
        Outlet o = null;
        var best = 4f;
        foreach (var x in Outlets)
        {
            if (x.Frame == frame || x.Up != up) continue;
            var d = (x.Pos - pos).sqrMagnitude;
            if (d < best) { best = d; o = x; }
        }
        if (o == null || !Effects.Alive(o.Smoke))
        {
            if (o != null) Outlets.Remove(o);
            var inst = Effects.Loop(up ? MachineUp : Machine, pos, Quaternion.identity);
            if (inst == null) return false;
            o = new Outlet { Smoke = inst, Up = up };
            Outlets.Add(o);
        }
        o.Pos = pos;
        o.Seen = Time.time;
        o.Frame = frame;
        o.Smoke.Tr.SetPositionAndRotation(pos, Quaternion.FromToRotation(MachineAxis, dir));
        Effects.SetRate(o.Smoke, Mathf.Clamp(intensity, 0f, 3f));
        return true;
    }

    public static void Tick()
    {
        var now = Time.time;
        for (var i = Screens.Count - 1; i >= 0; i--)
        {
            var s = Screens[i];
            if (now < s.StopAt && Effects.Alive(s.Smoke)) continue;
            Effects.Stop(s.Smoke, 30f);   
            Screens.RemoveAt(i);
        }
        if (Outlets.Count == 0) return;
        for (var i = Outlets.Count - 1; i >= 0; i--)
        {
            var o = Outlets[i];
            if (now - o.Seen < 0.3f && Effects.Alive(o.Smoke)) continue;
            Effects.Stop(o.Smoke, 30f);   
            Outlets.RemoveAt(i);
        }
    }

    public static void StopAll()
    {
        foreach (var o in Outlets) Effects.Stop(o.Smoke, 30f);
        Outlets.Clear();
        foreach (var s in Screens) Effects.Stop(s.Smoke, 30f);
        Screens.Clear();
    }
}
