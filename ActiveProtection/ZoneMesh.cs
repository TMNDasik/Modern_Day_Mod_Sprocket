using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveProtection;





internal static class ZoneMesh
{
    public static readonly Color DetectorFill = new(0.25f, 0.72f, 1f, 0.10f);
    public static readonly Color LauncherFill = new(1f, 0.55f, 0.12f, 0.10f);
    public static readonly Color FarBoundary = new(1f, 0.22f, 0.14f, 0.20f);
    public static readonly Color NearBoundary = new(1f, 0.9f, 0.2f, 0.26f);
    
    public static readonly Color WarnerFill = new(0.72f, 0.45f, 1f, 0.09f);
    public static readonly Color SmokeFill = new(0.85f, 0.85f, 0.85f, 0.16f);
    public static readonly Color JammerFill = new(1f, 0.15f, 0.1f, 0.1f);

    public static Color Fill(ModuleKind k) => k switch
    {
        ModuleKind.Detector => DetectorFill,
        ModuleKind.Launcher => LauncherFill,
        ModuleKind.Warner => WarnerFill,
        ModuleKind.Smoke => SmokeFill,
        _ => JammerFill
    };

    private const float MaxStepDeg = 6f;

    private sealed class Builder
    {
        public readonly List<Vector3> Verts = new();
        public readonly List<Color> Colors = new();
        public readonly List<Vector4> Uv = new();
        public readonly List<Vector2> Grid = new();
        public readonly List<int> Tris = new();

        public void Patch(int nu, int nv, Func<float, float, Vector3> pos, Func<float, float, Vector2> grid, Color col, bool edgeU, bool edgeV)
        {
            nu = Math.Max(1, nu);
            nv = Math.Max(1, nv);
            var start = Verts.Count;
            for (var j = 0; j <= nv; j++)
            for (var i = 0; i <= nu; i++)
            {
                var u = i / (float)nu;
                var v = j / (float)nv;
                Verts.Add(pos(u, v));
                Colors.Add(col);
                Uv.Add(new Vector4(u, v, edgeU ? 1f : 0f, edgeV ? 1f : 0f));
                Grid.Add(grid(u, v));
            }
            var row = nu + 1;
            for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
            {
                var a = start + j * row + i;
                Tris.Add(a); Tris.Add(a + row); Tris.Add(a + 1);
                Tris.Add(a + 1); Tris.Add(a + row); Tris.Add(a + row + 1);
            }
        }
    }

    public static Mesh Build(ApsSettings s, Mesh mesh)
    {
        var b = new Builder();
        var launcher = s.Kind == ModuleKind.Launcher;
        var rNear = s.Near;
        var rFar = Mathf.Max(rNear + 0.1f, s.Far);
        var azLo = s.Full360 ? s.Azimuth - 180f : s.Azimuth - s.HalfH;
        var azHi = s.Full360 ? s.Azimuth + 180f : s.Azimuth + s.HalfH;
        var elLo = s.ElevationLow;
        var elHi = Mathf.Max(elLo + 0.5f, s.ElevationHigh);
        var nAz = Mathf.CeilToInt((azHi - azLo) / MaxStepDeg);
        var nEl = Mathf.CeilToInt((elHi - elLo) / MaxStepDeg);
        var gridR = rFar <= 20f ? 2f : rFar <= 100f ? 10f : 25f;
        var nR = Mathf.Clamp(Mathf.CeilToInt((rFar - rNear) / Mathf.Max(0.5f, gridR * 0.5f)), 1, 20);
        var fill = Fill(s.Kind);
        var edgeAz = !s.Full360;

        float Az(float u) => Mathf.Lerp(azLo, azHi, u);
        float El(float v) => Mathf.Lerp(elLo, elHi, v);
        float R(float u) => Mathf.Lerp(rNear, rFar, u);

        
        b.Patch(nAz, nEl, (u, v) => ApsSettings.Dir(Az(u), El(v)) * rFar, (u, v) => new Vector2(Az(u) / 15f, El(v) / 15f),
            launcher ? FarBoundary : fill, edgeAz, true);
        
        if (rNear > 0.01f)
            b.Patch(nAz, nEl, (u, v) => ApsSettings.Dir(Az(u), El(v)) * rNear, (u, v) => new Vector2(Az(u) / 15f, El(v) / 15f),
                launcher ? NearBoundary : fill, edgeAz, true);
        
        if (!s.Full360)
        {
            b.Patch(nR, nEl, (u, v) => ApsSettings.Dir(azLo, El(v)) * R(u), (u, v) => new Vector2(R(u) / gridR, El(v) / 15f), fill, true, true);
            b.Patch(nR, nEl, (u, v) => ApsSettings.Dir(azHi, El(v)) * R(u), (u, v) => new Vector2(R(u) / gridR, El(v) / 15f), fill, true, true);
        }
        
        if (elHi < 89.5f)
            b.Patch(nAz, nR, (u, v) => ApsSettings.Dir(Az(u), elHi) * R(v), (u, v) => new Vector2(Az(u) / 15f, R(v) / gridR), fill, edgeAz, true);
        if (elLo > -89.5f)
            b.Patch(nAz, nR, (u, v) => ApsSettings.Dir(Az(u), elLo) * R(v), (u, v) => new Vector2(Az(u) / 15f, R(v) / gridR), fill, edgeAz, true);

        if (mesh == null || mesh.WasCollected)
        {
            mesh = new Mesh { name = "APS zone" };
            mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mesh.MarkDynamic();
        }
        mesh.Clear();
        if (b.Verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = b.Verts.ToArray();
        mesh.colors = b.Colors.ToArray();
        var uv = new Il2CppSystem.Collections.Generic.List<Vector4>(b.Uv.Count);
        foreach (var x in b.Uv) uv.Add(x);
        mesh.SetUVs(0, uv);
        mesh.uv2 = b.Grid.ToArray();
        mesh.triangles = b.Tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
}
