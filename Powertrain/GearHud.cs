using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Powertrain;











internal sealed class GearHud
{
    private const float NodeSize = 30f, KnobSize = 24f, BridgeWidth = 14f, PanelWidth = 128f, TopPad = 30f, BottomPad = 46f, Margin = 24f;
    private const float StripHeight = 4f, StripInset = 14f, StripBottom = 6f;
    private const float MoveTime = 0.18f, PulseTime = 0.5f, EdgeFade = 0.55f;

    private static readonly Color PanelBg = new(0.07f, 0.09f, 0.1f, 0.47f);
    private static readonly Color BorderColor = new(0.3f, 0.36f, 0.4f, 0.15f);
    private static readonly Color LabelBg = new(0.21f, 0.28f, 0.33f, 0.9f);
    private static readonly Color LabelBgSel = new(0.3f, 0.39f, 0.45f, 0.95f);
    private static readonly Color Gold = new(0.95f, 0.8f, 0.38f, 1f);
    private static readonly Color StatusColor = new(0.85f, 0.87f, 0.88f, 1f);
    private static readonly Color FillColor = new(0.06f, 0.08f, 0.09f, 0.9f);
    private static readonly Color RingColor = new(0.3f, 0.36f, 0.4f, 1f);
    private static readonly Color KnobColor = new(0.3f, 0.39f, 0.45f, 1f);
    private static readonly Color BridgeColor = new(0.3f, 0.36f, 0.4f, 0.45f);
    private static readonly Color LineColor = new(0.45f, 0.52f, 0.56f, 0.9f);
    private static readonly Color TrailColor = new(0.3f, 0.39f, 0.45f, 1f);
    private static readonly Color PulseColor = new(1f, 0.75f, 0.25f, 1f);
    private static readonly Color EngagedColor = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color BusyColor = new(1f, 0.75f, 0.25f, 1f);
    private static readonly Color IdleColor = new(0.42f, 0.42f, 0.42f, 0.6f);
    private static readonly Color Red = new(1f, 0.3f, 0.2f, 1f);

    private sealed class Node
    {
        public RectTransform Rt;
        public Image Fill, Ring;
        public TextMeshProUGUI Label;
    }

    private sealed class Bridge
    {
        public RectTransform Rt;
        public Image Bar, Line;
    }

    private GameObject _root;
    private RectTransform _box, _column, _chipRt;
    private CanvasGroup _group;
    private Image _panel, _border, _strip, _trail, _knob, _pulse, _chip;
    private TextMeshProUGUI _status, _mode, _chipText;
    private readonly List<Node> _nodes = new();
    private readonly List<Bridge> _bridges = new();
    private Sprite _ringS, _discS, _glowS, _panelS, _chipS;
    private TMP_FontAsset _font;
    private bool _failed;

    private string _layoutKey;
    private float _spacing = 40f;
    private int _view;                 
    private float _scroll, _scrollTarget = -1f;
    private int _seenVersion = -1;
    private float _fromSlot, _toSlot, _knobSlot;
    private float _alpha, _bottom = Margin;
    private float _nextMunitionsLook;
    private TMP_Text _munitionsText;

    public void Update(GearSelector sel, bool visible, bool altHeld)
    {
        if (_failed) return;
        var target = visible && sel != null && Plugin.ShowGearPanel.Value ? 1f : 0f;
        _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 6f);
        if (_root == null && target <= 0f) return;
        if (!Ensure()) return;
        if (_alpha <= 0f)
        {
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf) _root.SetActive(true);
        _group.alpha = _alpha;
        if (sel == null) return;

        Layout(sel);
        Animate(sel, altHeld);
        Place();
        Show(_chipRt, Plugin.GearKeyLabel.Value);
        Show(_mode, Plugin.GearModeLabel.Value);
        Show(_status, Plugin.GearStatus.Value);
        Show(_strip, Plugin.GearStrip.Value);
    }

    
    private static void Show(Component c, bool on)
    {
        if (c == null || c.WasCollected) return;
        var go = c.gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }

    

    private bool Ensure()
    {
        if (_root != null && !_root.WasCollected) return true;
        try
        {
            var ring = Sounds.Ring;
            var disc = Sounds.Disc;
            var glow = Sounds.Glow;
            var panel = Sounds.Panel;
            var chip = Sounds.Chip;
            if (ring == null || disc == null || glow == null || panel == null || chip == null)
                throw new InvalidOperationException("gear panel textures missing");
            _ringS = Keep(Sprite.Create(ring, new Rect(0, 0, ring.width, ring.height), new Vector2(0.5f, 0.5f), 100f));
            _discS = Keep(Sprite.Create(disc, new Rect(0, 0, disc.width, disc.height), new Vector2(0.5f, 0.5f), 100f));
            _glowS = Keep(Sprite.Create(glow, new Rect(0, 0, glow.width, glow.height), new Vector2(0.5f, 0.5f), 100f));
            _panelS = Keep(Sprite.Create(panel, new Rect(0, 0, panel.width, panel.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(20f, 20f, 20f, 20f)));
            _chipS = Keep(Sprite.Create(chip, new Rect(0, 0, chip.width, chip.height), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(10f, 10f, 10f, 10f)));

            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { _font = f; break; }

            _root = new GameObject("PowertrainGearHud");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            _box = Rect("Box", _root.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(PanelWidth, 200f));
            _group = _box.gameObject.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _border = Img("Border", _box, _panelS, BorderColor, Image.Type.Sliced);
            Stretch(_border.rectTransform, -1f);
            _panel = Img("Panel", _box, _panelS, PanelBg, Image.Type.Sliced);
            Stretch(_panel.rectTransform, 0f);
            
            _strip = Img("Strip", _box, _chipS, IdleColor, Image.Type.Sliced);
            _strip.pixelsPerUnitMultiplier = 5f;
            Anchor(_strip.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, StripBottom), new Vector2(-2f * StripInset, StripHeight));
            _strip.rectTransform.anchorMax = new Vector2(1f, 0f);

            _column = Rect("Column", _box, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            _status = Text("Status", _box, 15f, TextAlignmentOptions.BottomRight, StatusColor);
            Anchor(_status.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-13f, 12f), new Vector2(PanelWidth - 20f, 24f));
            _mode = Text("Mode", _box, 11f, TextAlignmentOptions.TopLeft, Gold);
            Anchor(_mode.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -9f), new Vector2(PanelWidth - 20f, 18f));

            _chipRt = Rect("Chip", _box, new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(62f, 22f));
            _chip = Img("ChipBg", _chipRt, _chipS, LabelBg, Image.Type.Sliced);
            Stretch(_chip.rectTransform, 0f);
            _chipText = Text("ChipText", _chipRt, 12f, TextAlignmentOptions.Center, Gold);
            Stretch(_chipText.rectTransform, 0f);
            _chipText.text = "Q / E";

            _root.SetActive(false);
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("Gear panel unavailable: " + e.Message);
            if (_root != null) Object.Destroy(_root);
            _root = null;
            return false;
        }
    }

    
    private void Layout(GearSelector sel)
    {
        var view = Math.Min(sel.Count, Math.Clamp(Plugin.VisibleGears.Value, 3, 12));
        var key = (sel.Manual ? "M" : "A") + sel.Count + "/" + view;
        if (key == _layoutKey) return;
        _layoutKey = key;
        foreach (var n in _nodes) Object.Destroy(n.Rt.gameObject);
        foreach (var b in _bridges) Object.Destroy(b.Rt.gameObject);
        _nodes.Clear();
        _bridges.Clear();
        if (_trail != null) Object.Destroy(_trail.gameObject);
        if (_knob != null) Object.Destroy(_knob.gameObject);
        if (_pulse != null) Object.Destroy(_pulse.gameObject);

        var count = sel.Count;
        _view = view;
        _spacing = Mathf.Clamp(230f / Mathf.Max(1, view - 1), 30f, 40f);
        var panelHeight = (view - 1) * _spacing + NodeSize + TopPad + BottomPad;
        _box.sizeDelta = new Vector2(PanelWidth, panelHeight);

        
        for (var i = 0; i < count - 1; i++)
        {
            var rt = Rect("Bridge" + i, _column, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(BridgeWidth, _spacing - NodeSize + 12f));
            var bar = Img("Bar", rt, _panelS, BridgeColor, Image.Type.Sliced);
            bar.pixelsPerUnitMultiplier = 2.8f;
            Stretch(bar.rectTransform, 0f);
            var line = Img("Line", rt, null, LineColor, Image.Type.Simple);
            Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, _spacing - NodeSize + 12f));
            _bridges.Add(new Bridge { Rt = rt, Bar = bar, Line = line });
        }
        
        for (var i = 0; i < count; i++)
        {
            var rt = Rect("Node" + i, _column, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(NodeSize, NodeSize));
            var fill = Img("Fill", rt, _discS, FillColor, Image.Type.Simple);
            Stretch(fill.rectTransform, -2f);
            _nodes.Add(new Node { Rt = rt, Fill = fill });
        }
        _trail = Img("Trail", _column, _panelS, new Color(TrailColor.r, TrailColor.g, TrailColor.b, 0f), Image.Type.Sliced);
        _trail.rectTransform.anchorMin = _trail.rectTransform.anchorMax = Vector2.zero;
        _trail.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _trail.pixelsPerUnitMultiplier = 2.2f;
        _knob = Img("Knob", _column, _discS, KnobColor, Image.Type.Simple);
        Anchor(_knob.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(KnobSize, KnobSize));
        for (var i = 0; i < count; i++)
        {
            var n = _nodes[i];
            n.Ring = Img("Ring", n.Rt, _ringS, Color.white, Image.Type.Simple);
            Stretch(n.Ring.rectTransform, 0f);
            n.Label = Text("Label", n.Rt, sel.Label(i).Length > 1 ? 11f : 14f, TextAlignmentOptions.Center, Color.white);
            Stretch(n.Label.rectTransform, 0f);
            n.Label.text = sel.Label(i);
        }
        _pulse = Img("Pulse", _column, _glowS, new Color(PulseColor.r, PulseColor.g, PulseColor.b, 0f), Image.Type.Simple);
        Anchor(_pulse.rectTransform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(NodeSize * 1.6f, NodeSize * 1.6f));

        _seenVersion = -1;
        _scrollTarget = -1f;
        _knobSlot = _fromSlot = _toSlot = sel.Pos;
        _mode.text = sel.Manual ? "MANUAL" : "AUTO";
    }

    

    private void Animate(GearSelector sel, bool altHeld)
    {
        var now = Time.unscaledTime;
        var dt = Time.unscaledDeltaTime;
        var count = _nodes.Count;
        if (sel.Version != _seenVersion)
        {
            var first = _seenVersion < 0;
            _seenVersion = sel.Version;
            _fromSlot = first ? sel.Pos : _knobSlot;
            _toSlot = sel.Pos;
            if (first) _knobSlot = _toSlot;
        }

        
        var maxScroll = Math.Max(0, count - _view);
        if (_scrollTarget < 0f)
        {
            _scrollTarget = Mathf.Clamp(sel.NeutralPos - 1, 0, maxScroll);
            _scroll = _scrollTarget;
        }
        if (sel.Pos < _scrollTarget + 1) _scrollTarget = sel.Pos - 1;
        if (sel.Pos > _scrollTarget + _view - 2) _scrollTarget = sel.Pos - _view + 2;
        _scrollTarget = Mathf.Clamp(_scrollTarget, 0, maxScroll);
        _scroll = Mathf.Lerp(_scroll, _scrollTarget, 1f - Mathf.Exp(-dt * 14f));
        if (Mathf.Abs(_scroll - _scrollTarget) < 0.001f) _scroll = _scrollTarget;

        var t = now - sel.ShiftTime;
        var u = Mathf.Clamp01(t / MoveTime);
        var ease = 1f - (1f - u) * (1f - u) * (1f - u);
        _knobSlot = Mathf.Lerp(_fromSlot, _toSlot, ease);
        float Y(float slot) => (slot - _scroll) * _spacing;

        
        var lo = _scroll;
        var hi = _scroll + _view - 1;
        float Visible(float slot) => Mathf.Clamp01(1f - Mathf.Max(0f, Mathf.Max(lo - slot, slot - hi)) / EdgeFade);
        for (var i = 0; i < count; i++)
        {
            var n = _nodes[i];
            var a = Visible(i);
            var selected = i == sel.Pos;
            var flash = selected && t < MoveTime + 0.3f ? Mathf.Clamp01(1f - (t - MoveTime) / 0.3f) : 0f;
            n.Rt.anchoredPosition = new Vector2(0f, Y(i));
            n.Rt.localScale = Vector3.one * (1f + 0.14f * flash);
            n.Fill.color = new Color(FillColor.r, FillColor.g, FillColor.b, FillColor.a * a);
            
            var ring = selected ? Color.white : RingColor;
            n.Ring.color = new Color(ring.r, ring.g, ring.b, (selected ? 1f : 0.9f) * a);
            n.Label.color = new Color(1f, 1f, 1f, a);
        }
        for (var i = 0; i < _bridges.Count; i++)
        {
            var b = _bridges[i];
            var a = Mathf.Min(Visible(i), Visible(i + 1));
            b.Rt.anchoredPosition = new Vector2(0f, Y(i + 0.5f));
            b.Bar.color = new Color(BridgeColor.r, BridgeColor.g, BridgeColor.b, BridgeColor.a * a);
            b.Line.color = new Color(LineColor.r, LineColor.g, LineColor.b, LineColor.a * a);
        }

        _knob.rectTransform.anchoredPosition = new Vector2(0f, Y(_knobSlot));

        
        var trailAlpha = 0.55f * (1f - Mathf.Clamp01(t / 0.35f));
        var from = Y(_fromSlot);
        var cur = Y(_knobSlot);
        var lowY = Mathf.Min(from, cur);
        var highY = Mathf.Max(from, cur);
        _trail.rectTransform.anchoredPosition = new Vector2(0f, (lowY + highY) * 0.5f);
        _trail.rectTransform.sizeDelta = new Vector2(KnobSize * 0.62f, highY - lowY + KnobSize * 0.62f);
        _trail.color = new Color(TrailColor.r, TrailColor.g, TrailColor.b, highY - lowY > 1f ? trailAlpha : 0f);

        
        var pu = Mathf.Clamp01((t - MoveTime * 0.9f) / PulseTime);
        var pulseOn = t >= MoveTime * 0.9f && pu < 1f;
        _pulse.rectTransform.anchoredPosition = new Vector2(0f, Y(_toSlot));
        _pulse.rectTransform.localScale = Vector3.one * (0.8f + 0.9f * pu);
        _pulse.color = new Color(PulseColor.r, PulseColor.g, PulseColor.b, pulseOn ? 0.9f * (1f - pu) * (1f - pu) : 0f);

        
        var bt = now - sel.BlockedTime;
        var blocked = bt < 0.5f ? 1f - bt / 0.5f : 0f;
        _knob.color = Color.Lerp(KnobColor, Red, blocked);

        
        var kick = t < 0.3f ? -sel.LastStep * 4f * Mathf.Sin(Mathf.PI * t / 0.3f) : 0f;
        var shake = bt < 0.4f ? 7f * Mathf.Sin(bt * 48f) * (1f - bt / 0.4f) : 0f;
        _column.anchoredPosition = new Vector2(PanelWidth * 0.5f + shake, BottomPad + NodeSize * 0.5f + kick);

        _status.text = sel.NoDriver ? "NO DRIVER" : sel.StatusText();
        _status.color = sel.NoDriver ? Red : StatusColor;
        _chipText.text = altHeld ? "ALT" : "Q / E";
        _chip.color = altHeld ? LabelBgSel : LabelBg;
        _chipText.color = altHeld ? Color.white : Gold;

        
        var dir = sel.Direction;
        Color strip;
        if (sel.NoDriver) strip = new Color(Red.r, Red.g, Red.b, 0.55f + 0.45f * Mathf.Sin(now * 9f));
        else if (dir == 0) strip = IdleColor;
        else
        {
            var engaged = sel.ActiveMode == (dir > 0 ? 1 : 4) && (!sel.Manual || sel.ActiveGear == sel.GearIndex);
            strip = engaged ? EngagedColor : BusyColor;
        }
        _strip.color = strip;
    }

    
    private void Place()
    {
        var now = Time.unscaledTime;
        if (now >= _nextMunitionsLook)
        {
            _nextMunitionsLook = now + 1f;
            if (_munitionsText == null || _munitionsText.WasCollected)
            {
                _munitionsText = null;
                try
                {
                    var hud = GameObject.Find("MunitionsHud");
                    var ammo = hud != null ? hud.transform.Find("Ammo") : null;
                    if (ammo != null) _munitionsText = ammo.GetComponent<TMP_Text>();
                }
                catch { }
            }
        }
        var target = Margin;
        var vanilla = VanillaHud.BottomRightTop();
        if (vanilla > 0f) target = Mathf.Max(target, vanilla + 12f);
        try
        {
            if (_munitionsText != null && !_munitionsText.WasCollected && _munitionsText.gameObject.activeInHierarchy
                && !string.IsNullOrEmpty(_munitionsText.text))
            {
                var h = _munitionsText.preferredHeight;
                var bottom = _munitionsText.rectTransform.anchoredPosition.y; 
                if (h > 1f) target = Mathf.Max(target, bottom + h + 14f);
            }
        }
        catch { }
        _bottom = Mathf.MoveTowards(_bottom, target, Time.unscaledDeltaTime * 600f);
        _box.anchoredPosition = new Vector2(-Margin, _bottom);
    }

    

    private static T Keep<T>(T o) where T : Object
    {
        if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return o;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        Anchor(rt, anchor, pivot, pos, size);
        return rt;
    }

    private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static Image Img(string name, Transform parent, Sprite sprite, Color color, Image.Type type)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = type;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI Text(string name, Transform parent, float size, TextAlignmentOptions align, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var t = go.AddComponent<TextMeshProUGUI>();
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
}
