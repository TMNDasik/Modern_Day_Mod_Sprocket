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

namespace Munitions;




internal static class EjectorPart
{
    public const string PartGuid = "e7c4b2a9-5d31-4f86-9b0e-2a6c8d4f1e73";
    public const string PartName = "mdCasingEjector"; 
    public const string SourceGuid = "8af72408-97fd-4051-b8a3-dd0643674337"; 
    public const string DisplayName = "Casing Ejection Port";

    public static string Description =>
        "Hatch that throws spent cases overboard, in the direction it faces. It serves the gun chosen in that gun's " +
        "\"Casing ejection\" setting, otherwise the nearest gun. Without it, no cases are ejected.";

    public static bool IsEjector(VehicleObject vo)
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
            if (!db.partDefinitionLookup.ContainsKey(SourceGuid))
            {
                Plugin.Log.LogWarning("Loader port part definition not found — casing ejection port not added");
                return;
            }

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
            catch (Exception e) { Plugin.Log.LogWarning("Ejection port card registration failed: " + e.Message); }
            Plugin.Log.LogInfo("Casing ejection port part added to the Cannons tab");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Casing ejection port injection failed: " + e);
            _nextSearch = Time.unscaledTime + 30f;
        }
    }

    
    private static PartDefinition CreateDefinition()
    {
        var path = Path.Combine(Application.streamingAssetsPath, "Parts", "loaderPortPart.json");
        var json = File.Exists(path) ? File.ReadAllText(path) : FallbackJson;
        json = Regex.Replace(json, "\"guid\"\\s*:\\s*\"[^\"]*\"", $"\"guid\": \"{PartGuid}\"");
        json = Regex.Replace(json, "\"name\"\\s*:\\s*\"[^\"]*\"", $"\"name\": \"{PartName}\"");
        json = Regex.Replace(json, "\"tags\"\\s*:\\s*\\[[^\\]]*\\]", $"\"tags\": [ \"{ModernDayTags.Or(ModernDayTags.Other)}\", \"decoration\" ]");
        
        json = Regex.Replace(json, "\"minScale\"\\s*:\\s*\\[[^\\]]*\\]", "\"minScale\": [ 0.25, 0.25, 0.25 ]");
        json = Regex.Replace(json, "\"maxScale\"\\s*:\\s*\\[[^\\]]*\\]", "\"maxScale\": [ 4.0, 4.0, 4.0 ]");
        var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
        var def = obj?.TryCast<PartDefinition>();
        if (def == null) throw new InvalidOperationException("DeserializeJSON returned null");
        return def;
    }

    private const string FallbackJson = @"{
  ""v"": ""0.0"", ""guid"": ""8af72408-97fd-4051-b8a3-dd0643674337"", ""name"": ""loaderPort"", ""tags"": [ ""decoration"" ],
  ""components"": [
    { ""fileID"": ""model"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": ""5c6fd48d4efc97641bc6cabb91a9c520"",
      ""materialGuids"": [ ""2f0182c14a6466048ad88ef63276b8c7"" ], ""scaling"": true, ""flipping"": true, ""selectable"": true,
      ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": true, ""autoRotate"": false,
    ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false,
    ""placementMode"": ""forwardFaceNormal"", ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ 2.0, 2.0, 2.0 ], ""minScale"": [ 0.5, 0.5, 0.5 ], ""objects"": [] }
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
                    var tex = Fx.EjectorIcon;
                    if (tex != null)
                    {
                        _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        _icon.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    }
                }
                if (_icon != null) __result.Icon = _icon;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Ejection port icon failed: " + e.Message); }
        }
    }
}
