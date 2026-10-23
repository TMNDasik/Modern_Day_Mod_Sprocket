using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using DynamicGUI;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModMenu;





public class MenuWindow : MonoBehaviour
{
    public MenuWindow(IntPtr ptr) : base(ptr) { }

    public static bool IsOpen;

    private sealed class Mod
    {
        public string Guid, Name, Version;
        public ConfigFile Config;
        public bool SaveOnSet;
        public bool Switchable;   
        public bool Running;      
        public readonly List<ConfigEntryBase> Entries = new();
    }

    private Panel _panel;
    private DynamicGUILayout _layout;
    private List<Mod> _mods;
    private HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private bool _redraw;
    private readonly HashSet<string> _expanded = new();
    private readonly HashSet<ConfigFile> _unsaved = new();
    private float _nextSave;
    private int _errors;

    private static readonly Vector2 WindowSize = new(820f, 700f);
    private const string WindowTitle = "Mod menu";
    private static readonly Color Notice = new(1f, 0.72f, 0.38f, 1f);

    private void Update()
    {
        try
        {
            var kb = Keyboard.current;
            if (kb != null && Plugin.OpenKey != Key.None && kb[Plugin.OpenKey].wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }

            
            if (IsOpen && (_panel == null || _panel.WasCollected)) Close();
            if (IsOpen && _redraw) Draw();

            if (_unsaved.Count > 0 && (Time.unscaledTime >= _nextSave || !IsOpen)) SaveAll();
        }
        catch (Exception e)
        {
            if (_errors++ < 5) Plugin.Log.LogError("Mod menu failed: " + e);
        }
    }

    

    private void Open()
    {
        _mods = FindMods();
        _disabled = DisabledList.Read();
        try
        {
            _panel = Panel.New(WindowTitle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), WindowSize);
            var canvas = _panel.canvas;
            if (canvas != null) canvas.sortingOrder = 30000; 
            _layout = _panel.Layout;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Could not open the Mod menu: " + e);
            _panel = null;
            _layout = null;
            return;
        }
        
        foreach (var m in _mods)
        {
            if (m.Config == null) continue;
            m.SaveOnSet = m.Config.SaveOnConfigSet;
            m.Config.SaveOnConfigSet = false;
        }
        IsOpen = true;
        _redraw = true;
    }

    private void Close()
    {
        IsOpen = false;
        
        ClearDecor();
        try
        {
            if (_panel != null && !_panel.WasCollected) _panel.Release();
        }
        catch { }
        _panel = null;
        _layout = null;
        if (_mods != null)
        {
            SaveAll();
            foreach (var m in _mods)
                if (m.Config != null) m.Config.SaveOnConfigSet = m.SaveOnSet;
        }
    }

    private void SaveAll()
    {
        foreach (var c in _unsaved)
        {
            try { c.Save(); }
            catch (Exception e) { Plugin.Log.LogWarning("Config save failed: " + e.Message); }
        }
        _unsaved.Clear();
    }

    private void Changed(ConfigFile config)
    {
        if (_unsaved.Count == 0) _nextSave = Time.unscaledTime + 1f;
        _unsaved.Add(config);
    }

    
    
    
    private static readonly HashSet<string> OurGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprocket.activeprotection", "sprocket.combateffects", "sprocket.crewvoices", "sprocket.drivetuning", "sprocket.fpvdrones", "sprocket.fpvpilot",
        "sprocket.gunstabilizer", "sprocket.modendaymenu", "sprocket.modernday", "sprocket.munitions", "sprocket.powertrain",
        "sprocket.showroommenu", "sprocket.soundoverhaul", "sprocket.tankedit", "sprocket.thermalsight.modernday",
        "sprocket.vehiclematerials", "sprocket.wheels"
    };

    private static bool IsOurs(string guid, object instance)
    {
        if (OurGuids.Contains(guid)) return true;
        try
        {
            var type = instance.GetType();
            return type.Assembly.GetType(type.Namespace + ".ModSwitch") != null;
        }
        catch { return false; }
    }

    private static List<Mod> FindMods()
    {
        var list = new List<Mod>();
        foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
        {
            try
            {
                var guid = info.Metadata.GUID;
                if (guid == Plugin.ConfigManagerGuid || info.Instance is not BasePlugin plugin) continue;
                var ours = IsOurs(guid, plugin);
                var mod = new Mod
                {
                    Guid = guid, Name = info.Metadata.Name, Version = info.Metadata.Version?.ToString(), Config = plugin.Config,
                    Switchable = ours && guid != Plugin.Guid && guid != "sprocket.configmanager.fix",
                    Running = !Plugin.StartupDisabled.Contains(guid)
                };
                if (plugin.Config != null)
                    foreach (var def in plugin.Config.Keys)
                    {
                        var entry = plugin.Config[def];
                        if (entry != null) mod.Entries.Add(entry);
                    }
                if (mod.Entries.Count > 0 || mod.Switchable) list.Add(mod);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Plugin settings unavailable: " + e.Message); }
        }
        return list
            .OrderBy(m => m.Guid == Plugin.Guid ? 2 : m.Switchable ? 0 : 1)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    

    
    
    
    
    private const int RowLines = 3;
    private const string RowMark = "mdmodrow:";
    private readonly List<GameObject> _decor = new();
    private static readonly Color RowText = new(0.93f, 0.92f, 0.88f, 1f), BarText = new(0.93f, 0.66f, 0.3f, 1f);
    private static readonly Color BarFill = new(0.07f, 0.07f, 0.07f, 0.92f), BarLine = new(0.32f, 0.28f, 0.22f, 1f);
    private static readonly Color BoxOn = new(0.98f, 0.88f, 0.72f, 1f), BoxOff = new(0.12f, 0.12f, 0.12f, 1f), DimText = new(0.6f, 0.6f, 0.6f, 1f);

    private void Draw()
    {
        _redraw = false;
        var l = _layout;
        if (l == null) return;
        ClearDecor();
        l.Clear();
        l.InfoField("Checkboxes turn mods on and off: that takes effect after restarting the game. SETTINGS opens a mod's settings; " +
                    $"they apply right away and are saved to BepInEx\\config — point at one to see what it does. {Plugin.OpenKeyConfig.Value} opens and closes this menu.", 3);

        var pending = new List<string>();
        foreach (var mod in _mods)
        {
            var on = !_disabled.Contains(mod.Guid);
            if (mod.Switchable && on != mod.Running) pending.Add(mod.Name + (on ? " on" : " off"));
        }
        if (pending.Count > 0)
            l.InfoField("Restart the game to apply: " + string.Join(", ", pending) + ".", 2, Notice);

        
        var rows = new List<Mod>();
        foreach (var mod in _mods)
        {
            if (!mod.Switchable && mod.Entries.Count == 0) continue;
            l.InfoField(RowMark + rows.Count, RowLines, new Color(0f, 0f, 0f, 0f));
            rows.Add(mod);
            if (mod.Entries.Count > 0 && _expanded.Contains(mod.Guid))
            {
                DrawMod(l, mod);
                l.AddSpace(10f);
            }
        }
        l.Apply();
        DecorateRows(rows);
    }

    

    private void ClearDecor()
    {
        foreach (var go in _decor)
        {
            try
            {
                if (go == null || go.WasCollected || !go) continue;
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }
            catch { }
        }
        _decor.Clear();
    }

    private void DecorateRows(List<Mod> rows)
    {
        var elements = _layout?.activeElements;
        if (elements == null) return;
        for (var i = 0; i < elements.Count; i++)
        {
            try
            {
                var label = elements[i]?.TryCast<LabelField>();
                var text = label?.TextBox != null ? label.TextBox.text : null;
                if (text == null || !text.StartsWith(RowMark, StringComparison.Ordinal)) continue;
                if (!int.TryParse(text.Substring(RowMark.Length), out var index) || index < 0 || index >= rows.Count) continue;
                label.TextBox.color = new Color(0f, 0f, 0f, 0f);   
                Decorate(rows[index], label);
            }
            catch (Exception e)
            {
                if (_errors++ < 10) Plugin.Log.LogWarning("Mod row not drawn: " + e.Message);
            }
        }
    }

    private void Decorate(Mod m, LabelField host)
    {
        var font = host.TextBox;
        var hostRt = host.RectTransform;
        var height = hostRt.rect.height > 20f ? hostRt.rect.height : 66f;
        var root = new GameObject("ModRow");
        _decor.Add(root);
        var rt = root.AddComponent<RectTransform>();
        rt.SetParent(hostRt, false);
        Stretch(rt, 0f, 0f, 0f, 0f);

        
        var iconSize = height - 8f;
        var icon = UiImage(rt, "Icon", Color.white);
        icon.sprite = ModIcons.For(m.Guid);
        icon.preserveAspect = true;
        var irt = icon.rectTransform;
        irt.anchorMin = new Vector2(0f, 0.5f);
        irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.sizeDelta = new Vector2(iconSize, iconSize);
        irt.anchoredPosition = new Vector2(4f, 0f);
        var left = iconSize + 14f;

        
        var on = !_disabled.Contains(m.Guid);
        var title = string.IsNullOrEmpty(m.Version) ? m.Name : $"{m.Name}  {m.Version}";
        var nameLeft = left;
        if (m.Switchable)
        {
            
            var box = UiImage(rt, "Checkbox", BoxOn);
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 0.75f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.sizeDelta = new Vector2(24f, 24f);
            brt.anchoredPosition = new Vector2(left, 0f);
            var inner = UiImage(brt, "Inner", on ? BoxOn : BoxOff);
            Stretch(inner.rectTransform, 2f, 2f, 2f, 2f);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = inner;
            var mod = m;
            button.onClick.AddListener((UnityAction)(Action)(() =>
            {
                var v = _disabled.Contains(mod.Guid);   
                if (v) _disabled.Remove(mod.Guid);
                else _disabled.Add(mod.Guid);
                DisabledList.Write(_disabled);
                Plugin.Log.LogInfo($"{mod.Name} will be turned {(v ? "on" : "off")} after restarting the game");
                _redraw = true;
            }));
            nameLeft = left + 34f;
        }
        var name = UiText(rt, "Name", title, font, 20f, m.Switchable && !on ? DimText : RowText, TMPro.TextAlignmentOptions.MidlineLeft);
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0.5f);
        nrt.anchorMax = new Vector2(1f, 1f);
        nrt.offsetMin = new Vector2(nameLeft, 0f);
        nrt.offsetMax = new Vector2(-8f, -2f);

        
        var has = m.Entries.Count > 0;
        var open = has && _expanded.Contains(m.Guid);
        var bar = UiImage(rt, "Settings", BarLine);
        var bart = bar.rectTransform;
        bart.anchorMin = new Vector2(0f, 0f);
        bart.anchorMax = new Vector2(1f, 0.5f);
        bart.offsetMin = new Vector2(left, 4f);
        bart.offsetMax = new Vector2(-6f, -2f);
        var fill = UiImage(bart, "Fill", BarFill);
        Stretch(fill.rectTransform, 1f, 1f, 1f, 1f);
        var label = UiText(bart, "Label", has ? "SETTINGS" : "NO SETTINGS", font, 18f, has ? BarText : DimText, TMPro.TextAlignmentOptions.MidlineLeft);
        Stretch(label.rectTransform, 10f, 0f, 30f, 0f);
        if (has)
        {
            var sign = UiText(bart, "Sign", open ? "-" : "+", font, 22f, BarText, TMPro.TextAlignmentOptions.MidlineRight);
            Stretch(sign.rectTransform, 10f, 0f, 10f, 0f);
            var button = bar.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            var guid = m.Guid;
            button.onClick.AddListener((UnityAction)(Action)(() =>
            {
                if (!_expanded.Remove(guid)) _expanded.Add(guid);
                _redraw = true;
            }));
        }
    }

    private static Image UiImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name);
        var img = go.AddComponent<Image>();
        img.rectTransform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = true;
        return img;
    }

    
    
    private static TMPro.TextMeshProUGUI UiText(RectTransform parent, string name, string text, TMPro.TMP_Text template, float size, Color color,
        TMPro.TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
        t.rectTransform.SetParent(parent, false);
        var font = template != null ? template.font : null;
        if (font == null) font = TMPro.TMP_Settings.defaultFontAsset;
        if (font == null)
            foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
        if (font != null) t.font = font;
        if (template != null && template.fontSharedMaterial != null) t.fontSharedMaterial = template.fontSharedMaterial;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.overflowMode = TMPro.TextOverflowModes.Overflow;
        t.raycastTarget = false;
        return t;
    }

    
    private static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    private void DrawMod(DynamicGUILayout l, Mod m)
    {
        
        foreach (var section in m.Entries.Select(e => e.Definition.Section).Distinct())
        {
            l.Header(section);
            foreach (var e in m.Entries.Where(e => e.Definition.Section == section))
            {
                try { DrawEntry(l, m, e); }
                catch (Exception ex)
                {
                    if (_errors++ < 10) Plugin.Log.LogWarning($"Setting '{e.Definition.Key}' not shown: {ex.Message}");
                }
            }
        }

        UnityAction reset = (Action)(() =>
        {
            foreach (var e in m.Entries)
            {
                try { e.BoxedValue = e.DefaultValue; }
                catch { }
            }
            Changed(m.Config);
            _redraw = true;
        });
        try
        {
            var tip = new UITooltip("Reset", $"Set every setting of {m.Name} back to its default value.");
            l.Button($"Reset {m.Name} to defaults", reset, ref tip);
        }
        catch (Exception ex)
        {
            if (_errors++ < 10) Plugin.Log.LogWarning("Reset button not shown: " + ex.Message);
        }
    }

    private void DrawEntry(DynamicGUILayout l, Mod m, ConfigEntryBase e)
    {
        var name = e.Definition.Key;
        var desc = e.Description?.Description ?? "";
        var type = e.SettingType;

        if (type == typeof(bool))
        {
            Il2CppSystem.Action<bool> onToggle = (Action<bool>)(v =>
            {
                e.BoxedValue = v;
                Changed(m.Config);
            });
            l.ToggleField(name, (bool)e.BoxedValue, onToggle, desc);
            return;
        }

        if (type == typeof(float) || type == typeof(int) || type == typeof(double) || type == typeof(long))
        {
            l.Float(Number(m, e, name, desc));
            return;
        }

        if (type == typeof(string) && KeyNames.IsKey((string)e.BoxedValue))
        {
            var (options, values) = KeyNames.Options((string)e.BoxedValue);
            var current = KeyNames.IndexOf(values, (string)e.BoxedValue);
            UnityAction<int> onSelect = (Action<int>)(i =>
            {
                if (i < 0 || i >= values.Count) return;
                e.BoxedValue = values[i];
                Changed(m.Config);
            });
            l.Dropdown(name, options, current, onSelect, desc);
            return;
        }

        if (type.IsEnum)
        {
            var names = Enum.GetNames(type);
            var list = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var n in names) list.Add(KeyNames.Pretty(n));
            var current = Array.IndexOf(names, e.BoxedValue.ToString());
            UnityAction<int> onSelect = (Action<int>)(i =>
            {
                if (i < 0 || i >= names.Length) return;
                e.BoxedValue = Enum.Parse(type, names[i]);
                Changed(m.Config);
            });
            l.Dropdown(name, list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), Math.Max(0, current), onSelect, desc);
            return;
        }

        
        l.InfoField(name, 1);
        UnityAction<string> onEdit = (Action<string>)(text =>
        {
            try
            {
                e.SetSerializedValue(text);
                Changed(m.Config);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"'{text}' is not a valid value for {name}: {ex.Message}"); }
        });
        l.InputField(name, e.GetSerializedValue(), onEdit);
    }

    
    private FloatProperty Number(Mod m, ConfigEntryBase e, string name, string desc)
    {
        var type = e.SettingType;
        var integer = type == typeof(int) || type == typeof(long);
        var def = Convert.ToSingle(e.DefaultValue, CultureInfo.InvariantCulture);
        var cur = Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture);
        float min, max;
        if (!TryRange(e, out min, out max))
        {
            if (def > 0f) { min = 0f; max = Math.Max(def * 4f, integer ? 10f : 1f); }
            else if (def < 0f) { min = def * 4f; max = -def * 4f; }
            else { min = 0f; max = integer ? 100f : 1f; }
            min = Math.Min(min, cur);
            max = Math.Max(max, cur);
        }
        var step = integer ? 1f : NiceStep((max - min) / 200f);
        var format = integer || step >= 1f ? "0" : step >= 0.1f ? "0.0" : step >= 0.01f ? "0.00" : "0.###";

        Il2CppSystem.Func<float> get = (Func<float>)(() =>
        {
            try { return Convert.ToSingle(e.BoxedValue, CultureInfo.InvariantCulture); }
            catch { return min; }
        });
        Il2CppSystem.Action<float> set = (Action<float>)(v =>
        {
            try
            {
                v = Math.Clamp(v, min, max);
                object value = type == typeof(int) ? (int)Math.Round(v)
                    : type == typeof(long) ? (long)Math.Round(v)
                    : type == typeof(double) ? Math.Round((double)v, 4)
                    : (object)(float)Math.Round(v, 4);
                if (Equals(value, e.BoxedValue)) return;
                e.BoxedValue = value;
                Changed(m.Config);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Setting {name} failed: {ex.Message}"); }
        });
        return new FloatProperty(name, get, set, min, max, step, format, null, desc);
    }

    private static bool TryRange(ConfigEntryBase e, out float min, out float max)
    {
        min = max = 0f;
        var av = e.Description?.AcceptableValues;
        if (av == null || !av.GetType().IsGenericType || av.GetType().GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
            return false;
        min = Convert.ToSingle(av.GetType().GetProperty("MinValue")?.GetValue(av), CultureInfo.InvariantCulture);
        max = Convert.ToSingle(av.GetType().GetProperty("MaxValue")?.GetValue(av), CultureInfo.InvariantCulture);
        return max > min;
    }

    private static float NiceStep(float raw)
    {
        foreach (var s in new[] { 0.001f, 0.002f, 0.005f, 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f, 200f, 500f, 1000f })
            if (s >= raw) return s;
        return 1000f;
    }
}


internal static class KeyNames
{
    private static readonly string[] Common = BuildCommon();

    private static string[] BuildCommon()
    {
        var keys = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        for (var d = 1; d <= 9; d++) keys.Add("Digit" + d);
        keys.Add("Digit0");
        for (var f = 1; f <= 12; f++) keys.Add("F" + f);
        keys.AddRange(new[]
        {
            "Space", "Tab", "Enter", "Backspace", "CapsLock",
            "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt",
            "Backquote", "Minus", "Equals", "LeftBracket", "RightBracket", "Backslash", "Semicolon", "Quote",
            "Comma", "Period", "Slash",
            "UpArrow", "DownArrow", "LeftArrow", "RightArrow",
            "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
            "NumpadPlus", "NumpadMinus", "NumpadMultiply", "NumpadDivide", "NumpadPeriod", "NumpadEnter"
        });
        return keys.ToArray();
    }

    public static bool IsKey(string value) =>
        !string.IsNullOrEmpty(value) && Enum.TryParse<Key>(value, true, out var k) && k != Key.None && !int.TryParse(value, out _);

    
    public static (Il2CppSystem.Collections.Generic.IReadOnlyList<string> Options, List<string> Values) Options(string current)
    {
        var values = new List<string>(Common);
        if (Enum.TryParse<Key>(current, true, out var k) && IndexOf(values, k.ToString()) < 0) values.Insert(0, k.ToString());
        var list = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var v in values) list.Add(Pretty(v));
        return (list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), values);
    }

    public static int IndexOf(List<string> values, string key)
    {
        if (!Enum.TryParse<Key>(key, true, out var k)) return 0;
        for (var i = 0; i < values.Count; i++)
            if (Enum.TryParse<Key>(values[i], true, out var v) && v == k) return i;
        return -1;
    }

    
    public static string Pretty(string name)
    {
        if (name.StartsWith("Digit") && name.Length == 6) return name.Substring(5);
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            else if (i > 0 && char.IsDigit(c) && !char.IsDigit(name[i - 1]) && name[0] != 'F') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
