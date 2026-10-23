using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Sprocket.Localization;
using Sprocket.UI;
using Sprocket.VehicleDesigner.Access;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.Cannons.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Munitions;




internal static class MuzzleDevices
{
    private const string OldObjectName = "Munitions muzzle device";
    private static readonly Regex OldToken = new(@"\s?\[muz:\d+\]", RegexOptions.Compiled);

    private sealed class Option
    {
        public string Guid, Name;
    }

    private static List<Option> _options;
    private static Il2CppSystem.Collections.Generic.List<string> _names;

    
    private static List<Option> Options()
    {
        if (_options != null) return _options;
        var db = AutoWeaponPart.Database;
        if (db == null) return null;
        var list = new List<Option>();
        try
        {
            if (db.partDefinitionTagLookup.ContainsKey("cannonMuzzle"))
                foreach (var def in db.partDefinitionTagLookup["cannonMuzzle"])
                {
                    if (def == null || def.guid == LauncherPart.NoMuzzleGuid) continue;
                    list.Add(new Option { Guid = def.guid, Name = DisplayName(def.name) });
                }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Muzzle brake list failed: " + e.Message); }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, new Option { Guid = LauncherPart.NoMuzzleGuid, Name = "None" });
        _options = list;
        _names = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var o in list) _names.Add(o.Name);
        return list;
    }

    private static string DisplayName(string id)
    {
        try
        {
            var loc = PartLocalizationIO.GetPartLocalization(id);
            if (!string.IsNullOrWhiteSpace(loc?.name)) return loc.name;
        }
        catch { }
        
        var s = Regex.Replace(id ?? "?", "([a-z0-9])([A-Z])", "$1 $2");
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
    }

    

    public static void Draw(CannonEditor editor, Cannon cannon, IGUIElementDrawer drawer)
    {
        var bp = cannon.Blueprint;
        var options = Options();
        if (bp == null || options == null) return;
        var current = bp.MuzzlePartGuid;
        var selected = 0;
        for (var i = 0; i < options.Count; i++)
            if (options[i].Guid == current) { selected = i; break; }
        UnityEngine.Events.UnityAction<int> onPick = (Action<int>)(i =>
        {
            try
            {
                if (i >= 0 && i < options.Count) Set(editor, cannon, options[i].Guid);
            }
            catch (Exception e) { Plugin.Log.LogError("Muzzle brake change failed: " + e); }
        });
        drawer.Dropdown("Muzzle brake", _names.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected, onPick,
            "One of the game's muzzle brakes (the same ones as on a normal gun), sized to this weapon's caliber. " +
            "A muzzle brake lowers the recoil.");
    }

    private static void Set(CannonEditor editor, Cannon cannon, string guid)
    {
        var bp = cannon.Blueprint;
        if (bp == null || (bp.MuzzlePartGuid == guid && !OldToken.IsMatch(bp.Description ?? ""))) return;
        Il2CppSystem.IDisposable frame = null;
        try { frame = editor.NewBlueprintEditFrame("Muzzle brake", bp, null, editor.GetNewOperationGroupID(), VehicleOpExecutionMode.Synchronous); }
        catch { }
        try
        {
            if (OldToken.IsMatch(bp.Description ?? "")) bp.Description = OldToken.Replace(bp.Description, "").Trim();
            bp.MuzzlePartGuid = guid;
            bp.physicalPropertiesDirty = true;
        }
        finally { if (frame != null) frame.Dispose(); else bp.MarkModified(); }
        try { editor.RequestRedraw(); } catch { }
    }

    
    

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class BuildPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                if (!AutoWeaponPart.IsAuto(__instance)) return;
                var model = __instance.GetComponentInChildren<CannonBarrelModel>(true);
                if (model == null) return;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == OldObjectName) Object.Destroy(t.gameObject);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Muzzle cleanup failed: " + e.Message); }
        }
    }
}
