using System;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Localization;
using Sprocket.PartImporting;
using Sprocket.Vehicles;
using UnityEngine;

namespace ThermalSight;


internal readonly struct SightConfig
{
    public readonly int Gen, Optical, Digital;

    public SightConfig(int gen, int optical, int digital)
    {
        Gen = Math.Clamp(gen, 1, 4);
        Optical = Math.Clamp(optical, 0, ThermalParts.MaxZoom);
        Digital = Math.Clamp(digital, 0, ThermalParts.MaxZoom);
    }

    
    
    public float OpticalMax => Math.Max(1, Optical);
    public float TotalMax => Math.Min(ThermalParts.MaxZoom, Math.Max(OpticalMax, Optical + Digital));
    
    public float DigitalCropMax => TotalMax / OpticalMax;

    public SightConfig WithGen(int g) => new(g, Optical, Digital);
    public SightConfig WithOptical(int o) => new(Gen, o, Digital);
    public SightConfig WithDigital(int d) => new(Gen, Optical, d);
}



internal static class ThermalParts
{
    public const string GunnerSightGuid = "f90f373f-4edb-4563-a162-6eee6a5d77dd";
    public const int MaxZoom = 50;
    public const int DefaultOptical = 10;
    public const int DefaultDigital = 10;

    
    public static readonly string[] Guids =
    {
        "7b1e4c2a-9d3f-4e8a-b6c1-5f2a8e9d0c31",
        "7b1e4c2a-9d3f-4e8a-b6c1-5f2a8e9d0c32",
        "7b1e4c2a-9d3f-4e8a-b6c1-5f2a8e9d0c33",
        "7b1e4c2a-9d3f-4e8a-b6c1-5f2a8e9d0c34",
    };

    
    private const string ConfigPrefix = "7b1e4c2a-9d3f-4e8a-b6c2-";

    
    
    public static readonly string[] Names = { "mdThermalSight", "mdThermalSightGen2", "mdThermalSightGen3", "mdThermalSightGen4" };

    public const string DisplayName = "Thermal Sight";

    public static string Description =>
        "A thermal imaging sight for the gunner. Inside the vehicle press " + Plugin.ToggleKeyName +
        " to switch to the thermal channel and back, zoom with the mouse wheel. Generation and zoom limits are set in the part properties.";

    
    public const string AnyGuidPattern = "7b1e4c2a-9d3f-4e8a-b6c[12]-[0-9a-fA-F]{12}";

    public static bool TryDecode(string guid, out SightConfig config)
    {
        config = default;
        if (guid == null) return false;
        for (var i = 0; i < Guids.Length; i++)
            if (string.Equals(Guids[i], guid, StringComparison.OrdinalIgnoreCase))
            {
                config = new SightConfig(i + 1, DefaultOptical, DefaultDigital);
                return true;
            }
        if (guid.Length == 36 && guid.StartsWith(ConfigPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var tail = guid.Substring(ConfigPrefix.Length);
            if (int.TryParse(tail.Substring(0, 1), NumberStyles.HexNumber, null, out var g) &&
                int.TryParse(tail.Substring(1, 2), NumberStyles.HexNumber, null, out var o) &&
                int.TryParse(tail.Substring(3, 2), NumberStyles.HexNumber, null, out var d) && g is >= 1 and <= 4)
            {
                config = new SightConfig(g, o, d);
                return true;
            }
        }
        return false;
    }

    public static string Encode(SightConfig c)
    {
        if (c.Optical == DefaultOptical && c.Digital == DefaultDigital) return Guids[c.Gen - 1];
        return $"{ConfigPrefix}{c.Gen:x1}{c.Optical:x2}{c.Digital:x2}0000000";
    }

    public static int GenFromGuid(string guid) => TryDecode(guid, out var c) ? c.Gen : 0;

    public static int GenFromName(string name)
    {
        if (name == null) return 0;
        for (var i = 0; i < Names.Length; i++)
            if (name == Names[i]) return i + 1;
        return 0;
    }

    public static PartDefinitionDatabase Database { get; private set; }
    private static PartDefinitionCardFactory _cards;
    private static string _sourceJson;

    

    private static float _nextLoaderSearch;

    
    public static void EnsureInjected(bool force = false)
    {
        if (Database != null) return;
        if (!force && UnityEngine.Time.unscaledTime < _nextLoaderSearch) return;
        _nextLoaderSearch = UnityEngine.Time.unscaledTime + 1f;
        try
        {
            var loader = UnityEngine.Object.FindObjectOfType<Sprocket.Vehicles.PartImporting.PartAssetDatabaseLoader>();
            var db = loader != null ? loader.partDatabase : null;
            if (db == null) return;
            _cards = loader.partCardFactory;
            Inject(db);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Part injection failed: " + e);
            _nextLoaderSearch = UnityEngine.Time.unscaledTime + 30f;
        }
    }

    private static void Inject(PartDefinitionDatabase db)
    {
        if (db.partDefinitions == null) return;
        if (!db.partDefinitionLookup.ContainsKey(GunnerSightGuid))
        {
            Database = db;
            Plugin.Log.LogWarning("Gunner Sight definition not found, thermal sight not added");
            return;
        }

        Database = db;
        _sourceJson = ReadGunnerSightJson();
        for (var gen = 1; gen <= 4; gen++)
            EnsureDefinition(new SightConfig(gen, DefaultOptical, DefaultDigital));
        for (var gen = 1; gen <= NightVisionProfile.All.Length; gen++)
            NightVisionParts.EnsureDefinition(gen);
        Plugin.Log.LogInfo($"Thermal and night vision sight parts added, database now has {db.partDefinitions.Length} parts");
    }

    
    public static string EnsureDefinition(SightConfig c)
    {
        var guid = Encode(c);
        if (Database == null) EnsureInjected(force: true);
        
        
        var inPalette = guid == Guids[0];
        var tags = inPalette
            ? new[] { "gunner", "scope", ModernDayTags.Or(ModernDayTags.Thermal), "thermal" }
            : new[] { "gunner", "scope", "thermal" };
        if (AddDefinition(guid, Names[c.Gen - 1], tags) && Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Thermal sight definition added: Gen {c.Gen}, optical x{c.Optical}, digital x{c.Digital}");
        return guid;
    }

    
    internal static bool AddDefinition(string guid, string name, string[] tags)
    {
        var db = Database;
        if (db == null || db.partDefinitionLookup.ContainsKey(guid)) return false;
        var def = CreateDefinition(_sourceJson ?? ReadGunnerSightJson(), guid, name, tags);

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
            var cache = _cards?.cardCache;
            if (cache != null && !cache.ContainsKey(guid)) cache[guid] = PartDefinitionCardFactory.CreateCard(def);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Card registration failed: " + e.Message); }
        return true;
    }

    
    private static string ReadGunnerSightJson()
    {
        var path = System.IO.Path.Combine(Application.streamingAssetsPath, "Parts", "gunnerSightPart.json");
        if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
        Plugin.Log.LogWarning("gunnerSightPart.json not found, using built-in copy");
        return FallbackGunnerSightJson;
    }

    private static PartDefinition CreateDefinition(string json, string guid, string name, string[] tags)
    {
        json = Regex.Replace(json, "\"guid\"\\s*:\\s*\"[^\"]*\"", $"\"guid\": \"{guid}\"");
        json = Regex.Replace(json, "\"name\"\\s*:\\s*\"[^\"]*\"", $"\"name\": \"{name}\"");
        json = Regex.Replace(json, "\"tags\"\\s*:\\s*\\[[^\\]]*\\]",
            "\"tags\": [" + string.Join(", ", Array.ConvertAll(tags, t => "\"" + t + "\"")) + "]");

        var obj = SprocketIO.DeserializeJSON(json, Il2CppType.Of<PartDefinition>());
        var def = obj?.TryCast<PartDefinition>();
        if (def == null) throw new InvalidOperationException("DeserializeJSON returned null for " + name);
        return def;
    }

    private const string FallbackGunnerSightJson = @"{
  ""v"": ""0.0"",
  ""guid"": ""f90f373f-4edb-4563-a162-6eee6a5d77dd"",
  ""name"": ""gunnerSight"",
  ""tags"": [ ""gunner"", ""scope"", ""cannon"" ],
  ""components"": [
    { ""fileID"": ""sight"", ""type"": ""sight"", ""info"": { ""v"": ""0.0"", ""objectiveLensPosition"": [ 0.0, 0.0, 0.0409 ], ""eyepiecePosition"": [ 0.0, 0.0, -0.351768 ], ""modelFileID"": ""model"" } },
    { ""fileID"": ""model"", ""type"": ""model"", ""info"": { ""v"": ""0.2"", ""meshGuid"": ""60f759b164460a140bf65902dd96dd5c"", ""materialGuids"": [ ""91dfa45c1494952458353d309cfa9aa9"" ], ""scaling"": true, ""flipping"": true, ""selectable"": true, ""placementSurface"": false, ""damageable"": false } }
  ],
  ""transform"": { ""mirroring"": true, ""duplication"": true, ""pickup"": true, ""snapToCentre"": true, ""autoRotate"": true, ""airPlacement"": true, ""translation"": true, ""rotation"": true, ""scaling"": true, ""dynamic"": false, ""placementMode"": ""forwards"",
    ""scale"": [ 1.0, 1.0, 1.0 ], ""maxScale"": [ 3.0, 3.0, 3.0 ], ""minScale"": [ 0.25, 0.25, 0.25 ], ""objects"": [] }
}";

    
    public static void Apply(VehicleObject target, SightConfig c)
    {
        target.guid = EnsureDefinition(c);
        target.gameObject.name = Names[c.Gen - 1];
    }

    

    [HarmonyPatch(typeof(PartLocalizationIO), nameof(PartLocalizationIO.GetPartLocalization))]
    private static class LocalizationPatch
    {
        private static bool Prefix(string localizationID, ref PartLocalization __result)
        {
            var night = NightVisionParts.GenFromName(localizationID) != 0;
            if (!night && GenFromName(localizationID) == 0) return true;
            try
            {
                var loc = new PartLocalization();
                loc.name = night ? NightVisionParts.DisplayName : DisplayName;
                loc.description = night ? NightVisionParts.Description : Description;
                __result = loc;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Localization patch failed: " + e);
                return true;
            }
        }
    }

    

    private static Sprite _icon, _nightIcon;
    private static bool _iconFailed;

    [HarmonyPatch(typeof(PartDefinitionCardFactory), nameof(PartDefinitionCardFactory.CreateCard))]
    private static class CardPatch
    {
        private static bool _inside;

        private static void Postfix(PartDefinition definition, PartDisplayCard __result)
        {
            if (_inside || __result == null || definition == null) return;
            var night = NightVisionParts.GenFromGuid(definition.guid) != 0;
            if (!night && GenFromGuid(definition.guid) == 0) return;
            
            
            __result.Name = night ? NightVisionParts.DisplayName : DisplayName;
            __result.Description = night ? NightVisionParts.Description : Description;
            try
            {
                if (_icon == null && !_iconFailed && Database != null)
                {
                    _inside = true;
                    var gunnerCard = PartDefinitionCardFactory.CreateCard(Database.Get(GunnerSightGuid));
                    _inside = false;
                    _icon = IconMaker.MakeThermalIcon(gunnerCard?.Icon);
                    _nightIcon = IconMaker.MakeNightVisionIcon(gunnerCard?.Icon);
                    if (_icon == null) _iconFailed = true;
                }
                var icon = night ? _nightIcon : _icon;
                if (icon != null) __result.Icon = icon;
            }
            catch (Exception e)
            {
                _inside = false;
                _iconFailed = true;
                Plugin.Log.LogError("Icon patch failed: " + e);
            }
        }
    }
}
