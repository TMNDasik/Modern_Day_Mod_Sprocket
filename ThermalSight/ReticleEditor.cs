using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.VehicleDesigner;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ThermalSight;







internal static class ReticleEditor
{
    public static bool IsOpen => _root != null;

    private static VehicleObject _vo;
    private static Reticles.Data _data;
    private static Action _onClosed;
    private static float _scale, _offX, _offY;
    private static float _lensRadius = 0.5f, _overlayRadius = 0.5f, _softness = 0.02f;   

    private static GameObject _root, _camGo;
    private static Camera _cam;
    private static RenderTexture _rt;
    private static Texture2D _tex, _vignetteTex;
    private static RectTransform _canvasRt;
    private static RawImage _view, _vignette, _reticle;
    private static Image _barL, _barR, _barT, _barB;
    private static readonly Image[] Frame = new Image[4], Handles = new Image[4];
    private static Image _aimH, _aimV;
    private static TextMeshProUGUI _title, _help, _readout;
    private static readonly List<(RectTransform Rect, Action Click)> Buttons = new();
    private static readonly List<TextMeshProUGUI> Labels = new();
    private static TMP_FontAsset _font;
    private static Sprite _white;

    private enum DragMode { None, Move, Resize }
    private static DragMode _drag;
    private static Vector2 _lastMouse;
    private static int _editorFrame = -100;

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.EditorUpdate))]
    private static class EditorHook
    {
        private static void Postfix() => _editorFrame = Time.frameCount;
    }

    

    public static void Open(VehicleObject vo, Reticles.Data data, Action onClosed)
    {
        if (IsOpen) Close(false);
        if (vo == null || data?.Png == null) return;
        try
        {
            _vo = vo;
            _data = data;
            _onClosed = onClosed;
            _scale = data.Scale;
            _offX = data.OffsetX;
            _offY = data.OffsetY;
            _drag = DragMode.None;
            ReadScopeLook();
            _tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(_tex, new Il2CppStructArray<byte>(data.Png))) throw new InvalidOperationException("the reticle image can't be read");
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Trilinear;
            CreateCamera();
            CreateUi();
            Layout();
            Plugin.Log.LogInfo($"Reticle editor opened for '{data.Name}' (lens {_lensRadius:0.###}, reticle {_overlayRadius:0.###} of the screen height)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Reticle editor failed to open: " + e);
            Close(false);
        }
    }

    private static void Close(bool save)
    {
        if (save && _data != null)
        {
            _data.Scale = Mathf.Clamp(_scale, Reticles.MinScale, Reticles.MaxScale);
            _data.OffsetX = Mathf.Clamp(_offX, -Reticles.MaxOffset, Reticles.MaxOffset);
            _data.OffsetY = Mathf.Clamp(_offY, -Reticles.MaxOffset, Reticles.MaxOffset);
            Plugin.Log.LogInfo($"Reticle '{_data.Name}': size {_data.Scale * 100f:0}%, offset {_data.OffsetX * 100f:+0.0;-0.0}% / {_data.OffsetY * 100f:+0.0;-0.0}%");
        }
        try { if (_root != null) Object.Destroy(_root); } catch { }
        try { if (_camGo != null) Object.Destroy(_camGo); } catch { }
        try { if (_rt != null) { _rt.Release(); Object.Destroy(_rt); } } catch { }
        try { if (_tex != null) Object.Destroy(_tex); } catch { }
        _root = _camGo = null;
        _cam = null;
        _rt = null;
        _tex = null;
        Buttons.Clear();
        Labels.Clear();
        _vo = null;
        _data = null;
        var cb = _onClosed;
        _onClosed = null;
        try { cb?.Invoke(); } catch { }
    }

    
    
    private static bool _knownLoaded, _known;
    private static string KnownFile => System.IO.Path.Combine(Plugin.PluginDir, "scope-look.txt");

    public static void Remember(float lensRadius, float overlayRadius, float softness)
    {
        if (lensRadius <= 0.05f || overlayRadius <= 0.01f) return;
        LoadKnown();
        if (_known && Mathf.Abs(lensRadius - _lensRadius) < 1e-4f && Mathf.Abs(overlayRadius - _overlayRadius) < 1e-4f &&
            Mathf.Abs(softness - _softness) < 1e-4f) return;
        _lensRadius = lensRadius;
        _overlayRadius = overlayRadius;
        _softness = Mathf.Clamp(softness, 0f, 0.3f);
        _known = true;
        _vignetteTex = null;
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.Directory.CreateDirectory(Plugin.PluginDir);
            System.IO.File.WriteAllText(KnownFile, string.Join(" ", _lensRadius.ToString(ci), _overlayRadius.ToString(ci), _softness.ToString(ci)));
        }
        catch { }
    }

    private static void LoadKnown()
    {
        if (_knownLoaded) return;
        _knownLoaded = true;
        try
        {
            if (!System.IO.File.Exists(KnownFile)) return;
            var parts = System.IO.File.ReadAllText(KnownFile).Split(' ');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (parts.Length >= 3 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out var r) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out var o) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, ci, out var s) && r > 0.05f && o > 0.01f)
            {
                _lensRadius = r;
                _overlayRadius = o;
                _softness = Mathf.Clamp(s, 0f, 0.3f);
                _known = true;
            }
        }
        catch { }
    }

    
    private static void ReadScopeLook()
    {
        LoadKnown();
        if (_known) return;
        try
        {
            Scope best = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<Scope>())
            {
                if (s == null) continue;
                if (best == null || s.overlay?.value != null) best = s;
                if (s.overlay?.value != null) break;
            }
            if (best == null) return;
            if (best.radius != null && best.radius.value > 0.05f) _lensRadius = best.radius.value;
            if (best.overlayRadius != null && best.overlayRadius.value > 0.01f) _overlayRadius = best.overlayRadius.value;
            if (best.softness != null) _softness = Mathf.Clamp(best.softness.value, 0f, 0.3f);
        }
        catch { }
    }

    

    private static void CreateCamera()
    {
        var main = Camera.main;
        _camGo = new GameObject("Reticle editor camera");
        Object.DontDestroyOnLoad(_camGo);
        _cam = _camGo.AddComponent<Camera>();
        if (main != null)
        {
            try { _cam.CopyFrom(main); } catch { }
            try
            {
                var src = main.GetComponent<HDAdditionalCameraData>();
                var dst = _camGo.GetComponent<HDAdditionalCameraData>() ?? _camGo.AddComponent<HDAdditionalCameraData>();
                var copy = typeof(HDAdditionalCameraData).GetMethod("CopyTo");
                if (src != null && dst != null && copy != null) copy.Invoke(src, new object[] { dst });
            }
            catch { }
        }
        _rt = new RenderTexture(Math.Max(64, Screen.width), Math.Max(64, Screen.height), 24, RenderTextureFormat.ARGB32) { name = "Reticle editor view" };
        _rt.Create();
        _cam.targetTexture = _rt;
        _cam.fieldOfView = 20f;
        _cam.nearClipPlane = 0.03f;
        _cam.farClipPlane = 5000f;
        _cam.enabled = true;
        PlaceCamera();
    }

    private static void PlaceCamera()
    {
        if (_cam == null || _vo == null) return;
        Transform parent = null;
        var offset = Vector3.zero;
        try
        {
            foreach (var b in _vo.behaviours)
            {
                var gs = b != null ? b.TryCast<GunnerSight>() : null;
                if (gs == null) continue;
                parent = gs.SightParent;
                offset = gs.ObjectiveLensLocalPosition;
                break;
            }
        }
        catch { }
        parent ??= _vo.transform;
        _camGo.transform.SetPositionAndRotation(parent.TransformPoint(offset), parent.rotation);
    }

    

    private static void CreateUi()
    {
        _font = TMP_Settings.defaultFontAsset;
        if (_font == null) foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { _font = f; break; }
        if (_white == null || _white.WasCollected)
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (var i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            _white.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }
        _vignetteTex ??= MakeVignette();

        _root = new GameObject("Reticle editor");
        Object.DontDestroyOnLoad(_root);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        _root.AddComponent<GraphicRaycaster>();
        _canvasRt = _root.GetComponent<RectTransform>();

        
        _view = Raw("View", _rt, true);
        _vignette = Raw("Lens", _vignetteTex, false);
        _barL = Img("Bar L", Color.black);
        _barR = Img("Bar R", Color.black);
        _barT = Img("Bar T", Color.black);
        _barB = Img("Bar B", Color.black);
        _reticle = Raw("Reticle", _tex, false);
        for (var i = 0; i < 4; i++) Frame[i] = Img("Frame", new Color(1f, 1f, 1f, 0.28f));
        for (var i = 0; i < 4; i++) Handles[i] = Img("Handle", new Color(1f, 0.78f, 0.3f, 0.95f));
        _aimH = Img("Aim H", new Color(1f, 0.35f, 0.25f, 0.9f));
        _aimV = Img("Aim V", new Color(1f, 0.35f, 0.25f, 0.9f));

        _title = Text("Title", 24f, TextAlignmentOptions.TopLeft, Color.white);
        _help = Text("Help", 16f, TextAlignmentOptions.TopLeft, new Color(0.8f, 0.83f, 0.86f, 1f));
        _readout = Text("Readout", 18f, TextAlignmentOptions.BottomLeft, Color.white);
        _title.text = "Edit reticle — " + (_data.Name ?? "reticle");
        _help.text = "Drag with the left mouse button to move the reticle. Mouse wheel or the corner handles: size.\n" +
                     "Arrow keys: fine move (Shift: faster). The red cross is the aim point. Enter: save.";

        Button("Save", () => Close(true));
        Button("Cancel", () => Close(false));
        Button("Reset", () => { _scale = 1f; _offX = _offY = 0f; });
    }

    
    private static Texture2D MakeVignette()
    {
        const int n = 512;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Reticle editor lens" };
        var px = new Color32[n * n];
        var soft = Mathf.Max(1.5f / n, _softness / Mathf.Max(0.05f, _lensRadius) * 0.5f);
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var dx = (x + 0.5f) / n - 0.5f;
            var dy = (y + 0.5f) / n - 0.5f;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            var a = Mathf.Clamp01((r - (0.5f - soft)) / soft);
            px[y * n + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
        }
        t.SetPixels32(px);
        t.Apply(false, false);
        t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return t;
    }

    private static RectTransform NewRect(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    private static RawImage Raw(string name, Texture tex, bool raycast)
    {
        var img = NewRect(name).gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = raycast;
        return img;
    }

    private static Image Img(string name, Color color)
    {
        var img = NewRect(name).gameObject.AddComponent<Image>();
        img.sprite = _white;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI Text(string name, float size, TextAlignmentOptions align, Color color)
    {
        var t = NewRect(name).gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.richText = false;
        return t;
    }

    private static void Button(string label, Action click)
    {
        var bg = Img("Button " + label, new Color(0.085f, 0.105f, 0.125f, 0.92f));
        var text = Text("Label", 18f, TextAlignmentOptions.Center, Color.white);
        text.transform.SetParent(bg.transform, false);
        var trt = text.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        text.text = label;
        Buttons.Add((bg.rectTransform, click));
        Labels.Add(text);
    }

    private static void Place(Graphic g, Vector2 centre, Vector2 size)
    {
        var rt = g.rectTransform;
        rt.anchoredPosition = centre;
        rt.sizeDelta = size;
    }

    
    private static float H => Mathf.Max(64f, Screen.height);
    private static float W => Mathf.Max(64f, Screen.width);
    private static Vector2 LensCentre => new(W * 0.5f, H * 0.5f);
    private static float ReticleSide => 2f * _overlayRadius * _scale * H;
    private static Vector2 ReticleCentre => LensCentre + new Vector2(_offX, _offY) * ReticleSide;

    private static void Layout()
    {
        var k = H / 1080f;
        var c = LensCentre;
        var r = _lensRadius * H;
        Place(_view, c, new Vector2(W, H));
        Place(_vignette, c, new Vector2(2f * r, 2f * r));
        var side = Mathf.Max(0f, c.x - r);
        Place(_barL, new Vector2(side * 0.5f, c.y), new Vector2(side + 2f, H + 4f));
        Place(_barR, new Vector2(W - side * 0.5f, c.y), new Vector2(side + 2f, H + 4f));
        var vert = Mathf.Max(0f, c.y - r);
        Place(_barB, new Vector2(c.x, vert * 0.5f), new Vector2(W + 4f, vert + 2f));
        Place(_barT, new Vector2(c.x, H - vert * 0.5f), new Vector2(W + 4f, vert + 2f));

        var s = ReticleSide;
        var rc = ReticleCentre;
        Place(_reticle, rc, new Vector2(s, s));
        var line = Mathf.Max(1f, 1.5f * k);
        Place(Frame[0], rc + new Vector2(0f, s * 0.5f), new Vector2(s, line));
        Place(Frame[1], rc - new Vector2(0f, s * 0.5f), new Vector2(s, line));
        Place(Frame[2], rc + new Vector2(s * 0.5f, 0f), new Vector2(line, s));
        Place(Frame[3], rc - new Vector2(s * 0.5f, 0f), new Vector2(line, s));
        var hs = 12f * k;
        for (var i = 0; i < 4; i++) Place(Handles[i], Corner(i), new Vector2(hs, hs));
        Place(_aimH, c, new Vector2(18f * k, Mathf.Max(1f, 2f * k)));
        Place(_aimV, c, new Vector2(Mathf.Max(1f, 2f * k), 18f * k));

        var pad = 28f * k;
        PlaceText(_title, new Vector2(pad, H - pad), new Vector2(W * 0.6f, 32f * k), new Vector2(0f, 1f), 24f * k);
        PlaceText(_help, new Vector2(pad, H - pad - 40f * k), new Vector2(W * 0.6f, 50f * k), new Vector2(0f, 1f), 16f * k);
        _readout.text = $"Size {_scale * 100f:0}%   Offset {_offX * 100f:+0.0;-0.0;0.0}% right, {_offY * 100f:+0.0;-0.0;0.0}% up";
        PlaceText(_readout, new Vector2(pad, pad), new Vector2(W * 0.5f, 30f * k), new Vector2(0f, 0f), 18f * k);

        var bw = 150f * k;
        var bh = 44f * k;
        for (var i = 0; i < Buttons.Count; i++)
        {
            var rt = Buttons[i].Rect;
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(W - pad - i * (bw + 12f * k), pad);
            rt.sizeDelta = new Vector2(bw, bh);
            Labels[i].fontSize = Mathf.Max(9f, 18f * k);
        }
    }

    private static void PlaceText(TextMeshProUGUI t, Vector2 pos, Vector2 size, Vector2 pivot, float fontSize)
    {
        var rt = t.rectTransform;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        t.fontSize = Mathf.Max(9f, fontSize);
    }

    private static Vector2 Corner(int i)
    {
        var h = ReticleSide * 0.5f;
        return ReticleCentre + new Vector2((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h);
    }

    

    public static void Tick()
    {
        if (!IsOpen) return;
        try
        {
            
            var alive = false;
            try { alive = _vo != null && !_vo.WasCollected && _vo; } catch { }
            if (!alive || Time.frameCount - _editorFrame > 10) { Close(false); return; }

            PlaceCamera();
            if (_rt != null && (_rt.width != Screen.width || _rt.height != Screen.height))
            {
                _cam.targetTexture = null;
                _rt.Release();
                Object.Destroy(_rt);
                _rt = new RenderTexture(Math.Max(64, Screen.width), Math.Max(64, Screen.height), 24, RenderTextureFormat.ARGB32) { name = "Reticle editor view" };
                _rt.Create();
                _cam.targetTexture = _rt;
                _view.texture = _rt;
            }
            HandleInput();
            if (IsOpen) Layout();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Reticle editor failed: " + e);
            Close(false);
        }
    }

    private static void HandleInput()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { Close(true); return; }
        if (mouse == null) return;
        var pos = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            foreach (var (rect, click) in Buttons)
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, pos, null)) { click(); return; }
            _drag = DragMode.Move;
            var grab = 16f * H / 1080f;
            for (var i = 0; i < 4; i++)
                if ((pos - Corner(i)).sqrMagnitude <= grab * grab) { _drag = DragMode.Resize; break; }
            _lastMouse = pos;
        }
        if (!mouse.leftButton.isPressed) _drag = DragMode.None;

        if (_drag == DragMode.Move)
        {
            var d = pos - _lastMouse;
            var s = ReticleSide;
            _offX = Mathf.Clamp(_offX + d.x / s, -Reticles.MaxOffset, Reticles.MaxOffset);
            _offY = Mathf.Clamp(_offY + d.y / s, -Reticles.MaxOffset, Reticles.MaxOffset);
        }
        else if (_drag == DragMode.Resize)
        {
            var rc = ReticleCentre;
            var half = Mathf.Max(Mathf.Abs(pos.x - rc.x), Mathf.Abs(pos.y - rc.y));
            Resize(2f * half);
        }
        _lastMouse = pos;

        var wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f) Resize(ReticleSide * (wheel > 0f ? 1.05f : 1f / 1.05f));

        if (kb != null)
        {
            var step = (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 10f : 1f) / Mathf.Max(1f, ReticleSide);
            if (kb.leftArrowKey.wasPressedThisFrame) _offX -= step;
            if (kb.rightArrowKey.wasPressedThisFrame) _offX += step;
            if (kb.upArrowKey.wasPressedThisFrame) _offY += step;
            if (kb.downArrowKey.wasPressedThisFrame) _offY -= step;
            _offX = Mathf.Clamp(_offX, -Reticles.MaxOffset, Reticles.MaxOffset);
            _offY = Mathf.Clamp(_offY, -Reticles.MaxOffset, Reticles.MaxOffset);
        }
    }

    
    private static void Resize(float side)
    {
        var rc = ReticleCentre;
        var unit = 2f * _overlayRadius * H;
        _scale = Mathf.Clamp(side / Mathf.Max(1f, unit), Reticles.MinScale, Reticles.MaxScale);
        var s = ReticleSide;
        var c = LensCentre;
        _offX = Mathf.Clamp((rc.x - c.x) / s, -Reticles.MaxOffset, Reticles.MaxOffset);
        _offY = Mathf.Clamp((rc.y - c.y) / s, -Reticles.MaxOffset, Reticles.MaxOffset);
    }
}
