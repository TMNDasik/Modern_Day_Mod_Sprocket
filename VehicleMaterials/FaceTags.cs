using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.MeshEditing;
using Sprocket.PlateMesh;
using Sprocket.Vehicles;
using Sprocket.Vehicles.PlateStructures;
using UnityEngine;
using PmFace = Sprocket.PlateMesh.Face;

namespace VehicleMaterials;



internal static class FaceTags
{
    public static PlateStructure Plate(VehicleObject vo)
    {
        if (vo == null) return null;
        try
        {
            foreach (var c in vo.behaviours)
            {
                var p = c != null ? c.TryCast<PlateStructure>() : null;
                if (p != null) return p;
            }
        }
        catch { }
        return null;
    }

    public static EditMesh Mesh(PlateStructure plate)
    {
        try
        {
            if (plate == null || !plate.HasMesh) return null;
            return plate.Mesh?.EditMesh;
        }
        catch { return null; }
    }

    public static EditMesh Mesh(VehicleObject vo) => Mesh(Plate(vo));

    public static int Count(EditMesh em)
    {
        try { return em?.faces?.Count ?? 0; }
        catch { return 0; }
    }

    public static int[] Read(EditMesh em)
    {
        if (em == null) return null;
        var faces = em.faces;
        if (faces == null) return null;
        var n = faces.Count;
        var tags = new int[n];
        for (var i = 0; i < n; i++)
        {
            var f = faces[i];
            if (f == null) continue;
            var t = (int)f.submeshIndex;
            tags[i] = t >= 0 && t <= Kinds.Count ? t : 0;
        }
        return tags;
    }

    public static void Write(EditMesh em, IReadOnlyList<int> indices, IReadOnlyList<int> tags)
    {
        if (em == null || indices == null) return;
        var faces = em.faces;
        if (faces == null) return;
        var n = faces.Count;
        for (var i = 0; i < indices.Count; i++)
        {
            var idx = indices[i];
            if (idx < 0 || idx >= n) continue;
            var f = faces[idx];
            if (f != null) f.submeshIndex = (short)Math.Clamp(tags[Math.Min(i, tags.Count - 1)], 0, Kinds.Count);
        }
    }

    public static void WriteAll(EditMesh em, int[] tags)
    {
        if (em == null || tags == null) return;
        var faces = em.faces;
        if (faces == null) return;
        var n = Math.Min(faces.Count, tags.Length);
        for (var i = 0; i < n; i++)
        {
            var f = faces[i];
            if (f != null) f.submeshIndex = (short)Math.Clamp(tags[i], 0, Kinds.Count);
        }
    }

    
    
    public static List<int> Selected(EditMesh em)
    {
        var list = new List<int>();
        if (em == null) return list;
        var faces = em.faces;
        if (faces == null) return list;
        var n = faces.Count;
        for (var i = 0; i < n; i++)
        {
            var f = faces[i];
            if (f != null && (f.flags & ElementFlags.Selected) != 0) list.Add(i);
        }
        if (list.Count == 0 && em.selectedVertexCount > 0)
            for (var i = 0; i < n; i++)
            {
                var f = faces[i];
                if (f != null && AllVerticesSelected(f)) list.Add(i);
            }
        if (list.Count > 0 && SymmetryOn(em)) AddMirrors(em, list);
        return list;
    }

    private static bool AllVerticesSelected(PmFace f)
    {
        var vc = f.vertexCount;
        if (vc < 3 || vc > 1024) return false;
        var l = f.firstLoop;
        for (var k = 0; k < vc; k++)
        {
            if (l == null) return false;
            var v = l.vertex;
            if (v == null || (v.flags & ElementFlags.Selected) == 0) return false;
            l = l.next;
        }
        return true;
    }

    private static bool SymmetryOn(EditMesh em)
    {
        try
        {
            var editor = Hooks.MeshEditor;
            if (editor == null || !editor.Symmetry) return false;
            var target = editor.Mesh?.EditMesh;
            return target != null && target.Pointer == em.Pointer;
        }
        catch { return false; }
    }

    private static void AddMirrors(EditMesh em, List<int> list)
    {
        try
        {
            var faces = em.faces;
            var input = new Il2CppReferenceArray<PmFace>(list.Count);
            for (var i = 0; i < list.Count; i++) input[i] = faces[list[i]];
            var result = ElementSelection.AppendMirrors(em, input);
            if (result == null || result.Length <= list.Count) return;
            var index = new Dictionary<IntPtr, int>();
            for (var i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                if (f != null) index[f.Pointer] = i;
            }
            var have = new HashSet<int>(list);
            foreach (var f in result)
                if (f != null && index.TryGetValue(f.Pointer, out var i) && have.Add(i)) list.Add(i);
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Mirror faces lookup failed: " + e.Message);
        }
    }

    
    public static int SelectionSignature(EditMesh em)
    {
        if (em == null) return 0;
        try
        {
            var faces = em.faces;
            var h = 17 + em.selectedVertexCount * 7919;
            var n = faces.Count;
            for (var i = 0; i < n; i++)
            {
                var f = faces[i];
                if (f != null && (f.flags & ElementFlags.Selected) != 0) h = h * 31 + i + 1;
            }
            return h * 31 + n;
        }
        catch { return 0; }
    }

    public static Vector3 Newell(Vector3[] p)
    {
        var n = Vector3.zero;
        for (var i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            n.x += (a.y - b.y) * (a.z + b.z);
            n.y += (a.z - b.z) * (a.x + b.x);
            n.z += (a.x - b.x) * (a.y + b.y);
        }
        return n;
    }
}
