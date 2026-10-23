using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace Powertrain;



internal static class VanillaHud
{
    private static float _nextScan = -100f, _top;

    public static float BottomRightTop()
    {
        var now = Time.unscaledTime;
        if (now < _nextScan) return _top;
        _nextScan = now + 1f;
        try { _top = Scan(); }
        catch { }
        return _top;
    }

    private static float Scan()
    {
        float w = Screen.width, h = Screen.height;
        if (w < 1f || h < 1f) return 0f;
        var top = 0f;
        var corners = new Il2CppStructArray<Vector3>(4);
        foreach (var g in UnityEngine.Object.FindObjectsOfType<Graphic>())
        {
            try
            {
                if (g == null || !g.enabled || !g.gameObject.activeInHierarchy || g.color.a < 0.05f) continue;
                var canvas = g.canvas;
                if (canvas == null) continue;
                var root = canvas.rootCanvas;
                if (root == null || IsOurs(root.gameObject.name)) continue;
                var tmp = g.TryCast<TMPro.TMP_Text>();
                if (tmp != null && string.IsNullOrWhiteSpace(tmp.text)) continue;

                g.rectTransform.GetWorldCorners(corners);
                var cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var p = cam == null ? (Vector2)corners[i] : RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    minX = Math.Min(minX, p.x);
                    maxX = Math.Max(maxX, p.x);
                    minY = Math.Min(minY, p.y);
                    maxY = Math.Max(maxY, p.y);
                }
                if (maxX - minX < 1f || maxY - minY < 1f) continue;
                if (maxX - minX > w * 0.4f || maxY - minY > h * 0.4f) continue; 
                if (minX < w * 0.7f || minY > h * 0.3f || maxX > w + 2f) continue; 
                top = Math.Max(top, maxY);
            }
            catch { }
        }
        return top * 1080f / h;
    }

    
    private static bool IsOurs(string name) =>
        name != null && (name.Contains("Munitions") || name.Contains("Powertrain") || name.Contains("GunStabilizer") || name.Contains("Thermal") || name.Contains("MDHydropneumatic"));
}
