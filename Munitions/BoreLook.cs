using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.Vehicles.Cannons;
using UnityEngine;

namespace Munitions;






internal static class BoreLook
{
    private const string VanillaName = "RifledCannonBore";
    private static Material _vanilla, _smooth;
    private static readonly Dictionary<IntPtr, Cannon> Cannons = new();
    private static readonly List<IntPtr> Dead = new();
    private static float _next;
    private static bool _warned;

    [HarmonyPatch(typeof(Cannon), nameof(Cannon.Build))]
    private static class BuildPatch
    {
        private static void Postfix(Cannon __instance)
        {
            try
            {
                if (Cannons.Count > 1024) Cannons.Clear();
                Cannons[__instance.Pointer] = __instance;
                Apply(__instance);
            }
            catch (Exception e) { if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Bore look failed: " + e.Message); }
        }
    }

    public static void Tick()
    {
        if (Cannons.Count == 0 || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        Dead.Clear();
        foreach (var kv in Cannons)
        {
            var c = kv.Value;
            if (c == null || c.WasCollected) { Dead.Add(kv.Key); continue; }
            try { Apply(c); }
            catch { Dead.Add(kv.Key); }
        }
        foreach (var k in Dead) Cannons.Remove(k);
    }

    private static void Apply(Cannon cannon)
    {
        var model = cannon.Barrel?.TryCast<CannonBarrelModel>();
        var bore = model != null ? model.boreModel : null;
        if (bore == null || bore.WasCollected) return;
        
        
        var launcher = LauncherPart.IsLauncher(cannon);
        var smooth = launcher || Bore.Get(cannon.Blueprint) == Bore.Smooth;
        if (launcher) RearCap.Ensure(cannon, bore.transform);
        foreach (var r in bore.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            var mat = r.sharedMaterial;
            if (mat == null) continue;
            if (smooth)
            {
                
                if (!IsVanilla(mat)) continue;
                var s = Smooth(mat);
                if (s != null) r.sharedMaterial = s;
            }
            else if (_smooth != null && mat.Pointer == _smooth.Pointer && _vanilla != null && !_vanilla.WasCollected)
                r.sharedMaterial = _vanilla;
        }
    }

    private static bool IsVanilla(Material m)
    {
        if (_vanilla != null && !_vanilla.WasCollected && m.Pointer == _vanilla.Pointer) return true;
        var name = m.name;
        if (name == null || !name.StartsWith(VanillaName, StringComparison.Ordinal)) return false;
        _vanilla = m;
        return true;
    }

    private static Material Smooth(Material vanilla)
    {
        if (_smooth != null && !_smooth.WasCollected) return _smooth;
        var baseMap = Fx.BoreBase;
        var mask = Fx.BoreMask;
        var normal = Fx.BoreNormal;
        if (baseMap == null || mask == null || normal == null)
        {
            if (!_warned) Plugin.Log.LogWarning("Smooth bore textures missing in munitions.bundle — smoothbore guns keep the rifled bore look");
            _warned = true;
            return null;
        }
        
        _smooth = new Material(vanilla) { name = "SmoothCannonBore (Munitions)" };
        _smooth.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        _smooth.SetTexture("_BaseColorMap", baseMap);
        _smooth.SetTexture("_MainTex", baseMap);
        _smooth.SetTexture("_MaskMap", mask);
        _smooth.SetTexture("_NormalMap", normal);
        Plugin.Log.LogInfo("Smooth bore material ready");
        return _smooth;
    }
}
