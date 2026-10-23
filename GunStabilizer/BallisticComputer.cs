using System;
using System.Collections.Generic;
using Sprocket.DamageModelling;
using UnityEngine;

namespace GunStabilizer;






internal static class BallisticComputer
{
    public struct Solution
    {
        public bool Valid;
        public float Superelevation;   
        public float TimeOfFlight;     
        public string Reason;          
    }

    private sealed class Table
    {
        public float[] Decel;          
        public float MinSpeed;
    }

    private const float Step = 10f, MaxSpeed = 3000f, Dt = 0.005f, MaxTime = 40f, MaxExtra = 25f;
    private static readonly Dictionary<IntPtr, Table> Tables = new();

    
    public static ProjectileDefinition Definition(int typeId)
    {
        try
        {
            var reg = IProjectileTypeRegister.Instance;
            if (reg == null || !reg.TryGet(new ProjectileTypeID(typeId), out var type) || type == null) return null;
            return type.TryCast<ProjectileDefinition>();
        }
        catch { return null; }
    }

    private static Table TableOf(ProjectileDefinition def)
    {
        if (Tables.TryGetValue(def.Pointer, out var t)) return t;
        var n = (int)(MaxSpeed / Step) + 2;
        t = new Table { Decel = new float[n] };
        var mass = def.Mass;
        try
        {
            for (var i = 0; i < n; i++) t.Decel[i] = mass > 1e-6f ? Mathf.Max(0f, def.CalculateDragForce(i * Step)) / mass : 0f;
            t.MinSpeed = Mathf.Max(0f, def.MinSpeed);
        }
        catch (Exception e)
        {
            Array.Clear(t.Decel, 0, n);
            Plugin.Log.LogWarning($"Ballistic computer: no drag data for '{def.Name}' ({e.Message}), vacuum trajectory used");
        }
        if (Tables.Count > 256) Tables.Clear();
        Tables[def.Pointer] = t;
        return t;
    }

    private static float Decel(Table t, float speed)
    {
        var x = speed / Step;
        var i = Mathf.Clamp((int)x, 0, t.Decel.Length - 2);
        var f = Mathf.Clamp01(x - i);
        return t.Decel[i] + (t.Decel[i + 1] - t.Decel[i]) * f;
    }

    
    
    private static bool HeightAt(Table t, float v0, float angle, float x, float g, out float y, out float time)
    {
        var px = 0f;
        var py = 0f;
        var vx = v0 * Mathf.Cos(angle);
        var vy = v0 * Mathf.Sin(angle);
        time = 0f;
        y = 0f;
        while (time < MaxTime)
        {
            
            var s = Mathf.Sqrt(vx * vx + vy * vy);
            if (s <= t.MinSpeed || s < 1f) return false;
            var k = Decel(t, s) / s;
            var mx = vx + (-k * vx) * Dt * 0.5f;
            var my = vy + (-k * vy - g) * Dt * 0.5f;
            var ms = Mathf.Sqrt(mx * mx + my * my);
            var km = ms > 1e-3f ? Decel(t, ms) / ms : 0f;
            var nx = px + mx * Dt;
            var ny = py + my * Dt;
            vx += -km * mx * Dt;
            vy += (-km * my - g) * Dt;
            time += Dt;
            if (nx >= x)
            {
                var f = (x - px) / Mathf.Max(nx - px, 1e-6f);
                y = py + (ny - py) * f;
                time -= Dt * (1f - f);
                return true;
            }
            px = nx;
            py = ny;
            if (py < -2000f || vx <= 0f) return false;
        }
        return false;
    }

    
    public static Solution Solve(ProjectileDefinition def, float v0, Vector3 muzzle, Vector3 target)
    {
        if (def == null) return new Solution { Reason = "NO DATA" };
        if (v0 < 20f) return new Solution { Reason = "NO DATA" };
        var t = TableOf(def);
        var g = Mathf.Max(0.1f, -Physics.gravity.y);
        var d = target - muzzle;
        var x = new Vector2(d.x, d.z).magnitude;
        var h = d.y;
        if (x < 1f) return new Solution { Valid = true, Superelevation = 0f, TimeOfFlight = 0f };
        var los = Mathf.Atan2(h, x);

        
        var a0 = los;
        var flight = Mathf.Sqrt(x * x + h * h) / v0;
        var a1 = los + Mathf.Atan2(0.5f * g * flight * flight, x);
        if (!HeightAt(t, v0, a0, x, g, out var y0, out _)) y0 = -1e4f;
        if (!HeightAt(t, v0, a1, x, g, out var y1, out var time1)) return new Solution { Reason = "OUT OF RANGE" };
        var e0 = y0 - h;
        var e1 = y1 - h;
        var max = los + MaxExtra * Mathf.Deg2Rad;
        for (var i = 0; i < 12 && Mathf.Abs(e1) > 0.02f; i++)
        {
            var de = e1 - e0;
            var next = Mathf.Abs(de) > 1e-6f ? a1 - e1 * (a1 - a0) / de : a1 + 0.001f;
            next = Mathf.Clamp(next, los - 2f * Mathf.Deg2Rad, max);
            a0 = a1;
            e0 = e1;
            a1 = next;
            if (!HeightAt(t, v0, a1, x, g, out y1, out time1)) return new Solution { Reason = "OUT OF RANGE" };
            e1 = y1 - h;
        }
        if (Mathf.Abs(e1) > 0.5f || a1 >= max - 1e-4f) return new Solution { Reason = "OUT OF RANGE" };
        return new Solution { Valid = true, Superelevation = (a1 - los) * Mathf.Rad2Deg, TimeOfFlight = time1 };
    }
}
