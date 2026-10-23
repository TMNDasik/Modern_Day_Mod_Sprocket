using System;
using Sprocket;
using UnityEngine;

namespace GunStabilizer;






internal static class SightHold
{
    private const float Threshold = 0.03f;      
    private static Vector2 _held;
    private static bool _valid, _mouse;
    private static float _lastLog = -100f, _sum;
    private static int _count;

    
    public static void Allow() => _valid = false;

    
    private static bool ThermalClamped()
    {
        try { return AppDomain.CurrentDomain.GetData("SprocketMods.SightLimitFrame") is int f && Time.frameCount - f <= 2; }
        catch { return false; }
    }

    
    public static void Begin(ScopeCameraController scope) => Restore(scope, null, "between frames");

    
    public static void End(ScopeCameraController scope, bool mouse)
    {
        _held = scope.targetAngles;
        _mouse = mouse;
        _valid = true;
    }

    
    public static void BeforeRender(ScopeCameraController scope, Camera cam) => Restore(scope, cam, "before render");

    public static void Reset() => _valid = false;

    
    
    private static float _pausedAt = -100f;
    private static IntPtr _scope;

    public static void Pause()
    {
        if (_pausedAt < 0f) _pausedAt = Time.unscaledTime;
    }

    public static void Resume(ScopeCameraController scope)
    {
        if (_pausedAt >= 0f && Time.unscaledTime - _pausedAt > 0.3f || scope.Pointer != _scope) _valid = false;
        _pausedAt = -1f;
        _scope = scope.Pointer;
    }

    private static void Restore(ScopeCameraController scope, Camera cam, string stage)
    {
        if (!_valid || _mouse || scope == null || !Plugin.HoldSight.Value) return;
        if (ThermalClamped())
        {
            _valid = false;
            return;
        }
        var ta = scope.targetAngles;
        var drift = new Vector2(Mathf.DeltaAngle(_held.x, ta.x), Mathf.DeltaAngle(_held.y, ta.y));
        var size = drift.magnitude;
        if (size < Threshold) return;
        if (size > 45f)
        {
            
            _valid = false;
            return;
        }
        var from = Dir(ta);
        var to = Dir(_held);
        scope.targetAngles = _held;
        var fix = Quaternion.FromToRotation(from, to);
        var cur = scope.currentRotation;
        scope.currentRotation = Quaternion.LookRotation(fix * (cur * Vector3.forward), cur * Vector3.up);
        if (cam != null)
        {
            var t = cam.transform;
            var q = t.rotation;
            t.rotation = Quaternion.LookRotation(fix * (q * Vector3.forward), q * Vector3.up);
        }
        _count++;
        _sum += size;
        var now = Time.unscaledTime;
        if (now - _lastLog < 3f) return;
        _lastLog = now;
        Plugin.Log.LogInfo($"Sight hold: the line of sight was moved {size:0.00}° (yaw {drift.x:+0.00;-0.00}°, pitch {drift.y:+0.00;-0.00}°) " +
                           $"without mouse input ({stage}, {Time.time - OwnFireShake.LastShot:0.0} s after the last own shot) — kept on target; " +
                           $"{_count} corrections, {_sum:0.0}° in total so far");
    }

    private static Vector3 Dir(Vector2 cameraAngles)
    {
        var e = ScopeCameraController.CameraAngleToEuler(cameraAngles);
        return Quaternion.Euler(e.x, e.y, 0f) * Vector3.forward;
    }
}
