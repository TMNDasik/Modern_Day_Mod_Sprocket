using System;
using HarmonyLib;
using Sprocket.Controls;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ThermalSight;


public class ThermalController : MonoBehaviour
{
    public ThermalController(IntPtr ptr) : base(ptr) { }

    
    internal static bool ForceScope;

    private IThermalView _view;
    
    
    private NightVisionView _nv;
    private bool _night;
    private IThermalView View => _night ? _nv : _view;
    private bool _gpu;
    private readonly HeatModel _heat = new();
    private readonly ThermalEffects _effects = new();

    private VehicleControlPlayerState _state;
    private float _nextStateSearch;
    private bool _thermalOn;
    private GunnerSight _thermalSight;
    private IntPtr _thermalSightPtr;
    internal static VehicleControlPlayerState CurrentState; 
    private IGunnerSight _prevSight;
    private bool _wasScoped;
    private IntPtr _vehiclePtr;
    
    private Transform _ownWeapon;
    private bool _weaponChanged;
    private float _keepSightAfter;
    private int _keepCount;
    private float _keepWindowStart;

    private static ThermalController _instance;

    
    
    
    
    [HarmonyPatch(typeof(Sprocket.Vehicles.ScopeController), nameof(Sprocket.Vehicles.ScopeController.SetActiveTransitioned))]
    private static class NoFadePatch
    {
        private static bool Prefix(Sprocket.Vehicles.ScopeController __instance, IGunnerSight scope)
        {
            try
            {
                var c = _instance;
                if (c == null || !c._thermalOn || c._thermalSight == null || scope == null) return true;
                if (scope.Pointer == c._thermalSightPtr) return true;          
                if (c._state == null || c._state.scopeController == null || c._state.scopeController.Pointer != __instance.Pointer) return true;
                if (Time.unscaledTime < c._keepSightAfter) return true;         
                c.AdoptGroup(scope);
                return false;
            }
            catch { return true; }
        }
    }

    
    private void AdoptGroup(IGunnerSight scope)
    {
        _prevSight = scope;
        var weapon = scope.LinkedWeapon;
        if (weapon != null && (_thermalSight.LinkedWeapon == null || _thermalSight.LinkedWeapon.Pointer != weapon.Pointer))
        {
            _thermalSight._LinkedWeapon_k__BackingField = weapon;
            _weaponChanged = true;
        }
        Plugin.Log.LogInfo((_night ? "Night vision" : "Thermal") + " sight kept on fire group change (no fade)");
    }

    private void Update()
    {
        _instance = this;
        DynamicSightCompat.Tick();
        try { Tick(); }
        catch (Exception e)
        {
            Plugin.Log.LogError("ThermalController: " + e);
            SafeReset();
        }
    }

    private bool _selfTested;
    private float _nextDiag;
    private bool _forcedView, _autoViewDone;
    private int _forcedGen = 3;
    private float _forcedUntil;

    private void StartForcedView(int gen, float seconds)
    {
        if (_view == null) return;
        if (_forcedView) { _forcedUntil = 0; return; }
        _forcedView = true;
        _forcedGen = gen;
        _forcedUntil = Time.unscaledTime + seconds;
        Plugin.Log.LogInfo($"[debug] forced thermal view on {Camera.main?.name} for {seconds}s (Gen {gen})");
    }

    
    private static void SelfTest()
    {

        
        try
        {
            var bundle = GpuTest.Bundle;
            if (bundle != null)
            {
                var shader = BundleLoader.LoadAsset<Shader>(bundle, "assets/thermal/thermaltest.shader");
                Plugin.Log.LogInfo($"[selftest] bundle shader: {(shader == null ? "null" : shader.name)}, supported={(shader != null && shader.isSupported)}");
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[selftest] bundle load failed: " + e); }
        try
        {
            var ptr = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.AssetBundle::LoadFromFile_Internal(System.String,System.UInt32,System.UInt64)");
            Plugin.Log.LogInfo($"[selftest] icall LoadFromFile_Internal: {(ptr == IntPtr.Zero ? "not found" : "found")}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[selftest] icall lookup failed: " + e.Message); }

        try
        {
            var go = new GameObject("ThermalSelfTest");
            var volume = go.AddComponent<UnityEngine.Rendering.HighDefinition.CustomPassVolume>();
            volume.isGlobal = true;
            var pass = new UnityEngine.Rendering.HighDefinition.FullScreenCustomPass();
            pass.enabled = false;
            volume.customPasses.Add(pass);
            var draw = new UnityEngine.Rendering.HighDefinition.DrawRenderersCustomPass();
            draw.enabled = false;
            volume.customPasses.Add(draw);
            Plugin.Log.LogInfo($"[selftest] CustomPassVolume works ({volume.customPasses.Count} passes)");
            Destroy(go);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[selftest] Custom passes unavailable: " + e.Message); }
    }

    private void Tick()
    {
        
        if (!_selfTested && Time.unscaledTime > 5f) { _selfTested = true; if (Plugin.DebugLogging.Value) SelfTest(); }
        if (_view == null)
        {
            _gpu = GpuResources.TryLoad();
            _view = _gpu ? new GpuThermalView() : new ThermalView();
            _nv = new NightVisionView();
            Plugin.Log.LogInfo(_gpu ? "Using GPU thermal pipeline" : "Using CPU thermal pipeline");
        }
        
        
        if(DroneThermalApi.Active){if(_view.Active)_view.Disable();if(_nv.Active)_nv.Disable();ActiveView=null;return;}
        ThermalParts.EnsureInjected();
        EnsureLimitHook();
        Reticles.EnsureRenderHook();
        ReticleEditor.Tick();

        
        if (!_autoViewDone && Plugin.DebugAutoView.Value > 0 && Time.unscaledTime > 20f && Camera.main != null)
        {
            _autoViewDone = true;
            StartForcedView(3, Plugin.DebugAutoView.Value);
        }

        if (_state == null && Time.unscaledTime > _nextStateSearch)
        {
            _nextStateSearch = Time.unscaledTime + 1f;
            _state = FindObjectOfType<VehicleControlPlayerState>();
        }

        CurrentState = _state;
        OpticsDamage.Update(_state);
        var sc = _state != null ? _state.scopeController : null;
        var vc = _state != null ? _state.vehicleController : null;

        
        var vehicleGo = vc != null ? vc.ControlledVehicleGameObject : null;
        var vehiclePtr = vehicleGo != null ? vehicleGo.Pointer : IntPtr.Zero;
        if (vehiclePtr != _vehiclePtr)
        {
            _vehiclePtr = vehiclePtr;
            _range = 0;
            if (_thermalOn) TurnOff(sc, restoreSight: false);
            
            if (_convergenceSet && sc != null)
            {
                _convergenceSet = false;
                try { sc.SetGunConvergance(Sprocket.Vehicles.ScopeController.InitialGunConvergance); }
                catch { }
            }
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[Plugin.ToggleKey].wasPressedThisFrame && sc != null && vehicleGo != null)
            {
                if (_thermalOn && !_night) TurnOff(sc, restoreSight: true);
                
                else if (!ColdWarThermal.OwnsToggle())
                {
                    if (_thermalOn) TurnOff(sc, restoreSight: true);   
                    TurnOn(sc, vehicleGo, night: false);
                }
            }
            if (kb[Plugin.NightVisionKey].wasPressedThisFrame && sc != null && vehicleGo != null)
            {
                if (_thermalOn && _night) TurnOff(sc, restoreSight: true);
                else
                {
                    if (_thermalOn) TurnOff(sc, restoreSight: true);
                    TurnOn(sc, vehicleGo, night: true);
                }
            }
            if (Plugin.DebugLogging.Value)
            {
                if (kb.f10Key.wasPressedThisFrame) _view.SaveSnapshot("manual");
                if (kb.f11Key.wasPressedThisFrame) GpuTest.Toggle(Camera.main);
                if (kb.f9Key.wasPressedThisFrame) StartForcedView(3, 30f);
                if (kb.f6Key.wasPressedThisFrame) ScaleHeat(1f / 1.5f);
                if (kb.f7Key.wasPressedThisFrame) ScaleHeat(1.5f);
            }
        }
        
        if (Plugin.RangefinderPressed() && sc != null && sc.scoped && vehicleGo != null)
            Lase(sc, vc, vehicleGo);

        
        if (_thermalOn && sc != null && _thermalSight != null && Time.unscaledTime >= _keepSightAfter)
            KeepThermalSight(sc);

        
        var viewWanted = false;
        var config = default(SightConfig);
        var nightGen = 0;
        if (_thermalOn && sc != null && _thermalSight != null)
        {
            var active = sc.activeSight;
            viewWanted = sc.scoped && active != null && active.Pointer == _thermalSightPtr;
            if (_night) nightGen = NightVisionParts.GenOf(_thermalSight);
            else config = SightConfigOf(_thermalSight);
        }

        
        if (_night)
        {
            if (viewWanted && nightGen > 0 && (!_nv.Active || _nv.ActiveGen != nightGen))
                _nv.Enable(_state.defaultCamera != null ? _state.defaultCamera : Camera.main, NightVisionProfile.Get(nightGen),
                    unchecked((int)_thermalSightPtr.ToInt64()));
            else if (!viewWanted && _nv.Active) _nv.Disable();
            if (_nv.Active) UpdateNightZoom(NightVisionProfile.Get(nightGen));
            _nv.Update();
            if (_view.Active) _view.Disable();
            _heat.Update(Time.deltaTime, false);
            _effects.Update(Time.deltaTime);
            ActiveView = _nv.Active ? _nv : null;
            return;
        }
        if (_nv != null && _nv.Active) _nv.Disable();

        
        _heat.Update(Time.deltaTime, _thermalOn || _forcedView);
        _effects.Update(Time.deltaTime);

        
        if (_thermalOn && Plugin.DebugLogging.Value && Time.unscaledTime > _nextDiag)
        {
            _nextDiag = Time.unscaledTime + 1f;
            var active = sc?.activeSight;
            Plugin.Log.LogInfo($"[diag] scoped={sc?.scoped} requested={sc?.scopeRequested} activeSight={(active == null ? "null" : active.Pointer.ToString("X"))} " +
                               $"thermal={_thermalSightPtr.ToString("X")} viewWanted={viewWanted} viewActive={_view.Active} " +
                               $"relYaw={_lastRelYaw:0.0} relPitch={_lastRelPitch:0.0} limitMode={_limitMode}");
        }

        
        if (_forcedView)
        {
            ActiveView = null;
            if (!_view.Active) _view.Enable(Camera.main, GenerationProfile.Get(_forcedGen), 12345);
            if (_view.Active && _gpu) { _heat.UpdateProxies(true, _view.Camera); _effects.Draw(_view.Camera); }
            _view.Update();
            if (Time.unscaledTime > _forcedUntil) { _forcedView = false; _view.Disable(); _heat.UpdateProxies(false, null); }
            return;
        }

        if (viewWanted && (!_view.Active || _view.ActiveGen != config.Gen))
            _view.Enable(_state.defaultCamera != null ? _state.defaultCamera : Camera.main,
                GenerationProfile.Get(config.Gen), unchecked((int)_thermalSightPtr.ToInt64()));
        else if (!viewWanted && _view.Active)
            _view.Disable();

        if (_view.Active)
        {
            UpdateZoom(config);
            _view.SetStatus(SightStatusSource.Build(vc, _range));
            if (_gpu)
            {
                _heat.UpdateProxies(true, _view.Camera);
                _effects.UpdateVehicleEffects(_heat.Vehicles, _view.Camera.transform.position, Time.deltaTime);
                _effects.Draw(_view.Camera);
            }
            else _heat.Draw(_view.Camera);
        }
        else if (_gpu) { _heat.UpdateProxies(false, null); _effects.HideAll(); }
        _view.Update();
        ActiveView = _view.Active && _thermalOn ? _view : null;
    }

    
    
    

    private int _range;
    private float _nextLaseNote;
    private bool _convergenceSet;   

    private void Lase(Sprocket.Vehicles.ScopeController sc, Sprocket.Gameplay.VehicleControl.VehicleController vc, GameObject vehicleGo)
    {
        try
        {
            var cam = View != null && View.Active ? View.Camera : vc != null ? vc.ActiveCamera : null;
            var range = SightStatusSource.Lase(cam, vehicleGo, out var note);
            if (range < 0)
            {
                
                if (Time.unscaledTime >= _nextLaseNote) { _nextLaseNote = Time.unscaledTime + 3f; Plugin.Log.LogInfo("Laser: " + note); }
                return;
            }
            _range = range;
            if (_range > 0 && Plugin.RangefinderSetsConvergence.Value)
            {
                sc.SetGunConvergance(_range);
                _convergenceSet = true;
            }
            Plugin.Log.LogInfo(_range > 0 ? $"Laser range {_range} m" : note != null ? $"Laser range: no return ({note})" : "Laser range: no return");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Rangefinder failed: " + e.Message); }
    }

    
    
    
    
    
    
    
    

    private const float YawLimit = 7.5f, PitchMin = -10f, PitchMax = 30f;
    private bool _limitHooked;
    private float _lastRelYaw, _lastRelPitch;
    private string _limitMode = "-";

    
    internal static IThermalView ActiveView;

    private void EnsureLimitHook()
    {
        if (_limitHooked) return;
        _limitHooked = true;
        try
        {
            UnityEngine.Events.UnityAction cb = (Action)ApplySightLimits;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Sight limit hook failed: " + e.Message); }
    }

    private Transform _limitMount;
    private bool _limitMountSearched;

    
    
    
    
    private static Transform FindMount(Transform sight)
    {
        Transform vehicle = null;
        for (var t = sight.parent; t != null; t = t.parent)
            if (t.GetComponent<Vehicle>() != null) { vehicle = t; break; }
        var up = vehicle != null ? vehicle.up : Vector3.up;
        Transform outer = null;
        for (var t = sight.parent; t != null && t != vehicle; t = t.parent)
            if (t.GetComponent<Sprocket.Vehicles.Turrets.TurretRing>() != null && Mathf.Abs(Vector3.Dot(t.up, up)) > 0.7f)
                outer = t;
        return outer ?? vehicle;
    }

    private void ApplySightLimits()
    {
        try
        {
            var view = View;
            if (!_thermalOn || _thermalSight == null || view == null || !view.Active) return;
            var cam = view.Camera;
            if (cam == null) return;
            var ctrl = _state != null ? _state.scopeMovementController : null;
            if (ctrl == null) return;
            if (_limitMount == null && !_limitMountSearched)
            {
                _limitMountSearched = true;
                _limitMount = FindMount(_thermalSight.transform);
                Plugin.Log.LogInfo(_limitMount != null
                    ? $"Thermal sight aiming limits measured from '{_limitMount.name}'" +
                      $"{(_limitMount.GetComponent<Vehicle>() != null ? " (hull)" : " (turret)")}"
                    : "Thermal sight: no turret or hull found above the sight — aiming limits off");
            }
            var mount = _limitMount;
            var ta = ctrl.targetAngles;
            AimWatch(ctrl, ta);
            if (mount == null) return;
            var mq = mount.rotation;

            
            var taDir = AnglesToDir(ta);
            if (ClampToMount(mq, taDir, out var taLimited))
            {
                ctrl.targetAngles = ta + DirDelta(taDir, taLimited);
                _watchPulled += Vector3.Angle(taDir, taLimited);
                _limitMode = mount.name;
                
                try { AppDomain.CurrentDomain.SetData("SprocketMods.SightLimitFrame", Time.frameCount); } catch { }
            }

            
            var cur = ctrl.currentRotation;
            var curDir = cur * Vector3.forward;
            if (ClampToMount(mq, curDir, out var curLimited))
            {
                
                var fix = Quaternion.FromToRotation(curDir, curLimited);
                ctrl.currentRotation = Quaternion.LookRotation(curLimited, cur * Vector3.up);
                var camRot = cam.transform.rotation;
                cam.transform.rotation = Quaternion.LookRotation(fix * (camRot * Vector3.forward), camRot * Vector3.up);
            }

            var local = Quaternion.Inverse(mq) * (cam.transform.rotation * Vector3.forward);
            _lastRelYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            _lastRelPitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Sight limits failed: " + e.Message);
            _thermalSight = null;
        }
    }

    
    private static bool ClampToMount(Quaternion mount, Vector3 dir, out Vector3 limited)
    {
        limited = dir;
        var local = Quaternion.Inverse(mount) * dir;
        var yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        var pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        var cy = Mathf.Clamp(yaw, -YawLimit, YawLimit);
        var cp = Mathf.Clamp(pitch, PitchMin, PitchMax);
        if (Mathf.Abs(cy - yaw) < 0.01f && Mathf.Abs(cp - pitch) < 0.01f) return false;
        limited = mount * (Quaternion.Euler(-cp, cy, 0f) * Vector3.forward);
        return true;
    }

    private static Vector3 AnglesToDir(Vector2 cameraAngles)
    {
        var e = Sprocket.ScopeCameraController.CameraAngleToEuler(cameraAngles);
        return Quaternion.Euler(e.x, e.y, 0f) * Vector3.forward;
    }

    
    private static Vector2 DirDelta(Vector3 from, Vector3 to)
    {
        var ef = Quaternion.LookRotation(from, Vector3.up).eulerAngles;
        var et = Quaternion.LookRotation(to, Vector3.up).eulerAngles;
        var a0 = Sprocket.ScopeCameraController.EulerToCameraAngle(new Vector2(ef.x, ef.y));
        var a1 = Sprocket.ScopeCameraController.EulerToCameraAngle(new Vector2(et.x, et.y));
        return new Vector2(Mathf.DeltaAngle(a0.x, a1.x), Mathf.DeltaAngle(a0.y, a1.y));
    }

    
    
    private float _watchStart = -1f, _watchYaw, _watchPulled, _watchNextLog;
    private int _watchFrames, _watchMouseFrames;
    private Vector2 _watchPrevTa, _watchLastInput;

    private void AimWatch(Sprocket.ScopeCameraController ctrl, Vector2 ta)
    {
        var now = Time.unscaledTime;
        if (_watchStart < 0f || now - _watchStart > 5f)
        {
            _watchStart = now;
            _watchYaw = _watchPulled = 0f;
            _watchFrames = _watchMouseFrames = 0;
            _watchPrevTa = ta;
            return;
        }
        _watchYaw += Mathf.DeltaAngle(_watchPrevTa.x, ta.x);
        _watchPrevTa = ta;
        _watchFrames++;
        try
        {
            var input = ctrl.movementInput;
            if (input.sqrMagnitude > 0f) { _watchMouseFrames++; _watchLastInput = input; }
        }
        catch { }
        if (now - _watchStart < 2f) return;
        if (Mathf.Abs(_watchYaw) > 25f && now >= _watchNextLog)
        {
            _watchNextLog = now + 10f;
            var fps = _watchFrames / Mathf.Max(1e-3f, now - _watchStart);
            Plugin.Log.LogInfo($"Thermal sight: aim swung {_watchYaw:0}° in {now - _watchStart:0.0} s; mouse moved in " +
                               $"{100f * _watchMouseFrames / Mathf.Max(1, _watchFrames):0}% of frames (last input {_watchLastInput.x:0.##}, " +
                               $"{_watchLastInput.y:0.##}); limits pulled the aim back {_watchPulled:0.0}°; sight {_lastRelYaw:0.0}° / " +
                               $"{_lastRelPitch:0.0}° from '{_limitMode}'; {fps:0} fps, screen {Screen.width}x{Screen.height}");
        }
        _watchStart = now;
        _watchYaw = _watchPulled = 0f;
        _watchFrames = _watchMouseFrames = 0;
    }

    
    
    
    
    internal static float DigitalZoom = 1f;

    [HarmonyPatch(typeof(Sprocket.ScopeCameraController), nameof(Sprocket.ScopeCameraController.ProcessInputs))]
    private static class MouseScalePatch
    {
        
        private static void Prefix(Sprocket.ScopeCameraController __instance, out Vector4? __state)
        {
            __state = null;
            var zoom = DigitalZoom;
            if (ActiveView == null || zoom <= 1.001f) return;
            try
            {
                var input = __instance.movementInput;
                if (input.sqrMagnitude <= 0f) return;
                var scaled = input / zoom;
                __instance.movementInput = scaled;
                __state = new Vector4(input.x, input.y, scaled.x, scaled.y);
            }
            catch { }
        }

        private static void Postfix(Sprocket.ScopeCameraController __instance, Vector4? __state)
        {
            if (__state == null) return;
            try
            {
                var s = __state.Value;
                if (__instance.movementInput == new Vector2(s.z, s.w)) __instance.movementInput = new Vector2(s.x, s.y);
            }
            catch { }
        }
    }

    
    private static Vector2 _remapOut;
    private static bool _remapHas;

    [HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
    private static class AimPointPatch
    {
        private static void Postfix(VehicleControlPlayerState __instance)
        {
            var view = ActiveView;
            if (view == null) { _remapHas = false; return; }
            try
            {
                var vc = __instance.vehicleController;
                if (vc == null) return;
                var vp = vc.AimPointInViewport;
                
                if (_remapHas && vp == _remapOut) return;
                if (!view.CameraToScreen(vp, out var screen)) return;
                vc.AimPointInViewport = screen;
                _remapOut = screen;
                _remapHas = true;
            }
            catch { }
        }
    }
    private static float AngleDistance(Vector2 a, Vector2 b) =>
        Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) + Mathf.Abs(Mathf.DeltaAngle(a.y, b.y));

    
    
    

    private float _zoomTotal = 1f;     
    private float _zoomOptical = 1f;   
    private bool _zoomSaved;
    private float _savedTargetFocal, _savedAppliedFocal, _savedMinZoom, _savedMaxZoom, _savedZoomSensitivity;

    private void ResetZoom()
    {
        _zoomTotal = 1f;
        _zoomOptical = 1f;
    }

    
    private bool YieldZoom(IThermalView view)
    {
        if (!DynamicSightCompat.ZoomActive) return false;
        RestoreGameZoom();
        _zoomTotal = 1f;
        _zoomOptical = 1f;
        _nightDigital = 1f;
        view.SetDigitalZoom(1f);
        DigitalZoom = 1f;
        view.SetZoomText(SightZoom.Format(DynamicSightCompat.Magnification(view.Camera)) + "  (Dynamic Gun Sight zoom)");
        return true;
    }

    private void UpdateZoom(SightConfig config)
    {
        if (YieldZoom(View)) return;
        var scroll = 0f;
        try { scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f; } catch { }

        
        var max = config.TotalMax;
        if (scroll > 0.01f) _zoomTotal = SightZoom.Next(_zoomTotal, 1, max);   
        else if (scroll < -0.01f) _zoomTotal = SightZoom.Next(_zoomTotal, -1, max);
        _zoomTotal = Math.Clamp(_zoomTotal, 1f, max);

        var opticalTarget = Math.Min(_zoomTotal, config.OpticalMax);
        var digital = Math.Clamp(_zoomTotal / opticalTarget, 1f, config.DigitalCropMax);

        
        var k = SightZoom.Smooth ? 1f - MathF.Exp(-Time.unscaledDeltaTime * 5f) : 1f;
        _zoomOptical = MathF.Exp(MathF.Log(_zoomOptical) + (MathF.Log(opticalTarget) - MathF.Log(_zoomOptical)) * k);
        if (Math.Abs(_zoomOptical - opticalTarget) < 0.005f) _zoomOptical = opticalTarget;

        ApplyOpticalZoom(_zoomOptical);
        View.SetDigitalZoom(digital);
        DigitalZoom = digital;

        var total = _zoomOptical * digital;
        var text = SightZoom.Format(total);
        if (digital > 1.001f) text += $"  (optical {SightZoom.Format(_zoomOptical)} + digital x{total - _zoomOptical:0.0})";
        else text += $"  (optical {SightZoom.Format(_zoomOptical)})";
        if (total >= max - 0.01f && max > 1f) text += "  MAX";
        View.SetZoomText(text);
    }

    
    private int _nightMode;
    private float _nightDigital = 1f;

    private void UpdateNightZoom(NightVisionProfile p)
    {
        if (YieldZoom(_nv)) return;
        var scroll = 0f;
        try { scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f; } catch { }
        var modes = p.Magnifications;
        _nightMode = Math.Clamp(_nightMode, 0, modes.Length - 1);
        var last = _nightMode == modes.Length - 1;
        if (scroll > 0.01f)
        {
            if (!last) _nightMode++;
            else if (p.DigitalMax > 1f) _nightDigital = SightZoom.Next(_nightDigital, 1, p.DigitalMax);
        }
        else if (scroll < -0.01f)
        {
            if (_nightDigital > 1.001f) _nightDigital = SightZoom.Next(_nightDigital, -1, p.DigitalMax);
            else if (_nightMode > 0) _nightMode--;
        }
        if (!last && _nightMode < modes.Length - 1) _nightDigital = 1f;
        var optical = modes[_nightMode];
        ApplyOpticalZoom(optical);
        _nv.SetDigitalZoom(_nightDigital);
        DigitalZoom = _nightDigital;
        var text = SightZoom.Format(optical * _nightDigital) + "  " + p.ModeName(_nightMode);
        if (_nightDigital > 1.001f) text += $"  + digital {SightZoom.Format(_nightDigital)}";
        _nv.SetZoomText(text);
    }

    private void ApplyOpticalZoom(float zoom)
    {
        var cam = View.Camera;
        if (cam == null) return;
        var baseFov = Math.Clamp(Plugin.BaseFieldOfView.Value, 5f, 120f) * Mathf.Deg2Rad;
        var fov = 2f * MathF.Atan(MathF.Tan(baseFov * 0.5f) / Math.Max(1f, zoom));
        var fovDeg = fov * Mathf.Rad2Deg;

        
        try
        {
            var controller = _state != null ? _state.scopeMovementController : null;
            if (controller != null)
            {
                if (!_zoomSaved)
                {
                    _savedTargetFocal = controller.targetFocalLength;
                    _savedAppliedFocal = controller.appliedFocalLength;
                    _savedMinZoom = controller.minZoom;
                    _savedMaxZoom = controller.maxZoom;
                    _savedZoomSensitivity = controller.zoomSensitivity;
                    _zoomSaved = true;
                }
                var sensor = cam.usePhysicalProperties ? cam.sensorSize.y : 24f;
                var focal = sensor / (2f * MathF.Tan(fov * 0.5f));
                
                controller.zoomSensitivity = 0f;
                controller.minZoom = focal;
                controller.maxZoom = focal;
                controller.scrollValue = 0f;
                controller.targetFocalLength = focal;
                controller.appliedFocalLength = focal;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Scope zoom control failed: " + e.Message); }

        cam.fieldOfView = fovDeg;
    }

    private void RestoreGameZoom()
    {
        if (!_zoomSaved) return;
        _zoomSaved = false;
        try
        {
            var controller = _state != null ? _state.scopeMovementController : null;
            if (controller != null)
            {
                controller.zoomSensitivity = _savedZoomSensitivity;
                controller.minZoom = _savedMinZoom;
                controller.maxZoom = _savedMaxZoom;
                controller.targetFocalLength = _savedTargetFocal;
                controller.appliedFocalLength = _savedAppliedFocal;
            }
        }
        catch { }
    }

    private void TurnOn(Sprocket.Vehicles.ScopeController sc, GameObject vehicleGo, bool night)
    {
        var current = sc.activeSight;
        var thermal = FindThermalSight(vehicleGo, current, night);
        if (thermal == null)
        {
            Plugin.Log.LogInfo(night ? "No night vision sight installed on this vehicle" : "No thermal sight installed on this vehicle");
            return;
        }
        if (OpticsDamage.Destroyed(thermal)) Plugin.Log.LogInfo(night ? "The night vision sight is destroyed" : "The thermal sight is destroyed");
        _night = night;

        var thermalI = thermal.TryCast<IGunnerSight>();
        if (thermalI == null)
        {
            Plugin.Log.LogWarning("Thermal sight does not implement IGunnerSight");
            return;
        }

        _thermalSight = thermal;
        _thermalSightPtr = thermalI.Pointer;
        _wasScoped = sc.scoped;

        _ownWeapon = thermal.LinkedWeapon;
        _weaponChanged = false;
        _keepSightAfter = Time.unscaledTime + 1f; 
        _keepCount = 0;
        if (current == null || current.Pointer != _thermalSightPtr)
        {
            _prevSight = current;
            
            if (thermal.LinkedWeapon == null && current != null && current.LinkedWeapon != null)
            {
                thermal._LinkedWeapon_k__BackingField = current.LinkedWeapon;
                _weaponChanged = true;
            }
            
            
            if (thermal.LinkedWeapon == null)
            {
                var weapon = FallbackWeapon(vehicleGo);
                if (weapon != null)
                {
                    thermal._LinkedWeapon_k__BackingField = weapon;
                    _weaponChanged = true;
                }
            }
            try { sc.SetActiveTransitioned(thermalI); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("SetActiveTransitioned failed, using SetActive: " + e.Message);
                sc.SetActive(thermalI);
            }
        }
        else
        {
            
            _prevSight = null;
        }

        ForceScope = true;
        if (!sc.scopeRequested) sc.SetScopeRequested(true);
        _thermalOn = true;
        ResetZoom();
        _nightMode = 0;
        _nightDigital = 1f;
        if (night)
        {
            var p = NightVisionProfile.Get(NightVisionParts.GenOf(thermal));
            Plugin.Log.LogInfo($"Night vision channel ON ({p.Label}, range {p.Range:0} m, {p.Magnifications.Length} field(s) of view)");
            return;
        }
        var cfg = SightConfigOf(thermal);
        Plugin.Log.LogInfo($"Thermal channel ON (Gen {cfg.Gen}, optical x{cfg.Optical}, digital x{cfg.Digital})");
    }

    
    private Transform FallbackWeapon(GameObject vehicleGo)
    {
        try
        {
            var go = _state?.vehicleController?.activeGunner?.WeaponGameObject;
            if (go != null) return go.transform;
            var cannon = vehicleGo != null ? vehicleGo.GetComponentInChildren<Cannon>() : null;
            return cannon != null ? cannon.transform : null;
        }
        catch { return null; }
    }

    
    
    private void KeepThermalSight(Sprocket.Vehicles.ScopeController sc)
    {
        var active = sc.activeSight;
        if (active == null || active.Pointer == _thermalSightPtr) return;
        var now = Time.unscaledTime;
        if (now - _keepWindowStart > 1f)
        {
            _keepWindowStart = now;
            _keepCount = 0;
        }
        if (++_keepCount > 8)
        {
            
            Plugin.Log.LogWarning("The game keeps selecting another sight — thermal channel turned off");
            TurnOff(sc, restoreSight: false);
            return;
        }
        _prevSight = active;
        var weapon = active.LinkedWeapon;
        if (weapon != null && (_thermalSight.LinkedWeapon == null || _thermalSight.LinkedWeapon.Pointer != weapon.Pointer))
        {
            _thermalSight._LinkedWeapon_k__BackingField = weapon;
            _weaponChanged = true;
        }
        var thermalI = _thermalSight.TryCast<IGunnerSight>();
        if (thermalI == null) return;
        try { sc.SetActive(thermalI); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Keeping the thermal sight failed: " + e.Message);
            return;
        }
        if (!sc.scopeRequested) sc.SetScopeRequested(true);
        Plugin.Log.LogInfo("Fire group changed — thermal sight kept");
    }

    private void TurnOff(Sprocket.Vehicles.ScopeController sc, bool restoreSight)
    {
        ForceScope = false;
        _thermalOn = false;
        ActiveView = null;
        RestoreLink();
        RestoreGameZoom();
        try
        {
            if (restoreSight && sc != null)
            {
                var active = sc.activeSight;
                if (_prevSight != null && active != null && active.Pointer == _thermalSightPtr)
                    sc.SetActiveTransitioned(_prevSight);
                if (!_wasScoped) sc.SetScopeRequested(false);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Restoring sight failed: " + e.Message); }
        _prevSight = null;
        _limitMount = null;
        _limitMountSearched = false;
        _thermalSight = null;
        _thermalSightPtr = IntPtr.Zero;
        var wasNight = _night;
        View.Disable();
        _night = false;
        Plugin.Log.LogInfo(wasNight ? "Night vision channel OFF" : "Thermal channel OFF");
    }

    
    private void RestoreLink()
    {
        try
        {
            if (_weaponChanged && _thermalSight != null && !_thermalSight.WasCollected)
                _thermalSight._LinkedWeapon_k__BackingField = _ownWeapon;
        }
        catch { }
        _weaponChanged = false;
        _ownWeapon = null;
    }

    private void SafeReset()
    {
        ForceScope = false;
        _thermalOn = false;
        ActiveView = null;
        try { RestoreLink(); } catch { }
        _thermalSight = null;
        _prevSight = null;
        _limitMount = null;
        _limitMountSearched = false;
        try { RestoreGameZoom(); } catch { }
        try { _view.Disable(); } catch { }
        try { _nv?.Disable(); } catch { }
        _night = false;
    }

    
    internal static bool IsSpecialSight(GunnerSight s) => SightConfigOf(s).Gen != 0 || NightVisionParts.GenOf(s) != 0;

    private static GunnerSight FindThermalSight(GameObject vehicle, IGunnerSight current, bool night)
    {
        GunnerSight best = null;
        var bestScore = float.MaxValue;
        var currentWeapon = current?.LinkedWeapon;
        var currentPos = current?.SightParent != null ? current.SightParent.position : vehicle.transform.position;

        foreach (var s in vehicle.GetComponentsInChildren<GunnerSight>(true))
        {
            if (s == null || (night ? NightVisionParts.GenOf(s) == 0 : SightConfigOf(s).Gen == 0)) continue;
            
            var score = Vector3.Distance(s.transform.position, currentPos);
            if (currentWeapon != null && s.LinkedWeapon != null && s.LinkedWeapon.Pointer == currentWeapon.Pointer) score -= 1000f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    
    
    
    internal static SightConfig SightConfigOf(GunnerSight s)
    {
        var obj = s.GetComponent<VehicleObject>();
        if (obj != null && !string.IsNullOrEmpty(obj.guid)) return ThermalParts.TryDecode(obj.guid, out var c) ? c : default;
        var gen = ThermalParts.GenFromName(s.gameObject.name);
        return gen != 0 ? new SightConfig(gen, ThermalParts.DefaultOptical, ThermalParts.DefaultDigital) : default;
    }

    private static void ScaleHeat(float k)
    {
        Plugin.HeatOverlayScale.Value *= k;
        Plugin.Log.LogInfo($"Heat overlay scale = {Plugin.HeatOverlayScale.Value:0.###}");
    }

    

    [HarmonyPatch(typeof(Sprocket.VehicleControl.VehicleInputSystemInputs), nameof(Sprocket.VehicleControl.VehicleInputSystemInputs.Scope))]
    private static class ScopeInputPatch
    {
        private static void Postfix(InputState state, ref bool __result)
        {
            if (!ForceScope) return;
            if ((state & InputState.Held) != 0) __result = true;
            else if ((state & (InputState.Pressed | InputState.Released)) != 0) __result = false;
        }
    }

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.OnFire))]
    private static class CannonFirePatch
    {
        private static bool _loggedEnergy;

        private static void Postfix(Cannon __instance, Sprocket.WeaponFireEventArgs e)
        {
            try
            {
                HeatModel.Instance?.OnCannonFired(__instance);
                if (ThermalEffects.Instance == null) return;
                var energy = e != null ? e.KineticEnergy : 0f;
                if (!_loggedEnergy && Plugin.DebugLogging.Value) { _loggedEnergy = true; Plugin.Log.LogInfo($"Cannon fired: kinetic energy {energy:0}"); }
                var scale = energy > 0 ? MathF.Pow(energy / 2e6f, 0.25f) : 1f;
                if (HeatModel.Instance != null && HeatModel.Instance.TryGetMuzzle(__instance, out var muzzle, out var dir))
                    ThermalEffects.Instance.OnCannonFired(muzzle, dir, scale);
                else
                    ThermalEffects.Instance.OnCannonFired(__instance.transform.position + __instance.transform.forward * 3f, __instance.transform.forward, scale);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Muzzle effect failed: " + ex.Message); }
        }
    }

    
    [HarmonyPatch(typeof(Sprocket.Vehicles.Weapons.ProjectileEffectConfig), nameof(Sprocket.Vehicles.Weapons.ProjectileEffectConfig.PlayEffect))]
    private static class ImpactPatch
    {
        private static void Postfix(Sprocket.DamageModelling.ProjectileEffectInfo info)
        {
            try
            {
                if (ThermalEffects.Instance == null || info == null) return;
                ThermalEffects.Instance.OnImpact(info.Type, info.Position, info.HitNormal, info.HitVelocity, info.Parent, info.Calibre);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Impact effect failed: " + ex.Message); }
        }
    }
}
