using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShowroomMenu;






internal sealed class FlickerWatch
{
    private const float Period = 0.2f;
    private const int MaxLinesPerSecond = 15;

    private readonly float _start = Time.unscaledTime;
    private float _next;
    private float _windowStart;
    private int _windowLines, _suppressed;
    private bool _started;

    private Dictionary<string, string> _lights = new();
    private string _volumes, _stackState, _scenes, _cameraState;
    private int _vehicleRenderers = int.MinValue;
    private VolumeStack _stack;
    private bool _haveSetPose;
    private Vector3 _setPos;
    private Quaternion _setRot;
    private readonly HashSet<string> _failed = new();
    private readonly float[] _frames = new float[60];      
    private int _frameIndex;

    private float T => Time.unscaledTime - _start;

    

    
    public void CheckCamera(Camera cam)
    {
        if (!_haveSetPose || cam == null) return;
        var t = cam.transform;
        var d = (t.position - _setPos).magnitude;
        var a = Quaternion.Angle(t.rotation, _setRot);
        if (d > 1e-4f || a > 0.01f) Emit($"camera was moved by something else: {d * 1000f:0.#} mm, {a:0.###} deg");
    }

    public void CameraSet(Camera cam)
    {
        if (cam == null) return;
        _setPos = cam.transform.position;
        _setRot = cam.transform.rotation;
        _haveSetPose = true;
    }

    public void Update(Camera cam, HDAdditionalCameraData hdCam, Showcase showcase)
    {
        if (!_started)
        {
            _started = true;
            Plugin.Log.LogInfo("[watch] lighting watch started (only changes are logged; F8 = mark a flicker)");
            Section("settings", LogSettings);
            Sample(cam, hdCam, showcase, true);
            _next = Time.unscaledTime + Period;
        }

        var dt = Time.unscaledDeltaTime;
        _frames[_frameIndex++ % _frames.Length] = dt;
        if (dt > 0.05f) Emit($"long frame {dt * 1000f:0} ms");

        var mark = false;
        try
        {
            var kb = Keyboard.current;
            mark = kb != null && kb.f8Key.wasPressedThisFrame;
        }
        catch { }
        if (mark)
        {
            Plugin.Log.LogInfo($"[watch t={T:0.00}] ===== F8: flicker seen, full snapshot =====");
            Sample(cam, hdCam, showcase, true);
            var recent = new List<string>();
            for (var i = Math.Max(0, _frameIndex - _frames.Length); i < _frameIndex; i++) recent.Add((_frames[i % _frames.Length] * 1000f).ToString("0"));
            Plugin.Log.LogInfo($"[watch t={T:0.00}] last frames, ms: {string.Join(" ", recent)}");
            return;
        }

        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Period;
        Sample(cam, hdCam, showcase, false);
    }

    public void Dispose()
    {
        if (_suppressed > 0) Plugin.Log.LogInfo($"[watch] ... {_suppressed} more lines skipped");
        _suppressed = 0;
        try { _stack?.Dispose(); } catch { }
        _stack = null;
    }

    

    private void Sample(Camera cam, HDAdditionalCameraData hdCam, Showcase showcase, bool full)
    {
        Section("lights", () => SampleLights(full));
        Section("volumes", () => SampleVolumes(hdCam, full));
        Section("stack", () => SampleStack(cam, hdCam, full));
        Section("scenes", () => SampleScenes(full));
        Section("camera", () => SampleCamera(cam, hdCam, full));
        Section("vehicle", () =>
        {
            var n = showcase?.CountRenderers() ?? -1;
            if (full || n != _vehicleRenderers)
                Line(full, $"vehicle renderers: {n}" + (_vehicleRenderers == int.MinValue || full ? "" : $" (was {_vehicleRenderers})") +
                           (full && showcase != null ? $", turntable at {showcase.Yaw:0} deg" : ""));
            _vehicleRenderers = n;
        });
    }

    private void SampleLights(bool full)
    {
        var now = new Dictionary<string, string>();
        foreach (var o in Object.FindObjectsByType(Il2CppType.Of<Light>(), FindObjectsSortMode.None))
        {
            var l = o?.TryCast<Light>();
            if (l == null || !l.isActiveAndEnabled) continue;
            var c = l.color;
            var p = l.transform.position;
            now[Key(l)] = $"{l.type} shadows {l.shadows} I={l.intensity:G4} C=({c.r:0.00},{c.g:0.00},{c.b:0.00}) R={l.range:0.#} A={l.spotAngle:0.#} at ({p.x:0.00},{p.y:0.00},{p.z:0.00})";
        }
        foreach (var o in Object.FindObjectsByType(Il2CppType.Of<ReflectionProbe>(), FindObjectsSortMode.None))
        {
            var r = o?.TryCast<ReflectionProbe>();
            if (r == null || !r.isActiveAndEnabled) continue;
            now["probe " + Key(r)] = $"mode {r.mode} intensity {r.intensity:0.##}";
        }
        if (full)
        {
            foreach (var kv in now.OrderBy(x => x.Key)) Line(true, $"on: {kv.Key}: {kv.Value}");
            if (now.Count == 0) Line(true, "no lights on");
        }
        else
        {
            foreach (var kv in now)
            {
                if (!_lights.TryGetValue(kv.Key, out var old)) Emit($"turned ON: {kv.Key}: {kv.Value}");
                else if (old != kv.Value) Emit($"changed: {kv.Key}: {old} -> {kv.Value}");
            }
            foreach (var kv in _lights)
                if (!now.ContainsKey(kv.Key)) Emit($"turned OFF: {kv.Key}");
        }
        _lights = now;
    }

    private void SampleVolumes(HDAdditionalCameraData hdCam, bool full)
    {
        var mask = hdCam != null ? hdCam.volumeLayerMask : (LayerMask)(-1);
        var parts = new List<string>();
        foreach (var v in VolumeManager.instance.GetVolumes(mask))
        {
            if (v == null || !v.enabled || !v.gameObject.activeInHierarchy) continue;
            var profile = v.sharedProfile;
            parts.Add($"{v.gameObject.name}[{v.priority:0.##}, w {v.weight:0.##}{(v.isGlobal ? "" : ", local")}, {(profile != null ? profile.name : "no profile")}]");
        }
        var s = string.Join("; ", parts);
        if (full || s != _volumes) Line(full, "volumes: " + s);
        _volumes = s;
    }

    private void SampleStack(Camera cam, HDAdditionalCameraData hdCam, bool full)
    {
        if (cam == null) return;
        var mask = hdCam != null ? hdCam.volumeLayerMask : (LayerMask)(-1);
        _stack ??= VolumeManager.instance.CreateStack();
        VolumeManager.instance.Update(_stack, cam.transform, mask);
        var parts = new List<string>();
        var exp = Get<Exposure>();
        if (exp != null) parts.Add($"exposure {exp.mode.value} fixed {exp.fixedExposure.value:0.00} comp {exp.compensation.value:0.00}");
        var fog = Get<Fog>();
        if (fog != null) parts.Add($"fog {fog.enabled.value} mfp {fog.meanFreePath.value:0.#} volumetric {fog.enableVolumetricFog.value}");
        var env = Get<VisualEnvironment>();
        if (env != null) parts.Add($"sky {env.skyType.value}");
        var sh = Get<HDShadowSettings>();
        if (sh != null) parts.Add($"shadow distance {sh.maxShadowDistance.value:0.#}");
        var ind = Get<IndirectLightingController>();
        if (ind != null) parts.Add($"indirect {ind.indirectDiffuseLightingMultiplier.value:0.##}/{ind.reflectionLightingMultiplier.value:0.##}");
        var s = string.Join(", ", parts);
        if (full || s != _stackState) Line(full, "post: " + s);
        _stackState = s;
    }

    private T Get<T>() where T : VolumeComponent
    {
        var c = _stack.GetComponent(Il2CppType.Of<T>());
        return c != null ? c.TryCast<T>() : null;
    }

    private void SampleScenes(bool full)
    {
        var parts = new List<string>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var sc = SceneManager.GetSceneAt(i);
            parts.Add(sc.name + (sc.isLoaded ? "" : " (loading)") + (sc == SceneManager.GetActiveScene() ? " (active)" : ""));
        }
        var s = string.Join(", ", parts);
        if (full || s != _scenes) Line(full, "scenes: " + s);
        _scenes = s;
    }

    private void SampleCamera(Camera cam, HDAdditionalCameraData hdCam, bool full)
    {
        if (cam == null) return;
        var s = $"fov {cam.fieldOfView:0.0} aspect {cam.aspect:0.000} near {cam.nearClipPlane:0.###} far {cam.farClipPlane:0} mask {cam.cullingMask}";
        if (hdCam != null)
        {
            s += $" AA {hdCam.antialiasing} dynres {hdCam.allowDynamicResolution} DLSS {hdCam.allowDeepLearningSuperSampling} clear {hdCam.clearColorMode}";
            try
            {
                var drs = DynamicResolutionHandler.instance;
                if (drs != null) s += $" scale {drs.GetCurrentScale():0.00}";
            }
            catch { }
        }
        if (full || s != _cameraState) Line(full, "camera: " + s);
        _cameraState = s;
    }

    private void LogSettings()
    {
        var asset = GraphicsSettings.currentRenderPipeline?.TryCast<HDRenderPipelineAsset>();
        var line = $"quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', vsync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}, screen {Screen.width}x{Screen.height} {Screen.fullScreenMode}";
        if (asset != null)
        {
            var rp = asset.currentPlatformRenderPipelineSettings;
            var sh = rp.hdShadowInitParams;
            var atlas = sh.punctualLightShadowAtlas;
            var grd = rp.gpuResidentDrawerSettings;
            line += $"; HDRP '{asset.name}': punctual shadow atlas {atlas.shadowAtlasResolution} (rescale {atlas.useDynamicViewportRescale}), max shadows {sh.maxShadowRequests}, " +
                    $"punctual tiers {Tiers(sh.shadowResolutionPunctual)}, directional tiers {Tiers(sh.shadowResolutionDirectional)}, " +
                    $"GPU resident drawer {grd.mode} (occlusion {grd.enableOcclusionCullingInCameras}), volumetrics {rp.supportVolumetrics}, SSR {rp.supportSSR}";
        }
        Plugin.Log.LogInfo("[watch] " + line);
    }

    private static string Tiers(IntScalableSetting s)
    {
        if (s == null) return "?";
        var values = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            try { values.Add(s[i].ToString()); }
            catch { break; }
        }
        return string.Join("/", values);
    }

    

    private static string Key(Component c)
    {
        var t = c.transform;
        var parent = t.parent;
        return $"{c.gameObject.scene.name}:{(parent != null ? parent.name + "/" : "")}{t.name}";
    }

    private void Line(bool full, string text)
    {
        if (full) Plugin.Log.LogInfo($"[watch t={T:0.00}] {text}");
        else Emit(text);
    }

    
    private void Emit(string text)
    {
        var now = Time.unscaledTime;
        if (now - _windowStart >= 1f)
        {
            if (_suppressed > 0) Plugin.Log.LogInfo($"[watch t={T:0.00}] ... {_suppressed} more changes skipped");
            _windowStart = now;
            _windowLines = 0;
            _suppressed = 0;
        }
        if (_windowLines++ < MaxLinesPerSecond) Plugin.Log.LogInfo($"[watch t={T:0.00}] {text}");
        else _suppressed++;
    }

    private void Section(string name, Action a)
    {
        if (_failed.Contains(name)) return;
        try { a(); }
        catch (Exception e)
        {
            _failed.Add(name);
            Plugin.Log.LogWarning($"[watch] {name} check turned off: {e.Message}");
        }
    }
}
