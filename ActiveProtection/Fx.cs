using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ActiveProtection;



internal static class Fx
{
    private sealed class Flash
    {
        public GameObject Go;
        public MeshRenderer R;
        public float Start, Life, Size0, Size1;
        public Color Colour;
    }

    private static readonly List<Flash> Flashes = new();
    private static readonly MaterialPropertyBlock Mpb = new();
    private static readonly int TintId = Shader.PropertyToID("_Tint");
    private static Mesh _quad;

    public static void Shot(Module launcher, Vector3 point, Vector3 threatDir, bool kinetic, bool success)
    {
        try
        {
            var muzzle = launcher.Muzzle;
            if (!MunitionsBridge.Effect(muzzle, point, threatDir, kinetic, success))
            {
                AddFlash(muzzle, 0.6f, 1.4f, 0.08f, new Color(1f, 0.85f, 0.6f, 1f));
                AddFlash(point, 2.5f, 6f, 0.14f, new Color(1f, 0.72f, 0.35f, 1f));
            }
            if (!SoundBridge.Play("gl.launch", muzzle, launcher.T, 0.8f, 1.05f + 0.1f * UnityEngine.Random.value))
                Assets.Play(Assets.LaunchClip, muzzle, 0.8f, 10f);
            var pitch = 0.95f + 0.1f * UnityEngine.Random.value;
            if (!SoundBridge.Play("aps.burst", point, null, 1f, pitch))
                Assets.Play(Assets.BurstClip, point, 1f, 30f, pitch);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("APS shot effect failed: " + e.Message);
        }
    }

    
    public static void Era(Vector3 point, Vector3 normal, float tileSize)
    {
        try
        {
            if (!MunitionsBridge.EraEffect(point, normal, tileSize))
            {
                AddFlash(point + normal * 0.15f, 0.8f, 2.6f, 0.08f, new Color(1f, 0.92f, 0.75f, 1f));
                AddFlash(point + normal * 0.4f, 0.6f, 1.8f, 0.14f, new Color(1f, 0.6f, 0.3f, 1f));
            }
            var pitch = 0.95f + 0.1f * UnityEngine.Random.value;
            if (!SoundBridge.Play("era.burst", point, null, 1f, pitch))
                Assets.Play(Assets.BurstClip, point, 0.9f, 25f, pitch * 0.85f);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("ERA effect failed: " + e.Message);
        }
    }

    
    
    public static void Vent(Module panel, float intensity, float duration)
    {
        try
        {
            var size = panel.Size;
            var local = new Vector3(0f, 0f, size.z);
            var center = panel.T.TransformPoint(local);
            var up = panel.Rotation * Vector3.forward;
            if (!MunitionsBridge.VentFire(panel.T, local, Vector3.forward, Mathf.Max(size.x, size.y), intensity, duration))
            {
                AddFlash(center + up * 1.5f, 2f, 6f, 0.25f, new Color(1f, 0.85f, 0.55f, 1f));
                AddFlash(center + up * 4f, 3f, 9f, 0.6f, new Color(1f, 0.55f, 0.2f, 1f));
            }
            var pitch = 0.9f + 0.15f * UnityEngine.Random.value;
            if (!SoundBridge.Play("vent.blast", center, null, 1f, pitch))
                Assets.Play(Assets.BurstClip, center, 1f, 40f, pitch * 0.7f);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Blow-off vent effect failed: " + e.Message);
        }
    }

    public static void ModuleDestroyed(Module m)
    {
        try { AddFlash(m.Center, 0.4f, 1.2f, 0.08f, new Color(1f, 0.8f, 0.5f, 1f)); }
        catch { }
    }

    private static void AddFlash(Vector3 pos, float size0, float size1, float life, Color colour)
    {
        var mat = Assets.FlashMat;
        if (mat == null) return;
        var go = new GameObject("APS flash");
        go.transform.position = pos;
        go.AddComponent<MeshFilter>().sharedMesh = Quad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        Flashes.Add(new Flash { Go = go, R = r, Start = Time.time, Life = life, Size0 = size0, Size1 = size1, Colour = colour });
    }

    
    public static void Update(Camera cam)
    {
        if (Flashes.Count == 0) return;
        var now = Time.time;
        for (var i = Flashes.Count - 1; i >= 0; i--)
        {
            var f = Flashes[i];
            var t = (now - f.Start) / f.Life;
            if (t >= 1f || f.Go == null || f.Go.WasCollected)
            {
                try { if (f.Go != null && !f.Go.WasCollected) Object.Destroy(f.Go); } catch { }
                Flashes.RemoveAt(i);
                continue;
            }
            var tr = f.Go.transform;
            if (cam != null) tr.rotation = Quaternion.LookRotation(tr.position - cam.transform.position, cam.transform.up);
            var size = Mathf.Lerp(f.Size0, f.Size1, 1f - (1f - t) * (1f - t));
            tr.localScale = new Vector3(size, size, size);
            var c = f.Colour;
            c.a *= (1f - t) * (1f - t);
            Mpb.Clear();
            Mpb.SetVector(TintId, new Vector4(c.r, c.g, c.b, c.a));
            f.R.SetPropertyBlock(Mpb);
        }
    }

    private static Mesh Quad()
    {
        if (_quad != null && !_quad.WasCollected) return _quad;
        _quad = new Mesh { name = "APS flash quad" };
        _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
        _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _quad.RecalculateBounds();
        _quad.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return _quad;
    }
}
