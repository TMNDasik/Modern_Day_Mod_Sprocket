using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;

namespace Munitions;









internal static class DynamicSightCompat
{
    private const string AssemblyName = "SprocketDynamicSight";

    private static bool _found, _failed;
    private static float _nextSearch;
    private static int _errors;

    private static FieldInfo _behaviour, _ammoCounts, _ammoOrder, _requested;
    private static FieldInfo _infoType, _infoPredicted;
    private static FieldInfo _primary, _secondary, _trace, _hasPipper, _hasTof, _ladderType, _ladderMarks;

    public static void Tick()
    {
        if (_found || _failed || Time.unscaledTime < _nextSearch) return;
        _nextSearch = Time.unscaledTime + 3f;
        Assembly asm = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == AssemblyName) { asm = a; break; }
        if (asm == null) return;
        _found = true;
        try { Patch(asm); }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("Dynamic Gun Sight compatibility unavailable: " + e.Message);
        }
    }

    private static void Patch(Assembly asm)
    {
        var reader = asm.GetType("SprocketDynamicSight.Game.GameReader");
        var catalog = asm.GetType("SprocketDynamicSight.Game.AmmoCatalog");
        var round = asm.GetType("SprocketDynamicSight.Game.RoundInfo");
        var frame = asm.GetType("SprocketDynamicSight.Core.SightFrame");
        var ladder = asm.GetType("SprocketDynamicSight.Core.Ladder");
        var harmony = new Harmony(Plugin.Guid + ".dynamicsight");
        var done = new List<string>();

        if (reader != null)
        {
            _behaviour = AccessTools.Field(reader, "behaviour");
            _ammoCounts = AccessTools.Field(reader, "ammoCounts");
            _ammoOrder = AccessTools.Field(reader, "ammoOrder");
            _requested = AccessTools.Field(reader, "requestedType");
            if (_behaviour != null && _ammoCounts != null && _ammoOrder != null && _requested != null &&
                Hook(harmony, AccessTools.Method(reader, "ScanAmmo"), null, nameof(ScanAmmoPostfix))) done.Add("ammo");
        }
        if (catalog != null && round != null)
        {
            _infoType = AccessTools.Field(round, "TypeId");
            _infoPredicted = AccessTools.Field(round, "PredictedVelocity");
            if (_infoType != null && _infoPredicted != null &&
                Hook(harmony, AccessTools.Method(catalog, "TryPredictVelocity"), null, nameof(PredictPostfix))) done.Add("predicted speed");
            if (Hook(harmony, AccessTools.Method(catalog, "RecordMeasurement"), nameof(MeasurePrefix), null)) done.Add("measured speed");
        }
        if (reader != null && frame != null && ladder != null)
        {
            _primary = AccessTools.Field(frame, "Primary");
            _secondary = AccessTools.Field(frame, "Secondary");
            _trace = AccessTools.Field(frame, "Trace");
            _hasPipper = AccessTools.Field(frame, "HasPipper");
            _hasTof = AccessTools.Field(frame, "HasTof");
            _ladderType = AccessTools.Field(ladder, "TypeId");
            _ladderMarks = AccessTools.Field(ladder, "Marks");
            if (_primary != null && _secondary != null && _ladderType != null && _ladderMarks != null &&
                Hook(harmony, AccessTools.Method(reader, "Fill"), null, nameof(FillPostfix))) done.Add("missile scales");
        }
        Plugin.Log.LogInfo($"Dynamic Gun Sight {asm.GetName().Version} found: Munitions feeds its " +
                           (done.Count > 0 ? string.Join(", ", done) : "nothing (its internals changed)"));
    }

    private static bool Hook(Harmony harmony, MethodInfo target, string prefix, string postfix)
    {
        if (target == null) return false;
        try
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(DynamicSightCompat).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(DynamicSightCompat).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Dynamic Gun Sight: could not hook {target.Name}: {e.Message}");
            return false;
        }
    }

    private static bool Ours(int typeId, out SizeTypes types, out int kind)
    {
        types = null;
        kind = -1;
        return typeId > 0 && TypeRegistry.TryGetKind(typeId, out types, out kind);
    }

    private static bool IsMissileType(object typeId) =>
        typeId is int id && Ours(id, out _, out var kind) && Kind.IsMissile(kind);

    
    private static void ScanAmmoPostfix(object __instance)
    {
        try
        {
            var view = Controller.LastView;
            if (!view.Valid || view.Launcher || view.AutoVanilla || view.Counts == null || view.Types == null) return;
            if (_behaviour.GetValue(__instance) is not Il2CppObjectBase beh || beh.Pointer != view.Weapon) return;
            if (_ammoCounts.GetValue(__instance) is not Dictionary<int, int> counts || _ammoOrder.GetValue(__instance) is not List<int> order) return;
            counts.Clear();
            order.Clear();
            foreach (var k in Kind.Order)
            {
                if (k >= view.Counts.Length) continue;
                var id = view.Types.TypeIds[k];
                if (id == 0 || (view.Counts[k] == 0 && k != Controller.Selected)) continue;
                counts[id] = view.Counts[k];
                order.Add(id);
            }
            var selected = Controller.Selected >= 0 && Controller.Selected < Kind.Count ? view.Types.TypeIds[Controller.Selected] : 0;
            _requested.SetValue(__instance, selected != 0 ? selected : -1);
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Dynamic Gun Sight ammo feed failed: " + e.Message); }
    }

    
    private static void PredictPostfix(object __0, object __1)
    {
        try
        {
            if (__0 == null || _infoType.GetValue(__0) is not int id || !Ours(id, out _, out var kind)) return;
            var cannon = (__1 as Il2CppObjectBase) is { } b ? new CannonBehaviour(b.Pointer) : null;
            var bp = cannon != null ? ApfsdsSettings.BlueprintOf(cannon.transform) : null;
            float v;
            if (Kind.IsMissile(kind)) v = 0f;
            else if (kind == Kind.Apfsds && bp != null)
            {
                ApfsdsSettings.Read(bp, out _, out var speedSet, out _);
                v = ApfsdsSettings.EffectiveSpeed(bp, speedSet);
            }
            else if (kind == Kind.Heat && bp != null)
            {
                HeatSettings.Read(bp, out _, out var speedSet);
                v = HeatSettings.EffectiveSpeed(bp, speedSet);
            }
            else if (Kind.IsRocket(kind))
            {
                RocketSettings.Read(bp, out _, out var speed);
                v = speed;
            }
            else return;
            _infoPredicted.SetValue(__0, v);
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Dynamic Gun Sight speed prediction failed: " + e.Message); }
    }

    
    
    private static void MeasurePrefix(int __0, ref float __1)
    {
        try
        {
            if (!Ours(__0, out _, out var kind)) return;
            if (Kind.IsMissile(kind) || Kind.IsRocket(kind)) { __1 = float.NaN; return; }
            if (kind != Kind.Apfsds && kind != Kind.Heat) return;
            var weapon = PlayerTracker.Cannon != null ? PlayerTracker.Cannon.Behaviour : null;
            if (weapon != null) __1 = Api.LaunchSpeed(weapon.Pointer, __0, __1);
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Dynamic Gun Sight speed measurement failed: " + e.Message); }
    }

    
    private static void FillPostfix(object __0)
    {
        try
        {
            if (__0 == null) return;
            var primary = _primary.GetValue(__0);
            if (primary != null && IsMissileType(_ladderType.GetValue(primary)))
            {
                (_ladderMarks.GetValue(primary) as IList)?.Clear();
                (_trace?.GetValue(__0) as IList)?.Clear();
                _hasPipper?.SetValue(__0, false);
                _hasTof?.SetValue(__0, false);
            }
            if (_secondary.GetValue(__0) is IList secondary)
                for (var i = secondary.Count - 1; i >= 0; i--)
                    if (secondary[i] != null && IsMissileType(_ladderType.GetValue(secondary[i]))) secondary.RemoveAt(i);
        }
        catch (Exception e) { if (_errors++ < 3) Plugin.Log.LogWarning("Dynamic Gun Sight missile scale fix failed: " + e.Message); }
    }
}
