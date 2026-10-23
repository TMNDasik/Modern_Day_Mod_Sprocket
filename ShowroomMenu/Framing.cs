using UnityEngine;

namespace ShowroomMenu;





internal static class Framing
{
    
    private const float FocusX = 0.32f, FocusY = 0.46f;
    
    private const float PMinX = 0.02f, PMaxX = 0.62f, PMinY = -0.03f, PMaxY = 0.9f;
    
    private const float VMinX = 0.02f, VMaxX = 0.62f, VMinY = 0.08f, VMaxY = 0.88f;
    private const int Samples = 16;

    public static void Compute(Camera cam, Showcase show, out Vector3 pos, out Quaternion rot)
    {
        var tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var tanX = tanY * (cam.aspect > 0.1f ? cam.aspect : 16f / 9f);
        var pitch = Plugin.CameraTilt.Value;
        var view = Plugin.ViewAngle.Value;

        
        
        var dirC = new Vector3((2f * FocusX - 1f) * tanX, (2f * FocusY - 1f) * tanY, 1f).normalized;
        var tilted = Quaternion.Euler(pitch, 0f, 0f) * dirC;
        var angle0 = Mathf.Atan2(tilted.x, tilted.z) * Mathf.Rad2Deg;
        rot = Quaternion.Euler(pitch, 180f - view - angle0, 0f);
        var ray = rot * dirC;
        var focus = new Vector3(0f, show.MidY, 0f);
        var inv = Quaternion.Inverse(rot);

        var baseR = show.BaseRadius;
        var bodyR = Mathf.Max(show.FootprintRadius, 0.5f);
        var top = show.TopY;
        var distance = 3f;
        for (var step = 0; step < 160; step++, distance *= 1.02f)
            if (Fits(focus - ray * distance, inv, tanX, tanY, baseR, bodyR, top)) break;
        distance *= Mathf.Clamp(Plugin.CameraDistance.Value, 40f, 250f) / 100f;
        pos = focus - ray * distance;
    }

    private static bool Fits(Vector3 camPos, Quaternion inv, float tanX, float tanY, float baseR, float bodyR, float top)
    {
        for (var i = 0; i < Samples; i++)
        {
            var a = Mathf.PI * 2f * i / Samples;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (!Inside(dir * baseR, camPos, inv, tanX, tanY, PMinX, PMaxX, PMinY, PMaxY)) return false;
            if (!Inside(dir * baseR + Vector3.up * Podium.BaseTop, camPos, inv, tanX, tanY, PMinX, PMaxX, PMinY, PMaxY)) return false;
            if (!Inside(dir * bodyR + Vector3.up * Podium.DeckTop, camPos, inv, tanX, tanY, VMinX, VMaxX, VMinY, VMaxY)) return false;
            if (!Inside(dir * bodyR + Vector3.up * top, camPos, inv, tanX, tanY, VMinX, VMaxX, VMinY, VMaxY)) return false;
        }
        return true;
    }

    private static bool Inside(Vector3 world, Vector3 camPos, Quaternion inv, float tanX, float tanY,
        float minX, float maxX, float minY, float maxY)
    {
        var p = inv * (world - camPos);
        if (p.z < 0.5f) return false;
        var vx = 0.5f + 0.5f * p.x / (p.z * tanX);
        var vy = 0.5f + 0.5f * p.y / (p.z * tanY);
        return vx >= minX && vx <= maxX && vy >= minY && vy <= maxY;
    }
}
