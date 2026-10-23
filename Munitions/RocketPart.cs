using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
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

namespace Munitions;




internal static class RocketSettings
{
    public const int He = 0, Thermobaric = 1, Heat = 2;
    public static readonly string[] WarheadNames = { "High explosive (HE)", "Thermobaric", "HEAT (shaped charge)" };
    public const float MinSpeed = 200f, MaxSpeed = 1000f;

    private static readonly Regex Token = new(@"\[rocket:([0-9]),([0-9.]+)\]", RegexOptions.Compiled);

    public static float ClampSpeed(float v) => Mathf.Clamp(v, MinSpeed, MaxSpeed);

    public static float DefaultSpeed => ClampSpeed(Plugin.RocketSpeed.Value);

    public static int KindOf(int warhead) => warhead switch { Thermobaric => Kind.RocketTb, Heat => Kind.RocketHeat, _ => Kind.RocketHe };

    public static int Clamp(int warhead) => warhead is Thermobaric or Heat ? warhead : He;

    
    public static void ReadRaw(string description, out int warhead, out float rawSpeed)
    {
        warhead = He;
        rawSpeed = 0f;
        if (string.IsNullOrEmpty(description)) return;
        var m = Token.Match(description);
        if (!m.Success) return;
        warhead = m.Groups[1].Value switch { "1" => Thermobaric, "2" => Heat, _ => He };
        if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0f) rawSpeed = ClampSpeed(s);
    }

    public static void Read(CannonBlueprint bp, out int warhead, out float speed)
    {
        string description = null;
        try { description = bp?.Description; }
        catch { }
        ReadRaw(description, out warhead, out var raw);
        speed = raw > 0f ? raw : DefaultSpeed;
    }

    public static string With(string description, int warhead, float rawSpeed)
    {
        var token = string.Format(CultureInfo.InvariantCulture, "[rocket:{0},{1:0}]", Clamp(warhead),
            rawSpeed > 0f ? ClampSpeed(rawSpeed) : 0f);
        if (string.IsNullOrEmpty(description)) return token;
        return Token.IsMatch(description) ? Token.Replace(description, token, 1) : description + token;
    }

    
    public static void ForWeapon(Transform weapon, out int warhead, out float speed)
    {
        CannonBlueprint bp = null;
        try
        {
            var cannon = weapon != null ? weapon.GetComponentInParent<Cannon>() : null;
            bp = cannon != null ? cannon.Blueprint : null;
        }
        catch { }
        Read(bp, out warhead, out speed);
    }
}




internal static class RocketPart
{
    public const string PartGuid = "6b2e9f41-c37a-4d58-a1e6-0f84d2b7c935";
    public const string PartName = "mdRocketLauncher"; 
    public const string DisplayName = "Rocket Launcher";
    public const int MinCaliber = 57, MaxCaliber = 250, DefaultCaliber = 122;
    public const float MinTubeCalibers = 4f, MaxTubeCalibers = 25f, DefaultTubeCalibers = 16f;
    public const float BodyCalibers = 14f;   
    public const float Lifetime = 30f;       

    public static string Description =>
        "Single-use tube with one unguided rocket (S-8, Grad, TOS-1A class). Set the caliber (57-250 mm), warhead (HE, " +
        "thermobaric or HEAT) and speed in its panel. Select it with its fire group key: a click fires one tube, holding the trigger " +
        "ripple-fires the whole group.";

    public static bool IsRocketLauncher(Component c) => LauncherPart.GuidOf(c) == PartGuid;

    

    public static int WallThickness(int caliber) => LauncherPart.WallThickness(caliber);

    public static float ClampTube(float tubeCalibers) => Math.Clamp(tubeCalibers, MinTubeCalibers, MaxTubeCalibers);

    public static ushort TubeLengthMm(int caliber, float tubeCalibers) => (ushort)Math.Round(ClampTube(tubeCalibers) * caliber);

    public static float TubeCaliberOf(CannonBlueprint bp)
    {
        try
        {
            var segments = bp?.GetSegment(0)?.Segments;
            if (segments == null || segments.Length == 0 || bp.Caliber == 0) return DefaultTubeCalibers;
            return ClampTube((float)Math.Round(segments[0].Length / (float)bp.Caliber * 2f) / 2f);
        }
        catch { return DefaultTubeCalibers; }
    }

    
    public static float RocketMassLoaded(int caliber)
    {
        var k = caliber / 80f;
        return 1.5f * Math.Max(2f, 11f * k * k * k);
    }

    public static float MassFor(int caliber, float tubeCalibers)
    {
        var d = caliber / 1000f;
        var wall = WallThickness(caliber) / 1000f;
        var length = TubeLengthMm(caliber, tubeCalibers) / 1000f;
        var tube = MathF.PI * d * wall * length * 7850f;
        return Math.Max(3f, RocketMassLoaded(caliber) + tube);
    }

    public static void ApplyGeometry(CannonBlueprint bp, ShellSlotBlueprint shell, int caliber, float tubeCalibers)
    {
        caliber = Math.Clamp(caliber, MinCaliber, MaxCaliber);
        var prop = LauncherPart.PropellantFor(caliber);
        if (shell != null)
        {
            shell.Diameter = (ushort)caliber;
            shell.PropellantLength = prop;
        }
        if (bp == null) return;
        bp.Caliber = (ushort)caliber;
        bp.PropellantLength = prop;
        bp.MuzzlePartGuid = LauncherPart.NoMuzzleGuid;
        if (bp.SegmentCount != 1) bp.SegmentCount = 1;
        var group = bp.GetSegment(0);
        if (group == null) return;
        if (group.SegmentCount != 1) group.SegmentCount = 1;
        var segments = group.Segments;
        if (segments == null || segments.Length == 0) return;
        var seg = segments[0];
        var wall = (ushort)WallThickness(caliber);
        seg.Length = TubeLengthMm(caliber, tubeCalibers);
        seg.StartThickness = wall;
        seg.EndThickness = wall;
        bp.physicalPropertiesDirty = true;
    }

    
    public static bool IsTube(CannonBlueprint bp)
    {
        try
        {
            if (bp == null || bp.MuzzlePartGuid != LauncherPart.NoMuzzleGuid || bp.SegmentCount != 1) return false;
            int cal = bp.Caliber;
            if (cal < MinCaliber || cal > MaxCaliber) return false;
            var segments = bp.GetSegment(0)?.Segments;
            if (segments == null || segments.Length != 1) return false;
            var seg = segments[0];
            return seg.StartThickness == WallThickness(cal) && seg.EndThickness == WallThickness(cal) &&
                   seg.Length >= TubeLengthMm(cal, MinTubeCalibers) - 1 && seg.Length <= TubeLengthMm(cal, MaxTubeCalibers) + 1;
        }
        catch { return false; }
    }

    private static bool GeometryMatches(CannonBlueprint bp, int caliber, float tubeCalibers)
    {
        try
        {
            if (!IsTube(bp) || bp.Caliber != caliber) return false;
            return bp.GetSegment(0).Segments[0].Length == TubeLengthMm(caliber, tubeCalibers);
        }
        catch { return false; }
    }

    
    [HarmonyPatch(typeof(Cannon), nameof(Cannon.CreateDefaultBlueprint))]
    private static class DefaultBlueprintPatch
    {
        private static void Postfix(Cannon __instance, CannonBlueprint __result)
        {
            try
            {
                if (__result == null || !IsRocketLauncher(__instance)) return;
                __result.Name = DisplayName;
                ApplyGeometry(__result, __instance.ShellBlueprint, DefaultCaliber, DefaultTubeCalibers);
            }
            catch (Exception e) { Plugin.Log.LogError("Rocket launcher blueprint setup failed: " + e); }
        }
    }

    
    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class MassPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                if (!IsRocketLauncher(__instance)) return;
                var bp = __instance.Blueprint;
                if (bp == null) return;
                __instance.SetMass(MassFor(bp.Caliber, TubeCaliberOf(bp)), MassType.Weapons);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Rocket launcher mass failed: " + e.Message); }
        }
    }

    

    private static bool _expanded = true;
    private static Il2CppSystem.Object _header;
    private static FloatProperty _caliberProp, _lengthProp, _speedProp;
    private static CannonEditor _editor;
    private static readonly HashSet<IntPtr> Fixed = new();
    private static int _uiErrors;

    private static Il2CppSystem.Object Header =>
        _header ??= new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(DisplayName));

    [HarmonyPatch(typeof(CannonEditor), nameof(CannonEditor.OnGUI))]
    private static class EditorPatch
    {
        private static bool Prefix(CannonEditor __instance, IGUILayout layout)
        {
            try
            {
                var cannon = __instance.Component;
                if (!IsRocketLauncher(cannon)) return true;
                DrawPanel(__instance, cannon, layout);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Rocket launcher panel failed: " + e);
                return true;
            }
        }
    }

    private static void DrawPanel(CannonEditor editor, Cannon cannon, IGUILayout layout)
    {
        _editor = editor;
        var drawer = layout.TryCast<IGUIElementDrawer>();
        if (drawer == null) return;
        var bp = cannon.Blueprint;
        var caliber = bp != null ? Math.Clamp((int)bp.Caliber, MinCaliber, MaxCaliber) : DefaultCaliber;

        
        if (bp != null && !IsTube(bp) && Fixed.Add(bp.Pointer)) SetGeometry(editor, cannon, caliber, DefaultTubeCalibers);
        var tube = TubeCaliberOf(bp);
        EnsureProperties();

        
        LauncherPart.FixFireGroupOnce(editor, cannon, "Rocket launcher");

        
        try
        {
            var name = editor.properties?.name;
            if (name != null)
            {
                UnityEngine.Events.UnityAction save = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_0(); }
                    catch (Exception e) { Plugin.Log.LogError("Rocket launcher save failed: " + e.Message); }
                });
                UnityEngine.Events.UnityAction load = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_1(); }
                    catch (Exception e) { Plugin.Log.LogError("Rocket launcher load failed: " + e.Message); }
                });
                drawer.InputField(name, save, load);
            }
        }
        catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Rocket launcher name field failed: " + e.Message); }

        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => _expanded = v);
        layout.BeginDropdown(Header, _expanded, onExpand);
        try
        {
            drawer.Float(_caliberProp);
            drawer.Float(_lengthProp);

            RocketSettings.Read(bp, out var warhead, out var speed);
            var options = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var n in RocketSettings.WarheadNames) options.Add(n);
            UnityEngine.Events.UnityAction<int> onWarhead = (Action<int>)(index =>
            {
                try
                {
                    var b = _editor?.Component?.Blueprint;
                    if (b == null) return;
                    RocketSettings.ReadRaw(b.Description, out _, out var raw);
                    Write(_editor, RocketSettings.Clamp(index), raw);
                    _editor.RequestRedraw();
                }
                catch (Exception e) { Plugin.Log.LogError("Rocket warhead change failed: " + e); }
            });
            drawer.Dropdown("Warhead", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), warhead, onWarhead,
                "High explosive: blast and fragments, like an HE shell. Thermobaric (TOS-1A 'Solntsepyok'): a fuel-air cloud that " +
                "ignites into a huge fireball — a much stronger and wider blast that can set vehicles on fire, but weak against armour. " +
                "HEAT (S-8KOM): a shaped charge against armour — the same penetration at any range, with a smaller blast.");
            drawer.Float(_speedProp);

            var spec = ShellSpec.For(caliber);
            var heRadius = Mathf.Sqrt(spec.RocketHePower / 10f);
            var tbRadius = Mathf.Sqrt(spec.RocketTbPower / 10f);
            var pen = ShellSpec.Penetration(caliber, spec.RocketMass, speed,
                warhead == RocketSettings.Thermobaric ? spec.RocketTbConstant : spec.RocketHeConstant);
            var heatRadius = Mathf.Sqrt(spec.RocketHeatBlast / 10f);
            Info(drawer, $"Tube {TubeLengthMm(caliber, tube) / 1000f:0.00} m, {MassFor(caliber, tube):0} kg with the rocket " +
                         $"(rocket {RocketMassLoaded(caliber):0} kg). One rocket: the tube is empty after launch.");
            Info(drawer, warhead == RocketSettings.Thermobaric
                ? $"Thermobaric warhead: blast radius about {tbRadius:0} m ({Mathf.Clamp(Plugin.ThermobaricPower.Value, 1f, 10f):0.#}x HE), " +
                  $"fireball {spec.RocketTbRadius:0} m that can set vehicles on fire. Weak against armour: about {pen:0} mm."
                : warhead == RocketSettings.Heat
                    ? $"HEAT warhead: shaped charge, {spec.RocketHeatPenetration:0} mm at any range (Rockets: HEAT rocket penetration in the " +
                      $"mod settings), blast radius about {heatRadius:0.#} m. Spaced armour, ERA and side skirts make it detonate early."
                    : $"HE warhead: {spec.RocketFiller:0.0} kg of explosive, blast radius about {heRadius:0} m, punches about {pen:0} mm before bursting.");
            var burn = Mathf.Clamp(Plugin.RocketBurnTime.Value, 0.2f, 4f);
            Info(drawer, $"The motor burns {burn:0.0} s: the rocket flies straight along the tube for about {speed * 0.575f * burn:0} m, " +
                         $"then falls like a shell flying at {speed:0} m/s (aim higher at long range). Dispersion about " +
                         $"{Mathf.Tan(Mathf.Clamp(Plugin.RocketDispersion.Value, 0f, 5f) * Mathf.Deg2Rad) * 1000f:0} m at 1 km.");

            var group = LauncherPart.EffectiveGroup(cannon);
            var key = LauncherPart.GroupLabel(group & -group);
            Info(drawer, $"Fire group {LauncherPart.GroupLabel(group)}: press {key} in battle to switch to the rockets. A click launches " +
                         "one rocket; hold the trigger to fire all tubes of the group one after another (salvo). Press the gun's key (F1) to switch back.");
            if (!LauncherPart.HasOperator(cannon))
                Info(drawer, "No crew member operates the launcher yet! Assign it to a crew member in the crew seat settings " +
                             "(the gunner of the main gun can operate it too), otherwise it cannot be selected in battle. No loader is needed.");
            drawer.InfoField("Saved with the launcher: use the save / load buttons next to its name at the top of this panel.", 2);
        }
        finally { layout.EndDropdown(); }

        
        var instanceDrawer = editor.instancePropertyDrawer;
        if (instanceDrawer != null)
        {
            try { instanceDrawer.DrawScopeSelect(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Rocket launcher sight select failed: " + e.Message); }
            try { instanceDrawer.OnMulticannonsGUI(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Rocket launcher fire group UI failed: " + e.Message); }
        }
    }

    private static void EnsureProperties()
    {
        if (_caliberProp == null)
        {
            Il2CppSystem.Func<float> get = (Func<float>)(() =>
            {
                try { return _editor?.Component?.Blueprint?.Caliber ?? DefaultCaliber; }
                catch { return DefaultCaliber; }
            });
            Il2CppSystem.Action<float> set = (Action<float>)(v =>
            {
                try
                {
                    var ed = _editor;
                    var c = ed?.Component;
                    if (c != null) SetGeometry(ed, c, (int)Math.Round(v), TubeCaliberOf(c.Blueprint));
                }
                catch (Exception e) { Plugin.Log.LogError("Rocket launcher caliber change failed: " + e); }
            });
            _caliberProp = new FloatProperty("Caliber", get, set, MinCaliber, MaxCaliber, 1f, "0", null,
                "Rocket caliber in millimetres (S-5 57, S-8 80, Grad 122, TOS-1A 220). The warhead and the blast grow with the caliber; " +
                "the tube keeps its length in calibers.");
        }
        if (_lengthProp == null)
        {
            Il2CppSystem.Func<float> get = (Func<float>)(() =>
            {
                try { return TubeCaliberOf(_editor?.Component?.Blueprint); }
                catch { return DefaultTubeCalibers; }
            });
            Il2CppSystem.Action<float> set = (Action<float>)(v =>
            {
                try
                {
                    var ed = _editor;
                    var c = ed?.Component;
                    var b = c?.Blueprint;
                    if (b != null) SetGeometry(ed, c, b.Caliber, (float)Math.Round(v * 2f) / 2f);
                }
                catch (Exception e) { Plugin.Log.LogError("Rocket launcher length change failed: " + e); }
            });
            _lengthProp = new FloatProperty("Tube length", get, set, MinTubeCalibers, MaxTubeCalibers, 0.5f, "0.0", null,
                "Tube length in calibers (Grad about 24, TOS-1A about 15). Changes the look and the mass; the rocket flies the same.");
        }
        if (_speedProp == null)
        {
            Il2CppSystem.Func<float> get = (Func<float>)(() =>
            {
                try
                {
                    RocketSettings.Read(_editor?.Component?.Blueprint, out _, out var s);
                    return s;
                }
                catch { return RocketSettings.DefaultSpeed; }
            });
            Il2CppSystem.Action<float> set = (Action<float>)(v =>
            {
                try
                {
                    var b = _editor?.Component?.Blueprint;
                    if (b == null) return;
                    RocketSettings.ReadRaw(b.Description, out var w, out _);
                    Write(_editor, w, (float)Math.Round(v / 10f) * 10f);
                }
                catch (Exception e) { Plugin.Log.LogError("Rocket speed change failed: " + e); }
            });
            _speedProp = new FloatProperty("Rocket speed", get, set, RocketSettings.MinSpeed, RocketSettings.MaxSpeed, 10f, "0", null,
                "Speed of the rocket after the motor has burnt out, in m/s (S-8 about 600, Grad about 690). Faster rockets drop less " +
                "and reach farther.");
        }
    }

    private static void Info(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

    
    private static void Write(CannonEditor editor, int warhead, float rawSpeed)
    {
        var bp = editor?.Component?.Blueprint;
        if (bp == null) return;
        var text = RocketSettings.With(bp.Description, warhead, rawSpeed);
        if (text == bp.Description) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("Rocket settings", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
        try { bp.Description = text; }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
    }

    private static void SetGeometry(CannonEditor editor, Cannon cannon, int caliber, float tubeCalibers)
    {
        caliber = Math.Clamp(caliber, MinCaliber, MaxCaliber);
        tubeCalibers = ClampTube(tubeCalibers);
        var bp = cannon.Blueprint;
        var shell = cannon.ShellBlueprint;
        if (bp == null) return;
        if (GeometryMatches(bp, caliber, tubeCalibers) && (shell == null || shell.Diameter == caliber)) return;

        var group = editor.GetNewOperationGroupID();
        Il2CppSystem.IDisposable shellFrame = null, bpFrame = null;
        try
        {
            if (shell != null) shellFrame = editor.NewBlueprintEditFrame("Rocket launcher size", shell, null, group, VehicleOpExecutionMode.Synchronous);
            bpFrame = editor.NewBlueprintEditFrame("Rocket launcher size", bp, null, group, VehicleOpExecutionMode.Synchronous);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message);
        }
        try { ApplyGeometry(bp, shell, caliber, tubeCalibers); }
        finally
        {
            if (bpFrame != null) bpFrame.Dispose(); else bp.MarkModified();
            if (shellFrame != null) shellFrame.Dispose(); else shell?.MarkModified();
        }
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"Rocket launcher {caliber} mm, tube {tubeCalibers:0.0} calibers");
    }

    

    
    internal static PartDefinition CreateDefinition() => LauncherPart.CloneCannonDefinition(PartGuid, PartName);

    [HarmonyPatch(typeof(PartLocalizationIO), nameof(PartLocalizationIO.GetPartLocalization))]
    private static class LocalizationPatch
    {
        private static bool Prefix(string localizationID, ref PartLocalization __result)
        {
            if (localizationID != PartName) return true;
            try
            {
                var loc = new PartLocalization();
                loc.name = DisplayName;
                loc.description = Description;
                __result = loc;
                return false;
            }
            catch { return true; }
        }
    }

    private static Sprite _icon;

    [HarmonyPatch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard))]
    private static class CardPatch
    {
        private static void Postfix(PartDefinition definition, PartDisplayCard __result)
        {
            if (__result == null || definition == null || definition.guid != PartGuid) return;
            __result.Name = DisplayName;            
            __result.Description = Description;
            try
            {
                if (_icon == null || _icon.WasCollected)
                {
                    var tex = Fx.RocketIcon ?? Fx.LauncherIcon;
                    if (tex != null)
                    {
                        _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        _icon.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    }
                }
                if (_icon != null) __result.Icon = _icon;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Rocket launcher icon failed: " + e.Message); }
        }
    }
}
