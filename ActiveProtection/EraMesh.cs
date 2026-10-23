using System.Collections.Generic;
using UnityEngine;

namespace ActiveProtection;




internal static class EraMesh
{
    public static readonly Color Front = new(1f, 0.55f, 0.1f, 0.3f);
    public static readonly Color Sides = new(1f, 0.55f, 0.1f, 0.17f);

    public static Mesh Build(ApsSettings s, Mesh mesh)
    {
        var size = s.EraSize;
        var h = size * 0.5f;
        var tw = Mathf.Max(0.001f, s.TileWidth / 1000f);
        var th = Mathf.Max(0.001f, s.TileHeight / 1000f);

        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var uv = new List<Vector4>();
        var grid = new List<Vector2>();
        var tris = new List<int>();

        
        void Face(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, Vector2 g00, Vector2 g10, Vector2 g11, Vector2 g01, Color c)
        {
            var i = verts.Count;
            verts.Add(p00); verts.Add(p10); verts.Add(p11); verts.Add(p01);
            uv.Add(new Vector4(0f, 0f, 1f, 1f)); uv.Add(new Vector4(1f, 0f, 1f, 1f));
            uv.Add(new Vector4(1f, 1f, 1f, 1f)); uv.Add(new Vector4(0f, 1f, 1f, 1f));
            grid.Add(g00); grid.Add(g10); grid.Add(g11); grid.Add(g01);
            for (var k = 0; k < 4; k++) colors.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        
        Vector2 G(float x, float y) => new((x + h.x) / tw, (y + h.y) / th);
        Vector2 Gx(float x) => new((x + h.x) / tw, 0.5f);
        Vector2 Gy(float y) => new(0.5f, (y + h.y) / th);

        
        Face(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            G(-h.x, -h.y), G(h.x, -h.y), G(h.x, h.y), G(-h.x, h.y), Front);
        Face(new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z),
            G(h.x, -h.y), G(-h.x, -h.y), G(-h.x, h.y), G(h.x, h.y), Sides);
        
        Face(new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
            Gx(-h.x), Gx(h.x), Gx(h.x), Gx(-h.x), Sides);
        Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z),
            Gx(-h.x), Gx(h.x), Gx(h.x), Gx(-h.x), Sides);
        
        Face(new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z),
            Gy(-h.y), Gy(-h.y), Gy(h.y), Gy(h.y), Sides);
        Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z),
            Gy(-h.y), Gy(-h.y), Gy(h.y), Gy(h.y), Sides);

        if (mesh == null || mesh.WasCollected)
        {
            mesh = new Mesh { name = "ERA block" };
            mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.vertices = verts.ToArray();
        mesh.colors = colors.ToArray();
        var uv0 = new Il2CppSystem.Collections.Generic.List<Vector4>(uv.Count);
        foreach (var x in uv) uv0.Add(x);
        mesh.SetUVs(0, uv0);
        mesh.uv2 = grid.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }
}
