using System;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Localization;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GunStabilizer;




internal static class FcsPart
{
    public const string PartGuid = "3d9f6a21-7c4e-4b8a-9e15-6f2d0c8b7a43";
    public const string PartName = "mdFireControlComputer"; 
    public const string SourceGuid = "945303c3-4da2-42d9-9b05-d8d277c20265"; 
    public const string DisplayName = "Fire Control Computer";

    public static string Description =>
        "Ballistic computer and laser rangefinder of the vehicle. Its panel sets the generation, the laser, automatic " +
        "correction, lead and the firing interlock. Without it the vehicle keeps the basic fire control (range correction only).";

    public static bool IsOurs(VehicleObject vo)
    {
        try { return vo != null && vo.guid == PartGuid; }
        catch { return false; }
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
            if (db.partDefinitionLookup.ContainsKey(PartGuid)) return;

            var def = CreateDefinition();
            var defs = db.partDefinitions;
            var merged = new Il2CppReferenceArray<PartDefinition>(defs.Length + 1);
            for (var i = 0; i < defs.Length; i++) merged[i] = defs[i];
            merged[defs.Length] = def;
            db.partDefinitions = merged;
            db.partDefinitionLookup[PartGuid] = def;
            foreach (var tag in def.tags)
            {
                if (!db.partDefinitionTagLookup.ContainsKey(tag))
                    db.partDefinitionTagLookup[tag] = new Il2CppSystem.Collections.Generic.List<PartDefinition>();
                db.partDefinitionTagLookup[tag].Add(def);
            }
            
            try
            {
                var cache = loader.partCardFactory?.cardCache;
                if (cache != null && !cache.ContainsKey(PartGuid)) cache[PartGuid] = PartDefinitionCardFactory.CreateCard(def);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fire control computer card registration failed: " + e.Message); }
            Plugin.Log.LogInfo(ModernDayTags.Active
                ? "Fire control computer part added to Modern Day → Weapon"
                : "Fire control computer part added to Firepower → Cannons");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Fire control computer part injection failed: " + e);
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    
    private static PartDefinition CreateDefinition()
    {
        var path = Path.Combine(Application.streamingAssetsPath, "Parts", "radioPart.json");
        var json = File.Exists(path) ? File.ReadAllText(path) : FallbackJson;
        json = Regex.Replace(json, ",\\s*\\{\\s*\"fileID\"\\s*:\\s*\"radio\"[^{}]*\"info\"\\s*:\\s*\\{[^{}]*\\}\\s*\\}", "");
        json = Regex.Replace(json, "\"guid\"\\s*:\\s*\"[^\"]*\"", $"\"guid\": \"{PartGuid}\"");
        json = Regex.Replace(json, "\"name\"\\s*:\\s*\"[^\"]*\"", $"\"name\": \"{PartName}\"");
        json = Regex.Replace(json, "\"tags\"\\s*:\\s*\\[[^\\]]*\\]", $"\"tags\": [ \"{ModernDayTags.Or(ModernDayTags.Other)}\" ]");
        json = Regex.Replace(json, "\"maxScale\"\\s*:\\s*\\[[^\\]]*\\]", "\"maxScale\": [ 3.0, 3.0, 3.0 ]");
        var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
        var def = obj?.TryCast<PartDefinition>();
        if (def == null) throw new InvalidOperationException("DeserializeJSON returned null");
        return def;
    }

    private const string FallbackJson = @"{
  ""v"": ""0.0"", ""guid"": ""945303c3-4da2-42d9-9b05-d8d277c20265"", ""name"": ""radio"", ""tags"": [ ""radio"" ],
  ""components"": [
    { ""fileID"": ""model"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": ""73bbf75141ec1ca449b05e3755719904"",
      ""materialGuids"": [ ""8035bef3cbca93b4eb73edb1527ef0d8"" ], ""scaling"": true, ""flipping"": true, ""selectable"": true,
      ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": true, ""autoRotate"": false,
    ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false,
    ""placementMode"": ""upright"", ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ 3.0, 3.0, 3.0 ], ""minScale"": [ 0.5, 0.5, 0.5 ], ""objects"": [] }
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
    private static bool _iconFailed;

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
                if ((_icon == null || _icon.WasCollected) && !_iconFailed) _icon = FcsIcon.Make();
                if (_icon != null) __result.Icon = _icon;
            }
            catch (Exception e)
            {
                _iconFailed = true;
                Plugin.Log.LogWarning("Fire control computer icon failed: " + e.Message);
            }
        }
    }

}


internal static class ModernDayTags
{
    public const string Other = "modernOther";
    private static bool? _active;

    public static bool Active
    {
        get
        {
            if (_active != null) return _active.Value;
            var active = false;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "ModernDay") continue;
                    active = a.GetType("ModernDay.Api")?.GetProperty("Active")?.GetValue(null) is true;
                    break;
                }
            }
            catch { }
            _active = active;
            return active;
        }
    }

    public static string Or(string modernTag) => Active ? modernTag : "cannon";
}
