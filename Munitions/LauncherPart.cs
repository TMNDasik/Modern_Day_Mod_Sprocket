using System;
using System.Collections.Generic;
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




internal static class LauncherPart
{
    public const string PartGuid = "d3a5f1c2-7b64-4e19-8a2f-5c0e9b3d7a15";
    
    
    public const string PartName = "mdAtgmLauncher";
    public const string CannonGuid = "bfa86d1e-41ab-43fa-92f8-ef29e1e52c00";
    public const string NoMuzzleGuid = "34c1eda8-bcff-4879-89df-1e826cce8b51";
    public const int MinCaliber = 75, MaxCaliber = 250, DefaultCaliber = 152;
    public const float TubeCalibers = 8f;

    public const string DisplayName = "ATGM Launcher";

    public static string Description =>
        "Single-use tube with one anti-tank guided missile (Kornet class), 75-250 mm. Select it with its fire group key; " +
        "the missile leaves about a second after the trigger and flies along your line of sight. Needs a crew member to operate it.";

    
    
    public static bool IsLauncher(Component c)
    {
        var guid = GuidOf(c);
        return guid == PartGuid || guid == RocketPart.PartGuid;
    }

    
    public static bool IsAtgmLauncher(Component c) => GuidOf(c) == PartGuid;

    
    internal static string GuidOf(Component c)
    {
        if (c == null) return null;
        try
        {
            var vo = c.GetComponent<VehicleObject>();
            if (vo == null) vo = c.GetComponentInParent<VehicleObject>();
            return vo != null ? vo.guid : null;
        }
        catch { return null; }
    }

    

    public static int WallThickness(int caliber) => Math.Max(3, (int)Math.Round(0.045 * caliber));

    public static ushort PropellantFor(int caliber) => (ushort)Math.Max(50, caliber / 2);

    
    public const float MinTubeCalibers = 3f, MaxTubeCalibers = 20f;

    public static float ClampTube(float tubeCalibers) => Math.Clamp(tubeCalibers, MinTubeCalibers, MaxTubeCalibers);

    public static ushort TubeLengthMm(int caliber, float tubeCalibers) => (ushort)Math.Round(ClampTube(tubeCalibers) * caliber);

    public static float TubeCaliberOf(CannonBlueprint bp)
    {
        try
        {
            var segments = bp?.GetSegment(0)?.Segments;
            if (segments == null || segments.Length == 0 || bp.Caliber == 0) return TubeCalibers;
            return ClampTube((float)Math.Round(segments[0].Length / (float)bp.Caliber * 2f) / 2f);
        }
        catch { return TubeCalibers; }
    }

    
    public static float MassFor(int caliber, float tubeCalibers)
    {
        var k = caliber / 152f;
        return Math.Max(4f, Plugin.LauncherMass.Value * k * k * k * (0.6f + 0.4f * ClampTube(tubeCalibers) / TubeCalibers));
    }

    
    public static void ApplyGeometry(CannonBlueprint bp, ShellSlotBlueprint shell, int caliber, float tubeCalibers)
    {
        caliber = Math.Clamp(caliber, MinCaliber, MaxCaliber);
        var prop = PropellantFor(caliber);
        if (shell != null)
        {
            shell.Diameter = (ushort)caliber;
            shell.PropellantLength = prop;
        }
        if (bp == null) return;
        bp.Caliber = (ushort)caliber;
        bp.PropellantLength = prop;
        bp.MuzzlePartGuid = NoMuzzleGuid;
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

    
    
    public const float BreechWidthCalibers = 1.0f, BreechLengthCalibers = 0.25f;

    
    public static bool IsLauncherBlueprint(CannonBlueprint bp)
    {
        if (bp == null || bp.MuzzlePartGuid != NoMuzzleGuid) return false;
        return IsTube(bp) || RocketPart.IsTube(bp);
    }

    [HarmonyPatch(typeof(CannonBlueprint), nameof(CannonBlueprint.RecalculatePhysicalValues))]
    private static class BreechPatch
    {
        private static void Postfix(CannonBlueprint __instance)
        {
            try
            {
                if (!IsLauncherBlueprint(__instance)) return;
                var c = __instance.Caliber / 1000f;
                __instance.breechSize = new Vector3(BreechWidthCalibers * c, BreechWidthCalibers * c, BreechLengthCalibers * c);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Launcher breech failed: " + e.Message); }
        }
    }

    
    public static bool IsTube(CannonBlueprint bp)
    {
        try
        {
            if (bp == null || bp.SegmentCount != 1) return false;
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

    public static bool GeometryMatches(CannonBlueprint bp, int caliber, float tubeCalibers)
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
                if (__result == null || !IsAtgmLauncher(__instance)) return;
                __result.Name = DisplayName;
                ApplyGeometry(__result, __instance.ShellBlueprint, DefaultCaliber, TubeCalibers);
            }
            catch (Exception e) { Plugin.Log.LogError("Launcher blueprint setup failed: " + e); }
        }
    }

    

    
    public const int AllGroups = 0x3FF;

    public static string GroupLabel(int mask)
    {
        var sb = new System.Text.StringBuilder();
        for (var b = 0; b < 10; b++)
            if ((mask & (1 << b)) != 0) sb.Append(sb.Length > 0 ? "+" : "").Append('F').Append(b + 1);
        return sb.Length > 0 ? sb.ToString() : "-";
    }

    
    
    
    
    
    public static Dictionary<IntPtr, int> ResolveFireGroups(Component anyPart)
    {
        var result = new Dictionary<IntPtr, int>();
        if (anyPart == null) return result;
        var vehicle = anyPart.GetComponentInParent<Vehicle>();
        var root = vehicle != null ? vehicle.transform : anyPart.transform.root;
        var used = 0;
        var launchers = new List<Cannon>();
        var rockets = new List<Cannon>();
        foreach (var c in root.GetComponentsInChildren<Cannon>(true))
        {
            var guid = GuidOf(c);
            if (guid == PartGuid) { launchers.Add(c); continue; }
            if (guid == RocketPart.PartGuid) { rockets.Add(c); continue; }
            var ib = c.InstanceBlueprint;
            used |= ib != null ? ib.FireGroup : 1;
        }
        var atgmGroups = AssignGroups(launchers, used, result);
        AssignGroups(rockets, used | atgmGroups, result);
        return result;
    }

    
    private static int AssignGroups(List<Cannon> tubes, int used, Dictionary<IntPtr, int> result)
    {
        var all = 0;
        foreach (var c in tubes)
        {
            var ib = c.InstanceBlueprint;
            var own = (ib != null ? ib.FireGroup : 1) & AllGroups;
            var mask = own & ~used;
            if (mask == 0)
                for (var b = 0; b < 10; b++)
                    if ((used & (1 << b)) == 0) { mask = 1 << b; break; }
            if (mask == 0) mask = own; 
            result[c.Pointer] = mask;
            all |= mask;
        }
        return all;
    }

    
    public static bool HasOperator(Cannon cannon)
    {
        try
        {
            var op = cannon != null ? cannon.Operable : null;
            return op == null || op.OperatorAssigned;
        }
        catch { return true; }
    }

    
    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class MassPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                if (!IsAtgmLauncher(__instance)) return;
                var bp = __instance.Blueprint;
                if (bp == null) return;
                __instance.SetMass(MassFor(bp.Caliber, TubeCaliberOf(bp)), MassType.Weapons);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Launcher mass failed: " + e.Message); }
        }
    }

    

    private static bool _expanded = true;
    private static Il2CppSystem.Object _header;
    private static FloatProperty _caliberProp, _lengthProp;
    private static CannonEditor _editor;
    private static readonly System.Collections.Generic.HashSet<IntPtr> Fixed = new();

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
                if (!IsAtgmLauncher(cannon)) return true;
                DrawPanel(__instance, cannon, layout);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Launcher panel failed: " + e);
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

        
        if (bp != null && !IsTube(bp) && Fixed.Add(bp.Pointer)) SetGeometry(editor, cannon, caliber, TubeCalibers);
        var tube = TubeCaliberOf(bp);

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
                catch (Exception e) { Plugin.Log.LogError("Launcher caliber change failed: " + e); }
            });
            _caliberProp = new FloatProperty("Caliber", get, set, MinCaliber, MaxCaliber, 1f, "0", null,
                "Missile caliber in millimetres. Penetration and warhead grow with the caliber; the tube keeps its length in calibers.");
        }
        if (_lengthProp == null)
        {
            Il2CppSystem.Func<float> get = (Func<float>)(() =>
            {
                try { return TubeCaliberOf(_editor?.Component?.Blueprint); }
                catch { return TubeCalibers; }
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
                catch (Exception e) { Plugin.Log.LogError("Launcher length change failed: " + e); }
            });
            _lengthProp = new FloatProperty("Tube length", get, set, MinTubeCalibers, MaxTubeCalibers, 0.5f, "0.0", null,
                "Tube length in calibers (8 = Kornet). Changes the look and the mass; the missile flies the same.");
        }

        
        FixFireGroupOnce(editor, cannon, "ATGM launcher");

        
        try
        {
            var name = editor.properties?.name;
            if (name != null)
            {
                UnityEngine.Events.UnityAction save = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_0(); }
                    catch (Exception e) { Plugin.Log.LogError("Launcher save failed: " + e.Message); }
                });
                UnityEngine.Events.UnityAction load = (Action)(() =>
                {
                    try { editor._OnGUI_b__18_1(); }
                    catch (Exception e) { Plugin.Log.LogError("Launcher load failed: " + e.Message); }
                });
                drawer.InputField(name, save, load);
            }
        }
        catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Launcher name field failed: " + e.Message); }

        Il2CppSystem.Action<bool> onExpand = (Action<bool>)(v => _expanded = v);
        layout.BeginDropdown(Header, _expanded, onExpand);
        try
        {
            drawer.Float(_caliberProp);
            drawer.Float(_lengthProp);
            Info(drawer, $"Tube {TubeLengthMm(caliber, tube) / 1000f:0.00} m, {MassFor(caliber, tube):0} kg with the missile. " +
                         "One missile: the tube is empty after launch. Guided along the line of sight, like the gun-launched ATGM.");
            AtgmPanel.Draw(editor, drawer, Kind.Launcher);

            var group = EffectiveGroup(cannon);
            var key = GroupLabel(group & -group);
            Info(drawer, $"Fire group {GroupLabel(group)}: press {key} in battle to switch to the launcher, then fire as usual " +
                         "(the launch follows about a second after the trigger). Several tubes in one fire group launch one by one: " +
                         "one missile per click. Press the gun's key (F1) to switch back.");
            if (!HasOperator(cannon))
                Info(drawer, "No crew member operates the launcher yet! Assign it to a crew member in the crew seat settings " +
                             "(the gunner of the main gun can operate it too), otherwise it cannot be selected in battle. No loader is needed.");
        }
        finally { layout.EndDropdown(); }

        
        var instanceDrawer = editor.instancePropertyDrawer;
        if (instanceDrawer != null)
        {
            try { instanceDrawer.DrawScopeSelect(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Launcher sight select failed: " + e.Message); }
            try { instanceDrawer.OnMulticannonsGUI(layout); }
            catch (Exception e) { if (_uiErrors++ < 3) Plugin.Log.LogWarning("Launcher fire group UI failed: " + e.Message); }
        }
    }

    private static int _uiErrors;
    private static readonly HashSet<IntPtr> GroupFixed = new();
    private static IntPtr _groupCannon;
    private static int _groupMask;
    private static float _groupTime;

    private static void Info(IGUIElementDrawer drawer, string text) =>
        drawer.InfoField(text, Math.Max(1, (int)Math.Ceiling(text.Length / 46.0)));

    
    internal static int EffectiveGroup(Cannon cannon)
    {
        if (cannon.Pointer == _groupCannon && Time.unscaledTime < _groupTime) return _groupMask;
        _groupCannon = cannon.Pointer;
        _groupTime = Time.unscaledTime + 0.5f;
        try
        {
            _groupMask = ResolveFireGroups(cannon).TryGetValue(cannon.Pointer, out var m) ? m : cannon.InstanceBlueprint?.FireGroup ?? 1;
        }
        catch { _groupMask = 1; }
        return _groupMask;
    }

    
    internal static void FixFireGroupOnce(CannonEditor editor, Cannon cannon, string label)
    {
        var ib = cannon.InstanceBlueprint;
        if (ib != null && GroupFixed.Add(ib.Pointer)) FixFireGroup(editor, cannon, ib, label);
    }

    private static void FixFireGroup(CannonEditor editor, Cannon cannon, CannonInstanceBlueprint ib, string label)
    {
        try
        {
            if (!ResolveFireGroups(cannon).TryGetValue(cannon.Pointer, out var mask) || mask == 0 || mask == ib.FireGroup) return;
            Il2CppSystem.IDisposable frame = null;
            try { frame = editor.NewBlueprintEditFrame(label + " fire group", ib, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Edit frame unavailable: " + e.Message); }
            try { ib.FireGroup = (ushort)mask; }
            finally { if (frame != null) frame.Dispose(); else ib.MarkModified(); }
            _groupTime = 0f;
            Plugin.Log.LogInfo($"{label} moved to fire group {GroupLabel(mask)}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Launcher fire group setup failed: " + e.Message); }
    }

    internal static void SetGeometry(CannonEditor editor, Cannon cannon, int caliber, float tubeCalibers)
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
            if (shell != null) shellFrame = editor.NewBlueprintEditFrame("ATGM launcher size", shell, null, group, VehicleOpExecutionMode.Synchronous);
            bpFrame = editor.NewBlueprintEditFrame("ATGM launcher size", bp, null, group, VehicleOpExecutionMode.Synchronous);
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
        if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"ATGM launcher {caliber} mm, tube {tubeCalibers:0.0} calibers");
    }

    

    private static PartDefinitionDatabase _db;
    private static float _nextSearch;

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
                Plugin.Log.LogWarning("Cannon part definition not found — ATGM and rocket launchers not added");
                return;
            }
            Inject(loader, db, PartGuid, () => CloneCannonDefinition(PartGuid, PartName), "ATGM launcher");
            Inject(loader, db, RocketPart.PartGuid, RocketPart.CreateDefinition, "Rocket launcher");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Launcher part injection failed: " + e);
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    
    private static void Inject(Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader loader, PartDefinitionDatabase db, string guid,
        Func<PartDefinition> create, string label)
    {
        try
        {
            if (db.partDefinitionLookup.ContainsKey(guid)) return;
            var def = create();
            var defs = db.partDefinitions;
            var merged = new Il2CppReferenceArray<PartDefinition>(defs.Length + 1);
            for (var i = 0; i < defs.Length; i++) merged[i] = defs[i];
            merged[defs.Length] = def;
            db.partDefinitions = merged;
            db.partDefinitionLookup[guid] = def;
            foreach (var tag in def.tags)
            {
                if (!db.partDefinitionTagLookup.ContainsKey(tag))
                    db.partDefinitionTagLookup[tag] = new Il2CppSystem.Collections.Generic.List<PartDefinition>();
                db.partDefinitionTagLookup[tag].Add(def);
            }
            
            try
            {
                var cache = loader.partCardFactory?.cardCache;
                if (cache != null && !cache.ContainsKey(guid)) cache[guid] = PartDefinitionCardFactory.CreateCard(def);
            }
            catch (Exception e) { Plugin.Log.LogWarning(label + " card registration failed: " + e.Message); }
            Plugin.Log.LogInfo(ModernDayTags.Active ? $"{label} part added to Modern Day → Other" : $"{label} part added to the Cannons tab");
        }
        catch (Exception e) { Plugin.Log.LogError($"{label} part injection failed: " + e); }
    }

    
    
    
    internal static PartDefinition CloneCannonDefinition(string guid, string name)
    {
        var path = Path.Combine(Application.streamingAssetsPath, "Parts", "cannonPart.json");
        var json = File.Exists(path) ? File.ReadAllText(path) : FallbackCannonJson;
        json = Regex.Replace(json, "\"guid\"\\s*:\\s*\"[^\"]*\"", $"\"guid\": \"{guid}\"");
        json = Regex.Replace(json, "\"name\"\\s*:\\s*\"[^\"]*\"", $"\"name\": \"{name}\"");
        json = Regex.Replace(json, "\"order\"\\s*:\\s*-?\\d+", "\"order\": 0");
        json = Regex.Replace(json, "\"dimensions\"\\s*:\\s*\\[[^\\]]*\\]", "\"dimensions\": [ 1000.0, 1000.0, 1000.0 ]");
        
        if (ModernDayTags.Active)
            json = Regex.Replace(json, "\"tags\"\\s*:\\s*\\[[^\\]]*\\]", $"\"tags\": [ \"{ModernDayTags.Other}\" ]");
        var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
        var def = obj?.TryCast<PartDefinition>();
        if (def == null) throw new InvalidOperationException("DeserializeJSON returned null");
        return def;
    }

    private const string FallbackCannonJson = @"{
  ""v"": ""0.1"", ""guid"": ""bfa86d1e-41ab-43fa-92f8-ef29e1e52c00"", ""name"": ""cannon"", ""order"": -1, ""tags"": [ ""cannon"" ],
  ""components"": [
    { ""fileID"": ""cannon"", ""type"": ""cannon"", ""info"": { ""v"": ""0.0"", ""barrelFileID"": ""barrel"", ""breechFileID"": ""breech"", ""materialGuid"": ""38c8229f8ae921e40936565b4fec4b4a"" } },
    { ""fileID"": ""barrel"", ""type"": ""cannonBarrel"", ""info"": { ""v"": ""0.0"", ""segmentPartGuid"": ""c56166b7-89f3-4791-9a22-a9c3c8e8ce16"", ""boreMeshGuid"": ""21caf9e215693864d91936efe5a34108"", ""boreMaterialGuid"": ""5bc48a80d6e5b5d43ae73c1d3bd35164"" } },
    { ""fileID"": ""breech"", ""type"": ""cannonBreech"", ""info"": { ""v"": ""0.0"", ""modelFileID"": ""breechModel"", ""dimensions"": [ 1.15, 0.5, 1.65 ] } },
    { ""fileID"": ""breechModel"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": ""7a8ff3cb025d810419bfac7113190a8f"", ""materialGuids"": [ ""45b8d5b164d20c240bcbdebd39a385d6"" ], ""scaling"": true, ""flipping"": true, ""selectable"": false, ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": true, ""autoRotate"": false, ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false, ""placementMode"": ""upright"",
    ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ 1.0, 1.0, 1.0 ], ""minScale"": [ 1.0, 1.0, 1.0 ], ""objects"": [] }
}";

    
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
                    var tex = Fx.LauncherIcon;
                    if (tex != null)
                    {
                        _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        _icon.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    }
                }
                if (_icon != null) __result.Icon = _icon;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Launcher icon failed: " + e.Message); }
        }
    }
}
