using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Munitions;









internal sealed class AmmoBar
{
    private const float CellW = 76f, LabelH = 22f, IconH = 70f, Gap = 6f, Bottom = 20f, Strip = 3f;
    private const float PanelW = 200f, PanelH = 26f, BarX = 122f;

    private static readonly Color LabelBg = new(0.21f, 0.28f, 0.33f, 0.9f);
    private static readonly Color LabelBgSel = new(0.3f, 0.39f, 0.45f, 0.95f);
    private static readonly Color LabelText = new(0.95f, 0.8f, 0.38f, 1f);
    private static readonly Color CellBg = new(0.06f, 0.08f, 0.09f, 0.55f);
    private static readonly Color CellBorder = new(0.3f, 0.36f, 0.4f, 0.75f);
    private static readonly Color SelBorder = new(0.93f, 0.93f, 0.93f, 1f);
    private static readonly Color Loaded = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color Busy = new(1f, 0.75f, 0.25f, 1f);
    private static readonly Color Recharge = new(0.7f, 0.8f, 0.9f, 0.9f);
    private static readonly Color Dim = new(0.42f, 0.42f, 0.42f, 0.6f);
    private static readonly Color FireRed = new(1f, 0.3f, 0.2f, 1f);

    private enum SlotId { Apfsds, He, Heat, Atgm, Ap, Aphe, Hesh, Smoke, Tda, Fire, Count }

    private sealed class Slot
    {
        public RectTransform Root;
        public Image LabelBox, CellBox, StripImg;
        public readonly Image[] Border = new Image[4];
        public TextMeshProUGUI Label, CountText;
        public RawImage Main;
    }

    private sealed class Panel
    {
        public RectTransform Root;
        public Image Bg, BarBg, BarFill;
        public RawImage Icon;
        public TextMeshProUGUI Key, CountText;
    }

    private GameObject _canvasGo;
    private RectTransform _root;
    private bool _failed;
    private TMP_FontAsset _font;
    private Sprite _white;
    private readonly Dictionary<string, Texture2D> _tex = new();
    private readonly Slot[] _slots = new Slot[(int)SlotId.Count];
    private readonly List<Panel> _panels = new();
    private TextMeshProUGUI _status, _message;

    
    private readonly Dictionary<IntPtr, int> _seen = new();
    private readonly Dictionary<IntPtr, int> _maxRounds = new();

    public void Hide()
    {
        if (_canvasGo != null && !_canvasGo.WasCollected && _canvasGo.activeSelf) _canvasGo.SetActive(false);
    }

    public void Update(bool inControl, PlayerView view, List<Launchers.GroupInfo> tubes, string message)
    {
        if (!inControl || !Plugin.ShowHud.Value) { Hide(); return; }
        var smoke = HudBridge.Smoke();
        var tda = HudBridge.Tda();
        var fire = HudBridge.Fire();

        
        var show = new bool[(int)SlotId.Count];
        var ammo = view.Valid && !view.Launcher && !view.AutoVanilla;
        if (ammo && Plugin.HudShells.Value)
        {
            _seen.TryGetValue(view.Weapon, out var mask);
            for (var k = 0; k < Kind.Count; k++) if (view.Counts[k] > 0) mask |= 1 << k;
            if (_seen.Count > 256) _seen.Clear();
            _seen[view.Weapon] = mask;
            show[(int)SlotId.Apfsds] = (mask & (1 << Kind.Apfsds)) != 0;
            show[(int)SlotId.He] = (mask & (1 << Kind.He)) != 0;
            show[(int)SlotId.Heat] = (mask & (1 << Kind.Heat)) != 0;
            show[(int)SlotId.Atgm] = (mask & (1 << Kind.Atgm)) != 0 && view.AtgmPossible;
            show[(int)SlotId.Ap] = (mask & (1 << Kind.Ap)) != 0;
            show[(int)SlotId.Aphe] = (mask & (1 << Kind.Aphe)) != 0;
            show[(int)SlotId.Hesh] = (mask & (1 << Kind.Hesh)) != 0;
        }
        show[(int)SlotId.Smoke] = smoke.w > 0.5f && Plugin.HudSmoke.Value;
        show[(int)SlotId.Tda] = tda.w > 0.5f && Plugin.HudEngineSmoke.Value;
        show[(int)SlotId.Fire] = fire.w > 0.5f && Plugin.HudExtinguisher.Value;
        var belt = view.Valid && view.Auto != null && Plugin.HudBelt.Value;
        if (!Plugin.HudLaunchers.Value) tubes = null;
        var statusLine = Plugin.HudStatus.Value;

        var anySlot = false;
        foreach (var s in show) anySlot |= s;
        var weaponPanels = (belt ? 1 : 0) + (tubes?.Count ?? 0);
        if (!anySlot && weaponPanels == 0) { Hide(); return; }
        if (!Ensure()) return;
        if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

        
        var visible = new List<SlotId>();
        for (var i = 0; i < show.Length; i++) if (show[i]) visible.Add((SlotId)i);
        var total = visible.Count * CellW + Math.Max(0, visible.Count - 1) * Gap;
        for (var i = 0; i < _slots.Length; i++) _slots[i].Root.gameObject.SetActive(false);
        for (var n = 0; n < visible.Count; n++)
        {
            var id = visible[n];
            var slot = _slots[(int)id];
            slot.Root.gameObject.SetActive(true);
            slot.Root.anchoredPosition = new Vector2(-total * 0.5f + n * (CellW + Gap) + CellW * 0.5f, Bottom);
            UpdateSlot(slot, id, view, smoke, tda, fire);
        }

        
        var top = Bottom + (visible.Count > 0 ? LabelH + IconH + 10f : 0f);
        var panels = new List<(string Key, float Count, string CountText, float Fraction, bool Selected, bool Warn, string Icon)>();
        if (belt)
        {
            var rounds = view.AutoVanilla ? view.VanillaRounds : Kind.Sum(view.Counts);
            _maxRounds.TryGetValue(view.Weapon, out var max);
            if (rounds > max) { max = rounds; if (_maxRounds.Count > 256) _maxRounds.Clear(); _maxRounds[view.Weapon] = max; }
            panels.Add(("", rounds, rounds.ToString(), max > 0 ? rounds / (float)max : 0f, true, rounds == 0, "mg.png"));
        }
        if (tubes != null)
            foreach (var t in tubes)
            {
                var sel = view.Launcher && t.Group == view.LauncherGroup;
                var icon = !t.Rocket ? "atgm.png" : t.Thermobaric ? "nur_thermobaric.png" : "nur_high_explosive.png";
                panels.Add((t.Key, t.Ready, $"{t.Ready}/{t.Total}", t.Total > 0 ? t.Ready / (float)t.Total : 0f, sel, !t.Crewed || t.Ready == 0, icon));
            }
        while (_panels.Count < panels.Count) _panels.Add(NewPanel());
        var pw = panels.Count * PanelW + Math.Max(0, panels.Count - 1) * 8f;
        for (var i = 0; i < _panels.Count; i++)
        {
            var p = _panels[i];
            if (i >= panels.Count) { p.Root.gameObject.SetActive(false); continue; }
            var d = panels[i];
            p.Root.gameObject.SetActive(true);
            p.Root.anchoredPosition = new Vector2(-pw * 0.5f + i * (PanelW + 8f) + PanelW * 0.5f, top);
            
            var atgm = d.Icon == "atgm.png" || d.Icon.StartsWith("nur_", StringComparison.Ordinal);
            p.Icon.texture = Tex(d.Icon);
            p.Icon.uvRect = atgm ? new Rect(0.33f, 0.05f, 0.34f, 0.9f) : new Rect(0f, 0f, 1f, 1f);
            p.Icon.rectTransform.localEulerAngles = new Vector3(0f, 0f, atgm ? -90f : 0f);
            p.Icon.rectTransform.sizeDelta = atgm ? new Vector2(17.5f, 46f) : new Vector2(46f, 20f);
            p.Icon.color = d.Warn ? Dim : Color.white;
            p.Key.text = d.Key;
            p.CountText.text = d.CountText;
            p.CountText.color = d.Warn ? new Color(1f, 0.55f, 0.5f, 1f) : Color.white;
            p.Bg.color = d.Selected ? new Color(0.16f, 0.21f, 0.25f, 0.85f) : new Color(0.07f, 0.09f, 0.1f, 0.7f);
            var fill = p.BarFill.rectTransform;
            fill.sizeDelta = new Vector2(Mathf.Clamp01(d.Fraction) * (PanelW - BarX - 6f), 8f);
        }
        if (panels.Count > 0) top += PanelH + 8f;

        
        _status.text = statusLine ? FireText(fire) ?? StatusText(view) : "";
        _status.color = fire.y > 1.5f ? FireRed : new Color(0.85f, 0.87f, 0.88f, 1f);
        _status.rectTransform.anchoredPosition = new Vector2(0f, top);
        _message.text = statusLine ? message ?? "" : "";
        _message.rectTransform.anchoredPosition = new Vector2(0f, top + 22f);
    }

    
    private static string FireText(Vector4 fire)
    {
        if (fire.w < 0.5f) return null;
        if (fire.y > 0.5f && fire.y < 1.5f) return "EXTINGUISHING";
        if (fire.y < 1.5f) return null;
        if (fire.x < 0.5f) return "FIRE!  NO EXTINGUISHER CHARGES";
        return fire.w > 1.5f ? $"FIRE!  [{HudBridge.FireKey()}] EXTINGUISH" : "FIRE!  AUTOMATIC EXTINGUISHER";
    }

    private static string StatusText(PlayerView view)
    {
        if (!view.Valid || view.Launcher) return "";
        if (view.AutoVanilla) return view.VanillaRounds > 0 ? "" : "NO AMMO — MAKE AN AMMO RACK OF THIS CALIBER";
        var autoloader = view.Carousel != null ? Autoloader.StateText(view.Carousel, view.LoadedKind >= 0) : null;
        if (autoloader != null) return autoloader;
        if (view.LoadingKind >= 0) return "LOADING " + Kind.Names[view.LoadingKind];
        if (view.LoadedKind >= 0) return "";
        if (view.Auto != null && !view.Auto.Empty) return "";
        return "EMPTY";
    }

    private void UpdateSlot(Slot s, SlotId id, PlayerView view, Vector4 smoke, Vector4 tda, Vector4 fire)
    {
        var selected = false;
        var dim = false;
        string count;
        Color? strip = null;
        var stripFrac = 1f;
        switch (id)
        {
            case SlotId.Smoke:
                count = $"{(int)smoke.x}/{(int)smoke.y}";
                dim = smoke.x < 0.5f;
                if (smoke.z > 0.5f) strip = Busy;
                s.Label.text = HudBridge.SmokeKey();
                break;
            case SlotId.Tda:
                
                var active = tda.y > 0.5f;
                count = (active ? Mathf.CeilToInt(tda.x - 0.001f) : Mathf.FloorToInt(tda.x + 0.001f)) + "s";
                dim = tda.x < 0.5f && !active;
                if (active) { strip = Busy; stripFrac = tda.z; }
                else if (tda.z < 0.999f) { strip = Recharge; stripFrac = tda.z; }
                s.Label.text = HudBridge.TdaKey();
                break;
            case SlotId.Fire:
                
                count = ((int)fire.x).ToString();
                dim = fire.x < 0.5f;
                if (fire.y > 0.5f && fire.y < 1.5f) strip = Busy;
                else if (fire.y > 1.5f) strip = new Color(FireRed.r, FireRed.g, FireRed.b, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 9f));
                s.Label.text = HudBridge.FireKey();
                break;
            default:
            {
                var kind = KindOf(id);
                var n = view.Counts[kind];
                count = n.ToString();
                dim = n == 0;
                selected = Controller.Selected == kind;
                if (view.LoadingKind == kind) strip = Busy;
                else if (view.LoadedKind == kind) strip = Loaded;
                s.Label.text = KeyLabel(Controller.Keys[kind]);
                break;
            }
        }
        s.CountText.text = count;
        s.CountText.color = dim ? new Color(0.7f, 0.7f, 0.7f, 0.8f) : Color.white;
        var pulse = (id == SlotId.Tda && tda.y > 0.5f) || (id == SlotId.Fire && fire.y > 0.5f);
        s.Main.color = dim ? Dim : pulse ? new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f)) : Color.white;
        foreach (var b in s.Border) b.color = selected ? SelBorder : CellBorder;
        var w = selected ? 2f : 1f;
        SetBorder(s, w);
        s.LabelBox.color = selected ? LabelBgSel : LabelBg;
        if (strip != null)
        {
            s.StripImg.gameObject.SetActive(true);
            s.StripImg.color = strip.Value;
            s.StripImg.rectTransform.sizeDelta = new Vector2((CellW - 4f) * Mathf.Clamp01(stripFrac), Strip);
        }
        else s.StripImg.gameObject.SetActive(false);
    }

    private static int KindOf(SlotId id) => id switch
    {
        SlotId.Apfsds => Kind.Apfsds,
        SlotId.He => Kind.He,
        SlotId.Heat => Kind.Heat,
        SlotId.Ap => Kind.Ap,
        SlotId.Aphe => Kind.Aphe,
        SlotId.Hesh => Kind.Hesh,
        _ => Kind.Atgm
    };

    private static string KeyLabel(UnityEngine.InputSystem.Key key)
    {
        var s = key.ToString();
        return s.StartsWith("Digit") ? s.Substring(5) : s.StartsWith("Numpad") ? s.Substring(6) : s;
    }

    

    private bool Ensure()
    {
        if (_root != null && !_root.WasCollected) return true;
        if (_failed) return false;
        try
        {
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null) foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { _font = f; break; }
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (var i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply();
            t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            _white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            _white.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _canvasGo = new GameObject("MunitionsAmmoBar");
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            _root = Rect("Bar", _canvasGo.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero);
            _slots[(int)SlotId.Apfsds] = NewSlot("apdsfs_tank.png");
            _slots[(int)SlotId.He] = NewSlot("he_frag_tank.png");
            _slots[(int)SlotId.Heat] = NewSlot("heat_fs_tank.png");
            _slots[(int)SlotId.Atgm] = NewSlot("atgm.png");
            _slots[(int)SlotId.Ap] = NewSlot("ap_tank.png");
            _slots[(int)SlotId.Aphe] = NewSlot("aphe_tank.png");
            _slots[(int)SlotId.Hesh] = NewSlot("hesh_tank.png");
            _slots[(int)SlotId.Smoke] = NewSlot("smoke_screen.png");
            _slots[(int)SlotId.Tda] = NewSlot("engine_smoke_screen_system.png");
            _slots[(int)SlotId.Fire] = NewSlot("extinguisher.png");

            _status = Text("Status", _root, 15f, TextAlignmentOptions.Bottom, new Color(0.85f, 0.87f, 0.88f, 1f));
            Anchor(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(700f, 20f));
            _message = Text("Message", _root, 16f, TextAlignmentOptions.Bottom, LabelText);
            Anchor(_message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(700f, 22f));
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("Ammo panel unavailable: " + e);
            return false;
        }
    }

    private Slot NewSlot(string icon)
    {
        var s = new Slot { Root = Rect("Slot", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(CellW, LabelH + IconH)) };
        
        s.CellBox = Img("Cell", s.Root, CellBg);
        Anchor(s.CellBox.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(CellW, IconH));
        var side = IconH - 6f;
        s.Main = Raw("Icon", s.Root, Tex(icon));
        Anchor(s.Main.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, IconH * 0.5f), new Vector2(side, side));
        for (var i = 0; i < 4; i++) s.Border[i] = Img("Border", s.Root, CellBorder);
        SetBorder(s, 1f);
        
        s.LabelBox = Img("Key", s.Root, LabelBg);
        Anchor(s.LabelBox.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, IconH + 2f), new Vector2(CellW, LabelH - 2f));
        s.Label = Text("KeyText", s.LabelBox.rectTransform, 16f, TextAlignmentOptions.Center, LabelText);
        Stretch(s.Label.rectTransform);
        
        s.CountText = Text("Count", s.Root, 19f, TextAlignmentOptions.BottomRight, Color.white);
        Anchor(s.CountText.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(CellW * 0.5f - 4f, 1f), new Vector2(CellW - 6f, 24f));
        try
        {
            s.CountText.outlineWidth = 0.22f;
            s.CountText.outlineColor = new Color32(0, 0, 0, 230);
        }
        catch { }
        
        s.StripImg = Img("State", s.Root, Loaded);
        Anchor(s.StripImg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, IconH - 1f), new Vector2(CellW - 4f, Strip));
        s.StripImg.gameObject.SetActive(false);
        return s;
    }

    private static void SetBorder(Slot s, float w)
    {
        Anchor(s.Border[0].rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(CellW, w));
        Anchor(s.Border[1].rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, IconH), new Vector2(CellW, w));
        Anchor(s.Border[2].rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(-CellW * 0.5f, 0f), new Vector2(w, IconH));
        Anchor(s.Border[3].rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(CellW * 0.5f, 0f), new Vector2(w, IconH));
    }

    private Panel NewPanel()
    {
        var p = new Panel { Root = Rect("Weapon", _root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(PanelW, PanelH)) };
        p.Bg = Img("Bg", p.Root, new Color(0.07f, 0.09f, 0.1f, 0.7f));
        Stretch(p.Bg.rectTransform);
        
        p.Key = Text("Key", p.Root, 13f, TextAlignmentOptions.Left, LabelText);
        Anchor(p.Key.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(24f, PanelH));
        p.Icon = Raw("Icon", p.Root, Tex("mg.png"));
        Anchor(p.Icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(51f, 0f), new Vector2(46f, 20f));
        p.CountText = Text("Count", p.Root, 15f, TextAlignmentOptions.Right, Color.white);
        Anchor(p.CountText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(BarX - 6f, 0f), new Vector2(40f, PanelH));
        p.BarBg = Img("BarBg", p.Root, new Color(1f, 1f, 1f, 0.15f));
        Anchor(p.BarBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BarX, 0f), new Vector2(PanelW - BarX - 6f, 8f));
        p.BarFill = Img("BarFill", p.Root, new Color(0.92f, 0.92f, 0.92f, 0.9f));
        Anchor(p.BarFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BarX, 0f), new Vector2(PanelW - BarX - 6f, 8f));
        return p;
    }

    private Texture2D Tex(string file)
    {
        if (_tex.TryGetValue(file, out var t) && t != null && !t.WasCollected) return t;
        t = null;
        try
        {
            var path = Path.Combine(Plugin.PluginDir, "MDMunitions", "Hud", file);
            if (!File.Exists(path)) path = Path.Combine(Plugin.PluginDir, "Munitions", "Hud", file);
            if (!File.Exists(path)) path = Path.Combine(Plugin.PluginDir, "Hud", file);
            if (File.Exists(path))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Ammo bar " + file, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (!ImageConversion.LoadImage(t, new Il2CppStructArray<byte>(File.ReadAllBytes(path)))) { Object.Destroy(t); t = null; }
                else t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            else Plugin.Log.LogWarning("Ammo panel icon missing: " + path);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Ammo panel icon {file} failed: {e.Message}"); }
        _tex[file] = t;
        return t;
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Image Img(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.sprite = _white;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static RawImage Raw(string name, Transform parent, Texture tex)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
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
