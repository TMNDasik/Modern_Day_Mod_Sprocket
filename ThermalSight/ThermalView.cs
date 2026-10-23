using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace ThermalSight;



internal sealed class ThermalView : IThermalView
{
    public bool Active { get; private set; }
    public static bool AnyActive;
    public int ActiveGen { get; private set; }

    private Camera _cam;
    private HDAdditionalCameraData _hd;
    private HDAdditionalCameraData.ClearColorMode _origClear;
    private Color _origBackground;

    private RenderTexture _rt;
    private AsyncGPUReadbackRequest _req;
    private bool _pending, _haveFrame, _outputShown;
    private byte[] _in, _out;
    private Texture2D _outTex;
    private readonly ThermalProcessor _proc = new();
    private GenerationProfile _profile;

    private GameObject _canvasGo;
    private RawImage _image;

    private float _nextProcess, _lastProcess;
    private float _nucUntil, _nextNuc;
    private Quaternion _lastRot;
    private float _slew;
    private float _snapshotAt = -1;
    private readonly System.Random _rng = new();

    public void Enable(Camera cam, GenerationProfile profile, int seed)
    {
        if (Active) Disable();
        if (cam == null) return;

        _cam = cam;
        _profile = profile;
        ActiveGen = profile.Gen;

        var aspect = Math.Max(1f, (float)Screen.width / Math.Max(1, Screen.height));
        var h = Math.Max(120, (int)(profile.SensorLines * Plugin.ResolutionScale.Value));
        var w = Math.Max(160, (int)Math.Round(h * aspect));
        w -= w % 2;

        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ThermalSightCapture" };
        _rt.Create();
        _in = new byte[w * h * 4];
        _out = new byte[w * h * 4];
        _outTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        _proc.Setup(w, h, seed, profile);

        
        _hd = cam.GetComponent<HDAdditionalCameraData>();
        if (_hd != null)
        {
            _origClear = _hd.clearColorMode;
            _origBackground = _hd.backgroundColorHDR;
            _hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            _hd.backgroundColorHDR = Color.black;
        }

        

        
        DisableSunShadows();
        EnableCameraOverrides();

        cam.targetTexture = _rt;

        EnsureCanvas();
        _image.texture = _outTex;
        SetDigitalZoom(1f);
        _image.color = Color.black; 
        _canvasGo.SetActive(true);

        _pending = false; _haveFrame = false; _outputShown = false;
        _lastRot = cam.transform.rotation;
        _lastProcess = Time.unscaledTime;
        _nextProcess = 0;
        _nucUntil = 0;
        _nextNuc = profile.NucInterval > 0 ? Time.unscaledTime + profile.NucInterval * (0.3f + 0.7f * (float)_rng.NextDouble()) : float.MaxValue;
        _snapshotAt = Plugin.DebugSnapshots.Value ? Time.unscaledTime + 2.5f : -1;

        Active = true;
        AnyActive = true;
        _nextSmokeScan = 0;
        Plugin.Log.LogInfo($"Thermal view on: Gen {profile.Gen}, sensor {w}x{h}, camera {cam.name}");
    }

    public void Disable()
    {
        if (!Active) return;
        Active = false;
        AnyActive = false;
        RestoreSmoke();
        RestoreSunShadows();
        DisableCameraOverrides();
        try { if (_cam != null) _cam.targetTexture = null; } catch { }
        try
        {
            if (_hd != null)
            {
                _hd.clearColorMode = _origClear;
                _hd.backgroundColorHDR = _origBackground;
            }
        }
        catch { }
        if (_canvasGo != null) _canvasGo.SetActive(false);
        if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
        if (_outTex != null) { UnityEngine.Object.Destroy(_outTex); _outTex = null; }
        _pending = false;
        _cam = null; _hd = null;
        Plugin.Log.LogInfo("Thermal view off");
    }

    public void Update()
    {
        if (!Active) return;
        if (_cam == null || _rt == null) { Disable(); return; }
        if (_cam.targetTexture != _rt) _cam.targetTexture = _rt; 

        var now = Time.unscaledTime;
        if (now > _nextSmokeScan) { _nextSmokeScan = now + 0.5f; HideSmoke(); }
        var dtFrame = Math.Max(Time.unscaledDeltaTime, 1e-4f);

        
        var rot = _cam.transform.rotation;
        var angle = Quaternion.Angle(_lastRot, rot);
        _lastRot = rot;
        _slew += (angle / dtFrame - _slew) * Math.Min(1f, dtFrame * 10f);

        
        if (_pending && _req.done)
        {
            _pending = false;
            if (!_req.hasError)
            {
                try
                {
                    var data = _req.GetData<byte>(0);
                    var len = Math.Min(data.m_Length, _in.Length);
                    unsafe { Marshal.Copy((IntPtr)data.m_Buffer, _in, 0, len); }
                    _haveFrame = true;
                }
                catch (Exception e) { Plugin.Log.LogWarning("Readback copy failed: " + e.Message); }
            }
        }
        if (!_pending)
        {
            _req = AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGBA32);
            _pending = true;
        }

        
        if (now >= _nextNuc)
        {
            _nucUntil = now + _profile.NucDuration;
            _nextNuc = now + _profile.NucInterval * (0.8f + 0.4f * (float)_rng.NextDouble());
        }
        var frozen = now < _nucUntil;

        if (_haveFrame && !frozen && now >= _nextProcess)
        {
            var dt = Math.Clamp(now - _lastProcess, 1e-3f, 0.5f);
            _lastProcess = now;
            _nextProcess = now + 1f / _profile.FrameRate;
            var fwd = _cam.transform.forward;
            var pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            _proc.Process(_in, _out, _profile, dt, _slew, pitch, _cam.fieldOfView, Plugin.HeatDecodeGain.Value);
            unsafe
            {
                fixed (byte* p = _out) _outTex.LoadRawTextureData((IntPtr)p, _out.Length);
            }
            _outTex.Apply(false);
            if (!_outputShown) { _image.color = Color.white; _outputShown = true; }
        }

        if (_snapshotAt > 0 && now > _snapshotAt && _outputShown)
        {
            _snapshotAt = -1;
            SaveSnapshot("auto");
        }
    }

    public void SaveSnapshot(string tag)
    {
        if (!Active || !_outputShown) return;
        try
        {
            var dir = Path.Combine(Plugin.PluginDir, "debug");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("HHmmss");
            Bmp.Write(Path.Combine(dir, $"{stamp}_{tag}_gen{_profile.Gen}_scene.bmp"), _in, _proc.Width, _proc.Height);
            Bmp.Write(Path.Combine(dir, $"{stamp}_{tag}_gen{_profile.Gen}_thermal.bmp"), _out, _proc.Width, _proc.Height);
            Plugin.Log.LogInfo($"Snapshot saved to {dir}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Snapshot failed: " + e.Message); }
    }

    

    public Camera Camera => _cam;

    public bool CameraToScreen(Vector2 cameraViewport, out Vector2 screenViewport) =>
        ImageMapping.CameraToScreen(Active ? _image : null, cameraViewport, out screenViewport);

    public bool ScreenRect(out Rect rect) => ImageMapping.ScreenRect(Active ? _image : null, out rect);

    
    
    public void SetDigitalZoom(float zoom)
    {
        if (_image == null) return;
        zoom = Math.Max(1f, zoom);
        var size = 1f / zoom;
        _image.uvRect = new Rect(0.5f - size * 0.5f, 0.5f - size * 0.5f, size, size);
    }

    private TMPro.TextMeshProUGUI _zoomText;
    private bool _zoomTextFailed;

    
    public void SetStatus(in SightStatus status) { }

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
                _zoomText.alignment = TMPro.TextAlignmentOptions.BottomRight;
                _zoomText.raycastTarget = false;
                var rt = _zoomText.rectTransform;
                rt.anchorMin = new Vector2(1, 0); rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(1, 0);
                rt.sizeDelta = new Vector2(420, 60);
                rt.anchoredPosition = new Vector2(-40, 30);
            }
            catch (Exception e)
            {
                _zoomTextFailed = true;
                Plugin.Log.LogWarning("Zoom indicator unavailable: " + e.Message);
            }
        }
        if (_zoomText != null) _zoomText.text = Plugin.HudZoomText.Value ? text : "";
    }

    
    
    

    private GameObject _volumeGo;
    private HDAdditionalCameraData.AntialiasingMode _origAA;

    private void EnableCameraOverrides()
    {
        if (_hd != null)
        {
            try
            {
                _origAA = _hd.antialiasing;
                
                _hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            catch (Exception e) { Plugin.Log.LogWarning("AA override failed: " + e.Message); }
        }

        try
        {
            if (_volumeGo == null)
            {
                _volumeGo = new GameObject("ThermalSightVolume");
                UnityEngine.Object.DontDestroyOnLoad(_volumeGo);
                var volume = _volumeGo.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000f;
                volume.weight = 1f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                volume.sharedProfile = profile;

                T Add<T>() where T : VolumeComponent =>
                    profile.Add(Il2CppInterop.Runtime.Il2CppType.Of<T>(), false).TryCast<T>();

                Add<MotionBlur>().intensity.Override(0f);
                Add<DepthOfField>().focusMode.Override(DepthOfFieldMode.Off);
                Add<Bloom>().intensity.Override(0f);
                Add<Vignette>().intensity.Override(0f);
                Add<ChromaticAberration>().intensity.Override(0f);
                Add<FilmGrain>().intensity.Override(0f);
                
                Add<Tonemapping>().mode.Override(TonemappingMode.None);
                var ca = Add<ColorAdjustments>();
                ca.postExposure.Override(0f);
                ca.contrast.Override(0f);
                ca.saturation.Override(0f);
                ca.hueShift.Override(0f);
                ca.colorFilter.Override(Color.white);
                var wb = Add<WhiteBalance>();
                wb.temperature.Override(0f);
                wb.tint.Override(0f);
                profile.isDirty = true;
            }
            
            if (_hd != null)
            {
                var mask = _hd.volumeLayerMask.value;
                for (var i = 0; i < 32; i++)
                    if ((mask & (1 << i)) != 0) { _volumeGo.layer = i; break; }
            }
            _volumeGo.SetActive(true);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Camera effect overrides failed: " + e.Message);
        }
    }

    private void DisableCameraOverrides()
    {
        try { if (_volumeGo != null) _volumeGo.SetActive(false); } catch { }
        try { if (_hd != null) _hd.antialiasing = _origAA; } catch { }
    }

    

    private readonly System.Collections.Generic.List<(HDAdditionalLightData light, float dimmer, float volumetric)> _sunShadows = new();

    
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

    

    private float _nextSmokeScan;
    private readonly System.Collections.Generic.List<Renderer> _hiddenSmoke = new();
    private readonly System.Collections.Generic.HashSet<string> _loggedVfx = new();

    private void HideSmoke()
    {
        try
        {
            foreach (var vfx in UnityEngine.Object.FindObjectsOfType<UnityEngine.VFX.VisualEffect>())
            {
                if (vfx == null) continue;
                var asset = vfx.visualEffectAsset != null ? vfx.visualEffectAsset.name : "";
                var name = vfx.gameObject.name + " / " + asset;
                if (Plugin.DebugLogging.Value && _loggedVfx.Add(name)) Plugin.Log.LogInfo("VFX seen: " + name);
                if (name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("dust", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = vfx.GetComponent<Renderer>();
                if (r != null && r.enabled) { r.enabled = false; _hiddenSmoke.Add(r); }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Smoke scan failed: " + e.Message);
            _nextSmokeScan = float.MaxValue;
        }
    }

    private void RestoreSmoke()
    {
        foreach (var r in _hiddenSmoke)
            try { if (r != null) r.enabled = true; } catch { }
        _hiddenSmoke.Clear();
    }

    private void EnsureCanvas()
    {
        if (_canvasGo != null) return;
        _canvasGo = new GameObject("ThermalSightCanvas");
        UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = Plugin.CanvasSortingOrder.Value;

        var imgGo = new GameObject("ThermalImage");
        imgGo.transform.SetParent(_canvasGo.transform, false);
        _image = imgGo.AddComponent<RawImage>();
        _image.raycastTarget = false;
        Stretch(_image.rectTransform);

        
        var reticle = new GameObject("Reticle");
        reticle.transform.SetParent(_canvasGo.transform, false);
        var rrt = reticle.AddComponent<RectTransform>();
        Stretch(rrt);
        reticle.SetActive(Plugin.ShowReticle.Value); 
        const float len = 60f, gap = 14f, thick = 2f;
        AddBar(reticle.transform, new Vector2(0, gap + len / 2), new Vector2(thick, len));
        AddBar(reticle.transform, new Vector2(0, -(gap + len / 2)), new Vector2(thick, len));
        AddBar(reticle.transform, new Vector2(gap + len / 2, 0), new Vector2(len, thick));
        AddBar(reticle.transform, new Vector2(-(gap + len / 2), 0), new Vector2(len, thick));
        AddBar(reticle.transform, Vector2.zero, new Vector2(thick, thick));

        _canvasGo.SetActive(false);
    }

    private static void AddBar(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("bar");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}


internal static class Bmp
{
    public static void Write(string path, byte[] rgba, int w, int h)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        var size = w * h * 4;
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + size); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(size); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        var row = new byte[w * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                row[x * 4] = rgba[i + 2]; row[x * 4 + 1] = rgba[i + 1]; row[x * 4 + 2] = rgba[i]; row[x * 4 + 3] = 255;
            }
            bw.Write(row);
        }
    }
}
