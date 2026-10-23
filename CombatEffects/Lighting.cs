using UnityEngine;

namespace CombatEffects;




internal static class Lighting
{
    private static Light _sun;
    private static float _nextSunSearch;
    private static Camera _camera;
    private static float _nextCameraSearch;

    public static Camera Camera
    {
        get
        {
            if (_camera != null && !_camera.WasCollected && _camera.isActiveAndEnabled) return _camera;
            if (Time.unscaledTime < _nextCameraSearch) return null;
            _nextCameraSearch = Time.unscaledTime + 0.5f;
            _camera = Camera.main;
            return _camera;
        }
    }

    public static void Update()
    {
        if (Time.unscaledTime >= _nextSunSearch)
        {
            _nextSunSearch = Time.unscaledTime + 5f;
            _sun = null;
            var best = 0f;
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l == null || l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                if (l.intensity > best) { best = l.intensity; _sun = l; }
            }
        }
        var toSun = new Vector3(0.3f, 0.8f, 0.25f).normalized;
        var color = new Color(1f, 0.96f, 0.9f);
        var day = 1f;
        if (_sun != null && !_sun.WasCollected)
        {
            toSun = -_sun.transform.forward;
            day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.3f, toSun.y));
            var c = _sun.color;
            try { if (_sun.useColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(_sun.colorTemperature); } catch { }
            var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max > 1e-3f) color = new Color(c.r / max, c.g / max, c.b / max);
            color = Color.Lerp(new Color(1f, 0.72f, 0.5f), color, Mathf.InverseLerp(0.05f, 0.45f, toSun.y));
        }
        var sun = Mathf.Lerp(0.06f, 1.05f, day);
        Shader.SetGlobalVector("_CfxSunDir", new Vector4(toSun.x, toSun.y, toSun.z, 1f));
        Shader.SetGlobalVector("_CfxSunColor", new Vector4(color.r * sun, color.g * sun, color.b * sun, 1f));
        var amb = Mathf.Lerp(0.07f, 1f, day);
        Shader.SetGlobalVector("_CfxSky", new Vector4(0.46f * amb, 0.5f * amb, 0.57f * amb, 1f));
        Shader.SetGlobalVector("_CfxGround", new Vector4(0.3f * amb, 0.28f * amb, 0.25f * amb, 1f));
        Shader.SetGlobalFloat("_CfxFog", Mathf.Clamp01(Plugin.Fog.Value));
        Shader.SetGlobalFloat("_CfxDistortion", Mathf.Max(0f, Plugin.HeatHaze.Value));
    }
}
