using System;
using HarmonyLib;
using Sprocket;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons;
using Sprocket.VehicleControl;
using UnityEngine;

namespace GunStabilizer;













[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
internal static class ScopeCarry
{
    public static int PlayerVehicleKey;   
    public static int ScopedVehicleKey;   
    public static int LastControlFrame = -100;
    public static Cannon PlayerCannon;    
    public static Cannon SightCannon;     
    public static Camera PlayerCamera;
    public static Sprocket.Gameplay.VehicleControl.VehicleController Controller;   
    public static Sprocket.Vehicles.ScopeController Scope;                          

    private static bool _hasPrev;
    private static IntPtr _hullPtr;
    private static Quaternion _prevHull;
    private static int _errors;

    private static void Prefix(VehicleControlPlayerState __instance)
    {
        try { Update(__instance); }
        catch (Exception e)
        {
            _hasPrev = false;
            if (_errors++ < 5) Plugin.Log.LogError("Scope stabilization failed: " + e);
        }
    }

    private static void Update(VehicleControlPlayerState state)
    {
        LastControlFrame = Time.frameCount;
        var vc = state.vehicleController;
        Controller = vc;
        Scope = state.scopeController;
        if (vc == null) { Clear(); return; }
        PlayerCamera = vc.ActiveCamera;

        Cannon cannon = null;
        var gunner = vc.activeGunner;
        var weapon = gunner != null ? gunner.WeaponGameObject : null;
        if (weapon != null)
        {
            cannon = weapon.GetComponent<Cannon>();
            if (cannon == null) cannon = weapon.GetComponentInParent<Cannon>();
        }
        PlayerCannon = cannon;

        var vehicleGo = vc.ControlledVehicleGameObject;
        PlayerVehicleKey = cannon != null ? Registry.VehicleKeyOf(cannon.transform)
            : vehicleGo != null ? Registry.VehicleKeyOf(vehicleGo.transform) : 0;
        var sightGun = cannon != null ? SightGun(cannon, vehicleGo, PlayerVehicleKey) : null;
        SightCannon = sightGun;

        var active = state.activeMovementController;
        var scope = state.scopeMovementController;
        var scoped = active != null && scope != null && active.Pointer == scope.Pointer;
        ScopedVehicleKey = scoped ? PlayerVehicleKey : 0;

        var dt = Time.deltaTime;
        if (!scoped || sightGun == null || dt <= 0f)
        {
            Stats.End();
            _hasPrev = false; _hasTa = false; _hasLead = false; _mouseMoved = false;
            ScopeCam = null;
            SightHold.Pause();
            return;
        }
        
        SightHold.Resume(scope);
        ScopeCam = scope;
        SightHold.Begin(scope);
        UpdateAimDistance(vc, dt);
        LogSight(sightGun);
        Stats.Frame(dt);

        
        
        var taStart = scope.targetAngles;
        var input = _hasTa && _mouseMoved
            ? new Vector2(Mathf.DeltaAngle(_lastTa.x, taStart.x), Mathf.DeltaAngle(_lastTa.y, taStart.y))
            : Vector2.zero;
        if (input.magnitude > 30f) input = Vector2.zero; 

        var hullStep = Carry(scope, sightGun, dt);
        
        var gunBehaviour = sightGun.Behaviour;
        if (gunBehaviour != null && MunitionsBridge.GunLocked(gunBehaviour.Pointer)) _hasLead = false;
        else LimitLead(scope, sightGun, input, hullStep);
        _lastTa = scope.targetAngles;
        _hasTa = true;
        
        try { _mouseMoved = scope.movementInput.sqrMagnitude > 0f; }
        catch { _mouseMoved = true; }
        SightHold.End(scope, _mouseMoved);
        Stats.Log(sightGun);
    }

    public static ScopeCameraController ScopeCam;   

    private static bool _mouseMoved;

    
    
    

    private static Cannon _sightGun;
    private static int _sightGunVehicle;

    private static Cannon SightGun(Cannon active, GameObject vehicleGo, int vehicleKey)
    {
        if (_sightGunVehicle != vehicleKey)
        {
            _sightGun = null;
            _sightGunVehicle = vehicleKey;
        }
        if (HasElevationDrive(active)) return _sightGun = active;
        if (_sightGun != null && !_sightGun.WasCollected) return _sightGun;

        Cannon best = null;
        var bestCaliber = -1;
        try
        {
            var root = vehicleGo != null ? vehicleGo : active.gameObject;
            foreach (var c in root.GetComponentsInChildren<Cannon>())
            {
                if (c == null || !HasElevationDrive(c)) continue;
                var caliber = c.Blueprint != null ? c.Blueprint.Caliber : 0;
                if (caliber > bestCaliber) { bestCaliber = caliber; best = c; }
            }
        }
        catch { }
        return _sightGun = best ?? active;
    }

    
    
    private static string _lastSightLog;
    private static float _lastSightLogAt = -100f;

    private static void LogSight(Cannon gun)
    {
        try
        {
            var chain = Registry.ChainFor(gun);
            string Drive(DriveState d) => d == null ? "none"
                : $"{(d.IsTurret ? d.IsPitch ? "sideways ring" : "turret ring" : "laying drive")}" +
                  $"{(d.FlagStab ? " +stab" : "")}{(Registry.Stabilized(d) ? " ON" : d.FlagStab ? " off" : "")}" +
                  $"{(d.FlagStab && !d.PowerOk ? " (no electric traverse)" : "")}{(d.FlagStab && !d.Healthy ? " (damaged)" : "")}";
            var fov = PlayerCamera != null ? PlayerCamera.fieldOfView : 0f;
            var text = $"Sight: gun '{gun.name}' {(gun.Blueprint?.Caliber ?? 0)} mm — horizontal: {Drive(chain?.Yaw)}, vertical: {Drive(chain?.Pitch)}, " +
                       $"hull '{(chain?.Hull != null ? chain.Hull.name : "none")}'";
            if (text == _lastSightLog && Time.unscaledTime - _lastSightLogAt < 60f) return;
            _lastSightLog = text;
            _lastSightLogAt = Time.unscaledTime;
            Plugin.Log.LogInfo($"{text}; FOV {fov:0.##}°, lead {Plugin.SightField.Value:0.##}×FOV in {Plugin.SightLeadMin.Value:0.#}-{Plugin.SightLeadMax.Value:0.#}°, " +
                               $"slip {Plugin.SightSlip.Value:0.#}°, gyro drift {Plugin.GyroDrift.Value:0.##}, {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):0} fps, " +
                               $"screen {Screen.width}x{Screen.height}");
        }
        catch { }
    }

    private static bool HasElevationDrive(Cannon c)
    {
        try { return c != null && Registry.ChainFor(c)?.Pitch != null; }
        catch { return false; }
    }

    
    
    private static float _aimDistance = 500f;
    private static Sprocket.Gameplay.VehicleControl.VehicleController _vc;

    
    public static float AimDistanceFrom(Vector3 from)
    {
        try
        {
            if (_vc != null && !_vc.WasCollected)
            {
                var d = Vector3.Distance(from, _vc.targetPoint);
                if (!float.IsNaN(d) && d >= 1f) return Mathf.Clamp(d, 20f, 5000f);
            }
        }
        catch { }
        return _aimDistance;
    }

    private static void UpdateAimDistance(Sprocket.Gameplay.VehicleControl.VehicleController vc, float dt)
    {
        _vc = vc;
        try
        {
            var cam = PlayerCamera;
            if (cam == null) return;
            var d = Vector3.Distance(cam.transform.position, vc.targetPoint);
            if (float.IsNaN(d) || d < 1f) return;
            d = Mathf.Clamp(d, 20f, 5000f);
            _aimDistance += (d - _aimDistance) * (1f - Mathf.Exp(-dt / 0.3f));
        }
        catch { }
    }

    
    
    
    
    
    
    
    
    

    private static bool _hasTa;
    private static Vector2 _lastTa;
    private static bool _hasLead;
    private static float _prevLead;
    private static IntPtr _leadGun;

    private static void LimitLead(ScopeCameraController scope, Cannon cannon, Vector2 input, Quaternion hullStep)
    {
        var k = Plugin.SightField.Value;
        var weapon = cannon.Behaviour;
        var wt = weapon != null ? weapon.transform : null;
        float limit;
        if (k > 0f)
        {
            var fov = PlayerCamera != null ? PlayerCamera.fieldOfView : 30f;
            
            
            var min = Mathf.Max(0.3f, Plugin.SightLeadMin.Value);
            limit = Mathf.Clamp(k * fov, min, Mathf.Max(min, Plugin.SightLeadMax.Value));
        }
        else limit = Plugin.SightSlip.Value; 
        if (limit <= 0f || wt == null) { _hasLead = false; return; }
        var soft = k > 0f ? limit * 0.5f : limit;
        var gun = GunAsSeen(wt);

        var ta = scope.targetAngles;
        var lead = Lead(ta, gun);
        
        if (!_hasLead || _leadGun != wt.Pointer)
        {
            _leadGun = wt.Pointer;
            _hasLead = true;
            _prevLead = lead.magnitude;
        }

        var result = ta;
        
        if (k > 0f && lead.magnitude > soft && input.sqrMagnitude > 1e-8f)
        {
            var baseTa = ta - input;
            var baseLead = Lead(baseTa, gun);
            if (lead.magnitude > baseLead.magnitude)
            {
                var g = Mathf.Clamp01(1f - (baseLead.magnitude - soft) / (limit - soft));
                result = baseTa + input * (g * g);
                lead = Lead(result, gun);
            }
        }

        
        var push = Quaternion.identity;
        var cap = Mathf.Max(limit, _prevLead);
        if (lead.magnitude > cap && Quaternion.Angle(Quaternion.identity, hullStep) > 1e-5f)
        {
            var dir = Dir(result);
            var l0 = lead.magnitude;
            var l1 = LeadDir(hullStep * dir, gun).magnitude;
            if (l1 < l0)
            {
                
                var f = 1f;
                if (l1 < cap)
                {
                    float lo = 0f, hi = 1f;
                    for (var i = 0; i < 10; i++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (LeadDir(Quaternion.Slerp(Quaternion.identity, hullStep, mid) * dir, gun).magnitude > cap) lo = mid;
                        else hi = mid;
                    }
                    f = hi;
                }
                push = Quaternion.Slerp(Quaternion.identity, hullStep, f);
                var moved = push * dir;
                result += AngleDelta(dir, moved);
                lead = LeadDir(moved, gun);
                Stats.Pushed += Vector3.Angle(dir, moved);
            }
        }
        _prevLead = lead.magnitude;
        Stats.Lead(lead.magnitude, limit);
        if ((result - ta).sqrMagnitude < 1e-10f) return;
        scope.targetAngles = result;
        
        if (push != Quaternion.identity) TurnCamera(scope, push * (scope.currentRotation * Vector3.forward));
    }

    
    
    private static void TurnCamera(ScopeCameraController scope, Vector3 forward)
    {
        if (forward.sqrMagnitude < 1e-8f) return;
        var q = scope.currentRotation;
        scope.currentRotation = Quaternion.LookRotation(forward, q * Vector3.up);
    }

    
    
    
    
    
    
    private static Quaternion GunAsSeen(Transform wt)
    {
        
        var gun = FireControl.Lower(wt.rotation);
        var cam = PlayerCamera;
        if (cam == null) return gun;
        var c = cam.transform.position;
        var g = wt.position;
        var v = gun * Vector3.forward;
        
        var along = Mathf.Max(20f, _aimDistance + Vector3.Dot(c - g, v));
        var dir = g + v * along - c;
        if (dir.sqrMagnitude < 1e-4f) return gun;
        return Quaternion.LookRotation(dir.normalized, gun * Vector3.up);
    }

    private static Vector3 Dir(Vector2 cameraAngles)
    {
        var e = ScopeCameraController.CameraAngleToEuler(cameraAngles);
        return Quaternion.Euler(e.x, e.y, 0f) * Vector3.forward;
    }

    
    private static Vector2 Lead(Vector2 cameraAngles, Quaternion gun) => LeadDir(Dir(cameraAngles), gun);

    private static Vector2 LeadDir(Vector3 dir, Quaternion gun)
    {
        var local = Quaternion.Inverse(gun) * dir;
        return new Vector2(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg);
    }

    
    private static Vector2 AngleDelta(Vector3 from, Vector3 to)
    {
        var ef = Quaternion.LookRotation(from, Vector3.up).eulerAngles;
        var et = Quaternion.LookRotation(to, Vector3.up).eulerAngles;
        var a0 = ScopeCameraController.EulerToCameraAngle(new Vector2(ef.x, ef.y));
        var a1 = ScopeCameraController.EulerToCameraAngle(new Vector2(et.x, et.y));
        return new Vector2(Mathf.DeltaAngle(a0.x, a1.x), Mathf.DeltaAngle(a0.y, a1.y));
    }

    
    
    
    private static class Stats
    {
        public static float Carried, Pushed;
        private static float _time, _maxLead, _sumLead, _limit, _nextLog;
        private static int _frames, _leadFrames;
        private static bool _active;

        public static void Frame(float dt)
        {
            if (!_active)
            {
                Reset();
                _active = true;
                _nextLog = Time.unscaledTime + 30f;
                _describedAt = -100f;
            }
            _time += dt;
            _frames++;
            
            if (Time.unscaledTime - _describedAt > 1f)
            {
                _describedAt = Time.unscaledTime;
                _channels = Describe(SightCannon);
            }
        }

        private static float _describedAt;
        private static string _channels = "";

        private static string Describe(Cannon gun)
        {
            try
            {
                var chain = gun != null ? Registry.ChainFor(gun) : null;
                if (chain == null) return _channels;
                string Channel(DriveState d, bool free) =>
                    free ? "free" : d == null ? "none" : Registry.Stabilized(d) ? "stabilized" : "not stabilized";
                
                
                string Drive(DriveState d)
                {
                    if (d == null) return "-";
                    if (!d.IsTurret) return $"{d.Ldb.elevationInfo.MaxSpeed:0.#}°/s";
                    var m = Stabilization.Multiplier(d.Tb);
                    var real = m * d.Tb.maxAcceleration;
                    var servo = Registry.Stabilized(d) ? (d.IsPitch ? StabSpec.VerticalMaxAccel : StabSpec.HorizontalMaxAccel) : 0f;
                    return $"{d.Tb.maxTraverseRate:0.#}°/s, {real:0.#}°/s² (motor ×{m:0.##})" + (servo > real ? $" → stab {servo:0}°/s²" : "");
                }
                return $"horizontal {Channel(chain.Yaw, chain.FreeYaw)} (drive {Drive(chain.Yaw)}), " +
                       $"vertical {Channel(chain.Pitch, false)} (drive {Drive(chain.Pitch)})";
            }
            catch { return _channels; }
        }

        public static void Lead(float lead, float limit)
        {
            if (lead > _maxLead) _maxLead = lead;
            _sumLead += lead;
            _leadFrames++;
            _limit = limit;
        }

        public static void Log(Cannon gun)
        {
            if (!_active || Time.unscaledTime < _nextLog) return;
            Write(gun);
            Reset();
            _nextLog = Time.unscaledTime + 30f;
        }

        public static void End()
        {
            if (!_active) return;
            _active = false;
            if (_time > 2f) Write(SightCannon);
        }

        private static void Reset()
        {
            Carried = Pushed = _time = _maxLead = _sumLead = _limit = 0f;
            _frames = _leadFrames = 0;
        }

        private static void Write(Cannon gun)
        {
            try
            {
                var lead = _leadFrames > 0
                    ? $"gun off the line of sight avg {_sumLead / _leadFrames:0.00}°, max {_maxLead:0.0}° (stop at {_limit:0.0}°)"
                    : "no stop";
                Plugin.Log.LogInfo($"Sight stats: {_time:0} s, {_frames / Mathf.Max(_time, 1e-3f):0} fps; {_channels}; " +
                                   $"camera carried with the hull {Carried:0.0}°, pushed at the stop {Pushed:0.0}°; {lead}");
            }
            catch { }
        }
    }

    private static void Clear()
    {
        PlayerVehicleKey = ScopedVehicleKey = 0;
        PlayerCannon = null;
        SightCannon = null;
        _hasPrev = false;
        _hasLead = false;
    }

    
    
    
    private static Quaternion Carry(ScopeCameraController scope, Cannon cannon, float dt)
    {
        
        
        var chain = Registry.ChainFor(cannon);
        var hull = chain?.Hull;
        if (hull == null) { _hasPrev = false; return Quaternion.identity; }

        var hs = chain.Yaw;
        var vs = chain.Pitch;
        
        var hStab = chain.FreeYaw || Registry.Stabilized(hs);
        var vStab = Registry.Stabilized(vs);
        var unit = (hs ?? vs)?.Unit;

        var h1 = hull.rotation;
        if (!_hasPrev || _hullPtr != hull.Pointer)
        {
            _prevHull = h1;
            _hullPtr = hull.Pointer;
            _hasPrev = true;
            return Quaternion.identity;
        }
        var h0 = _prevHull;
        _prevHull = h1;

        var rot = Rot.Vector(Quaternion.Inverse(h0) * h1); 
        if (float.IsNaN(rot.x) || rot.magnitude > 20f) return Quaternion.identity; 

        var ta = scope.targetAngles;
        var e0 = ScopeCameraController.CameraAngleToEuler(ta);
        var q0 = Quaternion.Euler(e0.x, e0.y, 0f);
        var r0 = q0 * Vector3.forward;

        
        var local0 = Quaternion.Inverse(h0) * r0;
        var psi = Mathf.Atan2(local0.x, local0.z) * Mathf.Rad2Deg;
        var theta = Mathf.Asin(Mathf.Clamp(local0.y, -1f, 1f)) * Mathf.Rad2Deg;
        var psiRad = psi * Mathf.Deg2Rad;
        var trunnion = new Vector3(Mathf.Cos(psiRad), 0f, -Mathf.Sin(psiRad));

        
        Quaternion stabilizedStep;
        if (hStab && vStab) stabilizedStep = h1 * Quaternion.Inverse(h0);
        else if (hStab) stabilizedStep = Quaternion.AngleAxis(rot.y, h1 * Vector3.up);
        else if (vStab) stabilizedStep = Quaternion.AngleAxis(Vector3.Dot(rot, trunnion), h1 * trunnion);
        else stabilizedStep = Quaternion.identity;

        
        var driftScale = Mathf.Max(0f, Plugin.GyroDrift.Value);
        var driftH = hStab && !chain.FreeYaw && unit != null ? unit.DriftH.Value * driftScale * dt : 0f;
        var driftV = vStab && unit != null ? unit.DriftV.Value * driftScale * dt : 0f;
        if (hStab && vStab && driftH == 0f && driftV == 0f) return stabilizedStep;

        Vector3 r1;
        if (hStab && vStab)
        {
            
            var up = h1 * Vector3.up;
            r1 = Quaternion.AngleAxis(driftH, up) * r0;
            var right = Vector3.Cross(up, r1);
            if (right.sqrMagnitude > 1e-6f) r1 = Quaternion.AngleAxis(-driftV, right.normalized) * r1;
        }
        else
        {
            
            
            if (hStab) psi += -rot.y + driftH;
            if (vStab) theta += Vector3.Dot(rot, trunnion) + driftV;

            var dir = Quaternion.AngleAxis(psi, Vector3.up) * Quaternion.AngleAxis(-theta, Vector3.right) * Vector3.forward;
            r1 = h1 * dir;
        }

        
        var e1 = Quaternion.LookRotation(r1, Vector3.up).eulerAngles;
        var a0 = ScopeCameraController.EulerToCameraAngle(new Vector2(q0.eulerAngles.x, q0.eulerAngles.y));
        var a1 = ScopeCameraController.EulerToCameraAngle(new Vector2(e1.x, e1.y));
        var delta = new Vector2(Mathf.DeltaAngle(a0.x, a1.x), Mathf.DeltaAngle(a0.y, a1.y));
        if (Mathf.Abs(delta.x) > 20f || Mathf.Abs(delta.y) > 20f) return stabilizedStep;
        scope.targetAngles = ta + delta;
        
        TurnCamera(scope, Quaternion.FromToRotation(r0, r1) * (scope.currentRotation * Vector3.forward));
        Stats.Carried += Vector3.Angle(r0, r1);
        return stabilizedStep;
    }

    
    
    

    private static bool _renderHooked;
    private static Quaternion _lastOutput, _lastJitter = Quaternion.identity;
    private static IntPtr _lastCamera;

    public static void EnsureRenderHook()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        try
        {
            UnityEngine.Events.UnityAction cb = (Action)ApplyJitter;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Render hook failed: " + e.Message); }
    }

    private static void ApplyJitter()
    {
        try
        {
            var cam = PlayerCamera;
            var gun = SightCannon ?? PlayerCannon;
            if (cam == null || ScopedVehicleKey == 0 || Time.frameCount - LastControlFrame > 1 || gun == null)
            {
                _lastCamera = IntPtr.Zero;
                return;
            }
            
            if (ScopeCam != null && !ScopeCam.WasCollected) SightHold.BeforeRender(ScopeCam, cam);
            var chain = Registry.ChainFor(gun);
            var hs = chain?.Yaw;
            var vs = chain?.Pitch;
            var resH = hs != null && hs.IsTurret && Registry.Stabilized(hs) ? hs.Residual : 0f;
            var resV = Registry.Stabilized(vs) ? vs.Residual : 0f;

            var t = cam.transform;
            var q = t.rotation;
            
            if (_lastCamera == cam.Pointer && q == _lastOutput) q *= Quaternion.Inverse(_lastJitter);
            var jitter = Quaternion.Euler(-resV, resH, 0f);
            var output = q * jitter;
            t.rotation = output;
            _lastOutput = output;
            _lastJitter = jitter;
            _lastCamera = cam.Pointer;
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Sight jitter failed: " + e);
        }
    }
}
