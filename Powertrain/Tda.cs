using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sprocket.Blueprints;
using Sprocket.Vehicles.Engines;
using Sprocket.Vehicles.Exhausts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Powertrain;







internal static unsafe class Tda
{
    private static readonly Regex Token = new(@"\s?\[tda\]", RegexOptions.Compiled);

    public static bool Has(Blueprint bp)
    {
        try { return Token.IsMatch(bp?.Description ?? ""); }
        catch { return false; }
    }

    public static string With(string description, bool on)
    {
        var d = Token.Replace(description ?? "", "").Trim();
        if (!on) return d.Length == 0 ? null : d;
        return d.Length == 0 ? "[tda]" : d + " [tda]";
    }

    

    private sealed class Outlet
    {
        public Transform Parent;
        public Vector3 LocalPos, LocalDir;
    }

    private static IntPtr _vehicle;
    private static bool _present;
    private static float _nextScan;
    private static readonly List<Outlet> Outlets = new();
    private static readonly List<IntPtr> Engines = new();
    private static Rigidbody _body;
    
    private static readonly Dictionary<IntPtr, (float Left, float Stamp)> Reserve = new();
    private static bool _active, _keyWasDown;
    private static float _started, _nextCloud;

    private static float Total => Mathf.Max(1f, Plugin.TdaTime.Value);

    
    private static float Left(IntPtr vehicle, float now)
    {
        if (!Reserve.TryGetValue(vehicle, out var r))
        {
            if (Reserve.Count > 64) Reserve.Clear();
            r = (Total, now);
        }
        var dt = Mathf.Max(0f, now - r.Stamp);
        var left = _active && vehicle == _vehicle
            ? r.Left - dt
            : r.Left + dt * Mathf.Max(0f, Plugin.TdaRecharge.Value);
        left = Mathf.Clamp(left, 0f, Total);
        Reserve[vehicle] = (left, now);
        return left;
    }

    public static void Tick()
    {
        var go = PlayerTracker.VehicleGo;
        var vehicle = PlayerTracker.Vehicle;
        if (go == null || vehicle == IntPtr.Zero)
        {
            if (_active) Stop("out of control");
            _vehicle = IntPtr.Zero;
            _present = false;
            return;
        }
        var now = Time.time;
        if (vehicle != _vehicle || Time.unscaledTime >= _nextScan)
        {
            if (vehicle != _vehicle && _active) Stop("vehicle changed");
            _vehicle = vehicle;
            _nextScan = Time.unscaledTime + 1f;
            Scan(go);
        }
        if (!_present) return;
        var left = Left(vehicle, now);

        
        var kb = Keyboard.current;
        if (kb != null && Time.timeScale > 0f)
        {
            bool down, pressed;
            try
            {
                var key = kb[Plugin.TdaKey];
                down = key.isPressed;
                pressed = key.wasPressedThisFrame || (down && !_keyWasDown);
            }
            catch { down = pressed = false; }
            _keyWasDown = down;
            if (pressed)
            {
                if (_active) Stop("switched off");
                else Start(left, now);
            }
        }

        if (!_active) return;
        if (left <= 0f) { Stop("smoke time used up"); return; }
        if (!EngineRunning()) { Stop("engine stopped"); return; }
        Emit(now, left);
    }

    private static void Scan(GameObject go)
    {
        _present = false;
        Outlets.Clear();
        Engines.Clear();
        _body = null;
        try
        {
            CombustionEngine first = null;
            foreach (var ce in go.GetComponentsInChildren<CombustionEngine>(true))
            {
                if (ce == null || !Has(ce.Blueprint)) continue;
                first ??= ce;
                var beh = ce.behaviour;
                if (beh != null && beh.Pointer != IntPtr.Zero) Engines.Add(beh.Pointer);
            }
            if (first == null) return;
            _present = true;
            _body = go.GetComponent<Rigidbody>() ?? go.GetComponentInChildren<Rigidbody>();

            
            foreach (var ex in go.GetComponentsInChildren<EngineExhaust>(true))
            {
                if (ex == null) continue;
                var parent = ex.OutputParent ?? ex.transform;
                Outlets.Add(new Outlet { Parent = parent, LocalPos = ex.OutputLocalPosition, LocalDir = ex.OutputLocalDirection });
            }
            if (Outlets.Count == 0)
            {
                var t = first.transform;
                var back = -go.transform.forward * 0.7f + go.transform.up * 0.7f;
                Outlets.Add(new Outlet { Parent = t, LocalPos = t.InverseTransformPoint(t.position + go.transform.up * 0.5f), LocalDir = t.InverseTransformDirection(back) });
            }
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Engine smoke scan failed: " + e.Message);
        }
    }

    private static bool EngineRunning()
    {
        if (Engines.Count == 0) return true;
        foreach (var e in Engines)
        {
            try { if (((byte*)e)[Turbines.State] == Turbines.Running) return true; }
            catch { }
        }
        return false;
    }

    private static void Start(float left, float now)
    {
        if (left < 0.5f) { Plugin.Log.LogInfo("Engine smoke: no smoke time left"); return; }
        if (!EngineRunning()) { Plugin.Log.LogInfo("Engine smoke: the engine is not running"); return; }
        _active = true;
        _started = now;
        _nextCloud = now + 0.8f;
        Plugin.Log.LogInfo($"Engine smoke ON ({left:0} s left, {Outlets.Count} outlet(s){(ModBridge.CanSmoke ? "" : ", no visible smoke without Munitions")})");
    }

    
    private static void Stop(string why)
    {
        if (!_active) return;
        if (_vehicle != IntPtr.Zero) Left(_vehicle, Time.time);
        _active = false;
        Plugin.Log.LogInfo("Engine smoke OFF (" + why + ")");
    }

    private static void Emit(float now, float left)
    {
        var dt = Time.deltaTime;
        var vel = Vector3.zero;
        try { if (_body != null && !_body.WasCollected) vel = _body.linearVelocity; } catch { }
        
        var age = now - _started;
        var intensity = Mathf.Clamp01(age / 1.5f) * Mathf.Clamp01(left / 2f) * Mathf.Max(0.1f, Plugin.TdaDensity.Value);
        var sum = Vector3.zero;
        var count = 0;
        foreach (var o in Outlets)
        {
            try
            {
                if (o.Parent == null || o.Parent.WasCollected) continue;
                var pos = o.Parent.TransformPoint(o.LocalPos);
                var dir = o.Parent.TransformDirection(o.LocalDir);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
                ModBridge.ExhaustSmoke(pos, dir.normalized, vel, dt, intensity / Mathf.Sqrt(Outlets.Count));
                sum += pos;
                count++;
            }
            catch { }
        }
        
        if (count > 0 && now >= _nextCloud)
        {
            _nextCloud = now + 1.2f;
            var p = sum / count - vel.normalized * 2f;
            ModBridge.AddCloud(p + Vector3.up * 1.5f, 7f * intensity + 2f, 3.5f, 3f, Mathf.Max(10f, Plugin.TdaLinger.Value));
        }
    }

    
    
    public static Vector4 Status()
    {
        if (!_present || _vehicle == IntPtr.Zero || PlayerTracker.Vehicle == IntPtr.Zero) return Vector4.zero;
        var left = Left(_vehicle, Time.time);
        return new Vector4(left, _active ? 1f : 0f, left / Total, 1f);
    }
}
