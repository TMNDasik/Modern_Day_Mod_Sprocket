using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace ThermalSight;


internal static class GpuTest
{
    private static AssetBundle _bundle;
    private static GameObject _go;

    public static AssetBundle Bundle
    {
        get
        {
            if (_bundle == null)
            {
                GpuResources.TryLoad();
                _bundle = GpuResources.Bundle;
                Plugin.Log.LogInfo($"[gpu] bundle loaded: {_bundle != null}");
                if (_bundle != null)
                {
                    try { foreach (var n in _bundle.GetAllAssetNames()) Plugin.Log.LogInfo("[gpu]   asset: " + n); }
                    catch (Exception e) { Plugin.Log.LogInfo("[gpu]   GetAllAssetNames unavailable: " + e.GetType().Name); }
                }
            }
            return _bundle;
        }
    }

    public static void Toggle(Camera cam)
    {
        try
        {
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
                Plugin.Log.LogInfo("[gpu] test pass off");
                return;
            }

            var bundle = Bundle;
            if (bundle == null) return;
            var shader = BundleLoader.LoadAsset<Shader>(bundle, "assets/thermal/thermaltest.shader");
            Plugin.Log.LogInfo($"[gpu] shader: {(shader == null ? "null" : shader.name)} supported={(shader != null && shader.isSupported)}");
            if (shader == null) return;

            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            _go = new GameObject("ThermalGpuTest");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            var hd = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
            if (hd != null)
            {
                var mask = hd.volumeLayerMask.value;
                for (var i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { _go.layer = i; break; }
            }
            var volume = _go.AddComponent<CustomPassVolume>();
            volume.isGlobal = true;
            volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            var pass = new FullScreenCustomPass
            {
                fullscreenPassMaterial = mat,
                materialPassName = "Custom Pass 0",
                fetchColorBuffer = true,
                name = "ThermalTest",
                enabled = true
            };
            volume.customPasses.Add(pass);
            Plugin.Log.LogInfo("[gpu] test pass on (inverted colours + distance at the bottom of the screen)");
        }
        catch (Exception e) { Plugin.Log.LogError("[gpu] test failed: " + e); }
    }
}
