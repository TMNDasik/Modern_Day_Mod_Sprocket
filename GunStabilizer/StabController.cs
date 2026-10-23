using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GunStabilizer;


public class StabController : MonoBehaviour
{
    public StabController(IntPtr ptr) : base(ptr) { }

    
    internal static int PanelState;
    internal static int TakenOverFrame = -100;

    private float _nextCleanup;
    private GameObject _canvasGo;
    private TMPro.TextMeshProUGUI _text;
    private bool _hudFailed;
    private string _message;
    private float _messageUntil;

    private void Update()
    {
        try
        {
            ScopeCarry.EnsureRenderHook();
            Boresight.EnsureRenderHook();
            FcsPart.EnsureInjected();
            var inControl = Time.frameCount - ScopeCarry.LastControlFrame <= 2 && ScopeCarry.PlayerVehicleKey != 0;
            var unit = inControl ? Registry.PeekUnit(ScopeCarry.PlayerVehicleKey) : null;
            var status = inControl ? Status.Of(ScopeCarry.SightCannon ?? ScopeCarry.PlayerCannon, unit) : default;

            if (inControl && status.HasAny && unit != null && Time.timeScale > 0f && KeyPressed())
            {
                unit.Toggle();
                _message = unit.Engaged ? "STABILIZER ON" : "STABILIZER OFF";
                _messageUntil = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo("Stabilizer " + (unit.Engaged ? "engaged" : "disengaged"));
            }

            PanelState = !inControl || !status.HasAny ? 0
                : unit == null ? 2
                : !unit.Engaged ? 1
                : unit.Engaging ? 2
                : (status.HasV && !status.VOk) || (status.HasH && !status.HOk) ? 4 : 3;
            
            var takenOver = Time.frameCount - TakenOverFrame <= 3;
            UpdateHud(inControl && status.HasAny && Plugin.ShowIndicator.Value && !takenOver, status, unit);
            if (inControl) LogPlayerState(status, unit);
            FireControl.Update(inControl);

            if (Time.unscaledTime >= _nextCleanup)
            {
                _nextCleanup = Time.unscaledTime + 5f;
                Registry.Cleanup();
            }
        }
        catch (Exception e) { Plugin.Log.LogError("Stabilizer controller failed: " + e); }
    }

    
    private int _loggedKey;
    private string _loggedStatus;
    private bool _loggedReady;

    private void LogPlayerState(Status st, StabUnit unit)
    {
        var key = ScopeCarry.PlayerVehicleKey;
        var desc = st.HasAny
            ? $"vertical={(st.HasV ? (st.VOk ? "ok" : "fault") : "none")}, horizontal={(st.HasH ? (st.HOk ? "ok" : st.HNoPower ? "no powered traverse" : "fault") : "none")}"
            : "no stabilizer";
        if (key != _loggedKey || desc != _loggedStatus)
        {
            _loggedKey = key;
            _loggedStatus = desc;
            _loggedReady = false;
            Plugin.Log.LogInfo($"Player vehicle: {desc}");
        }
        if (unit != null && st.HasAny && unit.Ready && !_loggedReady)
        {
            _loggedReady = true;
            Plugin.Log.LogInfo("Stabilization active");
        }
        if (unit != null && !unit.Engaged) _loggedReady = false;
    }

    private static bool KeyPressed()
    {
        try
        {
            var kb = Keyboard.current;
            return kb != null && kb[Plugin.ToggleKey].wasPressedThisFrame;
        }
        catch { return false; }
    }

    

    private static readonly Color Ready = new(0.55f, 0.95f, 0.55f, 0.95f);
    private static readonly Color Warm = new(1f, 0.78f, 0.3f, 0.95f);
    private static readonly Color Off = new(0.75f, 0.75f, 0.75f, 0.9f);
    private static readonly Color Fault = new(1f, 0.45f, 0.35f, 0.95f);

    private void UpdateHud(bool show, Status st, StabUnit unit)
    {
        if (!show)
        {
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
            return;
        }
        if (!EnsureHud()) return;
        if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

        string text;
        Color color;
        var key = Plugin.ToggleKeyName;
        if (unit == null) { text = "STAB  POWER-UP"; color = Warm; }
        else if (!unit.Engaged) { text = $"STAB  OFF  [{key}]"; color = Off; }
        else if (unit.Engaging) { text = "STAB  ENGAGING"; color = Warm; }
        else
        {
            text = "STAB  ON";
            color = Ready;
            if (st.HasV) text += st.VOk ? "  ELEV" : "  ELEV FAULT";
            if (st.HasH) text += st.HOk ? "  TRAV" : st.HNoPower ? "  TRAV NO POWER" : "  TRAV FAULT";
            if ((st.HasV && !st.VOk) || (st.HasH && !st.HOk)) color = Fault;
        }
        if (_message != null && Time.unscaledTime < _messageUntil) text = _message;
        _text.text = text;
        _text.color = color;
    }

    private bool EnsureHud()
    {
        if (_text != null) return true;
        if (_hudFailed) return false;
        try
        {
            _canvasGo = new GameObject("GunStabilizerHud");
            DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            var go = new GameObject("Status");
            go.transform.SetParent(_canvasGo.transform, false);
            _text = go.AddComponent<TMPro.TextMeshProUGUI>();
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
            if (font != null) _text.font = font;
            _text.fontSize = 20;
            _text.alignment = TMPro.TextAlignmentOptions.BottomLeft;
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            var rt = _text.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(24f, 24f);
            rt.sizeDelta = new Vector2(600f, 40f);
            return true;
        }
        catch (Exception e)
        {
            _hudFailed = true;
            Plugin.Log.LogWarning("Stabilizer indicator unavailable: " + e.Message);
            return false;
        }
    }
}


internal struct Status
{
    public bool HasV, HasH, VOk, HOk, HNoPower;
    public bool HasAny => HasV || HasH;

    public static Status Of(Sprocket.Vehicles.Cannons.Cannon cannon, StabUnit unit)
    {
        var st = new Status();
        if (cannon == null) return st;
        try
        {
            var chain = Registry.ChainFor(cannon);
            var vs = chain?.Pitch;
            var hs = chain?.Yaw;
            
            if (vs != null && vs.FlagStab) { st.HasV = true; st.VOk = vs.Healthy && vs.PowerOk; }
            if (hs != null && hs.IsTurret && hs.FlagStab)
            {
                st.HasH = true;
                st.HOk = hs.Healthy && hs.PowerOk;
                st.HNoPower = !hs.PowerOk;
            }
        }
        catch { }
        return st;
    }
}
