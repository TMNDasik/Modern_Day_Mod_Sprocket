using System;
using System.Collections.Generic;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectileDefinitionRef = Sprocket.DamageModelling.ProjectileDefinition;

namespace GunStabilizer;
































internal static class FireControl
{
    public enum State { Idle, Ranging, Ready, NoRange }

    public static bool Auto;              
    public static float LastRange;        
    public static float Superelevation;   
    public static State Current = State.Idle;

    private const float RangingTime = 0.35f;
    private const float SlewRate = 3f;            
    private const float MaxLead = 2.5f;           

    
    private static float _seApplied, _leadApplied;
    
    private static float _seTarget, _leadTarget;

    private static IntPtr _pitchPtr;      
    private static IntPtr _yawPtr;        
    private static FcsRegistry.Entry _fcs; 
    private static FcsSettings _spec;      
    private static FcsDamage.Health _health;
    private static float _heldRate;       
    private static float _offTrack;       
    private static Vector3 _leadPoint;
    private static bool _leadShown, _specLogged;
    private static int _vehicleKey;
    private static int _laserCount = -1, _refusedCount = -1;
    private static float _rangedAt = -100f, _nextSolve;
    private static bool _failed;          
    private static string _failNote = ""; 
    private static BallisticComputer.Solution _solution;
    private static string _ammo = "", _reason = "";
    private static MuzzleVelocity.Source _source;
    private static float _v0;
    private static Vector3 _lastTarget;
    private static IntPtr _lastDef, _lastWeapon;
    private static ProjectileDefinitionRef _lastDefObj;
    private static int _lastType;
    private static string _loggedSolution;
    private static State _loggedState = State.Idle;

    public static void Update(bool inControl)
    {
        FcsAi.Tick();
        if (!inControl || ScopeCarry.PlayerVehicleKey == 0)
        {
            Superelevation = _seApplied = _leadApplied = 0f;
            _pitchPtr = _yawPtr = IntPtr.Zero;
            Current = State.Idle;
            Hud.Hide();
            return;
        }
        if (ScopeCarry.PlayerVehicleKey != _vehicleKey) Reset(ScopeCarry.PlayerVehicleKey);
        RefreshSpec();

        if (Time.timeScale > 0f && KeyPressed())
        {
            if (_spec == null) Hud.Flash("NO FIRE CONTROL COMPUTER");
            else if (!_spec.Auto) Hud.Flash("FCS: MANUAL LAYING ONLY");
            else
            {
                Auto = !Auto;
                Hud.Flash(Auto ? "FCS AUTO" : "FCS OFF");
                Plugin.Log.LogInfo(Auto ? "Fire control: automatic ballistic correction on" : "Fire control: off (the rangefinder only measures)");
            }
        }

        var vc = ScopeCarry.Controller;
        if (vc != null && !vc.WasCollected && Time.timeScale > 0f) TrackLineOfSight(vc.aimRay.direction);

        
        if (RangefinderLink.Read(out var count, out var range))
        {
            if (_laserCount < 0) _laserCount = count;                     
            else if (count != _laserCount)
            {
                _laserCount = count;
                _rangedAt = Time.unscaledTime;
                _failed = range <= 0f;
                _failNote = _failed ? RangefinderLink.Note() : "";
                LastRange = _failed ? 0f : range;
                CaptureLead();
                Invalidate();
            }
        }
        
        if (RangefinderLink.Refused(out var refused, out var refusedNote))
        {
            if (_refusedCount < 0) _refusedCount = refused;
            else if (refused != _refusedCount)
            {
                _refusedCount = refused;
                if (refusedNote.Length > 0) Hud.Flash(refusedNote);
            }
        }

        WatchLead(Time.deltaTime);
        Solve();
        Slew(Time.deltaTime);
        UpdateState();
        Hud.Draw();
    }

    
    private static void RefreshSpec()
    {
        Sprocket.Vehicles.Vehicle vehicle = null;
        try
        {
            var go = ScopeCarry.Controller != null ? ScopeCarry.Controller.ControlledVehicleGameObject : null;
            vehicle = go != null ? go.GetComponent<Sprocket.Vehicles.Vehicle>() : null;
        }
        catch { }
        var fcs = FcsRegistry.ForVehicle(vehicle);
        if (fcs != _fcs || !_specLogged)
        {
            _fcs = fcs;
            _specLogged = true;
            Plugin.Log.LogInfo(fcs != null
                ? $"Fire control computer: {fcs.S.Label}, laser {(fcs.S.Laser ? $"to {fcs.S.MaxRange:0} m ({(fcs.S.LastReturn ? "last" : "first")} return)" : "none")}, " +
                  $"automatic correction {(fcs.S.Auto ? "yes" : "no")}, lead {(fcs.S.Lead ? "yes" : "no")}, firing interlock {(fcs.S.Interlock ? "yes" : "no")}"
                : Plugin.FcsRequirePart.Value ? "Fire control: no fire control computer on this vehicle — no fire control"
                : "Fire control: no fire control computer part — basic fire control (range correction only)");
        }
        _spec = fcs != null ? fcs.S : Plugin.FcsRequirePart.Value ? null : FcsSettings.Legacy;
        _health = fcs != null && fcs.S.Damageable ? FcsDamage.Of(fcs.Id) : null;
        if (_spec == null || !_spec.Auto || Failed) Auto = false;
    }

    private static bool Failed => _health != null && _health.Value <= 0f;
    private static bool Degraded => _health != null && _health.Value < 0.7f && _health.Value > 0f;
    private static bool LeadOn => _spec != null && _spec.Lead && !Failed;

    

    
    private static readonly List<(float T, float Yaw)> Track = new();
    private static float _unwrapped, _lastYaw;
    private static bool _hasYaw;

    private static void TrackLineOfSight(Vector3 dir)
    {
        if (dir.x * dir.x + dir.z * dir.z < 1e-6f) return;   
        var yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        _unwrapped = _hasYaw ? _unwrapped + Mathf.DeltaAngle(_lastYaw, yaw) : yaw;
        _lastYaw = yaw;
        _hasYaw = true;
        var now = Time.time;
        Track.Add((now, _unwrapped));
        while (Track.Count > 0 && now - Track[0].T > 1.5f) Track.RemoveAt(0);
    }

    
    private static bool Fit(float newest, float oldest, out float rate)
    {
        rate = 0f;
        var now = Time.time;
        double n = 0, st = 0, sy = 0, stt = 0, sty = 0;
        foreach (var s in Track)
        {
            var age = now - s.T;
            if (age < newest || age > oldest) continue;
            double t = s.T - now, y = s.Yaw;
            n++;
            st += t;
            sy += y;
            stt += t * t;
            sty += t * y;
        }
        if (n < 4) return false;
        var den = n * stt - st * st;
        if (den < 1e-9) return false;
        rate = (float)((n * sty - st * sy) / den);
        return true;
    }

    
    
    private static float SteadyRate(out string why)
    {
        var window = _spec != null && _spec.IsAnalog ? 0.6f : 1f;
        why = "";
        if (Track.Count < 6 || Time.time - Track[0].T < window * 0.9f) { why = "the sight was not on the target long enough"; return 0f; }
        if (!Fit(0f, window, out var all) || !Fit(0f, window * 0.5f, out var late) || !Fit(window * 0.5f, window, out var early))
        {
            why = "too few frames";
            return 0f;
        }
        var a = Mathf.Abs(all);
        if (a < 0.12f) { why = "the target is not moving across"; return 0f; }
        if (a > 6f) { why = "the sight was swinging, not tracking"; return 0f; }
        if (Mathf.Sign(early) != Mathf.Sign(all) || Mathf.Sign(late) != Mathf.Sign(all) || Mathf.Abs(early - late) > 0.35f * a + 0.05f)
        {
            why = $"the tracking was not steady ({early:0.00} then {late:0.00}°/s)";
            return 0f;
        }
        return all;
    }

    
    private static void CaptureLead()
    {
        _offTrack = 0f;
        _heldRate = 0f;
        if (_failed || !LeadOn) return;
        _heldRate = SteadyRate(out var why);
        Plugin.Log.LogInfo(_heldRate != 0f
            ? $"Fire control: lead from tracking {_heldRate:+0.00;-0.00}°/s ({Mathf.Abs(_heldRate) * Mathf.Deg2Rad * LastRange:0.0} m/s across at {LastRange:0} m)"
            : $"Fire control: no lead ({why})");
    }

    
    private static void WatchLead(float dt)
    {
        if (_heldRate == 0f || dt <= 0f) return;
        var tracking = Fit(0f, 0.5f, out var rate) && Mathf.Sign(rate) == Mathf.Sign(_heldRate) && Mathf.Abs(rate) > 0.35f * Mathf.Abs(_heldRate);
        _offTrack = tracking ? 0f : _offTrack + dt;
        if (_offTrack < 0.8f) return;
        _heldRate = 0f;
        _offTrack = 0f;
        Hud.Flash("LEAD OFF");
        Plugin.Log.LogInfo("Fire control: lead dropped (the gunner stopped tracking)");
    }

    
    public static bool HoldFire =>
        _spec != null && _spec.Interlock && Auto && !Failed && _solution.Valid && LastRange > 0f && !_failed && Current == State.Ranging;

    private static void Reset(int key)
    {
        _vehicleKey = key;
        _fcs = null;
        _specLogged = false;
        _heldRate = _offTrack = 0f;
        Track.Clear();
        _hasYaw = false;
        _leadShown = false;
        _seApplied = _leadApplied = _seTarget = _leadTarget = 0f;
        FcsRegistry.Forget();
        LastRange = 0f;
        _failed = false;
        _laserCount = -1;
        _rangedAt = -100f;
        _solution = default;
        _reason = "";
        Superelevation = 0f;
        _pitchPtr = _yawPtr = IntPtr.Zero;
        _loggedSolution = null;
        _lastDefObj = null;
        _lastWeapon = _lastDef = IntPtr.Zero;
        Current = _loggedState = State.Idle;
        MuzzleVelocity.Forget();
        Auto = Plugin.FcsAutoAtStart.Value;
    }

    
    private static void Invalidate()
    {
        _nextSolve = 0f;
        _lastTarget = new Vector3(float.MaxValue, 0f, 0f);
    }

    
    private static void Solve()
    {
        _seTarget = _leadTarget = 0f;
        _leadShown = false;
        if (_spec == null) { Drop(""); return; }
        if (Failed) { Drop("FCS FAIL"); return; }
        var cannon = ScopeCarry.PlayerCannon;
        CannonBehaviour weapon = null;
        try { weapon = cannon != null ? cannon.Behaviour : null; } catch { }
        if (weapon == null || weapon.WasCollected) { _pitchPtr = _yawPtr = IntPtr.Zero; Drop("NO GUN"); return; }

        var chain = Registry.ChainFor(cannon);
        var pitch = chain?.Pitch != null ? chain.Pitch.Ptr : IntPtr.Zero;
        _yawPtr = chain?.Yaw != null ? chain.Yaw.Ptr : IntPtr.Zero;
        if (pitch != _pitchPtr) { _pitchPtr = pitch; Invalidate(); }
        if (_failed || LastRange <= 0f) { Drop(_failed ? "NO RANGE" : ""); return; }

        var vc = ScopeCarry.Controller;
        if (vc == null || vc.WasCollected) return;
        var ray = vc.aimRay;
        var dir = ray.direction;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();
        
        var target = ray.origin + dir * LastRange;
        var muzzle = weapon.ShellSpawnPosition;

        var typeId = weapon.LoadedProjectileTypeID.Value;
        var def = BallisticComputer.Definition(typeId);
        
        if (def == null && weapon.Pointer == _lastWeapon && _lastDefObj != null && !_lastDefObj.WasCollected) { def = _lastDefObj; typeId = _lastType; }
        var defPtr = def != null ? def.Pointer : IntPtr.Zero;
        
        if (weapon.Pointer != _lastWeapon || defPtr != _lastDef) Invalidate();
        if (def != null) { _lastDefObj = def; _lastType = typeId; }

        var now = Time.unscaledTime;
        if ((target - _lastTarget).sqrMagnitude > 0.25f || now >= _nextSolve)
        {
            _nextSolve = now + 0.25f;
            _lastTarget = target;
            _lastWeapon = weapon.Pointer;
            _lastDef = defPtr;
            var kind = MunitionsBridge.ProjectileKind(typeId);
            if (kind == 2 || kind == 3) _solution = new BallisticComputer.Solution { Reason = "GUIDED" };
            else if (def == null) _solution = new BallisticComputer.Solution { Reason = "NO AMMO" };
            else
            {
                _v0 = MuzzleVelocity.For(weapon, cannon, def, typeId, out _source);
                _solution = BallisticComputer.Solve(def, _v0, muzzle, target);
                _ammo = def.Name ?? "";
            }
            _reason = _solution.Valid ? "" : _solution.Reason;
            LogSolution();
        }
        if (!_solution.Valid) return;

        
        _seTarget = _solution.Superelevation + (Degraded ? _health.BiasMil * 360f / 6400f : 0f);
        if (LeadOn && _heldRate != 0f)
            _leadTarget = Mathf.Clamp(_heldRate * _solution.TimeOfFlight * (_health != null ? _health.LeadScale : 1f), -MaxLead, MaxLead);
        _leadShown = Mathf.Abs(_leadTarget) >= 0.05f;
        _leadPoint = ray.origin + Quaternion.AngleAxis(_leadTarget, Vector3.up) * dir * LastRange;
        if (Auto && _pitchPtr == IntPtr.Zero) _reason = "NO DRIVE";
    }

    
    private static void Slew(float dt)
    {
        var on = Auto && _spec != null && !Failed && _solution.Valid && _pitchPtr != IntPtr.Zero;
        if (dt > 0f)
        {
            var step = SlewRate * dt;
            _seApplied = Mathf.MoveTowards(_seApplied, on ? _seTarget : 0f, step);
            _leadApplied = Mathf.MoveTowards(_leadApplied, on ? _leadTarget : 0f, step);
        }
        Superelevation = _seApplied;
    }

    private static void Drop(string reason)
    {
        _solution = default;
        _reason = reason;
    }

    private static void UpdateState()
    {
        var computing = _spec != null ? Mathf.Max(RangingTime, _spec.ComputeTime) : RangingTime;
        var settled = Mathf.Abs(_seApplied - _seTarget) < 0.01f && Mathf.Abs(_leadApplied - _leadTarget) < 0.01f;
        State s;
        if (_spec == null || Failed) s = State.Idle;
        else if (Time.unscaledTime - _rangedAt < computing) s = State.Ranging;     
        else if (_failed) s = State.NoRange;
        else if (!Auto || LastRange <= 0f) s = State.Idle;
        else if (!_solution.Valid) s = _reason is "GUIDED" or "NO GUN" ? State.Idle : State.NoRange;
        else if (_pitchPtr == IntPtr.Zero) s = State.Idle;
        else s = settled ? State.Ready : State.Ranging;                            
        Current = s;
        if (s != _loggedState && Auto)
        {
            _loggedState = s;
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Fire control: {Name(s)}{(_reason.Length > 0 ? " (" + _reason + ")" : "")}");
        }
    }

    public static string Name(State s) => s switch
    {
        State.Ranging => "RANGING",
        State.Ready => "READY",
        State.NoRange => "NO RANGE",
        _ => "IDLE",
    };

    
    private static void LogSolution()
    {
        var key = $"{LastRange:0}|{_ammo}|{_source}|{_v0:0}|{_solution.Valid}|{_solution.Reason}|{Auto}|{_laserCount}";
        if (key == _loggedSolution) return;
        _loggedSolution = key;
        if (LastRange <= 0f) return;
        var source = _source switch
        {
            MuzzleVelocity.Source.Measured => "measured",
            MuzzleVelocity.Source.Computed => "game launch formula",
            MuzzleVelocity.Source.Game => "cannon's default round",
            _ => "default",
        };
        var s = _solution.Valid
            ? $"{LastRange:0} m, '{_ammo}' {_v0:0} m/s ({source}), superelevation {_solution.Superelevation:+0.00;-0.00}° " +
              $"({Mils(_solution.Superelevation):+0.0;-0.0} mil), time of flight {_solution.TimeOfFlight:0.00} s"
            : $"{LastRange:0} m, no solution ({_solution.Reason})";
        Plugin.Log.LogInfo("Fire control: " + s + (Auto ? "" : " — not applied (FCS off)"));
    }

    private static float Mils(float degrees) => degrees * 6400f / 360f;

    

    
    
    public static Vector3 Raise(DriveState s, Vector3 d, out bool raised)
    {
        raised = false;
        if (s == null || s.VehicleKey != ScopeCarry.PlayerVehicleKey) return d;
        Vector3 r;
        if (s.Ptr == _pitchPtr && (_seApplied != 0f || _leadApplied != 0f)) r = Correct(d);
        else if (s.Ptr == _yawPtr && _leadApplied != 0f) r = Quaternion.AngleAxis(_leadApplied, Vector3.up) * d;
        else return d;
        raised = r != d;
        return r;
    }

    private static Vector3 Correct(Vector3 d) => Quaternion.AngleAxis(_leadApplied, Vector3.up) * Raise(d, _seApplied);

    
    public static Vector3 Raise(Vector3 d, float angle)
    {
        if (angle == 0f || d.sqrMagnitude < 1e-8f) return d;
        var right = Vector3.Cross(Vector3.up, d);
        if (right.sqrMagnitude < 1e-8f) return d;
        return Quaternion.AngleAxis(-angle, right.normalized) * d;
    }

    
    
    public static Vector3 Lower(Vector3 d)
    {
        if (_seApplied == 0f && _leadApplied == 0f) return d;
        return Raise(Quaternion.AngleAxis(-_leadApplied, Vector3.up) * d, -_seApplied);
    }

    public static Quaternion Lower(Quaternion rot)
    {
        if (_seApplied == 0f && _leadApplied == 0f) return rot;
        var q = _leadApplied != 0f ? Quaternion.AngleAxis(-_leadApplied, Vector3.up) * rot : rot;
        if (_seApplied == 0f) return q;
        var right = Vector3.Cross(Vector3.up, q * Vector3.forward);
        if (right.sqrMagnitude < 1e-8f) return q;
        return Quaternion.AngleAxis(_seApplied, right.normalized) * q;
    }

    
    public static float[] RangefinderSpec(GameObject vehicleGo)
    {
        Sprocket.Vehicles.Vehicle vehicle = null;
        try
        {
            if (vehicleGo != null)
            {
                vehicle = vehicleGo.GetComponent<Sprocket.Vehicles.Vehicle>();
                if (vehicle == null) vehicle = vehicleGo.GetComponentInParent<Sprocket.Vehicles.Vehicle>();
            }
        }
        catch { }
        var fcs = FcsRegistry.ForVehicle(vehicle);
        if (fcs == null) return Plugin.FcsRequirePart.Value ? new float[6] : null;
        var s = fcs.S;
        return new[] { s.Laser ? 1f : 0f, s.MinRange, s.MaxRange, s.RangeError, s.LaserCooldown, s.LastReturn ? 1f : 0f };
    }

    private static float _heldNoticeAt = -10f;

    
    public static void NoteHeld()
    {
        if (Time.unscaledTime - _heldNoticeAt < 1f) return;
        _heldNoticeAt = Time.unscaledTime;
        Hud.Flash("HOLD — LAYING");
    }

    private static bool KeyPressed()
    {
        try
        {
            var kb = Keyboard.current;
            return kb != null && kb[Plugin.FcsKey].wasPressedThisFrame;
        }
        catch { return false; }
    }

    

    private static class Hud
    {
        private static GameObject _go;
        private static RectTransform _anchor;
        private static TMPro.TextMeshProUGUI _text;
        private static bool _failed;
        private static string _flash;
        private static float _flashUntil;
        private static Vector2 _anchorPos;
        private static bool _hasAnchor;

        private static readonly Color Green = new(0.55f, 0.95f, 0.55f, 0.95f);
        private static readonly Color Amber = new(1f, 0.78f, 0.3f, 0.95f);
        private static readonly Color Red = new(1f, 0.5f, 0.4f, 0.95f);
        private static readonly Color Grey = new(0.85f, 0.85f, 0.85f, 0.85f);

        public static void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 1.5f;
        }

        public static void Hide()
        {
            _hasAnchor = false;
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }

        public static void Draw()
        {
            var scoped = false;
            try { scoped = ScopeCarry.Scope != null && !ScopeCarry.Scope.WasCollected && ScopeCarry.Scope.scoped; } catch { }
            var flash = _flash != null && Time.unscaledTime < _flashUntil;
            if (!Plugin.ShowFcs.Value || (!scoped && !flash)) { Hide(); return; }
            if (!Ensure()) return;
            if (!_go.activeSelf) _go.SetActive(true);

            
            
            var centre = Boresight.ReticleCentre(out var vp) ? vp : new Vector2(0.5f, 0.5f);
            if (!_hasAnchor || (centre - _anchorPos).sqrMagnitude > 0.0025f) _anchorPos = centre;
            else _anchorPos = Vector2.Lerp(_anchorPos, centre, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.15f));
            _hasAnchor = true;
            _anchor.anchorMin = _anchor.anchorMax = new Vector2(Mathf.Clamp01(_anchorPos.x), Mathf.Clamp01(_anchorPos.y));

            string text;
            Color color;
            var range = LastRange > 0f ? $"{LastRange:0} m" : "";
            if (flash) { text = _flash; color = Auto ? Green : Grey; }
            else if (_spec == null)
            {
                
                text = range;
                color = Grey;
            }
            else if (Failed) { text = $"{range}  FCS FAIL".Trim(); color = Red; }
            else
            {
                switch (Current)
                {
                    case State.Ranging: text = "RANGING"; color = Amber; break;
                    case State.NoRange:
                        text = _failed && _failNote.Length > 0 ? $"TOO CLOSE  {_failNote}"
                            : _reason.Length > 0 && _reason != "NO RANGE" ? $"{range}  {_reason}" : "NO RANGE";
                        color = Red;
                        break;
                    case State.Ready: text = $"{range}  READY"; color = Green; break;
                    default:
                        text = Auto ? (_reason.Length > 0 ? $"{range}  {_reason}" : range.Length > 0 ? range : "IDLE") : range;
                        color = Grey;
                        break;
                }
                text = text.Trim();
                var correction = Correction();
                if (correction.Length > 0 && Current != State.Ranging && Current != State.NoRange) text += "  " + correction;
                if (Degraded) { text += "  DEGRADED"; if (color == Green) color = Amber; }
                text = (Auto ? "FCS  " : "") + text + (Auto ? "" : _spec.Auto ? $"   FCS OFF [{Plugin.FcsKey}]" : "   MANUAL");
            }
            _text.text = text;
            _text.color = color;
            DrawLeadMark(scoped && !flash);
        }

        
        
        private static string Correction()
        {
            if (!_solution.Valid || LastRange <= 0f) return "";
            var mode = Plugin.FcsShowCorrection.Value;
            var degrees = string.Equals(mode, "Degrees", StringComparison.OrdinalIgnoreCase);
            if (!degrees && !string.Equals(mode, "Mils", StringComparison.OrdinalIgnoreCase)) return "";
            var shown = Auto ? "" : "~";
            string Fmt(float a, bool signed) =>
                degrees ? (signed ? $"{a:+0.00;-0.00}°" : $"{a:0.00}°") :
                signed ? $"{Mils(a):+0.0;-0.0} mil" : $"{Mils(a):0.0} mil";
            var text = shown + Fmt(_seTarget, true);
            if (Mathf.Abs(Mils(_leadTarget)) >= 0.1f) text += $"  {(_leadTarget > 0f ? "R" : "L")} {Fmt(Mathf.Abs(_leadTarget), false)}";
            return text;
        }

        
        private static UnityEngine.UI.Image _mark;

        private static void DrawLeadMark(bool show)
        {
            if (_mark == null) return;
            var on = show && Auto && _leadShown && Plugin.ShowLeadMark.Value && SightPoint(_leadPoint, out _);
            if (!on)
            {
                if (_mark.gameObject.activeSelf) _mark.gameObject.SetActive(false);
                return;
            }
            SightPoint(_leadPoint, out var p);
            var rt = _mark.rectTransform;
            rt.anchorMin = rt.anchorMax = p;
            if (!_mark.gameObject.activeSelf) _mark.gameObject.SetActive(true);
            _mark.color = Current == State.Ready ? Green : Amber;
        }

        
        private static bool SightPoint(Vector3 world, out Vector2 viewport)
        {
            viewport = default;
            var cam = ScopeCarry.PlayerCamera;
            if (cam == null || cam.WasCollected) return false;
            var v = cam.WorldToViewportPoint(world);
            if (v.z <= 0f) return false;
            viewport = new Vector2(v.x, v.y);
            if (SightMapping.Map(viewport, out var mapped)) viewport = mapped;
            return viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f;
        }

        private static bool Ensure()
        {
            if (_text != null) return true;
            if (_failed) return false;
            try
            {
                _go = new GameObject("GunStabilizerFireControlHud");
                UnityEngine.Object.DontDestroyOnLoad(_go);
                var canvas = _go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 31;
                var scaler = _go.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1f;
                _anchor = new GameObject("ReticleAnchor").AddComponent<RectTransform>();
                _anchor.SetParent(_go.transform, false);
                _anchor.sizeDelta = Vector2.zero;
                var t = new GameObject("FireControl");
                t.transform.SetParent(_anchor, false);
                _text = t.AddComponent<TMPro.TextMeshProUGUI>();
                var font = TMPro.TMP_Settings.defaultFontAsset;
                if (font == null)
                    foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
                if (font != null) _text.font = font;
                _text.fontSize = 18;
                _text.alignment = TMPro.TextAlignmentOptions.Top;
                _text.raycastTarget = false;
                _text.enableWordWrapping = false;
                
                var rt = _text.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -70f);
                rt.sizeDelta = new Vector2(700f, 28f);
                
                try
                {
                    var m = new GameObject("LeadMark");
                    m.transform.SetParent(_go.transform, false);
                    _mark = m.AddComponent<UnityEngine.UI.Image>();
                    _mark.raycastTarget = false;
                    var mrt = _mark.rectTransform;
                    mrt.sizeDelta = new Vector2(11f, 11f);
                    mrt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    var hole = new GameObject("Hole").AddComponent<UnityEngine.UI.Image>();
                    hole.transform.SetParent(m.transform, false);
                    hole.raycastTarget = false;
                    hole.color = new Color(0f, 0f, 0f, 0.55f);
                    hole.rectTransform.sizeDelta = new Vector2(5f, 5f);
                    m.SetActive(false);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Lead mark unavailable: " + e.Message); }
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.Log.LogWarning("Fire control indicator unavailable: " + e.Message);
                return false;
            }
        }
    }
}
