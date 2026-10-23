using System;
using HarmonyLib;
using Sprocket.VehicleControl;
using Sprocket.Vehicles;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ActiveProtection;


[HarmonyPatch(typeof(VehicleControlPlayerState), nameof(VehicleControlPlayerState.InternalUpdate))]
internal static class EoPlayer
{
    public static Vehicle Vehicle;
    public static Camera Camera;
    private static int _lastFrame = -100;
    private static IntPtr _vehicleGo;

    public static bool InControl => Time.frameCount - _lastFrame <= 2 && Vehicle != null && !Vehicle.WasCollected;

    private static void Prefix(VehicleControlPlayerState __instance)
    {
        try
        {
            _lastFrame = Time.frameCount;
            var vc = __instance.vehicleController;
            var go = vc != null ? vc.ControlledVehicleGameObject : null;
            Camera = vc != null ? vc.ActiveCamera : null;
            var ptr = go != null ? go.Pointer : IntPtr.Zero;
            if (ptr != _vehicleGo)
            {
                _vehicleGo = ptr;
                Vehicle = go != null ? go.GetComponentInParent<Vehicle>() ?? go.GetComponentInChildren<Vehicle>() : null;
            }
        }
        catch { }
    }
}



internal static class EoHud
{
    private static GameObject _canvasGo;
    private static TMPro.TextMeshProUGUI _text;
    private static Image _arrow;
    private static bool _failed;

    private static EoThreats _type;
    private static Vector3 _source;
    private static float _warnedAt = -100f, _beepAt = -100f;
    private static string _message;
    private static float _messageAt = -100f;
    private const float ShowTime = 4f;

    internal static bool IsPlayer(Vehicle v) =>
        v != null && EoPlayer.InControl && EoPlayer.Vehicle != null && EoPlayer.Vehicle.Pointer == v.Pointer;

    public static void Warn(Vehicle v, EoThreats type, Vector3 source)
    {
        if (!IsPlayer(v) || !Plugin.ShowWarnings.Value) return;
        var now = Time.unscaledTime;
        
        if (now - _warnedAt > 0.5f || type != _type) { _type = type; _source = source; }
        _warnedAt = now;
        if (now - _beepAt > 1.5f)
        {
            _beepAt = now;
            Assets.Play(Assets.WarningClip, Vector3.zero, 0.7f, 1f, 1f, spatial: false);
        }
    }

    public static void Smoke(Vehicle v, int left)
    {
        if (!IsPlayer(v)) return;
        Message($"AEROSOL SCREEN  ({left} left)");
    }

    public static void Message(string text)
    {
        _message = text;
        _messageAt = Time.unscaledTime;
    }

    public static void Update()
    {
        var now = Time.unscaledTime;
        var warning = now - _warnedAt < ShowTime && (Plugin.HudWarningText.Value || Plugin.HudWarningArrow.Value);
        var message = now - _messageAt < 2.5f && Plugin.HudMessages.Value;
        if (!EoPlayer.InControl || (!warning && !message))
        {
            if (_canvasGo != null && !_canvasGo.WasCollected && _canvasGo.activeSelf) _canvasGo.SetActive(false);
            return;
        }
        if (!Ensure()) return;
        if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

        var text = "";
        var showArrow = false;
        if (warning)
        {
            var v = EoPlayer.Vehicle;
            var blink = ((int)((now - _warnedAt) * 4f) & 1) == 0 ? "#FF4A3A" : "#FFB0A0";
            var dist = v != null ? (_source - v.transform.position).magnitude : 0f;
            if (Plugin.HudWarningText.Value)
                text = $"<color={blink}><b>{EoInfo.Short(_type)}</b></color>  <size=80%>{Clock(v, _source)}, {dist:0} m</size>";

            var cam = EoPlayer.Camera;
            if (Plugin.HudWarningArrow.Value && cam != null && !cam.WasCollected && _arrow != null)
            {
                var f = cam.transform.forward;
                f.y = 0f;
                var to = _source - cam.transform.position;
                to.y = 0f;
                if (f.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                {
                    var angle = Vector3.SignedAngle(f, to, Vector3.up);
                    var rt = _arrow.rectTransform;
                    var r = 150f;
                    var rad = angle * Mathf.Deg2Rad;
                    rt.anchoredPosition = new Vector2(Mathf.Sin(rad) * r, Mathf.Cos(rad) * r);
                    rt.localRotation = Quaternion.Euler(0f, 0f, -angle);
                    _arrow.color = new Color(1f, 0.3f, 0.22f, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(now * 8f)));
                    showArrow = true;
                }
            }
        }
        if (message) text += (text.Length > 0 ? "\n" : "") + $"<size=80%><color=#E6E6E6>{_message}</color></size>";
        _text.text = text;
        if (_arrow != null && _arrow.gameObject.activeSelf != showArrow) _arrow.gameObject.SetActive(showArrow);
    }

    
    private static string Clock(Vehicle v, Vector3 source)
    {
        if (v == null) return "";
        var t = v.transform;
        var f = t.forward;
        f.y = 0f;
        var to = source - t.position;
        to.y = 0f;
        if (f.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return "";
        var angle = Vector3.SignedAngle(f, to, Vector3.up);
        var hour = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 30f) % 12;
        return $"{(hour == 0 ? 12 : hour)} o'clock";
    }

    private static bool Ensure()
    {
        if (_text != null && !_text.WasCollected) return true;
        if (_failed) return false;
        try
        {
            _canvasGo = new GameObject("ActiveProtectionHud");
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Warning");
            go.transform.SetParent(_canvasGo.transform, false);
            _text = go.AddComponent<TMPro.TextMeshProUGUI>();
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
            if (font != null) _text.font = font;
            _text.fontSize = 28;
            _text.richText = true;
            _text.alignment = TMPro.TextAlignmentOptions.Top;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            var rt = _text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -110f);
            rt.sizeDelta = new Vector2(900f, 120f);

            var tex = Assets.Arrow;
            if (tex != null)
            {
                var ag = new GameObject("Arrow");
                ag.transform.SetParent(_canvasGo.transform, false);
                _arrow = ag.AddComponent<Image>();
                _arrow.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _arrow.raycastTarget = false;
                var art = _arrow.rectTransform;
                art.anchorMin = art.anchorMax = art.pivot = new Vector2(0.5f, 0.5f);
                art.sizeDelta = new Vector2(46f, 46f);
                ag.SetActive(false);
            }
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("EO warning display unavailable: " + e.Message);
            return false;
        }
    }
}
