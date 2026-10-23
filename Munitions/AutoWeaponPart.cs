using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Properties;
using Sprocket;
using Sprocket.Localization;
using Sprocket.PartImporting;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AmmunitionStorage;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using VehicleDesigner.Compartments.Design;
using Object = UnityEngine.Object;

namespace Munitions;

internal enum AutoType { None = 0, MachineGun = 1, Autocannon = 2 }


internal sealed class AutoSpec
{
    public AutoType Type;
    public string Guid, PartName, DisplayName, Description, Header;
    public float MinCal, MaxCal, CalStep, DefaultCal;
    public float MinLen, MaxLen, DefaultLen;        
    public float MinRpm, MaxRpm, RpmStep;
    public float CaseCalibers;                      
    public float WallStart, WallEnd;                
    public bool Mg => Type == AutoType.MachineGun;
}







internal static class AutoWeaponPart
{
    public const string CannonGuid = LauncherPart.CannonGuid;
    public const string NoMuzzleGuid = LauncherPart.NoMuzzleGuid;
    private const string HiddenTag = "munitionsLegacy";

    public static readonly AutoSpec Ac = new()
    {
        Type = AutoType.Autocannon, Guid = "a7c3e5f1-2b84-4d6a-9e1f-3c5b7d9a2e41", PartName = "mdAutocannon",
        DisplayName = "Autocannon", Header = "Autocannon",
        Description = "Automatic cannon, 20-75 mm. Fires APFSDS, HEAT and HE from ammo racks of its caliber without a loader. " +
                      "Set the caliber, barrel length and rate of fire in the part properties; hold the trigger for a burst.",
        MinCal = 20f, MaxCal = 75f, CalStep = 1f, DefaultCal = 30f,
        MinLen = 30f, MaxLen = 110f, DefaultLen = 80f,
        MinRpm = 30f, MaxRpm = 1200f, RpmStep = 5f,
        CaseCalibers = 5.5f, WallStart = 0.5f, WallEnd = 0.36f
    };

    public static readonly AutoSpec Mg = new()
    {
        Type = AutoType.MachineGun, Guid = "b8d4f6a2-3c95-4e7b-8f2a-4d6c8e0b3f52", PartName = "mdMachineGun",
        DisplayName = "Machine Gun", Header = "Machine gun",
        Description = "Machine gun, 7.62-12.7 mm. Fed from ammo racks of its caliber without a loader. " +
                      "Set the caliber, barrel length and rate of fire in the part properties; hold the trigger to fire.",
        MinCal = 7.62f, MaxCal = 12.7f, CalStep = 0.01f, DefaultCal = 7.62f,
        MinLen = 30f, MaxLen = 130f, DefaultLen = 90f,
        MinRpm = 300f, MaxRpm = 1500f, RpmStep = 10f,
        CaseCalibers = 7.5f, WallStart = 0.9f, WallEnd = 0.65f
    };

    public static readonly AutoSpec[] All = { Ac, Mg };

    public static AutoSpec SpecOfGuid(string guid)
    {
        foreach (var s in All) if (s.Guid == guid) return s;
        return null;
    }

    public static AutoSpec SpecOfType(int type)
    {
        foreach (var s in All) if ((int)s.Type == type) return s;
        return null;
    }

    private static string GuidOf(Component c)
    {
        var vo = c.GetComponent<VehicleObject>();
        if (vo == null) vo = c.GetComponentInParent<VehicleObject>();
        return vo?.guid;
    }

    
    public static AutoSpec SpecOf(Component c)
    {
        if (c == null) return null;
        try
        {
            var cannon = c.TryCast<Cannon>() ?? c.GetComponent<Cannon>() ?? c.GetComponentInParent<Cannon>();
            if (cannon == null) return null;
            if (TryReadType(cannon.Blueprint?.Description, out var type)) return SpecOfType(type);
            return SpecOfGuid(GuidOf(cannon));
        }
        catch { return null; }
    }

    public static bool IsAuto(Component c) => SpecOf(c) != null;

    public static bool IsLegacyPart(Component c)
    {
        try { return SpecOfGuid(GuidOf(c)) != null; }
        catch { return false; }
    }

    
    
    
    public const int FlagGrouped = 1, FlagCrewed = 2;

    private static readonly Regex Token = new(@"\[auto:(\d+),(\d+),(\d)(?:,(\d))?\]", RegexOptions.Compiled);

    private static bool TryReadType(string description, out int type)
    {
        type = 0;
        if (string.IsNullOrEmpty(description)) return false;
        var m = Token.Match(description);
        if (!m.Success || !m.Groups[4].Success) return false;
        type = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryRead(string description, out int rpm, out float caliber, out int flags)
    {
        rpm = 0;
        caliber = 0f;
        flags = 0;
        if (string.IsNullOrEmpty(description)) return false;
        var m = Token.Match(description);
        if (!m.Success) return false;
        rpm = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        caliber = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100f;
        flags = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return true;
    }

    public static string With(string description, AutoSpec spec, int rpm, float caliber, int flags)
    {
        var token = $"[auto:{rpm},{(int)Math.Round(caliber * 100f)},{flags & 3},{(int)spec.Type}]";
        var d = description ?? "";
        if (Token.IsMatch(d)) return Token.Replace(d, token, 1);
        return d.Length == 0 ? token : d + " " + token;
    }

    
    private static string AsCannon(string description, bool legacyPart)
    {
        var d = description ?? "";
        if (legacyPart) return Token.IsMatch(d) ? Token.Replace(d, "[auto:0,0,0,0]", 1) : (d.Length == 0 ? "[auto:0,0,0,0]" : d + " [auto:0,0,0,0]");
        return Token.Replace(d, "", 1).Trim();
    }

    
    public static float DefaultRpm(AutoSpec spec, float cal)
    {
        if (spec.Mg) return cal < 10f ? 750f : 600f;
        
        float[] cals = { 20f, 25f, 30f, 35f, 40f, 57f, 75f };
        float[] rpms = { 800f, 500f, 550f, 550f, 300f, 120f, 60f };
        if (cal <= cals[0]) return rpms[0];
        for (var i = 1; i < cals.Length; i++)
            if (cal <= cals[i]) return Mathf.Lerp(rpms[i - 1], rpms[i], (cal - cals[i - 1]) / (cals[i] - cals[i - 1]));
        return rpms[^1];
    }

    public static float ClampCal(AutoSpec spec, float cal) =>
        Mathf.Clamp((float)Math.Round(cal / spec.CalStep) * spec.CalStep, spec.MinCal, spec.MaxCal);

    public static float ClampLen(AutoSpec spec, float len) => Mathf.Clamp((float)Math.Round(len), spec.MinLen, spec.MaxLen);

    public static float ClampRpm(AutoSpec spec, float rpm) =>
        Mathf.Clamp((float)Math.Round(rpm / spec.RpmStep) * spec.RpmStep, spec.MinRpm, spec.MaxRpm);

    
    public static float NominalCaliber(AutoSpec spec, CannonBlueprint bp)
    {
        if (bp == null) return spec.DefaultCal;
        if (TryRead(bp.Description, out _, out var cal, out _) && cal > 0f && Math.Abs(cal - bp.Caliber) < 0.6f) return ClampCal(spec, cal);
        return ClampCal(spec, bp.Caliber);
    }

    public static float Rpm(AutoSpec spec, CannonBlueprint bp)
    {
        if (bp != null && TryRead(bp.Description, out var rpm, out _, out _) && rpm > 0) return ClampRpm(spec, rpm);
        return ClampRpm(spec, DefaultRpm(spec, NominalCaliber(spec, bp)));
    }

    public static int Flags(CannonBlueprint bp) => bp != null && TryRead(bp.Description, out _, out _, out var f) ? f : 0;

    public static bool Grouped(CannonBlueprint bp) => (Flags(bp) & FlagGrouped) != 0;

    
    
    

    public static int GameCaliber(float nominal) => Math.Max(1, (int)Math.Round(nominal));

    public static ushort CaseLength(AutoSpec spec, int gameCal) => (ushort)Math.Max(10, Math.Round(spec.CaseCalibers * gameCal));

    
    public static float MinCase(AutoSpec spec) => spec.Mg ? 4f : 3f;
    public static float MaxCase(AutoSpec spec) => spec.Mg ? 12f : 10f;

    public static ushort BarrelMm(float nominal, float lenCalibers) => (ushort)Math.Clamp(Math.Round(lenCalibers * nominal), 50, 60000);

    
    public static float TotalBarrelMm(CannonBlueprint bp)
    {
        var total = 0f;
        try
        {
            var groups = bp?.SegmentCount ?? 0;
            for (var g = 0; g < groups; g++)
            {
                var segs = bp.GetSegment(g)?.Segments;
                if (segs == null) continue;
                for (var i = 0; i < segs.Length; i++) total += segs[i].Length;
            }
        }
        catch { }
        return total;
    }

    public static float BarrelCalibersOf(AutoSpec spec, CannonBlueprint bp)
    {
        var total = TotalBarrelMm(bp);
        if (total <= 0f) return spec.DefaultLen;
        return total / Math.Max(1f, NominalCaliber(spec, bp));
    }

    
    public static void ScaleBarrel(CannonBlueprint bp, float kLen, float kWall)
    {
        var groups = bp.SegmentCount;
        for (var g = 0; g < groups; g++)
        {
            var segs = bp.GetSegment(g)?.Segments;
            if (segs == null) continue;
            for (var i = 0; i < segs.Length; i++)
            {
                var s = segs[i];
                if (Math.Abs(kLen - 1f) > 1e-4f) s.Length = (ushort)Math.Clamp(Math.Round(s.Length * kLen), 1, 60000);
                if (Math.Abs(kWall - 1f) > 1e-4f)
                {
                    s.StartThickness = (ushort)Math.Clamp(Math.Round(s.StartThickness * kWall), 1, 2000);
                    s.EndThickness = (ushort)Math.Clamp(Math.Round(s.EndThickness * kWall), 1, 2000);
                }
            }
        }
        bp.physicalPropertiesDirty = true;
    }

    
    public static void DefaultBarrel(AutoSpec spec, CannonBlueprint bp, float nominal, float lenCalibers)
    {
        if (bp.SegmentCount != 1) bp.SegmentCount = 1;
        var group = bp.GetSegment(0);
        if (group == null) return;
        if (group.SegmentCount != 1) group.SegmentCount = 1;
        var segments = group.Segments;
        if (segments == null || segments.Length == 0) return;
        var seg = segments[0];
        seg.Length = BarrelMm(nominal, lenCalibers);
        seg.StartThickness = (ushort)Math.Max(2, Math.Round(spec.WallStart * nominal));
        seg.EndThickness = (ushort)Math.Max(2, Math.Round(spec.WallEnd * nominal));
        bp.physicalPropertiesDirty = true;
    }

    public static void SetCaliberAndCase(CannonBlueprint bp, ShellSlotBlueprint shell, int gameCal, ushort propellant)
    {
        if (shell != null)
        {
            shell.Diameter = (ushort)gameCal;
            shell.PropellantLength = propellant;
        }
        bp.Caliber = (ushort)gameCal;
        bp.PropellantLength = propellant;
        bp.physicalPropertiesDirty = true;
    }

    
    public static float MassFor(AutoSpec spec, float cal, float lenCalibers)
    {
        if (spec.Mg) return 10.5f * Mathf.Pow(cal / 7.62f, 2.5f) * (0.75f + 0.25f * lenCalibers / 90f);
        return 115f * Mathf.Pow(cal / 30f, 2.2f) * (0.75f + 0.25f * lenCalibers / 80f);
    }

    
    [HarmonyPatch(typeof(Cannon), nameof(Cannon.CreateDefaultBlueprint))]
    private static class DefaultBlueprintPatch
    {
        private static void Postfix(Cannon __instance, CannonBlueprint __result)
        {
            try
            {
                if (__result == null) return;
                var spec = SpecOfGuid(GuidOf(__instance));
                if (spec == null) return;
                __result.Name = spec.DisplayName;
                MakeAuto(spec, __result, __instance.ShellBlueprint, 0);
            }
            catch (Exception e) { Plugin.Log.LogError("Auto weapon blueprint setup failed: " + e); }
        }
    }

    
    private static void MakeAuto(AutoSpec spec, CannonBlueprint bp, ShellSlotBlueprint shell, int flags)
    {
        var cal = GameCaliber(spec.DefaultCal);
        SetCaliberAndCase(bp, shell, cal, CaseLength(spec, cal));
        bp.MuzzlePartGuid = NoMuzzleGuid;
        DefaultBarrel(spec, bp, spec.DefaultCal, spec.DefaultLen);
        bp.Description = With(bp.Description, spec, (int)DefaultRpm(spec, spec.DefaultCal), spec.DefaultCal, flags);
    }

    
    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class MassPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                var spec = SpecOf(__instance);
                if (spec == null) return;
                var bp = __instance.Blueprint;
                if (bp == null) return;
                __instance.SetMass(MassFor(spec, NominalCaliber(spec, bp), BarrelCalibersOf(spec, bp)), MassType.Weapons);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Auto weapon mass failed: " + e.Message); }
        }
    }

    

    private sealed class PanelProps
    {
        public FloatProperty Caliber, Case, Length, Rpm;
        public Il2CppSystem.Object Header;
        public bool Expanded = true;
    }

    private static readonly Dictionary<AutoType, PanelProps> Props = new();
    private static CannonEditor _editor;
    private static readonly HashSet<IntPtr> Fixed = new();
    private static int _uiErrors;

    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class EditorPatch
    {
        private static bool Prefix(CannonEditor __instance, IGUILayout layout)
        {
            try
            {
                var cannon = __instance.Component;
                var spec = SpecOf(cannon);
                if (spec == null) return true;
                DrawPanel(__instance, cannon, spec, layout);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Auto weapon panel failed: " + e);
                return true;
            }
        }
    }

    private static Cannon Current(AutoSpec spec)
    {
        var c = _editor?.Component;
        return c != null && SpecOf(c) == spec ? c : null;
    }

    private static FloatProperty Prop(string name, string tip, float min, float max, float step, string format, Func<float> get, Action<float> set)
    {
        Il2CppSystem.Func<float> g = (Func<float>)(() =>
        {
            try { return get(); }
            catch { return min; }
        });
        Il2CppSystem.Action<float> s = (Action<float>)(v =>
        {
            try { set(v); }
            catch (Exception e) { Plugin.Log.LogError($"Auto weapon {name} change failed: " + e); }
        });
        return new FloatProperty(name, g, s, min, max, step, format, null, tip);
    }

    private static PanelProps PropsFor(AutoSpec spec)
    {
        if (Props.TryGetValue(spec.Type, out var p)) return p;
        p = new PanelProps { Header = new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(spec.Header)) };

        p.Caliber = Prop("Caliber",
            spec.Mg
                ? "Bullet caliber in millimetres (7.62 PKT/M240, 7.92, 12.7 DShK/NSVT/M2). The game counts calibers in whole millimetres, so 7.62 flies as 8 mm."
                : "Shell caliber in millimetres. Bigger shells hit harder but the gun is heavier and usually fires slower. The barrel keeps its length in calibers.",
            spec.MinCal, spec.MaxCal, spec.CalStep, spec.Mg ? "0.00" : "0",
            () => NominalCaliber(spec, _editor?.Component?.Blueprint),
            v => { var c = Current(spec); if (c != null) ChangeCaliber(_editor, c, spec, ClampCal(spec, v)); });

        p.Case = Prop("Propellant length",
            "Length of the cartridge case (propellant) in millimetres. A longer case gives a higher muzzle velocity but a bigger, heavier round " +
            "(30x165 for 2A42, 25x137 for M242, 7.62x54 for PKT, 12.7x108 for DShK).",
            10f, 1000f, 1f, "0",
            () => _editor?.Component?.Blueprint?.PropellantLength ?? 0f,
            v => { var c = Current(spec); if (c != null) ChangeCase(_editor, c, spec, v); });

        p.Length = Prop("Barrel length",
            "Length of the whole barrel in calibers (all segments stretch together). A longer barrel gives a higher muzzle velocity but weighs more. " +
            "The segments and recoil segments below can also be edited one by one, like on a normal gun.",
            spec.MinLen, spec.MaxLen, 1f, "0",
            () => BarrelCalibersOf(spec, _editor?.Component?.Blueprint),
            v => { var c = Current(spec); if (c != null) ChangeLength(_editor, c, spec, ClampLen(spec, v)); });

        p.Rpm = Prop("Rate of fire",
            "Rounds per minute while the trigger is held. Real guns: " +
            (spec.Mg ? "PKT 700-800, M240 650-950, DShK 600, NSVT 700-800, M2 450-600." : "MK 20 ~800-1000, M242 100-500, 2A42 200-550, Bofors 40 mm ~300, S-60 57 mm ~120."),
            spec.MinRpm, spec.MaxRpm, spec.RpmStep, "0",
            () => Rpm(spec, _editor?.Component?.Blueprint),
            v =>
            {
                var c = Current(spec);
                if (c == null) return;
                var bp = c.Blueprint;
                Edit(_editor, c, spec.DisplayName, false, (b, _) => b.Description = With(b.Description, spec, (int)ClampRpm(spec, v), NominalCaliber(spec, b), Flags(b)));
            });
        Props[spec.Type] = p;
        return p;
    }

    private static void DrawPanel(CannonEditor editor, Cannon cannon, AutoSpec spec, IGUILayout layout)
    {
        _editor = editor;
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;
        var bp = cannon.Blueprint;
        var props = PropsFor(spec);

        
        if (bp != null && Fixed.Add(bp.Pointer)) Normalize(editor, cannon, spec);

        
        if (bp != null && !Grouped(bp) && GroupFixed.Add(bp.Pointer)) FixFireGroup(editor, cannon, spec);

        
        if (bp != null && (Flags(bp) & FlagCrewed) == 0 && CrewTried.Add(bp.Pointer))
        {
            if (LauncherPart.HasOperator(cannon) || AutoConnect.AssignOperator(editor, cannon, spec.DisplayName)) MarkCrewed(editor, spec, bp);
        }

        
        try
        {
            var name = editor.properties?.name;
            if (name != null)
            {
                UnityEngine.Events.UnityAction save = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_0(); }
                    catch (Exception e) { Plugin.Log.LogError("Weapon preset save failed: " + e.Message); }
                });
                UnityEngine.Events.UnityAction load = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_1(); }
                    catch (Exception e) { Plugin.Log.LogError("Weapon preset load failed: " + e.Message); }
                });
                drawer.InputField(name, save, load);
            }
        }
        catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Weapon preset field failed: " + e.Message); }

        DrawType(editor, drawer);

        var cal = NominalCaliber(spec, bp);
        var len = BarrelCalibersOf(spec, bp);
        var rpm = Rpm(spec, bp);
        var game = GameCaliber(cal);
        props.Case.SetLimits(Mathf.Round(MinCase(spec) * game), Mathf.Round(MaxCase(spec) * game));

        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => props.Expanded = v);
        layout.BeginDropdown(props.Header, props.Expanded, onExpand);
        try
        {
            drawer.Float(props.Caliber);
            drawer.Float(props.Case);
            drawer.Float(props.Length);
            drawer.Float(props.Rpm);
            BreechScale.DrawInfo(cannon, drawer);
            MuzzleDevices.Draw(editor, cannon, drawer);
            Bore.Draw(editor, drawer);
            var mv = 0f;
            try { mv = bp?.MuzzleVelocity ?? 0f; } catch { }
            var prop = bp?.PropellantLength ?? 0;
            Info(drawer, $"{(spec.Mg ? "Bullet" : "Round")} {game}x{prop} mm, barrel {TotalBarrelMm(bp) / 1000f:0.00} m, {MassFor(spec, cal, len):0} kg. " +
                         $"{rpm / 60f:0.#} rounds per second" + (mv > 1f ? $", about {mv:0} m/s at the muzzle." : "."));
            var (racks, rounds) = AutoConnect.RacksFor(cannon);
            if (racks == 0)
                Warn(drawer, "No ammo rack feeds this weapon yet. Place an Ammo Rack next to it: a new rack connects to the nearest weapon by itself. " +
                             $"An existing rack: open it and choose \"{AutoConnect.LabelOf(cannon)}\" in its \"Ammo for\" list.");
            else
                Info(drawer, $"Fed from {racks} ammo rack{(racks == 1 ? "" : "s")}, {rounds} rounds. No loader is needed. " +
                             (spec.Mg
                                 ? "The belt feeds itself while you hold the trigger."
                                 : "Set the APFSDS / HE / HEAT mix in the rack's Ammunition panel. In battle 1 / 2 / 3 choose the next round " +
                                   "(APFSDS / HE / HEAT), the gun switches after the round in the chamber."));

            var group = cannon.InstanceBlueprint?.FireGroup ?? 1;
            var key = LauncherPart.GroupLabel(group & -group);
            Info(drawer, $"Fire group {LauncherPart.GroupLabel(group)}: press {key} in battle to select it, then hold the trigger to fire. " +
                         "Put it in the main gun's group to fire both together, or link it to another gun below.");
            if (!LauncherPart.HasOperator(cannon))
                Warn(drawer, "No crew member operates this weapon (the vehicle has no crew seat yet, or it was removed from the seat). " +
                             "Add a crew seat, or tick this weapon in a crew seat's settings — otherwise it cannot be selected in battle.");
        }
        finally { layout.EndDropdown(); }

        
        try
        {
            var cp = editor.properties;
            if (cp?.groupCount != null) drawer.Float(cp.groupCount);
            var barrel = editor.barrelProperties;
            if (barrel != null) editor.DrawBarrelProperties(layout, barrel);
        }
        catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Auto weapon barrel UI failed: " + e.Message); }

        
        var instanceDrawer = editor.instancePropertyDrawer;
        if (instanceDrawer != null)
        {
            try { instanceDrawer.DrawScopeSelect(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Auto weapon sight select failed: " + e.Message); }
            try { instanceDrawer.OnMulticannonsGUI(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Auto weapon fire group UI failed: " + e.Message); }
        }
    }

    

    private static readonly string[] TypeNames = { "Cannon", "Autocannon", "Machine gun" };
    private static readonly AutoSpec[] TypeSpecs = { null, Ac, Mg };
    private static CannonEditor _typeEditor;

    
    [HarmonyPatch(typeof(CannonInstancePropertyDrawer), nameof(CannonInstancePropertyDrawer.DrawScopeSelect))]
    private static class TypeSelectPatch
    {
        private static void Prefix(IGUILayout layout)
        {
            try
            {
                var editor = Bore.Drawing;
                var cannon = editor?.Component;
                if (cannon == null || LauncherPart.IsLauncher(cannon) || IsAuto(cannon)) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer != null) DrawType(editor, drawer);
            }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Weapon type select failed: " + e.Message); }
        }
    }

    private static void DrawType(CannonEditor editor, IGUIElementDrawer drawer)
    {
        var cannon = editor?.Component;
        if (cannon == null) return;
        _typeEditor = editor;
        var spec = SpecOf(cannon);
        var index = Array.IndexOf(TypeSpecs, spec);
        var options = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var n in TypeNames) options.Add(n);
        UnityEngine.Events.UnityAction<int> onSelect = (Action<int>)(i =>
        {
            try
            {
                var ed = _typeEditor;
                var c = ed?.Component;
                if (c == null || i < 0 || i >= TypeSpecs.Length) return;
                SetType(ed, c, TypeSpecs[i]);
                ed.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Weapon type change failed: " + e); }
        });
        drawer.Dropdown("Weapon type", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), Math.Max(0, index), onSelect,
            "Cannon: the game's gun, loaded by the crew. Autocannon (20-75 mm) and Machine gun (7.62-12.7 mm): automatic fire with " +
            "your own rate of fire, fed from ammo racks of the same caliber without a loader. Switching resets caliber, barrel and muzzle brake.");
    }

    
    private static void SetType(CannonEditor editor, Cannon cannon, AutoSpec spec)
    {
        var bp = cannon.Blueprint;
        if (bp == null || SpecOf(cannon) == spec) return;
        var legacy = IsLegacyPart(cannon);
        if (spec != null)
        {
            Edit(editor, cannon, "Weapon type", true, (b, shell) => MakeAuto(spec, b, shell, 0));
            
            GroupFixed.Remove(bp.Pointer);
            CrewTried.Remove(bp.Pointer);
            Fixed.Add(bp.Pointer);
        }
        else
        {
            
            float minCal = 20f, maxCal = 300f, minProp = 0f, maxProp = 60000f;
            try
            {
                var sp = editor.shellSlotProperties;
                if (sp?.caliber != null && sp.caliber.Max > sp.caliber.Min) { minCal = sp.caliber.Min; maxCal = sp.caliber.Max; }
                if (sp?.propellantLength != null && sp.propellantLength.Max > sp.propellantLength.Min) { minProp = sp.propellantLength.Min; maxProp = sp.propellantLength.Max; }
            }
            catch { }
            Edit(editor, cannon, "Weapon type", true, (b, shell) =>
            {
                b.Description = AsCannon(b.Description, legacy);
                var old = Math.Max(1, (int)b.Caliber);
                var cal = (int)Mathf.Clamp(old, minCal, maxCal);
                var caseCal = b.PropellantLength / (float)old;
                var prop = (ushort)Mathf.Clamp(Mathf.Round(caseCal * cal), Mathf.Max(minProp, 1f), maxProp);
                if (cal != old) ScaleBarrel(b, cal / (float)old, cal / (float)old);
                SetCaliberAndCase(b, shell, cal, prop);
            });
        }
        Api.ForgetAutoInfo();
        Plugin.Log.LogInfo($"Weapon type: {spec?.DisplayName ?? "Cannon"}");
    }

    private static void Info(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

    private static void Warn(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)), new Color(1f, 0.7f, 0.4f));

    private static readonly HashSet<IntPtr> GroupFixed = new();
    private static readonly HashSet<IntPtr> CrewTried = new();

    
    private static void MarkCrewed(CannonEditor editor, AutoSpec spec, CannonBlueprint bp)
    {
        try
        {
            var text = With(bp.Description, spec, (int)Rpm(spec, bp), NominalCaliber(spec, bp), Flags(bp) | FlagCrewed);
            if (text == bp.Description) return;
            Il2CppSystem.IDisposable frame = null;
            try { frame = editor.NewBlueprintEditFrame(spec.DisplayName, bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
            catch { }
            try { bp.Description = text; }
            finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
        }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Crew mark failed: " + e.Message); }
    }

    
    private static void FixFireGroup(CannonEditor editor, Cannon cannon, AutoSpec spec)
    {
        try
        {
            var ib = cannon.InstanceBlueprint;
            var bp = cannon.Blueprint;
            if (ib == null || bp == null) return;
            var vehicle = cannon.GetComponentInParent<Vehicle>();
            var root = vehicle != null ? vehicle.transform : cannon.transform.root;
            var used = 0;
            foreach (var c in root.GetComponentsInChildren<Cannon>(true))
            {
                if (c == null || c.Pointer == cannon.Pointer || LauncherPart.IsLauncher(c)) continue;
                var other = c.InstanceBlueprint;
                used |= other != null ? other.FireGroup : 1;
            }
            var own = ib.FireGroup & LauncherPart.AllGroups;
            var mask = own;
            if ((own & used) != 0)
                for (var b = 0; b < 10; b++)
                    if ((used & (1 << b)) == 0) { mask = 1 << b; break; }

            var op = editor.GetNewOperationGroupID();
            Il2CppSystem.IDisposable ibFrame = null, bpFrame = null;
            try
            {
                if (mask != ib.FireGroup)
                    ibFrame = editor.NewBlueprintEditFrame($"{spec.DisplayName} fire group", ib, null, op, VehicleOpExecutionMode.Synchronous);
                bpFrame = editor.NewBlueprintEditFrame($"{spec.DisplayName} fire group", bp, null, op, VehicleOpExecutionMode.Synchronous);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
            try
            {
                if (mask != ib.FireGroup) ib.FireGroup = (ushort)mask;
                bp.Description = With(bp.Description, spec, (int)Rpm(spec, bp), NominalCaliber(spec, bp), Flags(bp) | FlagGrouped);
            }
            finally
            {
                if (ibFrame != null) ibFrame.Dispose(); else if (mask != own) ib.MarkModified();
                if (bpFrame != null) bpFrame.Dispose(); else bp.MarkModified();
            }
            if (mask != own) Plugin.Log.LogInfo($"{spec.DisplayName} moved to fire group {LauncherPart.GroupLabel(mask)}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Auto weapon fire group setup failed: " + e.Message); }
    }

    
    internal static void Edit(CannonEditor editor, Cannon cannon, string name, bool shellToo, Action<CannonBlueprint, ShellSlotBlueprint> change)
    {
        var bp = cannon.Blueprint;
        var shell = cannon.ShellBlueprint;
        if (bp == null) return;
        var group = editor.GetNewOperationGroupID();
        Il2CppSystem.IDisposable shellFrame = null, bpFrame = null;
        try
        {
            if (shellToo && shell != null) shellFrame = editor.NewBlueprintEditFrame(name, shell, null, group, VehicleOpExecutionMode.Synchronous);
            bpFrame = editor.NewBlueprintEditFrame(name, bp, null, group, VehicleOpExecutionMode.Synchronous);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message);
        }
        try { change(bp, shell); }
        finally
        {
            if (bpFrame != null) bpFrame.Dispose(); else bp.MarkModified();
            if (shellFrame != null) shellFrame.Dispose(); else if (shellToo) shell?.MarkModified();
        }
    }

    
    private static void ChangeCaliber(CannonEditor editor, Cannon cannon, AutoSpec spec, float nominal)
    {
        var bp = cannon.Blueprint;
        if (bp == null) return;
        var oldNominal = NominalCaliber(spec, bp);
        if (Math.Abs(oldNominal - nominal) < 0.001f && bp.Caliber == GameCaliber(nominal)) return;
        var rpm = (int)Rpm(spec, bp);
        if (Math.Abs(rpm - (int)ClampRpm(spec, DefaultRpm(spec, oldNominal))) <= spec.RpmStep) rpm = (int)ClampRpm(spec, DefaultRpm(spec, nominal));
        Edit(editor, cannon, spec.DisplayName, true, (b, shell) =>
        {
            var k = nominal / Math.Max(0.1f, oldNominal);
            ScaleBarrel(b, k, k);
            var game = GameCaliber(nominal);
            var caseCal = b.Caliber > 0 ? b.PropellantLength / (float)b.Caliber : spec.CaseCalibers;
            caseCal = Mathf.Clamp(caseCal, MinCase(spec), MaxCase(spec));
            SetCaliberAndCase(b, shell, game, (ushort)Math.Round(caseCal * game));
            b.Description = With(b.Description, spec, rpm, nominal, Flags(b));
        });
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"{spec.DisplayName} {nominal:0.##} mm");
    }

    private static void ChangeCase(CannonEditor editor, Cannon cannon, AutoSpec spec, float mm)
    {
        var bp = cannon.Blueprint;
        if (bp == null) return;
        var game = bp.Caliber;
        var value = (ushort)Mathf.Clamp(Mathf.Round(mm), Mathf.Round(MinCase(spec) * game), Mathf.Round(MaxCase(spec) * game));
        if (value == bp.PropellantLength) return;
        Edit(editor, cannon, spec.DisplayName, true, (b, shell) => SetCaliberAndCase(b, shell, b.Caliber, value));
    }

    private static void ChangeLength(CannonEditor editor, Cannon cannon, AutoSpec spec, float calibers)
    {
        var bp = cannon.Blueprint;
        if (bp == null) return;
        var total = TotalBarrelMm(bp);
        var target = calibers * NominalCaliber(spec, bp);
        if (total <= 0f || Math.Abs(target - total) < 1f) return;
        Edit(editor, cannon, spec.DisplayName, false, (b, _) => ScaleBarrel(b, target / total, 1f));
    }

    
    
    private static void Normalize(CannonEditor editor, Cannon cannon, AutoSpec spec)
    {
        var bp = cannon.Blueprint;
        if (bp == null) return;
        var nominal = NominalCaliber(spec, bp);
        var game = GameCaliber(nominal);
        var caseOk = bp.PropellantLength >= Mathf.Round(MinCase(spec) * game) && bp.PropellantLength <= Mathf.Round(MaxCase(spec) * game);
        var noBarrel = TotalBarrelMm(bp) <= 0f;
        var typed = TryReadType(bp.Description, out _);
        if (typed && bp.Caliber == game && caseOk && !noBarrel) return;
        Edit(editor, cannon, spec.DisplayName, true, (b, shell) =>
        {
            var prop = caseOk ? b.PropellantLength : CaseLength(spec, game);
            if (b.Caliber != game || !caseOk) SetCaliberAndCase(b, shell, game, prop);
            if (noBarrel) DefaultBarrel(spec, b, nominal, spec.DefaultLen);
            
            if (!TryReadType(b.Description, out _)) b.Description = With(b.Description, spec, (int)Rpm(spec, b), nominal, Flags(b));
        });
    }

    

    private static PartDefinitionDatabase _db;
    private static float _nextSearch;

    internal static PartDefinitionDatabase Database => _db;

    public static void EnsureInjected()
    {
        if (_db != null || Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 1f;
        try
        {
            var loader = Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
            var db = loader != null ? loader.partDatabase : null;
            if (db?.partDefinitions == null) return;
            _db = db;
            if (!db.partDefinitionLookup.ContainsKey(CannonGuid))
            {
                Plugin.Log.LogWarning("Cannon part definition not found — autocannon and machine gun not added");
                return;
            }
            foreach (var spec in All)
            {
                if (db.partDefinitionLookup.ContainsKey(spec.Guid)) continue;
                var def = CreateDefinition(spec);
                var defs = db.partDefinitions;
                var merged = new Il2CppReferenceArray<PartDefinition>(defs.Length + 1);
                for (var i = 0; i < defs.Length; i++) merged[i] = defs[i];
                merged[defs.Length] = def;
                db.partDefinitions = merged;
                db.partDefinitionLookup[spec.Guid] = def;
                foreach (var tag in def.tags)
                {
                    if (!db.partDefinitionTagLookup.ContainsKey(tag))
                        db.partDefinitionTagLookup[tag] = new Il2CppSystem.Collections.Generic.List<PartDefinition>();
                    db.partDefinitionTagLookup[tag].Add(def);
                }
                try
                {
                    var cache = loader.partCardFactory?.cardCache;
                    if (cache != null && !cache.ContainsKey(spec.Guid)) cache[spec.Guid] = PartDefinitionCardFactory.CreateCard(def);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"{spec.DisplayName} card registration failed: " + e.Message); }
            }
            Plugin.Log.LogInfo("Old autocannon / machine gun parts registered (hidden; choose \"Weapon type\" on a normal gun instead)");
            BreechScale.UnlockDefinitions(db);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Autocannon / machine gun part injection failed: " + e);
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    
    
    private static PartDefinition CreateDefinition(AutoSpec spec)
    {
        var path = Path.Combine(Application.streamingAssetsPath, "Parts", "cannonPart.json");
        var json = File.ReadAllText(path);
        json = Regex.Replace(json, "\"guid\"\\s*:\\s*\"[^\"]*\"", $"\"guid\": \"{spec.Guid}\"");
        json = Regex.Replace(json, "\"name\"\\s*:\\s*\"[^\"]*\"", $"\"name\": \"{spec.PartName}\"");
        json = Regex.Replace(json, "\"order\"\\s*:\\s*-?\\d+", spec.Mg ? "\"order\": 2" : "\"order\": 1");
        json = Regex.Replace(json, "\"tags\"\\s*:\\s*\\[[^\\]]*\\]", $"\"tags\": [ \"{HiddenTag}\" ]");
        var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
        var def = obj?.TryCast<PartDefinition>();
        if (def == null) throw new InvalidOperationException("DeserializeJSON returned null");
        return def;
    }

    [HarmonyPatch(typeof(PartLocalizationIO), nameof(PartLocalizationIO.GetPartLocalization))]
    private static class LocalizationPatch
    {
        private static bool Prefix(string localizationID, ref PartLocalization __result)
        {
            AutoSpec spec = null;
            foreach (var s in All) if (s.PartName == localizationID) spec = s;
            if (spec == null) return true;
            try
            {
                var loc = new PartLocalization();
                loc.name = spec.DisplayName;
                loc.description = spec.Description;
                __result = loc;
                return false;
            }
            catch { return true; }
        }
    }
}
