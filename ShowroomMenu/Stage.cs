using System;
using System.Collections.Generic;
using Sprocket.ContinuousTracks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace ShowroomMenu;





internal sealed class Stage
{
    public const string SceneName = "Showroom";
    private const int DirectionalShadowResolution = 2048;   
    private const int PunctualShadowTier = 3;                
    private const float SunShadowDistance = 50f;             

    
    private static readonly List<(Scene scene, float since)> Pending = new();

    private Scene _menuScene;
    private AsyncOperation _load;
    private Scene _showroom;
    private bool _haveShowroom;
    private float _start;
    private readonly List<Renderer> _hiddenRenderers = new();
    private readonly List<Behaviour> _hiddenBehaviours = new();
    private readonly List<(Transform t, Vector3 pos, Quaternion rot)> _lights = new();
    private bool _lightsTurned;
    private Transform _keep;
    private GameObject _lens;
    private VolumeProfile _lensProfile;

    public bool Ready { get; private set; }
    public bool Failed { get; private set; }
    public float Elapsed => Time.unscaledTime - _start;

    
    public void Begin(Sprocket.MainMenu menu, Scene menuScene, Transform keep)
    {
        _menuScene = menuScene;
        _keep = keep;
        _start = Time.unscaledTime;
        HideFactory();
        CreateLens();
        _load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        if (_load == null)
        {
            Failed = true;
            return;
        }
        
        var count = SceneManager.sceneCount;
        for (var i = count - 1; i >= 0; i--)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.name != SceneName) continue;
            _showroom = s;
            _haveShowroom = true;
            break;
        }
        Plugin.Debug($"Showroom scene loading (found in list: {_haveShowroom})");
    }

    public void Update()
    {
        if (Ready)
        {
            ApplyLights();
            return;
        }
        if (Failed || _load == null) return;
        if (!_load.isDone)
        {
            if (Elapsed > 40f) Failed = true;
            return;
        }
        if (!_haveShowroom || !_showroom.isLoaded)
        {
            var s = SceneManager.GetSceneByName(SceneName);
            if (!s.IsValid() || !s.isLoaded)
            {
                Failed = true;
                return;
            }
            _showroom = s;
            _haveShowroom = true;
        }
        Configure();
        Ready = true;
        Plugin.Log.LogInfo($"Showroom scene ready after {Elapsed:0.0} s");
    }

    

    private void HideFactory()
    {
        foreach (var root in _menuScene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && !Kept(r.transform))
                {
                    r.enabled = false;
                    _hiddenRenderers.Add(r);
                }
            foreach (var l in root.GetComponentsInChildren<Light>(true)) Hide(l);
            foreach (var p in root.GetComponentsInChildren<ReflectionProbe>(true)) Hide(p);
            foreach (var v in root.GetComponentsInChildren<Volume>(true)) Hide(v);
            foreach (var t in root.GetComponentsInChildren<TrackBeltTester>(true)) Hide(t);
            
            foreach (var s in root.GetComponentsInChildren<StaticLightingSky>(true)) Hide(s);
        }
        Plugin.Debug($"Factory hidden: {_hiddenRenderers.Count} renderers, {_hiddenBehaviours.Count} lights/probes/volumes");
    }

    private bool Kept(Transform t) => _keep != null && t.IsChildOf(_keep);

    private void Hide(Behaviour b)
    {
        if (b == null || !b.enabled || Kept(b.transform)) return;
        b.enabled = false;
        _hiddenBehaviours.Add(b);
    }

    
    
    
    private void CreateLens()
    {
        try
        {
            _lens = new GameObject("ShowroomMenuLens");
            SceneManager.MoveGameObjectToScene(_lens, _menuScene);
            _lensProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var dof = _lensProfile.Add(Il2CppInterop.Runtime.Il2CppType.Of<DepthOfField>(), false).Cast<DepthOfField>();
            dof.focusMode.overrideState = true;
            dof.focusMode.value = DepthOfFieldMode.Off;
            var blur = _lensProfile.Add(Il2CppInterop.Runtime.Il2CppType.Of<MotionBlur>(), false).Cast<MotionBlur>();
            blur.intensity.overrideState = true;
            blur.intensity.value = 0f;
            var shadows = _lensProfile.Add(Il2CppInterop.Runtime.Il2CppType.Of<HDShadowSettings>(), false).Cast<HDShadowSettings>();
            shadows.maxShadowDistance.overrideState = true;
            shadows.maxShadowDistance.value = SunShadowDistance;
            var volume = _lens.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.weight = 1f;
            volume.sharedProfile = _lensProfile;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Could not turn off depth of field in the menu: " + e.Message); }
    }

    

    private void Configure()
    {
        foreach (var root in _showroom.GetRootGameObjects())
        {
            switch (root.name)
            {
                case "Environment":
                    foreach (var a in root.GetComponentsInChildren<AudioSource>(true))
                    {
                        a.Stop();
                        a.enabled = false;
                    }
                    var wind = root.transform.Find("Wind Ambiance Variant");
                    if (wind != null) wind.gameObject.SetActive(false);
                    
                    foreach (var d in root.GetComponentsInChildren<DecalProjector>(true)) d.gameObject.SetActive(false);
                    foreach (var l in root.GetComponentsInChildren<Light>(true))
                    {
                        _lights.Add((l.transform, l.transform.position, l.transform.rotation));
                        PrepareShadows(l);
                    }
                    break;
                case "Floor":
                    break;
                default:
                    root.SetActive(false);
                    break;
            }
        }
        ApplyLights();
        Plugin.Debug($"Showroom configured: {_lights.Count} lights");
    }

    
    
    
    
    private static void PrepareShadows(Light light)
    {
        try
        {
            var hd = light.GetComponent<HDAdditionalLightData>();
            if (hd == null) return;
            var mode = hd.shadowUpdateMode;
            if (mode != ShadowUpdateMode.EveryFrame) hd.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
            var res = hd.shadowResolution;
            var resText = res == null ? "?" : res.useOverride ? $"custom {res.@override}" : $"tier {res.level}";
            if (light.type == LightType.Directional)
            {
                hd.SetShadowResolution(DirectionalShadowResolution);
                hd.SetShadowResolutionOverride(true);
            }
            else
            {
                hd.SetShadowResolutionOverride(false);
                hd.SetShadowResolutionLevel(PunctualShadowTier);
            }
            Plugin.Log.LogInfo($"Showroom light '{light.name}': {light.type}, intensity {light.intensity:G4}, range {light.range:0.#}, " +
                               $"shadows {light.shadows} ({resText} -> {(light.type == LightType.Directional ? DirectionalShadowResolution + " px" : "tier " + PunctualShadowTier)}), " +
                               $"bias {hd.normalBias:0.###}/{hd.slopeBias:0.###}, update {mode} -> every frame");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Light '{light.name}' shadow setup failed: {e.Message}"); }
    }

    
    
    private void ApplyLights()
    {
        var want = Plugin.LightFromCamera.Value;
        if (want == _lightsTurned) return;
        _lightsTurned = want;
        var turn = Quaternion.Euler(0f, 180f, 0f);
        foreach (var (t, pos, rot) in _lights)
        {
            if (t == null || t.WasCollected) continue;
            if (want) t.SetPositionAndRotation(turn * pos, turn * rot);
            else t.SetPositionAndRotation(pos, rot);
        }
    }

    public void Dispose(bool restoreMenu)
    {
        if (restoreMenu)
        {
            foreach (var r in _hiddenRenderers)
                if (r != null && !r.WasCollected && r) r.enabled = true;
            foreach (var b in _hiddenBehaviours)
                if (b != null && !b.WasCollected && b) b.enabled = true;
        }
        _hiddenRenderers.Clear();
        _hiddenBehaviours.Clear();
        _lights.Clear();
        if (_lens != null && !_lens.WasCollected && _lens) UnityEngine.Object.Destroy(_lens);
        if (_lensProfile != null && !_lensProfile.WasCollected && _lensProfile) UnityEngine.Object.Destroy(_lensProfile);
        _lens = null;
        _lensProfile = null;

        
        if (_haveShowroom && _showroom.IsValid())
        {
            if (_showroom.isLoaded) Unload(_showroom);
            else
            {
                Pending.Add((_showroom, Time.unscaledTime));
                Plugin.Log.LogInfo("Left the main menu while the Showroom scene was still loading: it is unloaded as soon as it finishes");
            }
        }
        else if (_load != null && !_load.isDone)
            Plugin.Log.LogWarning("Showroom copy is still loading and could not be identified; it will stay loaded");
        _haveShowroom = false;
        _load = null;
    }

    private static void Unload(Scene scene)
    {
        try
        {
            
            foreach (var root in scene.GetRootGameObjects())
                if (root != null) root.SetActive(false);
            SceneManager.UnloadSceneAsync(scene);
            Plugin.Debug("Showroom scene unloading");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Could not unload the Showroom scene: " + e.Message); }
    }

    public static void ProcessPending()
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var (scene, since) = Pending[i];
            if (!scene.IsValid()) { Pending.RemoveAt(i); continue; }
            if (scene.isLoaded) { Unload(scene); Pending.RemoveAt(i); }
            else if (Time.unscaledTime - since > 60f) Pending.RemoveAt(i);
        }
    }
}
