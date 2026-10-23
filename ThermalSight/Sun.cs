using UnityEngine;

namespace ThermalSight;


internal static class Sun
{
    private static Light _light;
    private static float _nextSearch;
    private static Vector3 _last = new(0.3f, 0.8f, 0.2f);

    
    public static Vector3 Direction
    {
        get
        {
            var l = Find();
            if (l != null)
            {
                try { _last = -l.transform.forward; }
                catch { _light = null; }
            }
            return _last;
        }
    }

    private static Light Find()
    {
        if (_light != null && !_light.WasCollected && Time.unscaledTime < _nextSearch) return _light;
        _nextSearch = Time.unscaledTime + 3f;
        _light = null;
        try
        {
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l != null && l.type == LightType.Directional && l.isActiveAndEnabled) { _light = l; break; }
        }
        catch { }
        return _light;
    }
}
