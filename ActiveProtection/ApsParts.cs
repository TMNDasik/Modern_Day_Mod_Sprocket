using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Localization;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ActiveProtection;






internal static class ApsParts
{
    public const string DetectorGuid = "3d9b6e42-1f7a-4c5d-8e20-a64b9c7d1e53";
    public const string LauncherGuid = "8b4f2c19-6e3a-47d1-b5c8-0f92e1a7d634";
    
    
    public const string DetectorName = "mdApsDetector";
    public const string LauncherName = "mdApsLauncher";
    public const string DetectorTitle = "APS Detector";
    public const string LauncherTitle = "APS Launcher";

    
    private const string MeshGuid = "5c6fd48d4efc97641bc6cabb91a9c520";
    private const string MaterialGuid = "2f0182c14a6466048ad88ef63276b8c7";

    
    public const string WarnerGuid = "6a1d3f58-2c9e-4b07-91e4-7f5b0c2d8e16";
    public const string SmokeGuid = "d24e8b71-5f3a-4c69-a0d2-1b8e6f4c9a37";
    public const string JammerGuid = "0f7c5e93-8b14-4d2a-b6e1-c39a2d7f5b48";

    
    public const string EraGuid = "b3e9a4c7-5d21-4f68-9a0e-7c4d2b1f8e65";

    
    public const string PanelGuid = "e71c5a2d-9f46-4b83-a1d7-36c0b8e4f259";

    private const string HiddenTag = "apsLegacy";

    private sealed class PartInfo
    {
        public string Guid, Name, Title, Description;
        public ModuleKind Kind;
        public Sprite Icon;
    }

    private static readonly PartInfo[] Parts =
    {
        new() { Guid = "172ca7d9-08b7-439e-9492-f806a1c75202", Name = "mdRadioJammer", Title = "Radio Jammer", Kind = ModuleKind.RadioJammer,
            Description = "Jams the video of radio FPV drones inside its coverage: the pilot keeps control but loses the picture. " +
                          "Fiber-optic drones are immune." },
        new() { Guid = DetectorGuid, Name = DetectorName, Title = DetectorTitle, Kind = ModuleKind.Detector,
            Description = "Active protection radar. Set its range, direction, coverage angles and the threats it reports. " +
                          "Linked APS launchers fire only at threats it detects." },
        new() { Guid = LauncherGuid, Name = LauncherName, Title = LauncherTitle, Kind = ModuleKind.Launcher,
            Description = "Active protection launcher with 1-3 charges. Shoots down a threat inside its interception zone after its " +
                          "reaction time. Needs a linked, working APS Detector." },
        new() { Guid = WarnerGuid, Name = "mdEoSensor", Title = "EO Sensor", Kind = ModuleKind.Warner,
            Description = "Laser and missile-launch warner (Shtora type). Detects lasers aimed at the vehicle and launches in its " +
                          "sector, warns the crew and triggers linked grenade launchers and IR jammers." },
        new() { Guid = SmokeGuid, Name = "mdEoSmokeLauncher", Title = "Aerosol Grenade Launcher", Kind = ModuleKind.Smoke,
            Description = "Fires aerosol grenades, from a linked EO Sensor or the smoke key. The screen blocks sights, thermal " +
                          "imaging, laser rangefinders and missile guidance." },
        new() { Guid = JammerGuid, Name = "mdEoIrJammer", Title = "IR Jammer", Kind = ModuleKind.Jammer,
            Description = "Pulsed IR searchlight that pulls SACLOS missiles with IR trackers (Konkurs, TOW, Metis) off course. " +
                          "Laser beam-riding missiles are not affected." },
        new() { Guid = EraGuid, Name = "mdEraBlock", Title = "ERA Block", Kind = ModuleKind.Era,
            Description = "Old invisible reactive armour block, kept for older vehicles. New designs: turn on ERA in the panel of an " +
                          "applique armour part instead." },
        new() { Guid = PanelGuid, Name = "mdBlowoffPanel", Title = "Blow-off Panel", Kind = ModuleKind.Panel,
            Description = "Roof panel over an isolated ammo compartment (Abrams, Object 640). If those racks cook off, it blows off and " +
                          "vents the blast upward: the crew survives and only that ammo is lost." }
    };

    public static ModuleKind? KindOf(VehicleObject vo)
    {
        try
        {
            if (vo == null) return null;
            var g = vo.guid;
            foreach (var p in Parts) if (p.Guid == g) return p.Kind;
        }
        catch { }
        return null;
    }

    public static bool IsOurs(VehicleObject vo) => KindOf(vo) != null;

    

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
            var added = new List<PartDefinition>();
            foreach (var p in Parts)
            {
                if (db.partDefinitionLookup.ContainsKey(p.Guid)) continue;
                
                
                
                var flat = p.Kind is ModuleKind.Era or ModuleKind.Panel;
                var tag = p.Kind == ModuleKind.Era ? HiddenTag
                    : flat ? ModernDayTags.Era : ModernDayTags.Or((EoInfo.IsAps(p.Kind) || p.Kind == ModuleKind.RadioJammer) ? ModernDayTags.Aps : ModernDayTags.Eo);
                var json = flat ? EraJson(p.Guid, p.Name, tag, ApsSettings.MinScale(p.Kind), ApsSettings.MaxScale(p.Kind))
                    : Json(p.Guid, p.Name, EoInfo.IsAps(p.Kind) ? 3f : 4f, tag);
                var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
                var def = obj?.TryCast<PartDefinition>();
                if (def == null) throw new InvalidOperationException("DeserializeJSON returned null for " + p.Name);
                added.Add(def);
            }
            if (added.Count == 0) return;

            var defs = db.partDefinitions;
            var merged = new Il2CppReferenceArray<PartDefinition>(defs.Length + added.Count);
            for (var i = 0; i < defs.Length; i++) merged[i] = defs[i];
            for (var i = 0; i < added.Count; i++) merged[defs.Length + i] = added[i];
            db.partDefinitions = merged;
            foreach (var def in added)
            {
                db.partDefinitionLookup[def.guid] = def;
                foreach (var tag in def.tags)
                {
                    if (!db.partDefinitionTagLookup.ContainsKey(tag))
                        db.partDefinitionTagLookup[tag] = new Il2CppSystem.Collections.Generic.List<PartDefinition>();
                    db.partDefinitionTagLookup[tag].Add(def);
                }
                
                try
                {
                    var cache = loader.partCardFactory?.cardCache;
                    if (cache != null && !cache.ContainsKey(def.guid)) cache[def.guid] = PartDefinitionCardFactory.CreateCard(def);
                }
                catch (Exception e) { Plugin.Log.LogWarning("APS part card registration failed: " + e.Message); }
            }
            Plugin.Log.LogInfo(ModernDayTags.Active
                ? $"APS/EO/blow-off parts added to Modern Day → APS, Panels and EO / KOEP ({added.Count}; old ERA Block hidden)"
                : $"APS/EO/blow-off parts added to Firepower → Cannons ({added.Count}; old ERA Block hidden)");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("APS part injection failed: " + e);
            _db = null;
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    private static string Json(string guid, string name, float maxScale, string tag) => @"{
  ""v"": ""0.0"", ""guid"": """ + guid + @""", ""name"": """ + name + @""", ""tags"": [ """ + tag + @""" ],
  ""components"": [
    { ""fileID"": ""model"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": """ + MeshGuid + @""",
      ""materialGuids"": [ """ + MaterialGuid + @""" ], ""scaling"": true, ""flipping"": true, ""selectable"": true,
      ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": true, ""autoRotate"": true,
    ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false,
    ""placementMode"": ""faceNormalUpright"", ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ " + S(maxScale) + ", " + S(maxScale) + ", " + S(maxScale) +
        @" ], ""minScale"": [ 0.4, 0.4, 0.4 ], ""objects"": [] }
}";

    
    
    private static string EraJson(string guid, string name, string tag, Vector3 min, Vector3 max) => @"{
  ""v"": ""0.0"", ""guid"": """ + guid + @""", ""name"": """ + name + @""", ""tags"": [ """ + tag + @""" ],
  ""components"": [
    { ""fileID"": ""model"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": """ + MeshGuid + @""",
      ""materialGuids"": [ """ + MaterialGuid + @""" ], ""scaling"": true, ""flipping"": true, ""selectable"": true,
      ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": false, ""autoRotate"": false,
    ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false,
    ""placementMode"": ""forwardFaceNormal"", ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ " + V(max) + @" ], ""minScale"": [ " + V(min) +
        @" ], ""objects"": [] }
}";

    private static string S(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    private static string V(Vector3 v) => string.Join(", ", new[] { v.x, v.y, v.z }.Select(x => x.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)));

    
    [HarmonyPatch(typeof(PartLocalizationIO), nameof(PartLocalizationIO.GetPartLocalization))]
    private static class LocalizationPatch
    {
        private static bool Prefix(string localizationID, ref PartLocalization __result)
        {
            foreach (var p in Parts)
            {
                if (localizationID != p.Name) continue;
                try
                {
                    var loc = new PartLocalization();
                    loc.name = p.Title;
                    loc.description = p.Description;
                    __result = loc;
                    return false;
                }
                catch { return true; }
            }
            return true;
        }
    }

    
    
    [HarmonyPatch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard))]
    private static class CardPatch
    {
        private static void Postfix(PartDefinition definition, PartDisplayCard __result)
        {
            if (__result == null || definition == null) return;
            try
            {
                var guid = definition.guid;
                foreach (var p in Parts)
                {
                    if (p.Guid != guid) continue;
                    __result.Name = p.Title;
                    __result.Description = p.Description;
                    var icon = Icon(p);
                    if (icon != null) __result.Icon = icon;
                    break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("APS part icon failed: " + e.Message); }
        }
    }

    private static Sprite Icon(PartInfo p)
    {
        if (p.Icon != null && !p.Icon.WasCollected) return p.Icon;
        var tex = p.Kind switch
        {
            ModuleKind.Era => Era.Icon(),
            ModuleKind.Panel => Blowoff.Icon(),
            _ => Assets.Icon(p.Kind)
        };
        if (tex == null) return null;
        p.Icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        p.Icon.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return p.Icon;
    }

    

    [HarmonyPatch(typeof(VehicleObject), nameof(VehicleObject.Initiate))]
    private static class InitiatePatch
    {
        private static void Postfix(VehicleObject __instance)
        {
            try { Modules.Ensure(__instance); }
            catch { }
        }
    }

    
    
    
    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Initiate))]
    private static class ModelInitiatePatch
    {
        private static void Postfix(VehicleObjectModel __instance) => HideModel(__instance);
    }

    [HarmonyPatch(typeof(VehicleObjectModel), nameof(VehicleObjectModel.Build))]
    private static class ModelBuildPatch
    {
        private static void Postfix(VehicleObjectModel __instance) => HideModel(__instance);
    }

    private static void HideModel(VehicleObjectModel model)
    {
        try
        {
            var vo = model.GetComponent<VehicleObject>() ?? model.GetComponentInParent<VehicleObject>();
            
            if (EraHosts.IsOn(vo)) model.MarkDynamic();
            if (!IsOurs(vo)) return;
            model.MarkDynamic();
            model.SetVisible(false);
            var m = Modules.Ensure(vo);
            if (m != null && m.Kind is ModuleKind.Era or ModuleKind.Panel) Era.ApplyMass(m, model);
        }
        catch { }
    }
}
