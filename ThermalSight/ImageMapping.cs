using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace ThermalSight;






internal static class ImageMapping
{
    private static Il2CppStructArray<Vector3> _corners;

    public static bool CameraToScreen(RawImage image, Vector2 vp, out Vector2 screenVp)
    {
        screenVp = vp;
        if (image == null || !image.isActiveAndEnabled) return false;
        var sw = Screen.width;
        var sh = Screen.height;
        if (sw <= 0 || sh <= 0) return false;
        var uv = image.uvRect;
        if (uv.width <= 1e-6f || uv.height <= 1e-6f) return false;

        
        
        _corners ??= new Il2CppStructArray<Vector3>(4);
        image.rectTransform.GetWorldCorners(_corners);
        var min = _corners[0];
        var max = _corners[2];
        if (max.x - min.x < 1f || max.y - min.y < 1f) return false;

        var u = (vp.x - uv.x) / uv.width;
        var v = (vp.y - uv.y) / uv.height;
        screenVp = new Vector2(Mathf.LerpUnclamped(min.x, max.x, u) / sw, Mathf.LerpUnclamped(min.y, max.y, v) / sh);
        return true;
    }

    
    public static bool ScreenRect(RawImage image, out Rect rect)
    {
        rect = default;
        if (image == null || !image.isActiveAndEnabled) return false;
        _corners ??= new Il2CppStructArray<Vector3>(4);
        image.rectTransform.GetWorldCorners(_corners);
        var min = _corners[0];
        var max = _corners[2];
        if (max.x - min.x < 1f || max.y - min.y < 1f) return false;
        rect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        return true;
    }
}
