using System;
using System.Reflection;
using UnityEngine;

namespace Powertrain;



internal static class ModBridge
{
    private static bool _searched;
    private static MethodInfo _exhaustSmoke, _addCloud, _smokeDrift;

    private static void Find()
    {
        if (_searched) return;
        _searched = true;
        const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = asm.GetName().Name;
                if (name.StartsWith("MD", StringComparison.Ordinal)) name = name.Substring(2);   
                if (name == "Munitions")
                {
                    var api = asm.GetType("Munitions.Api");
                    _exhaustSmoke = api?.GetMethod("ExhaustSmoke", f, null,
                        new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float) }, null);
                    _smokeDrift = api?.GetMethod("SmokeDrift", f, null, Type.EmptyTypes, null);
                }
                else if (name == "ActiveProtection")
                {
                    var api = asm.GetType("ActiveProtection.Api");
                    _addCloud = api?.GetMethod("AddSmokeCloud", f, null,
                        new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float) }, null);
                }
            }
            catch { }
        }
        Plugin.Log.LogInfo($"Engine smoke: visible smoke {(_exhaustSmoke != null ? "via Munitions" : "unavailable (no Munitions)")}, " +
                           $"AI sight blocking {(_addCloud != null ? "via Active Protection" : "unavailable (no Active Protection)")}");
    }

    public static bool CanSmoke
    {
        get { Find(); return _exhaustSmoke != null; }
    }

    public static void ExhaustSmoke(Vector3 pos, Vector3 dir, Vector3 vehicleVelocity, float dt, float intensity)
    {
        Find();
        if (_exhaustSmoke == null) return;
        try { _exhaustSmoke.Invoke(null, new object[] { pos, dir, vehicleVelocity, dt, intensity }); }
        catch { _exhaustSmoke = null; }
    }

    public static void AddCloud(Vector3 center, float radius, float halfHeight, float build, float duration)
    {
        Find();
        if (_addCloud == null) return;
        try { _addCloud.Invoke(null, new object[] { center, radius, halfHeight, build, duration }); }
        catch { _addCloud = null; }
    }
}
