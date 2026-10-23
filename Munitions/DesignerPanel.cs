using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.AmmunitionStorage;
using VehicleDesigner.Compartments.Design;

namespace Munitions;



internal static class DesignerPanel
{
    private static bool _expanded = true;
    private static Il2CppSystem.Object _header;
    private static readonly Random Rng = new();
    private static readonly Dictionary<IntPtr, Panel> Panels = new();

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp("Ammunition"));

    private static readonly string[] Tips =
    {
        "APFSDS — armour-piercing fin-stabilized discarding sabot: a tungsten long rod in a sabot that falls away at the muzzle. Very fast and flat, the best penetration.",
        "HE — high-explosive fragmentation shell. Bursts on impact: blast and fragments wreck tracks, optics and other external parts, but it cannot go through thick armour.",
        "ATGM — gun-launched anti-tank guided missile with a tandem HEAT warhead. Flies along your line of sight (beam riding) and follows the sight with a short delay.",
        "HEAT — high-explosive anti-tank shell: a shaped charge burns a jet through the armour. Lighter and faster than a full-bore shell, and its penetration does not drop with range.",
        "AP — solid armour-piercing shot (with armour-piercing and ballistic caps). No explosive: it wrecks what it hits behind the armour. Less penetration than APFSDS, heavy and steady.",
        "APHE — armour-piercing high-explosive: an AP shell with a small bursting charge that explodes behind the armour after penetrating. A bit less penetration than solid AP, much worse for the crew.",
        "HESH — high-explosive squash head: a thin shell full of plastic explosive that flattens against the armour and bursts on it. A much stronger blast than HE, little penetration, slow."
    };

    
    private sealed class Panel
    {
        public AmmoRackEditor Editor;
        public AmmoRack Rack;
        public int Capacity, Caliber;
        public readonly FloatProperty[] Props = new FloatProperty[Kind.Count];
        public readonly FloatProperty[] Carousel = new FloatProperty[4];   
        public readonly FloatProperty[] Belt = new FloatProperty[2];       
        public int LastUsedFrame;

        public bool CarouselOn(out CarouselSpec spec) => CarouselSpec.TryRead(Rack?.Blueprint?.Description, out spec);

        
        public int[] Snapshot;
        public int SnapshotKind = -1, Seed;
        public float SnapshotTime;

        public int[] Current()
        {
            var bp = Rack?.Blueprint;
            return Design.Read(bp?.Description, Capacity, Caliber, true);
        }
    }

    [HarmonyPatch(typeof(AmmoRackEditor), nameof(AmmoRackEditor.OnGUI))]
    private static class RackPanel
    {
        private static void Postfix(AmmoRackEditor __instance, IGUILayout layout)
        {
            try { Draw(__instance, layout); }
            catch (Exception e) { Plugin.Log.LogError("Ammunition panel failed: " + e); }
        }
    }

    private static void Draw(AmmoRackEditor editor, IGUILayout layout)
    {
        var rack = editor.Component;
        var bp = rack?.Blueprint;
        if (bp == null) return;
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;

        
        AutoConnect.Process(editor, rack);
        var caliber = (int)bp.ShellDiameter;
        var capacity = Math.Max(0, rack.Capacity);
        if (!Design.Managed(caliber))
        {
            
            Il2CppSystem.Action<bool> onExpandSmall = (Action<bool>)(v => _expanded = v);
            layout.BeginDropdown(Header, _expanded, onExpandSmall);
            try { AutoConnect.DrawFeed(editor, rack, drawer); }
            catch (Exception e) { Plugin.Log.LogError("Ammo rack weapon list failed: " + e); }
            finally { layout.EndDropdown(); }
            return;
        }

        var panel = GetPanel(editor);
        panel.Editor = editor;
        panel.Rack = rack;
        panel.Capacity = capacity;
        panel.Caliber = caliber;
        panel.LastUsedFrame = UnityEngine.Time.frameCount;
        var atgm = Design.AtgmAllowed(caliber);

        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => _expanded = v);
        layout.BeginDropdown(Header, _expanded, onExpand);
        try
        {
            try { AutoConnect.DrawFeed(editor, rack, drawer); }
            catch (Exception e) { Plugin.Log.LogError("Ammo rack weapon list failed: " + e); }
            foreach (var k in Kind.Order)
            {
                var p = panel.Props[k];
                p.SetLimits(0f, k == Kind.Atgm && !atgm ? 0f : capacity);
                if (k == Kind.Atgm && !atgm) continue;
                drawer.Float(p);
            }
            if (!atgm)
            {
                var t = $"ATGM needs a gun of {Plugin.AtgmMinCaliber.Value} mm or more.";
                drawer.InfoField(t, Lines(t), new UnityEngine.Color(1f, 0.7f, 0.4f));
            }

            
            
            var carousel = panel.CarouselOn(out var spec);
            var p0 = panel;
            Il2CppSystem.Action<bool> onAz = (Action<bool>)(v => Switch(p0, v, LoaderType.Az));
            Il2CppSystem.Action<bool> onMz = (Action<bool>)(v => Switch(p0, v, LoaderType.Mz));
            Il2CppSystem.Action<bool> onBelt = (Action<bool>)(v => Switch(p0, v, LoaderType.Belt));
            drawer.ToggleField("Autoloader (AZ)", carousel && spec.Type == LoaderType.Az, onAz,
                "This rack is the carousel of an autoloader (AZ, T-72/T-90 style): in battle the number keys choose the round and R loads it. The gun needs no loader. " +
                "Turning it on turns the other loader types off.");
            drawer.ToggleField("Mech. loader (MZ)", carousel && spec.Mz, onMz,
                "Mechanized loader. This rack is the conveyor of a mechanized loader (MZ, T-64/T-80 style): works like the autoloader, with its own sounds. " +
                "Turning it on turns the other loader types off.");
            drawer.ToggleField("Belt loader", carousel && spec.Belt, onBelt,
                "This rack is the loading belt in the turret bustle (Object 640 \"Black Eagle\" style): in battle the number keys choose the round, R runs the belt " +
                "the short way until the nearest round of that type is at the rammer, then the hatch opens and the round is rammed. The gun needs no loader. " +
                "Place the rack behind the gun breech. Turning it on turns the other loader types off.");
            if (carousel)
            {
                if (spec.Belt)
                {
                    foreach (var p in panel.Belt) drawer.Float(p);
                    drawer.Float(panel.Carousel[2]);
                    drawer.Float(panel.Carousel[3]);
                }
                else foreach (var p in panel.Carousel) drawer.Float(p);
                var at = spec.Belt
                    ? $"Belt loader: {capacity} cells on the belt, rounds laid out evenly mixed. R runs the belt the short way to the nearest cell " +
                      $"with the selected round (up to {spec.Speed:0.#} rounds/s, worst case {capacity / 2} cells), the gun goes to {spec.LoadAngle:+0.#;-0.#;0}° " +
                      $"and cannot elevate while the round is rammed (at least {spec.RamTime:0.#} s, as long as the ramming sound). Serves the nearest gun " +
                      "of this caliber, which then needs no loader. When the belt is empty, nothing more is loaded."
                    : $"{spec.LoaderName}: {capacity} cells, rounds laid out evenly mixed. R turns the carousel the short way to the nearest " +
                      $"cell with the selected round (up to {spec.Speed:0}°/s), the gun goes to {spec.LoadAngle:+0.#;-0.#;0}° to the hull " +
                      $"and cannot elevate while the round is rammed ({spec.RamTime:0.#} s). Serves the nearest gun of this caliber, " +
                      "which then needs no loader. When the carousel is empty, nothing more is loaded.";
                drawer.InfoField(at, Lines(at), new UnityEngine.Color(0.75f, 0.9f, 1f));
            }

            var d = panel.Current();
            var info = Summary(d, capacity, caliber, carousel);
            drawer.InfoField(info, Lines(info));
        }
        finally { layout.EndDropdown(); }
    }

    private static Panel GetPanel(AmmoRackEditor editor)
    {
        var key = editor.Pointer;
        if (Panels.TryGetValue(key, out var panel)) return panel;
        if (Panels.Count > 32)
        {
            var old = new List<IntPtr>();
            foreach (var kv in Panels)
                if (UnityEngine.Time.frameCount - kv.Value.LastUsedFrame > 600) old.Add(kv.Key);
            foreach (var o in old) Panels.Remove(o);
        }
        panel = new Panel();
        for (var k = 0; k < Kind.Count; k++)
        {
            var kind = k;
            var p = panel;
            Il2CppSystem.Func<float> get = (Func<float>)(() =>
            {
                try { return p.Current()[kind]; }
                catch { return 0f; }
            });
            Il2CppSystem.Action<float> set = (Action<float>)(v =>
            {
                try { SetKind(p, kind, (int)Math.Round(v)); }
                catch (Exception e) { Plugin.Log.LogError("Ammunition slider failed: " + e); }
            });
            panel.Props[k] = new FloatProperty(Kind.Names[k], get, set, 0f, 1f, 1f, "0", null, Tips[k]);
        }

        
        var pp = panel;
        FloatProperty CarouselProp(string name, string tip, float min, float max, float step, string format,
            Func<CarouselSpec, float> get, Func<CarouselSpec, float, CarouselSpec> set)
        {
            Il2CppSystem.Func<float> g = (Func<float>)(() =>
            {
                try { pp.CarouselOn(out var s); return get(s); }
                catch { return min; }
            });
            Il2CppSystem.Action<float> a = (Action<float>)(v =>
            {
                try
                {
                    if (!pp.CarouselOn(out var s)) return;
                    var value = (float)Math.Round(v / step) * step;
                    SetCarousel(pp, set(s, Math.Clamp(value, min, max)));
                }
                catch (Exception e) { Plugin.Log.LogError("Carousel slider failed: " + e); }
            });
            return new FloatProperty(name, g, a, min, max, step, format, null, tip);
        }
        panel.Carousel[0] = CarouselProp("Carousel speed", "Largest rotation speed of the carousel, °/s (T-72/T-90 about 70°/s: a full turn in about 5 s).",
            CarouselSpec.MinSpeed, CarouselSpec.MaxSpeed, 1f, "0", s => s.Speed, (s, v) => { s.Speed = v; return s; });
        panel.Carousel[1] = CarouselProp("Carousel accel.", "Acceleration and braking of the carousel, °/s².",
            CarouselSpec.MinAccel, CarouselSpec.MaxAccel, 5f, "0", s => s.Accel, (s, v) => { s.Accel = v; return s; });
        panel.Carousel[2] = CarouselProp("Loading angle", "Fixed elevation of the gun to the hull for loading, °. The gun goes up or down to it.",
            CarouselSpec.MinAngle, CarouselSpec.MaxAngle, 0.5f, "0.0", s => s.LoadAngle, (s, v) => { s.LoadAngle = v; return s; });
        panel.Carousel[3] = CarouselProp("Ramming time", "Time to lift and ram the round once the cell is under the rammer and the gun is at the loading angle, s.",
            CarouselSpec.MinRam, CarouselSpec.MaxRam, 0.1f, "0.0", s => s.RamTime, (s, v) => { s.RamTime = v; return s; });
        panel.Belt[0] = CarouselProp("Belt speed", "Largest speed of the loading belt, rounds (cells) per second passing the rammer.",
            CarouselSpec.BeltMinSpeed, CarouselSpec.BeltMaxSpeed, 0.5f, "0.0", s => s.Speed, (s, v) => { s.Speed = v; return s; });
        panel.Belt[1] = CarouselProp("Belt accel.", "Acceleration and braking of the loading belt, rounds per second².",
            CarouselSpec.BeltMinAccel, CarouselSpec.BeltMaxAccel, 1f, "0", s => s.Accel, (s, v) => { s.Accel = v; return s; });

        Panels[key] = panel;
        return panel;
    }

    
    
    
    private static void Switch(Panel panel, bool on, LoaderType type)
    {
        try
        {
            var has = panel.CarouselOn(out var spec);
            if (on)
            {
                if (has && spec.Type == type) return;
                var belt = type == LoaderType.Belt;
                if (!has || spec.Belt != belt) spec = CarouselSpec.DefaultFor(type);
                spec.Type = type;
                SetCarousel(panel, spec);
                Plugin.Log.LogInfo($"Ammo rack loader type: {spec.LoaderName}");
            }
            else if (has && spec.Type == type)
            {
                SetCarousel(panel, null);
                Plugin.Log.LogInfo("Ammo rack loader type: none (ordinary rack)");
            }
            panel.Editor.RequestRedraw();
        }
        catch (Exception e) { Plugin.Log.LogError("Loader type toggle failed: " + e); }
    }

    
    private static void SetCarousel(Panel panel, CarouselSpec? spec)
    {
        var bp = panel.Rack?.Blueprint;
        if (bp == null) return;
        var text = CarouselSpec.With(bp.Description, spec);
        if (text == bp.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try
        {
            frame = panel.Editor.NewBlueprintEditFrame("Loader", bp, null, panel.Editor.GetNewOperationGroupID(),
                VehicleOpExecutionMode.Synchronous);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message);
        }
        try { bp.Description = text; }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }
    }

    private static void SetKind(Panel panel, int kind, int value)
    {
        var bp = panel.Rack?.Blueprint;
        if (bp == null) return;
        var current = panel.Current();
        if (current[kind] == value) return;

        
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (panel.Snapshot == null || panel.SnapshotKind != kind || now - panel.SnapshotTime > 1.5f)
        {
            panel.Snapshot = current;
            panel.SnapshotKind = kind;
            panel.Seed = Rng.Next();
        }
        panel.SnapshotTime = now;

        var atgm = Design.AtgmAllowed(panel.Caliber);
        var next = Design.Set(panel.Snapshot, kind, value, panel.Capacity, atgm, panel.Seed);
        if (!atgm) next[Kind.Atgm] = 0;
        var same = true;
        for (var k = 0; k < Kind.Count; k++) same &= next[k] == current[k];
        if (same) return;

        
        Il2CppSystem.IDisposable frame = null;
        try
        {
            frame = panel.Editor.NewBlueprintEditFrame("Ammunition", bp, null, panel.Editor.GetNewOperationGroupID(),
                VehicleOpExecutionMode.Synchronous);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message);
        }
        try { bp.Description = AmmoToken.With(bp.Description, next); }
        finally
        {
            if (frame != null) frame.Dispose();
            else bp.MarkModified();
        }

        
        for (var k = 0; k < Kind.Count; k++)
        {
            if (k == kind || next[k] == current[k]) continue;
            try
            {
                var p = panel.Props[k];
                var handler = p.ValueChanged;
                handler?.Invoke(p, next[k]);
            }
            catch (Exception e)
            {
                if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Slider refresh failed: " + e.Message);
            }
        }
        if (Plugin.DebugLogging.Value)
        {
            var parts = new List<string>();
            foreach (var k in Kind.Order) parts.Add($"{Kind.Names[k]} {next[k]}");
            Plugin.Log.LogInfo("Rack ammo: " + string.Join(", ", parts));
        }
    }

    private static string Summary(int[] d, int capacity, int caliber, bool carousel)
    {
        var free = capacity - Kind.Sum(d);
        var text = $"{Kind.Sum(d)} of {capacity} rounds" + (free > 0 ? $" ({free} empty)" : "") + ". " +
                   "Numbers and settings of each shell (APFSDS dart, HEAT, ATGM warhead and flight, AP / APHE / HE / HESH): " +
                   "Ammunition section of the gun's panel.";
        var keys = new List<string>();
        foreach (var k in Kind.Order)
            if (k != Kind.Atgm || Design.AtgmAllowed(caliber)) keys.Add($"{KeyName(k)} {Kind.Names[k]}");
        text += $" In battle: {string.Join(", ", keys)}" + (carousel
            ? $" select the round, {Autoloader.KeyName} loads it."
            : " select the next round, press twice to reload at once.");
        return text;
    }

    private static string KeyName(int kind)
    {
        var s = kind < Controller.Keys.Length ? Controller.Keys[kind].ToString() : "?";
        return s.StartsWith("Digit") ? s.Substring(5) : s.StartsWith("Numpad") ? s.Substring(6) : s;
    }

    
    private static int Lines(string text) => Math.Max(1, (int)Math.Ceiling(text.Length / 46.0));
}
