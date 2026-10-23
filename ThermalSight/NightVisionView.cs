using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace ThermalSight;






internal sealed class NightVisionView : IThermalView
{
    public bool Active { get; private set; }
    public int ActiveGen { get; private set; }
    public Camera Camera => _cam;

    private Camera _cam;
    private HDAdditionalCameraData _hd;
    private HDAdditionalCameraData.AntialiasingMode _origAA;
    private NightVisionProfile _p;
    private RenderTexture _rtScene, _rtLum, _rtA, _rtB, _rtOut;
    private bool _flip, _shown;
    private Material _mat;
    private GameObject _volumeGo, _canvasGo, _lightGo;
    private VolumeProfile _volumeProfile;
    private RawImage _image;
    private TMPro.TextMeshProUGUI _text;
    private float _nextFrame, _nextWarn;
    private int _frame;
    private Vector2 _lastScreen;

    public void Enable(Camera cam, GenerationProfile profile, int seed) => Enable(cam, NightVisionProfile.Get(profile.Gen), seed);

    public void Enable(Camera cam, NightVisionProfile p, int seed)
    {
        if (Active) Disable();
        GpuResources.TryLoad();
        if (cam == null || GpuResources.NightVisionShader == null)
        {
            if (Time.unscaledTime > _nextWarn)
            {
                _nextWarn = Time.unscaledTime + 5f;
                Plugin.Log.LogWarning($"Night vision cannot start: camera={(cam == null ? "null" : cam.name)}, shader {(GpuResources.NightVisionShader == null ? "missing (update thermalsight.bundle)" : "ok")}");
            }
            return;
        }
        _cam = cam;
        _p = p;
        ActiveGen = p.Gen;

        var size = Math.Max(160, (int)(p.Lines * Plugin.ResolutionScale.Value));
        size -= size % 2;
        _rtScene = NewRT(size, size, 24, RenderTextureFormat.ARGBHalf, "NightVisionScene", false);
        _rtLum = NewRT(size, size, 0, RenderTextureFormat.ARGBHalf, "NightVisionCathode", true);
        _rtA = NewRT(size, size, 0, RenderTextureFormat.ARGBHalf, "NightVisionTubeA", false);
        _rtB = NewRT(size, size, 0, RenderTextureFormat.ARGBHalf, "NightVisionTubeB", false);
        _rtOut = NewRT(size, size, 0, RenderTextureFormat.ARGB32, "NightVisionOut", false);
        _mat = new Material(GpuResources.NightVisionShader) { hideFlags = HideFlags.DontSave };

        _hd = cam.GetComponent<HDAdditionalCameraData>();
        if (_hd != null)
        {
            _origAA = _hd.antialiasing;
            _hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }
        EnableVolume();
        if (p.Illuminator) EnableSearchlight();
        cam.targetTexture = _rtScene;
        cam.aspect = 1f;

        EnsureCanvas();
        _image.texture = _rtOut;
        _image.color = Color.black;
        _canvasGo.SetActive(true);
        SetDigitalZoom(1f);
        _frame = 0;
        _shown = false;
        _nextFrame = 0f;
        Active = true;
        Plugin.Log.LogInfo($"Night vision view on: {p.Label}, {size}x{size}, {p.FrameRate:0} Hz, range {p.Range:0} m{(p.Illuminator ? ", IR searchlight" : "")}");
    }

    public void Disable()
    {
        if (!Active) return;
        Active = false;
        try { if (_cam != null) { _cam.targetTexture = null; _cam.ResetAspect(); } } catch { }
        try { if (_hd != null) _hd.antialiasing = _origAA; } catch { }
        if (_volumeGo != null) { _volumeGo.SetActive(false); UnityEngine.Object.Destroy(_volumeGo); _volumeGo = null; }
        if (_volumeProfile != null) { UnityEngine.Object.Destroy(_volumeProfile); _volumeProfile = null; }
        if (_lightGo != null) { _lightGo.SetActive(false); UnityEngine.Object.Destroy(_lightGo); _lightGo = null; }
        if (_canvasGo != null) _canvasGo.SetActive(false);
        foreach (var rt in new[] { _rtScene, _rtLum, _rtA, _rtB, _rtOut })
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        _rtScene = _rtLum = _rtA = _rtB = _rtOut = null;
        if (_mat != null) UnityEngine.Object.Destroy(_mat);
        _mat = null;
        _cam = null;
        _hd = null;
        Plugin.Log.LogInfo("Night vision view off");
    }

    public void Update()
    {
        if (!Active) return;
        if (_cam == null || _rtScene == null) { Disable(); return; }
        if (_cam.targetTexture != _rtScene) _cam.targetTexture = _rtScene;
        if (Math.Abs(_cam.aspect - 1f) > 1e-3f) _cam.aspect = 1f;
        LayoutCanvas();

        
        var now = Time.unscaledTime;
        if (now < _nextFrame) return;
        var fps = Math.Max(5f, _p.FrameRate);
        _nextFrame = Math.Max(_nextFrame + 1f / fps, now - 0.5f / fps);
        _frame++;

        var m = _mat;
        m.SetFloat("_NirBoost", _p.NirBoost);
        Graphics.Blit(_rtScene, _rtLum, m, 0);

        var top = Mathf.Log(Math.Max(_rtLum.width, _rtLum.height), 2f);
        m.SetFloat("_TopMip", top);
        m.SetFloat("_BlurMip", _p.Blur);
        m.SetFloat("_HaloMip", Math.Max(2f, top - 3.5f));
        m.SetFloat("_Halo", _p.Halo);
        m.SetFloat("_Target", 0.32f);
        m.SetFloat("_MaxGain", Mathf.Pow(2f, _p.SensitivityEv));
        m.SetFloat("_Noise", _p.Noise);
        m.SetFloat("_Persist", _shown ? _p.Persistence : 0f);
        m.SetFloat("_Frame", _frame % 10000);
        var cur = _flip ? _rtA : _rtB;
        var prev = _flip ? _rtB : _rtA;
        _flip = !_flip;
        m.SetTexture("_History", prev);
        Graphics.Blit(_rtLum, cur, m, 1);

        var ph = _p.Phosphor;
        m.SetVector("_Phosphor", new Vector4(ph.r, ph.g, ph.b, 1f));   
        m.SetFloat("_Floor", _p.FloorGlow);
        Graphics.Blit(cur, _rtOut, m, 2);

        if (!_shown && _frame > 2) { _image.color = Color.white; _shown = true; }
    }

    
    private void EnableVolume()
    {
        try
        {
            _volumeGo = new GameObject("NightVisionVolume");
            UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
            SetVolumeLayer(_volumeGo);
            var volume = _volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10500f;   
            volume.weight = 1f;
            _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = _volumeProfile;
            T Add<T>() where T : VolumeComponent => _volumeProfile.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), false).TryCast<T>();
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
            var fog = Add<Fog>();
            fog.enabled.Override(true);
            fog.colorMode.overrideState = true; fog.colorMode.value = FogColorMode.ConstantColor;
            fog.color.Override(Color.black);
            fog.meanFreePath.Override(Math.Max(50f, _p.Range * 0.5f));
            fog.baseHeight.Override(-200f);
            fog.maximumHeight.Override(3000f);
            fog.enableVolumetricFog.Override(false);
            _volumeProfile.isDirty = true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Night vision volume failed: " + e.Message); }
    }

    private void SetVolumeLayer(GameObject go)
    {
        if (_hd == null) return;
        var mask = _hd.volumeLayerMask.value;
        for (var i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) { go.layer = i; return; }
    }

    
    
    private void EnableSearchlight()
    {
        try
        {
            var ambient = 0f;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                if (l != null && l.type == LightType.Directional && l.isActiveAndEnabled) ambient = Math.Max(ambient, l.intensity);
            if (ambient <= 0f) ambient = 2000f;
            _lightGo = new GameObject("NightVisionSearchlight");
            _lightGo.transform.SetParent(_cam.transform, false);
            var light = _lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.96f, 0.92f);
            light.range = _p.Range * 1.3f;
            var fov = NightVisionProfile.FieldOfView(_p.Magnifications[0]);
            light.spotAngle = Mathf.Clamp(fov * 1.8f, 8f, 60f);
            light.innerSpotAngle = light.spotAngle * 0.55f;
            light.shadows = LightShadows.None;
            _lightGo.AddComponent<HDAdditionalLightData>();
            light.lightUnit = LightUnit.Candela;
            var half = _p.Range * 0.5f;
            light.intensity = 5f * ambient * half * half;
        }
        catch (Exception e) { Plugin.Log.LogWarning("Night vision searchlight failed: " + e.Message); }
    }

    private static RenderTexture NewRT(int w, int h, int depth, RenderTextureFormat f, string name, bool mips)
    {
        var rt = new RenderTexture(w, h, depth, f) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        if (mips) { rt.useMipMap = true; rt.autoGenerateMips = true; }
        rt.Create();
        return rt;
    }

    public bool CameraToScreen(Vector2 cameraViewport, out Vector2 screenViewport) =>
        ImageMapping.CameraToScreen(Active ? _image : null, cameraViewport, out screenViewport);

    public bool ScreenRect(out Rect rect) => ImageMapping.ScreenRect(Active ? _image : null, out rect);

    public void SetDigitalZoom(float zoom)
    {
        if (_image == null) return;
        zoom = Math.Max(1f, zoom);
        var s = 1f / zoom;
        _image.uvRect = new Rect(0.5f - s * 0.5f, 0.5f - s * 0.5f, s, s);
    }

    public void SetZoomText(string text)
    {
        if (_text != null) _text.text = Plugin.HudZoomText.Value ? $"NV GEN {ActiveGen}   {text}" : "";
    }

    public void SetStatus(in SightStatus status) { }
    public void SaveSnapshot(string tag) { }

    
    private void EnsureCanvas()
    {
        if (_canvasGo != null) return;
        _canvasGo = new GameObject("NightVisionCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Plugin.CanvasSortingOrder.Value;
        var bg = new GameObject("Background").AddComponent<Image>();
        bg.transform.SetParent(_canvasGo.transform, false);
        bg.color = Color.black;
        bg.raycastTarget = false;
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        _image = new GameObject("NightVisionImage").AddComponent<RawImage>();
        _image.transform.SetParent(_canvasGo.transform, false);
        _image.raycastTarget = false;
        try
        {
            _text = new GameObject("NightVisionText").AddComponent<TMPro.TextMeshProUGUI>();
            _text.transform.SetParent(_canvasGo.transform, false);
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
            if (font != null) _text.font = font;
            _text.fontSize = 22;
            _text.color = new Color(0.6f, 1f, 0.6f, 0.85f);
            _text.alignment = TMPro.TextAlignmentOptions.TopRight;
            _text.raycastTarget = false;
        }
        catch (Exception e) { _text = null; Plugin.Log.LogWarning("Night vision text unavailable: " + e.Message); }
        _lastScreen = Vector2.zero;
        LayoutCanvas();
        _canvasGo.SetActive(false);
    }

    private void LayoutCanvas()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        if (screen == _lastScreen || _image == null) return;
        _lastScreen = screen;
        var side = screen.y * 0.98f;
        var rt = _image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(side, side);
        rt.anchoredPosition = Vector2.zero;
        if (_text != null)
        {
            var t = _text.rectTransform;
            t.anchorMin = t.anchorMax = new Vector2(1f, 1f);
            t.pivot = new Vector2(1f, 1f);
            t.sizeDelta = new Vector2(520, 60);
            t.anchoredPosition = new Vector2(-30f, -24f);
        }
    }
}
