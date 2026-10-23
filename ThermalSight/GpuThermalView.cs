using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace ThermalSight;








internal sealed class GpuThermalView : IThermalView
{
    public const int ProxyLayer = 31;

    public bool Active { get; private set; }
    public int ActiveGen { get; private set; }
    public Camera Camera => _cam;
    internal bool ExternalOutput;
    internal Texture Output=>_rtOut;

    private Camera _cam;
    private HDAdditionalCameraData _hd;
    private int _origCullingMask;
    private HDAdditionalCameraData.AntialiasingMode _origAA;
    private GenerationProfile _profile;

    private RenderTexture _rtScene, _rtTmp, _rtOptics, _rtSensorA, _rtSensorB, _rtOut;
    private bool _sensorFlip;
    private Texture2D _fpn, _lut;
    private Material _radianceMat, _sensorMat;
    private GameObject _passGo, _volumeGo;
    private CustomPassVolume _heatVolume, _radianceVolume;

    private GameObject _canvasGo;
    private RawImage _image;
    private TMPro.TextMeshProUGUI _zoomText;
    private bool _zoomTextFailed;

    private float _nextProcess, _nucUntil, _nextNuc, _snapshotAt = -1, _nextEnableWarn;
    private Quaternion _lastRot;
    private float _slew;
    private int _frame;
    private bool _outputShown;
    private readonly System.Random _rng = new();

    
    private AsyncGPUReadbackRequest _statsReq;
    private bool _statsPending;
    private int _statsFrame;
    private float _agcLo = 0.3f, _agcHi = 0.9f, _levelGamma = 1f, _levelOffset;
    private bool _agcInit;
    private readonly int[] _hist = new int[512];
    private readonly int[] _eqHist = new int[256];
    private readonly float[] _lutValues = new float[256];
    private readonly Color[] _lutPixels = new Color[256];

    private readonly System.Collections.Generic.List<(HDAdditionalLightData light, float dimmer, float volumetric)> _sunShadows = new();

    public void Enable(Camera cam, GenerationProfile profile, int seed)
    {
        if (Active) Disable();
        if (cam == null || !GpuResources.Available)
        {
            if (Time.unscaledTime > _nextEnableWarn)
            {
                _nextEnableWarn = Time.unscaledTime + 5f;
                Plugin.Log.LogWarning($"GPU thermal view cannot start: camera={(cam == null ? "null" : cam.name)} shaders {GpuResources.Describe()}");
            }
            return;
        }

        _cam = cam;
        _profile = profile;
        ActiveGen = profile.Gen;

        var aspect = ExternalOutput ? _cam.aspect : OutputAspect();
        var h = ExternalOutput ? 256 : Math.Max(120, (int)(profile.SensorLines * Plugin.ResolutionScale.Value));
        var w = Math.Max(160, (int)Math.Round(h * aspect));
        w -= w % 2;

        _rtScene = NewRT(w, h, 24, RenderTextureFormat.ARGBHalf, "ThermalScene");
        _rtTmp = NewRT(w, h, 0, RenderTextureFormat.ARGBHalf, "ThermalTmp");
        _rtOptics = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { name = "ThermalOptics", useMipMap = true, autoGenerateMips = true };
        _rtOptics.Create();
        _rtSensorA = NewRT(w, h, 0, RenderTextureFormat.ARGBHalf, "ThermalSensorA");
        _rtSensorB = NewRT(w, h, 0, RenderTextureFormat.ARGBHalf, "ThermalSensorB");
        _rtOut = NewRT(w, h, 0, RenderTextureFormat.ARGB32, "ThermalOut");
        _rtOut.filterMode = FilterMode.Bilinear;

        _fpn = BuildFixedPattern(w, h, seed, profile);
        _lut = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (var i = 0; i < 256; i++) _lutValues[i] = i / 255f;
        UploadLut();

        _radianceMat = new Material(GpuResources.RadianceShader) { hideFlags = HideFlags.DontSave };
        _sensorMat = new Material(GpuResources.SensorShader) { hideFlags = HideFlags.DontSave };

        _hd = cam.GetComponent<HDAdditionalCameraData>();
        if (_hd != null)
        {
            _origAA = _hd.antialiasing;
            _hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
        _origCullingMask = cam.cullingMask;
        cam.cullingMask &= ~(1 << ProxyLayer);

        if(!ExternalOutput)DisableSunShadows();
        EnableEffectOverrides();
        CreatePasses();

        cam.targetTexture = _rtScene;

        EnsureCanvas();
        _image.texture = _rtOut;
        _image.color = Color.black;
        _canvasGo.SetActive(!ExternalOutput);
        SetDigitalZoom(1f);

        _frame = 0;
        _agcInit = false;
        _outputShown = false;
        _lastRot = cam.transform.rotation;
        _nextProcess = 0;
        _nucUntil = 0;
        _nextNuc = profile.NucInterval > 0 ? Time.unscaledTime + profile.NucInterval * (0.3f + 0.7f * (float)_rng.NextDouble()) : float.MaxValue;
        _snapshotAt = Plugin.DebugSnapshots.Value ? Time.unscaledTime + 2.5f : -1;

        Active = true;
        ThermalView.AnyActive = true;
        Plugin.Log.LogInfo($"GPU thermal view on: Gen {profile.Gen}, sensor {w}x{h}");
    }

    public void Disable()
    {
        if (!Active) return;
        Active = false;
        ThermalView.AnyActive = false;

        try { if (_cam != null) { _cam.targetTexture = null; _cam.cullingMask = _origCullingMask; } } catch { }
        try { if (_hd != null) _hd.antialiasing = _origAA; } catch { }
        RestoreSunShadows();
        RestoreEffects();
        try { if (_volumeGo != null) _volumeGo.SetActive(false); } catch { }
        
        if (_passGo != null) { _passGo.SetActive(false); UnityEngine.Object.Destroy(_passGo); _passGo = null; }
        _heatVolume = _radianceVolume = null;
        if (_canvasGo != null) _canvasGo.SetActive(false);

        if (_statsPending && !_statsReq.done) _statsReq.WaitForCompletion();
        _statsPending = false;

        foreach (var rt in new[] { _rtScene, _rtTmp, _rtOptics, _rtSensorA, _rtSensorB, _rtOut })
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        _rtScene = _rtTmp = _rtOptics = _rtSensorA = _rtSensorB = _rtOut = null;
        if (_fpn != null) UnityEngine.Object.Destroy(_fpn);
        if (_lut != null) UnityEngine.Object.Destroy(_lut);
        if (_radianceMat != null) UnityEngine.Object.Destroy(_radianceMat);
        if (_sensorMat != null) UnityEngine.Object.Destroy(_sensorMat);
        _statsPending = false;
        ReleaseDamage();
        if(ExternalOutput)
        {
            if(_canvasGo!=null)UnityEngine.Object.Destroy(_canvasGo);_canvasGo=null;_image=null;_damageImage=null;
            if(_volumeGo!=null){var volume=_volumeGo.GetComponent<Volume>();if(volume!=null && volume.sharedProfile!=null)UnityEngine.Object.Destroy(volume.sharedProfile);UnityEngine.Object.Destroy(_volumeGo);}_volumeGo=null;
        }
        _cam = null; _hd = null;
        Plugin.Log.LogInfo("GPU thermal view off");
    }

    public void Update()
    {
        if (!Active) return;
        Process();
        if (Active) Present();
    }

    private void Process()
    {
        if (_cam == null || _rtScene == null) { Disable(); return; }
        if (_cam.targetTexture != _rtScene) _cam.targetTexture = _rtScene;
        if(!ExternalOutput)LayoutCanvas();

        var now = Time.unscaledTime;
        var dt = Math.Max(Time.unscaledDeltaTime, 1e-4f);
        var rot = _cam.transform.rotation;
        _slew += (Quaternion.Angle(_lastRot, rot) / dt - _slew) * Math.Min(1f, dt * 10f);
        _lastRot = rot;

        UpdateRadianceParams();
        UpdateStats();
        if (!ExternalOutput && now > _nextFxScan) { _nextFxScan = now + 0.25f; ScanEffects(now); }

        
        var p = _profile;
        _sensorMat.SetFloat("_SigmaX", Math.Max(p.OpticalBlur, p.HorizontalSmear));
        _sensorMat.SetFloat("_SigmaY", p.OpticalBlur);
        Graphics.Blit(_rtScene, _rtTmp, _sensorMat, 0);
        Graphics.Blit(_rtTmp, _rtOptics, _sensorMat, 1);

        
        if (now >= _nextNuc)
        {
            _nucUntil = now + p.NucDuration;
            _nextNuc = now + p.NucInterval * (0.8f + 0.4f * (float)_rng.NextDouble());
        }
        if (now < _nucUntil) return;
        
        if (!ExternalOutput && _outputShown && OpticsDamage.Current != null && OpticsDamage.Current.Frozen(now)) return;
        
        if (ExternalOutput || Plugin.RealisticFrameRate.Value)
        {
            if (now < _nextProcess) return;
            
            _nextProcess = Math.Max(_nextProcess + 1f / p.FrameRate, now - 0.5f / p.FrameRate);
        }

        _frame++;
        var range = Math.Max(_agcHi - _agcLo, 0.2f);
        var lo = _agcHi - _agcLo < 0.2f ? (_agcLo + _agcHi) * 0.5f - 0.1f : _agcLo;
        var inv = 1f / range;

        var m = _sensorMat;
        m.SetFloat("_Frame", _frame % 10000);
        m.SetFloat("_Time2", now);
        m.SetFloat("_Noise", p.TemporalNoise * 2f);
        m.SetFloat("_FpnAmp", p.FixedPattern * 2f);
        m.SetFloat("_ColumnAmp", p.ColumnPattern * 2f);
        m.SetFloat("_Banding", p.ScanBanding * 2f);
        m.SetFloat("_BandDrift", p.BandingDrift);
        m.SetFloat("_Denoise", p.Denoise);
        m.SetFloat("_Tnr", _outputShown ? p.TemporalFilter : 0f);
        m.SetFloat("_Halo", p.Halo * 1.6f);
        m.SetFloat("_HaloMip", Mathf.Log(Math.Max(2f, _rtScene.height / 60f), 2f) + 1f);
        m.SetFloat("_HotThreshold", lo + 0.72f * range);
        var misreg = p.Misregistration * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 80f, _slew));
        m.SetFloat("_Misreg", misreg);
        m.SetFloat("_AgcLo", lo);
        m.SetFloat("_AgcInv", inv);
        m.SetFloat("_Eq", p.Equalization);
        m.SetFloat("_Lce", p.LocalContrast);
        
        m.SetFloat("_LceMip", Mathf.Log(Math.Max(2f, _rtScene.height / 80f), 2f));
        m.SetFloat("_Vignette", p.Vignette);
        m.SetFloat("_Gamma", p.Gamma);
        m.SetFloat("_Level", 1f);
        m.SetFloat("_LevelOffset", _levelOffset);
        m.SetTexture("_Optics", _rtOptics);
        m.SetVector("_OpticsSize", new Vector4(_rtOptics.width, _rtOptics.height, 1f / _rtOptics.width, 1f / _rtOptics.height));
        m.SetTexture("_Fpn", _fpn);
        m.SetTexture("_Lut", _lut);

        var cur = _sensorFlip ? _rtSensorA : _rtSensorB;
        var prev = _sensorFlip ? _rtSensorB : _rtSensorA;
        _sensorFlip = !_sensorFlip;
        m.SetTexture("_History", prev);
        Graphics.Blit(_rtOptics, cur, m, 2);
        Graphics.Blit(cur, _rtOut, m, 3);

        if (!_outputShown && _frame > 2) { _image.color = Color.white; _outputShown = true; }

        if (_snapshotAt > 0 && now > _snapshotAt && _outputShown)
        {
            _snapshotAt = -1;
            SaveSnapshot("auto");
        }
    }

    
    
    
    

    public bool DamageShader { get; private set; }
    private Material _damageMat;
    private RenderTexture _rtDamage;
    private RawImage _damageImage;
    private int _damageErrors;

    private void Present()
    {
        DamageShader = false;
        var dmg = ExternalOutput ? null : OpticsDamage.Current;
        if (dmg == null || !_outputShown || GpuResources.DisplayDamageShader == null || _image == null || _rtOut == null ||
            !ImageMapping.ScreenRect(_image, out var rect))
        {
            if (_damageImage != null && _damageImage.enabled) _damageImage.enabled = false;
            return;
        }
        try
        {
            var w = Math.Clamp(Mathf.RoundToInt(rect.width), 16, 4096);
            var h = Math.Clamp(Mathf.RoundToInt(rect.height), 16, 4096);
            if (_rtDamage == null || _rtDamage.width != w || _rtDamage.height != h)
            {
                if (_rtDamage != null) { _rtDamage.Release(); UnityEngine.Object.Destroy(_rtDamage); }
                _rtDamage = NewRT(w, h, 0, RenderTextureFormat.ARGB32, "ThermalDamage");
                _rtDamage.filterMode = FilterMode.Point;
            }
            if (_damageMat == null) _damageMat = new Material(GpuResources.DisplayDamageShader) { hideFlags = HideFlags.DontSave };
            if (_damageImage == null)
            {
                var go = new GameObject("ThermalDamage");
                go.transform.SetParent(_image.transform, false);
                _damageImage = go.AddComponent<RawImage>();
                _damageImage.raycastTarget = false;
                var rt = _damageImage.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            dmg.Prepare(w, h, _rtOut.width, _rtOut.height);
            dmg.Apply(_damageMat, _image.uvRect, Time.unscaledTime, Time.frameCount);
            Graphics.Blit(_rtOut, _rtDamage, _damageMat, 0);
            if (_damageImage.texture == null || _damageImage.texture.Pointer != _rtDamage.Pointer) _damageImage.texture = _rtDamage;
            if (!_damageImage.enabled) _damageImage.enabled = true;
            DamageShader = true;
        }
        catch (Exception e)
        {
            if (_damageErrors++ < 3) Plugin.Log.LogWarning("Thermal sight damage view failed: " + e.Message);
            if (_damageImage != null) _damageImage.enabled = false;
        }
    }

    private void ReleaseDamage()
    {
        DamageShader = false;
        if (_damageImage != null) _damageImage.enabled = false;
        if (_rtDamage != null) { _rtDamage.Release(); UnityEngine.Object.Destroy(_rtDamage); }
        _rtDamage = null;
        if (_damageMat != null) UnityEngine.Object.Destroy(_damageMat);
        _damageMat = null;
    }

    

    private void UpdateRadianceParams()
    {
        var sunDir = Sun.Direction;
        var m = _radianceMat;
        m.SetVector("_SunDir", sunDir);
        m.SetFloat("_SunIntensity", Mathf.Clamp01(sunDir.y * 3f));
        m.SetFloat("_AirTemp", 0.45f);
        m.SetFloat("_SkyZenithTemp", 0.08f);
        m.SetFloat("_HorizonTemp", 0.40f);
        m.SetFloat("_Visibility", Plugin.Visibility.Value);
        m.SetFloat("_SolarSoil", 0.10f);
        m.SetFloat("_SolarNeutral", 0.075f);
        m.SetFloat("_SolarVeg", 0.02f);
        m.SetFloat("_SolarVehicle", 0.10f);
        m.SetFloat("_FireGain", 0.3f); 
        m.SetFloat("_VehicleScale", Plugin.HeatOverlayScale.Value);
    }

    
    
    

    private float _nextFxScan, _nextParticleScan;
    private readonly System.Collections.Generic.List<Renderer> _hiddenFx = new();
    private System.Collections.Generic.HashSet<IntPtr> _seenFx = new(), _currentFx = new();

    
    private void ScanEffects(float now)
    {
        try
        {
            _currentFx.Clear();
            foreach (var vfx in UnityEngine.Object.FindObjectsOfType<UnityEngine.VFX.VisualEffect>())
            {
                if (vfx == null) continue;
                var ptr = vfx.Pointer;
                _currentFx.Add(ptr);
                if (_seenFx.Contains(ptr)) continue;
                var asset = vfx.visualEffectAsset != null ? vfx.visualEffectAsset.name : "";
                OnEffectActivated(vfx, (vfx.gameObject.name + "/" + asset).ToLowerInvariant());
                var r = vfx.GetComponent<Renderer>();
                if (r != null && r.enabled) { r.enabled = false; _hiddenFx.Add(r); }
            }
            (_seenFx, _currentFx) = (_currentFx, _seenFx);

            
            if (now >= _nextParticleScan)
            {
                _nextParticleScan = now + 1f;
                foreach (var ps in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>())
                    if (ps != null && ps.enabled) { ps.enabled = false; _hiddenFx.Add(ps); }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Effect scan failed: " + e.Message);
            _nextFxScan = float.MaxValue;
        }
    }

    private static void OnEffectActivated(UnityEngine.VFX.VisualEffect vfx, string name)
    {
        var fx = ThermalEffects.Instance;
        if (fx == null) return;
        var now = Time.unscaledTime;
        var pos = vfx.transform.position;
        if (name.Contains("cannonfire"))
        {
            
            if (now - fx.LastMuzzleTime > 0.3f || Vector3.Distance(fx.LastMuzzlePos, pos) > 4f)
            {
                var dir = HeatModel.Instance != null && HeatModel.Instance.TryNearestCannonDirection(pos, out var d) ? d : vfx.transform.forward;
                fx.OnCannonFired(pos, dir, 1f);
            }
        }
        else if (name.Contains("impact") || name.Contains("shell"))
        {
            
            if (now - fx.LastImpactTime > 0.3f || Vector3.Distance(fx.LastImpactPos, pos) > 1.5f)
            {
                Sprocket.DamageModelling.ProjectileEffectType type;
                if (name.Contains("nonpenetration")) type = Sprocket.DamageModelling.ProjectileEffectType.NonPenetration;
                else if (name.Contains("penetration")) type = Sprocket.DamageModelling.ProjectileEffectType.Penetration;
                else if (name.Contains("ricochet")) type = Sprocket.DamageModelling.ProjectileEffectType.Ricochet;
                else if (name.Contains("explosion")) type = Sprocket.DamageModelling.ProjectileEffectType.Explosion;
                else type = Sprocket.DamageModelling.ProjectileEffectType.EnvironmentImpact;
                fx.OnImpact(type, pos, -vfx.transform.forward, Vector3.zero, vfx.transform.parent, 100f);
            }
        }
        else if (name.Contains("exhaust")) fx.AddFireEmitter(vfx, vfx.transform, 1);
        else if ((name.Contains("flame") || name.Contains("fire")) && !name.Contains("cannonfire")) fx.AddFireEmitter(vfx, vfx.transform, 0);
        else if (name.Contains("turretpop") || (name.Contains("explosion") && !name.Contains("impact"))) fx.OnBigExplosion(vfx.transform.position);
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo("Thermal effect source: " + name);
    }

    private void RestoreEffects()
    {
        foreach (var r in _hiddenFx)
            try { if (r != null) r.enabled = true; } catch { }
        _hiddenFx.Clear();
        _seenFx.Clear();
    }

    

    private void CreatePasses()
    {
        _passGo = new GameObject("ThermalSightPasses");
        UnityEngine.Object.DontDestroyOnLoad(_passGo);
        SetVolumeLayer(_passGo);

        
        _heatVolume = _passGo.AddComponent<CustomPassVolume>();
        _heatVolume.isGlobal = true;
        _heatVolume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
        _heatVolume.useTargetCamera = true;
        _heatVolume.targetCamera = _cam;
        var draw = new DrawRenderersCustomPass
        {
            name = "ThermalVehicleHeat",
            layerMask = 1 << ProxyLayer,
            renderQueueType = CustomPass.RenderQueueType.All,
            targetColorBuffer = CustomPass.TargetBuffer.Custom,
            targetDepthBuffer = CustomPass.TargetBuffer.Camera,
            clearFlags = ClearFlag.Color,
            overrideMode = DrawRenderersCustomPass.OverrideMaterialMode.None,
            enabled = true
        };
        _heatVolume.customPasses.Add(draw);

        
        _radianceVolume = _passGo.AddComponent<CustomPassVolume>();
        _radianceVolume.isGlobal = true;
        _radianceVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
        _radianceVolume.useTargetCamera = true;
        _radianceVolume.targetCamera = _cam;
        var radiance = new FullScreenCustomPass
        {
            name = "ThermalRadiance",
            fullscreenPassMaterial = _radianceMat,
            materialPassName = "Custom Pass 0",
            fetchColorBuffer = true,
            enabled = true
        };
        _radianceVolume.customPasses.Add(radiance);
    }

    private void SetVolumeLayer(GameObject go)
    {
        if(ExternalOutput){go.layer=30;return;}
        if (_hd == null) return;
        var mask = _hd.volumeLayerMask.value;
        for (var i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) { go.layer = i; return; }
    }

    
    private void EnableEffectOverrides()
    {
        try
        {
            if (_volumeGo == null)
            {
                _volumeGo = new GameObject("ThermalSightGpuVolume");
                UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
                var volume = _volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000f;
                volume.weight = 1f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                volume.sharedProfile = profile;
                T Add<T>() where T : VolumeComponent => profile.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), false).TryCast<T>();
                Add<MotionBlur>().intensity.Override(0f);
                Add<DepthOfField>().focusMode.Override(DepthOfFieldMode.Off);
                Add<Bloom>().intensity.Override(0f);
                Add<Vignette>().intensity.Override(0f);
                Add<ChromaticAberration>().intensity.Override(0f);
                Add<FilmGrain>().intensity.Override(0f);
                Add<Tonemapping>().mode.Override(TonemappingMode.None);
                var ca = Add<ColorAdjustments>();
                ca.postExposure.Override(0f); ca.contrast.Override(0f); ca.saturation.Override(0f);
                ca.hueShift.Override(0f); ca.colorFilter.Override(Color.white);
                var wb = Add<WhiteBalance>();
                wb.temperature.Override(0f); wb.tint.Override(0f);
                profile.isDirty = true;
            }
            SetVolumeLayer(_volumeGo);
            _volumeGo.SetActive(true);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Effect overrides failed: " + e.Message); }
    }

    
    private void DisableSunShadows()
    {
        try
        {
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l == null || l.type != LightType.Directional) continue;
                var hdl = l.GetComponent<HDAdditionalLightData>();
                if (hdl == null) continue;
                _sunShadows.Add((hdl, hdl.shadowDimmer, hdl.volumetricShadowDimmer));
                hdl.shadowDimmer = 0f;
                hdl.volumetricShadowDimmer = 0f;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Sun shadows: " + e.Message); }
    }

    private void RestoreSunShadows()
    {
        foreach (var (l, dimmer, volumetric) in _sunShadows)
            try { if (l != null) { l.shadowDimmer = dimmer; l.volumetricShadowDimmer = volumetric; } } catch { }
        _sunShadows.Clear();
    }

    

    private void UpdateStats()
    {
        if (_statsPending && _statsReq.done)
        {
            _statsPending = false;
            if (!_statsReq.hasError)
            {
                try { ProcessStats(); }
                catch (Exception e) { Plugin.Log.LogWarning("AGC stats failed: " + e.Message); }
            }
        }
        if (!_statsPending && ++_statsFrame % 3 == 0)
        {
            _statsReq = AsyncGPUReadback.Request(_rtOptics, 2, TextureFormat.RGBAHalf);
            _statsPending = true;
        }
    }

    private unsafe void ProcessStats()
    {
        var data = _statsReq.GetData<byte>(0);
        var ptr = (ushort*)data.m_Buffer;
        var count = data.m_Length / 8;
        int w = Math.Max(1, _rtOptics.width >> 2), h = Math.Max(1, _rtOptics.height >> 2);
        if (count < w * h) return;

        Array.Clear(_hist, 0, _hist.Length);
        const float min = -0.2f, max = 2.0f;
        var scale = (_hist.Length - 1) / (max - min);
        var n = 0;
        for (var y = h / 20; y < h - h / 20; y++)
        for (var x = w / 20; x < w - w / 20; x++)
        {
            var v = (float)BitConverter.Int16BitsToHalf((short)ptr[(y * w + x) * 4]);
            var b = (int)((v - min) * scale);
            b = b < 0 ? 0 : b >= _hist.Length ? _hist.Length - 1 : b;
            _hist[b]++;
            n++;
        }
        if (n == 0) return;

        int loT = (int)(n * 0.01f), hiT = (int)(n * 0.9995f), medT = n / 2;
        int acc = 0, loB = 0, hiB = _hist.Length - 1, medB = 0;
        bool loF = false, medF = false;
        for (var i = 0; i < _hist.Length; i++)
        {
            acc += _hist[i];
            if (!loF && acc >= loT) { loB = i; loF = true; }
            if (!medF && acc >= medT) { medB = i; medF = true; }
            if (acc >= hiT) { hiB = i; break; }
        }
        var lo = min + loB / scale;
        var hi = min + (hiB + 1) / scale;
        var med = min + medB / scale;

        var dtStats = 3f / Math.Max(10f, 1f / Math.Max(Time.unscaledDeltaTime, 1e-3f));
        var t = 1f - MathF.Exp(-dtStats * _profile.AgcSpeed * 3f);
        if (!_agcInit) { _agcLo = lo; _agcHi = hi; _agcInit = true; t = 1f; }
        else { _agcLo += (lo - _agcLo) * t; _agcHi += (hi - _agcHi) * t; }

        
        var range = Math.Max(_agcHi - _agcLo, 0.2f);
        var loEff = _agcHi - _agcLo < 0.2f ? (_agcLo + _agcHi) * 0.5f - 0.1f : _agcLo;
        var medNorm = Math.Clamp((med - loEff) / range, 0.05f, 0.95f);
        
        var targetOffset = Math.Clamp(0.33f - medNorm, -0.25f, 0.2f); 
        _levelOffset += (targetOffset - _levelOffset) * t;

        
        if (_profile.Equalization > 0.01f)
        {
            var eqHist = _eqHist;
            Array.Clear(eqHist, 0, eqHist.Length);
            var total = 0;
            for (var y = h / 20; y < h - h / 20; y++)
            for (var x = w / 20; x < w - w / 20; x++)
            {
                var v = ((float)BitConverter.Int16BitsToHalf((short)ptr[(y * w + x) * 4]) - loEff) / range;
                var b = (int)(Math.Clamp(v, 0f, 1f) * 255f);
                eqHist[b]++;
                total++;
            }
            var plateau = Math.Max(1, (int)(total / 256f * 2f));
            long clipped = 0;
            for (var i = 0; i < 256; i++) clipped += Math.Min(eqHist[i], plateau);
            long a2 = 0;
            for (var i = 0; i < 256; i++)
            {
                a2 += Math.Min(eqHist[i], plateau);
                var target = a2 / (float)clipped;
                _lutValues[i] += (target - _lutValues[i]) * t;
            }
            UploadLut();
        }
    }

    private void UploadLut()
    {
        for (var i = 0; i < 256; i++) { var v = _lutValues[i]; _lutPixels[i] = new Color(v, v, v, 1f); }
        _lut.SetPixels(_lutPixels);
        _lut.Apply(false);
    }

    

    private static Texture2D BuildFixedPattern(int w, int h, int seed, GenerationProfile p)
    {
        var rng = new System.Random(seed);
        var n = w * h;
        var coarse = new float[n];
        for (var i = 0; i < n; i++) coarse[i] = (float)(rng.NextDouble() * 2 - 1);
        
        var tmp = new float[n];
        var r = Math.Max(2, h / 40);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float s = 0; var c = 0;
                for (var k = -r; k <= r; k += Math.Max(1, r / 3)) { var xx = Math.Clamp(x + k, 0, w - 1); s += coarse[y * w + xx]; c++; }
                tmp[y * w + x] = s / c;
            }
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float s = 0; var c = 0;
                for (var k = -r; k <= r; k += Math.Max(1, r / 3)) { var yy = Math.Clamp(y + k, 0, h - 1); s += tmp[yy * w + x]; c++; }
                coarse[y * w + x] = s / c;
            }
        }
        var norm = 1e-4f;
        for (var i = 0; i < n; i++) norm = Math.Max(norm, Math.Abs(coarse[i]));

        var cols = new float[w];
        for (var x = 0; x < w; x++) cols[x] = (float)(rng.NextDouble() * 2 - 1);
        var rows = new float[h];
        for (var y = 0; y < h; y++) rows[y] = (float)rng.NextDouble();

        var dead = new System.Collections.Generic.Dictionary<int, byte>();
        for (var i = 0; i < p.DeadPixels; i++)
        {
            int x, y;
            do { x = rng.Next(w); y = rng.Next(h); }
            while (Math.Abs(x - w / 2) < w / 12 && Math.Abs(y - h / 2) < h / 12);
            dead[y * w + x] = (byte)(rng.NextDouble() < 0.6 ? 255 : 0);
        }

        var px = new Color32[n];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * w + x;
            var f = coarse[i] / norm * 0.8f + (float)(rng.NextDouble() * 2 - 1) * 0.35f;
            px[i] = new Color32(
                (byte)Math.Clamp((f * 0.5f + 0.5f) * 255f, 0, 255),
                dead.TryGetValue(i, out var d) ? d : (byte)128,
                (byte)Math.Clamp((cols[x] * 0.5f + 0.5f) * 255f, 0, 255),
                (byte)(rows[y] * 255f));
        }
        
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(px);
        tex.Apply(false);
        return tex;
    }

    

    private static RenderTexture NewRT(int w, int h, int depth, RenderTextureFormat f, string name)
    {
        var rt = new RenderTexture(w, h, depth, f) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    public bool CameraToScreen(Vector2 cameraViewport, out Vector2 screenViewport) =>
        ImageMapping.CameraToScreen(Active ? _image : null, cameraViewport, out screenViewport);

    public bool ScreenRect(out Rect rect) => ImageMapping.ScreenRect(Active && !ExternalOutput ? _image : null, out rect);

    public void SetDigitalZoom(float zoom)
    {
        if (_image == null) return;
        zoom = Math.Max(1f, zoom);
        var size = 1f / zoom;
        _image.uvRect = new Rect(0.5f - size * 0.5f, 0.5f - size * 0.5f, size, size);
    }

    public void SetZoomText(string text)
    {
        if (_canvasGo == null) return;
        if (_zoomText == null && !_zoomTextFailed)
        {
            try
            {
                var go = new GameObject("ZoomText");
                go.transform.SetParent(_canvasGo.transform, false);
                _zoomText = go.AddComponent<TMPro.TextMeshProUGUI>();
                var font = TMPro.TMP_Settings.defaultFontAsset;
                if (font == null)
                    foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
                if (font != null) _zoomText.font = font;
                _zoomText.fontSize = 22;
                _zoomText.color = new Color(0.92f, 0.92f, 0.92f, 0.9f);
                _zoomText.alignment = TMPro.TextAlignmentOptions.TopRight;
                _zoomText.raycastTarget = false;
                var rt = _zoomText.rectTransform;
                rt.sizeDelta = new Vector2(460, 60);
                _lastScreen = Vector2.zero; 
            }
            catch (Exception e)
            {
                _zoomTextFailed = true;
                Plugin.Log.LogWarning("Zoom indicator unavailable: " + e.Message);
            }
        }
        if (_zoomText != null) _zoomText.text = Plugin.HudZoomText.Value ? text : "";
    }

    private RawImage _frameImage;
    private Image _background;
    private Vector2 _lastScreen;
    private SightDisplay _display;

    public void SetStatus(in SightStatus status) => _display?.Apply(status);

    private void EnsureCanvas()
    {
        if (_canvasGo != null) return;
        _canvasGo = new GameObject("ThermalSightGpuCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Plugin.CanvasSortingOrder.Value;

        
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(_canvasGo.transform, false);
        _background = bgGo.AddComponent<Image>();
        _background.color = Color.black;
        _background.raycastTarget = false;
        var bgRt = _background.rectTransform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

        var imgGo = new GameObject("ThermalImage");
        imgGo.transform.SetParent(_canvasGo.transform, false);
        _image = imgGo.AddComponent<RawImage>();
        _image.raycastTarget = false;

        
        if (GpuResources.SightFrame != null)
        {
            var frameGo = new GameObject("SightFrame");
            frameGo.transform.SetParent(_canvasGo.transform, false);
            _frameImage = frameGo.AddComponent<RawImage>();
            _frameImage.texture = GpuResources.SightFrame;
            _frameImage.raycastTarget = false;
            try { _display = SightDisplay.Create(_frameImage.rectTransform); }
            catch (Exception e) { Plugin.Log.LogWarning("Sight labels unavailable: " + e.Message); }
        }
        _lastScreen = Vector2.zero;
        if(!ExternalOutput)LayoutCanvas();
        _canvasGo.SetActive(false);
    }

    
    private void LayoutCanvas()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        if (screen == _lastScreen || _image == null) return;
        _lastScreen = screen;

        var imgRt = _image.rectTransform;
        imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
        imgRt.pivot = new Vector2(0.5f, 0.5f);

        if (_frameImage == null)
        {
            imgRt.sizeDelta = screen;
            imgRt.anchoredPosition = Vector2.zero;
            return;
        }

        var frameH = screen.y;
        var frameW = frameH * GpuResources.FrameAspect;
        var frRt = _frameImage.rectTransform;
        frRt.anchorMin = frRt.anchorMax = new Vector2(0.5f, 0.5f);
        frRt.pivot = new Vector2(0.5f, 0.5f);
        frRt.sizeDelta = new Vector2(frameW, frameH);
        frRt.anchoredPosition = Vector2.zero;

        var winW = frameW * (GpuResources.WindowRight - GpuResources.WindowLeft);
        var winH = frameH * (GpuResources.WindowTop - GpuResources.WindowBottom);
        var winCx = frameW * ((GpuResources.WindowLeft + GpuResources.WindowRight) * 0.5f - 0.5f);
        var winCy = frameH * ((GpuResources.WindowBottom + GpuResources.WindowTop) * 0.5f - 0.5f);
        imgRt.sizeDelta = new Vector2(winW, winH);
        imgRt.anchoredPosition = new Vector2(winCx, winCy);

        if (_zoomText != null)
        {
            var zt = _zoomText.rectTransform;
            zt.anchorMin = zt.anchorMax = new Vector2(0.5f, 0.5f);
            zt.pivot = new Vector2(1f, 1f);
            zt.anchoredPosition = new Vector2(winCx + winW * 0.5f - 24f, winCy + winH * 0.5f - 16f);
        }
    }

    
    private static float OutputAspect()
    {
        if (GpuResources.SightFrame == null) return Math.Max(1f, (float)Screen.width / Math.Max(1, Screen.height));
        return GpuResources.FrameAspect * (GpuResources.WindowRight - GpuResources.WindowLeft) /
               (GpuResources.WindowTop - GpuResources.WindowBottom);
    }

    public void SaveSnapshot(string tag)
    {
        if (!Active || _rtOut == null) return;
        try
        {
            var dir = Path.Combine(Plugin.PluginDir, "debug");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("HHmmss");
            SaveRT(_rtOut, Path.Combine(dir, $"{stamp}_{tag}_gen{_profile.Gen}_thermal.bmp"), 1f);
            SaveRT(_rtScene, Path.Combine(dir, $"{stamp}_{tag}_gen{_profile.Gen}_radiance.bmp"), 0.6f);
            if (Plugin.DebugLogging.Value)
            {
                SaveRT(_rtTmp, Path.Combine(dir, $"{stamp}_{tag}_step1_blurH.bmp"), 1f);
                SaveRT(_rtOptics, Path.Combine(dir, $"{stamp}_{tag}_step2_optics.bmp"), 1f);
                SaveRT(_sensorFlip ? _rtSensorB : _rtSensorA, Path.Combine(dir, $"{stamp}_{tag}_step3_sensor.bmp"), 1f);
            }
            Plugin.Log.LogInfo($"Snapshot saved to {dir} (AGC {_agcLo:0.000}..{_agcHi:0.000}, level offset {_levelOffset:0.00})");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Snapshot failed: " + e.Message); }
    }

    private static void SaveRT(RenderTexture rt, string path, float scale)
    {
        var tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(rt, tmp);
            RenderTexture.active = tmp;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            var px = tex.GetPixels32();
            var bytes = new byte[px.Length * 4];
            for (var i = 0; i < px.Length; i++)
            {
                bytes[i * 4] = px[i].r; bytes[i * 4 + 1] = px[i].g; bytes[i * 4 + 2] = px[i].b; bytes[i * 4 + 3] = 255;
            }
            Bmp.Write(path, bytes, rt.width, rt.height);
            UnityEngine.Object.Destroy(tex);
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
        }
    }
}
