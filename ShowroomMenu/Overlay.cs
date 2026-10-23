using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ShowroomMenu;





internal sealed class Overlay
{
    private const float FontSize = 40f, ContainerHeight = 56f, LineLength = 72f, LineGap = 22f;
    private const string ModCredit = "Mod by simen72 and Das1";
    private const float CreditWidth = 480f, CreditGap = 2f;
    private static readonly Color Cream = new(0.94f, 0.9f, 0.82f, 1f);

    private GameObject _root;
    private Image _fade;
    private CanvasGroup _subtitleGroup;
    private RectTransform _subtitle, _lineL, _lineR;
    private TextMeshProUGUI _text, _shadow;
    private string _shownText;
    private float _fadeAlpha = 1f, _subtitleAlpha;

    private RectTransform _panel;
    private bool _panelSaved;
    private Vector2 _panelAnchorMin, _panelAnchorMax, _panelPivot, _panelPos;

    private TMP_Text _credit;
    private GameObject _modCredit;
    private string _creditText;
    private Vector2 _creditSize;
    private HorizontalAlignmentOptions _creditAlign;

    public void Begin(Sprocket.MainMenu menu, Scene menuScene)
    {
        _root = new GameObject("ShowroomMenuOverlay");
        SceneManager.MoveGameObjectToScene(_root, menuScene);
        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        
        var fadeRt = Rect("Fade", _root.transform);
        fadeRt.anchorMin = Vector2.zero;
        fadeRt.anchorMax = Vector2.one;
        fadeRt.sizeDelta = Vector2.zero;
        _fade = fadeRt.gameObject.AddComponent<Image>();
        _fade.color = Color.black;
        _fade.raycastTarget = false;

        _subtitle = Rect("Subtitle", _root.transform);
        _subtitle.pivot = new Vector2(0.5f, 1f);
        _subtitle.sizeDelta = new Vector2(1400f, ContainerHeight);
        _subtitleGroup = _subtitle.gameObject.AddComponent<CanvasGroup>();
        _subtitleGroup.interactable = false;
        _subtitleGroup.blocksRaycasts = false;
        _subtitleGroup.alpha = 0f;

        var font = FindFont(menuScene);
        _shadow = Text("Shadow", _subtitle, font, new Color(0f, 0f, 0f, 0.6f));
        _shadow.rectTransform.anchoredPosition = new Vector2(2.5f, -2.5f);
        _text = Text("Text", _subtitle, font, Cream);
        _lineL = Line("LineL", _subtitle);
        _lineR = Line("LineR", _subtitle);

        var panel = menu.panel;
        if (panel != null) _panel = panel.transform.TryCast<RectTransform>();
        if (_panel != null)
        {
            _panelAnchorMin = _panel.anchorMin;
            _panelAnchorMax = _panel.anchorMax;
            _panelPivot = _panel.pivot;
            _panelPos = _panel.anchoredPosition;
            _panelSaved = true;
        }
        SetupCredits(menu);
    }

    
    private void SetupCredits(Sprocket.MainMenu menu)
    {
        try
        {
            var t = menu.transform.Find("Credit");
            var text = t != null ? t.GetComponent<TMP_Text>() : null;
            if (text == null)
            {
                Plugin.Log.LogWarning("Credit line not found in the main menu");
                return;
            }
            _credit = text;
            _creditText = text.text;
            var rt = text.rectTransform;
            _creditSize = rt.sizeDelta;
            var original = (_creditText ?? "").Trim();
            var author = original.StartsWith("By ", StringComparison.OrdinalIgnoreCase) ? original[3..].Trim() : "";
            text.text = "Game by " + (author.Length > 0 ? author : "Hamish Dunn");
            
            _creditAlign = text.horizontalAlignment;
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            rt.sizeDelta = new Vector2(Mathf.Max(_creditSize.x, CreditWidth), _creditSize.y);

            _modCredit = Object.Instantiate(text.gameObject, rt.parent, false);
            _modCredit.name = "Mod Credit";
            var mod = _modCredit.GetComponent<TMP_Text>();
            mod.text = ModCredit;
            var mrt = mod.rectTransform;
            mrt.sizeDelta = rt.sizeDelta;
            mrt.anchoredPosition = rt.anchoredPosition + new Vector2(0f, _creditSize.y + CreditGap);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Credits could not be set up: " + e.Message); }
    }

    public void Update(Sprocket.MainMenu menu, TitleCard title, bool onMainScreen, bool sceneReady, float dt)
    {
        if (_root == null) return;
        var rect = title.ViewRect;
        var cx = rect.center.x;

        
        if (_fade != null)
        {
            if (sceneReady) _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, 0f, dt / 0.7f);
            _fade.color = new Color(0f, 0f, 0f, _fadeAlpha);
            if (_fadeAlpha <= 0f)
            {
                Object.Destroy(_fade.gameObject);
                _fade = null;
            }
        }

        
        var text = (Plugin.Subtitle.Value ?? "").Trim().ToUpperInvariant();
        if (text != _shownText)
        {
            _shownText = text;
            _text.text = text;
            _shadow.text = text;
            _text.ForceMeshUpdate();
            var half = _text.preferredWidth * 0.5f + LineGap + LineLength * 0.5f;
            _lineL.anchoredPosition = new Vector2(-half, 0f);
            _lineR.anchoredPosition = new Vector2(half, 0f);
        }
        var hasText = text.Length > 0;
        _subtitleAlpha = Mathf.MoveTowards(_subtitleAlpha, hasText && onMainScreen ? title.Alpha : 0f, dt * 5f);
        _subtitleGroup.alpha = _subtitleAlpha;
        var subTop = rect.yMin - 0.004f;
        _subtitle.anchorMin = _subtitle.anchorMax = new Vector2(cx, subTop);
        _subtitle.anchoredPosition = Vector2.zero;

        
        if (_panel != null && !_panel.WasCollected && _panel)
        {
            var below = hasText ? ContainerHeight / 1080f + 0.022f : 0.03f;
            var anchor = new Vector2(cx, subTop - below);
            if (_panel.anchorMin != anchor || _panel.anchorMax != anchor) _panel.anchorMin = _panel.anchorMax = anchor;
            if (_panel.pivot != new Vector2(0.5f, 1f)) _panel.pivot = new Vector2(0.5f, 1f);
            if (_panel.anchoredPosition != Vector2.zero) _panel.anchoredPosition = Vector2.zero;
        }
    }

    public void Dispose(bool restoreMenu)
    {
        if (restoreMenu && _panelSaved && _panel != null && !_panel.WasCollected && _panel)
        {
            _panel.anchorMin = _panelAnchorMin;
            _panel.anchorMax = _panelAnchorMax;
            _panel.pivot = _panelPivot;
            _panel.anchoredPosition = _panelPos;
        }
        if (restoreMenu && _credit != null && !_credit.WasCollected && _credit)
        {
            _credit.text = _creditText;
            _credit.horizontalAlignment = _creditAlign;
            _credit.rectTransform.sizeDelta = _creditSize;
        }
        if (_modCredit != null && !_modCredit.WasCollected && _modCredit) Object.Destroy(_modCredit);
        _modCredit = null;
        _credit = null;
        if (_root != null && !_root.WasCollected && _root) Object.Destroy(_root);
        _root = null;
        _fade = null;
    }

    

    private static TMP_FontAsset FindFont(Scene menuScene)
    {
        try
        {
            foreach (var root in menuScene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t.font != null) return t.font;
        }
        catch (Exception e) { Plugin.Debug("Menu font lookup failed: " + e.Message); }
        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { font = f; break; }
        return font;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    private static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, Color color)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = FontSize;
        t.fontStyle = FontStyles.Bold;
        t.characterSpacing = 14f;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    private static RectTransform Line(string name, RectTransform parent)
    {
        var rt = Rect(name, parent);
        rt.sizeDelta = new Vector2(LineLength, 3f);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(Cream.r, Cream.g, Cream.b, 0.9f);
        img.raycastTarget = false;
        return rt;
    }
}
