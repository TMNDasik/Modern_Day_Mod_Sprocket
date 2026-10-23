using System.Collections.Generic;
using UnityEngine;

namespace ActiveProtection;




internal static class EraLayoutMesh
{
    
    
    private static readonly Color BoxFront = new(1f, 0.75f, 0.35f, 0f);
    private static readonly Color ExplosiveFront = new(1f, 0.42f, 0.08f, 0.32f);
    private static readonly Color ExplosiveSides = new(1f, 0.42f, 0.08f, 0.12f);
    private static readonly Color PlateFront = new(0.72f, 0.8f, 0.92f, 0.14f);
    private static readonly Color PlateSides = new(0.72f, 0.8f, 0.92f, 0.06f);

    private static readonly List<Vector3> Verts = new();
    private static readonly List<Color> Colors = new();
    private static readonly List<Vector4> Uv = new();
    private static readonly List<Vector2> Grid = new();
    private static readonly List<int> Tris = new();

    public static Mesh Build(EraConfig c, Mesh mesh)
    {
        Verts.Clear();
        Colors.Clear();
        Uv.Clear();
        Grid.Clear();
        Tris.Clear();

        foreach (var p in c.Panels)
        {
            var side = p.Side;
            
            var o = p.Center / 1000f;
            var hw = side * (p.W * 0.0005f);
            var hh = p.Up * (p.H * 0.0005f);
            Face(o - hw - hh, o + hw - hh, o + hw + hh, o - hw + hh, BoxFront);
            
            foreach (var e in c.Elements)
            {
                if (e.Explosive) continue;
                EraConfig.Box(p, e, out var center, out var half);
                AddBox(center, side, p.Up, p.Out, half, PlateFront, PlateSides);
            }
            foreach (var e in c.Elements)
            {
                if (!e.Explosive) continue;
                EraConfig.Box(p, e, out var center, out var half);
                AddBox(center, side, p.Up, p.Out, half, ExplosiveFront, ExplosiveSides);
            }
        }

        if (mesh == null || mesh.WasCollected)
        {
            mesh = new Mesh { name = "ERA layout" };
            mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.vertices = Verts.ToArray();
        mesh.colors = Colors.ToArray();
        var uv0 = new Il2CppSystem.Collections.Generic.List<Vector4>(Uv.Count);
        foreach (var x in Uv) uv0.Add(x);
        mesh.SetUVs(0, uv0);
        mesh.uv2 = Grid.ToArray();
        mesh.triangles = Tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    
    private static void AddBox(Vector3 center, Vector3 side, Vector3 up, Vector3 o, Vector3 half, Color front, Color sides)
    {
        Vector3 P(float sx, float sy, float sz) => center + side * (sx * half.x) + up * (sy * half.y) + o * (sz * half.z);
        
        Face(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), front);
        Face(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), sides);
        Face(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), sides);
        Face(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), sides);
        Face(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), sides);
        Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), sides);
    }

    private static void Face(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, Color col)
    {
        var i = Verts.Count;
        Verts.Add(p00); Verts.Add(p10); Verts.Add(p11); Verts.Add(p01);
        Uv.Add(new Vector4(0f, 0f, 1f, 1f)); Uv.Add(new Vector4(1f, 0f, 1f, 1f));
        Uv.Add(new Vector4(1f, 1f, 1f, 1f)); Uv.Add(new Vector4(0f, 1f, 1f, 1f));
        for (var k = 0; k < 4; k++)
        {
            Grid.Add(new Vector2(0.5f, 0.5f));
            Colors.Add(col);
        }
        
        Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
        Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
    }
}
