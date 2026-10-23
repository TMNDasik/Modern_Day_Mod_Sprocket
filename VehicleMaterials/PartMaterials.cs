using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sprocket.Vehicles;
using UnityEngine;

namespace VehicleMaterials;

internal enum MaterialKind { Default = 0, Glass = 1, Nickel = 2, Chrome = 3, Steel = 4, Fabric = 5, Rubber = 6 }

internal static class Kinds
{
    public const int Count = 7;
    public static readonly string[] Names = { "Default (paint)", "Glass", "Nickel", "Chrome", "Steel", "Fabric", "Rubber" };

    public static string Tooltip(MaterialKind k) => k switch
    {
        MaterialKind.Default => "The vehicle's paint (Paint tab).",
        MaterialKind.Glass => "Clear glass with reflections — you can see inside the vehicle.",
        MaterialKind.Nickel => "Polished nickel: warm, bright metal.",
        MaterialKind.Chrome => "Mirror chrome.",
        MaterialKind.Steel => "Bare machined steel.",
        MaterialKind.Fabric => "Canvas / tarpaulin.",
        MaterialKind.Rubber => "Black rubber.",
        _ => ""
    };

    
    public static int TagOf(MaterialKind k) => (int)k + 1;

    public static bool TryKind(int tag, out MaterialKind kind)
    {
        if (tag >= 1 && tag <= Count) { kind = (MaterialKind)(tag - 1); return true; }
        kind = MaterialKind.Default;
        return false;
    }

    public static string TagName(int tag) => TryKind(tag, out var k) ? Names[(int)k] : "Same as part";
}




internal sealed class PartMaterials
{
    public MaterialKind Whole;
    public int[] Faces;            
    public bool FacesPending;      
    public float PendingSince;
    public int Version;

    public bool HasFaces
    {
        get
        {
            if (Faces == null) return false;
            foreach (var t in Faces) if (t != 0) return true;
            return false;
        }
    }

    public bool IsDefault => Whole == MaterialKind.Default && !HasFaces;

    public PartMaterials Clone() => new()
    {
        Whole = Whole,
        Faces = Faces != null ? (int[])Faces.Clone() : null,
        FacesPending = FacesPending,
        PendingSince = PendingSince
    };

    public string ToToken() => ToToken(Whole, Faces);

    
    public static string ToToken(MaterialKind whole, int[] faces)
    {
        var sb = new StringBuilder("mat2;w=");
        sb.Append(((int)whole).ToString(CultureInfo.InvariantCulture));
        if (faces != null && Array.Exists(faces, t => t != 0))
        {
            sb.Append(";n=").Append(faces.Length.ToString(CultureInfo.InvariantCulture)).Append(";f=");
            var first = true;
            for (var i = 0; i < faces.Length;)
            {
                var t = faces[i];
                var j = i;
                while (j + 1 < faces.Length && faces[j + 1] == t) j++;
                if (t != 0)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    if (j > i) sb.Append('-').Append(j.ToString(CultureInfo.InvariantCulture));
                    sb.Append(':').Append(t.ToString(CultureInfo.InvariantCulture));
                }
                i = j + 1;
            }
        }
        return sb.ToString();
    }

    public static PartMaterials FromToken(string token)
    {
        var p = new PartMaterials();
        if (string.IsNullOrEmpty(token)) return p;
        var count = -1;
        string list = null;
        foreach (var part in token.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part.Substring(0, eq).Trim();
            var val = part.Substring(eq + 1).Trim();
            if (key == "w" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
                p.Whole = (MaterialKind)Math.Clamp(w, 0, Kinds.Count - 1);
            else if (key == "n" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                count = n;
            else if (key == "f") list = val;
            
        }
        if (count > 0 && count <= 65535 && !string.IsNullOrEmpty(list))
        {
            var faces = new int[count];
            foreach (var item in list.Split(','))
            {
                var colon = item.IndexOf(':');
                if (colon <= 0 || !int.TryParse(item.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tag)) continue;
                if (tag < 0 || tag > Kinds.Count) continue;
                var range = item.Substring(0, colon);
                var dash = range.IndexOf('-');
                int a, b;
                if (dash > 0)
                {
                    if (!int.TryParse(range.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out a) ||
                        !int.TryParse(range.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) continue;
                }
                else if (int.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) b = a;
                else continue;
                for (var i = Math.Max(0, a); i <= Math.Min(b, count - 1); i++) faces[i] = tag;
            }
            p.Faces = faces;
            p.FacesPending = true;
            p.PendingSince = Time.unscaledTime;
        }
        return p;
    }
}



internal static class Store
{
    private sealed class Entry
    {
        public VehicleObject Vo;
        public PartMaterials M;
    }

    private static readonly Dictionary<IntPtr, Entry> All = new();

    public static PartMaterials Get(VehicleObject vo)
    {
        if (vo == null) return null;
        try
        {
            if (All.TryGetValue(vo.Pointer, out var e) && Alive(e.Vo) && e.Vo.Pointer == vo.Pointer) return e.M;
        }
        catch { }
        return null;
    }

    
    public static PartMaterials Ensure(VehicleObject vo)
    {
        if (vo == null) return null;
        var m = Get(vo);
        if (m != null) return m;
        m = new PartMaterials();
        All[vo.Pointer] = new Entry { Vo = vo, M = m };
        return m;
    }

    public static void SetWhole(VehicleObject vo, MaterialKind whole)
    {
        var m = whole == MaterialKind.Default ? Get(vo) : Ensure(vo);
        if (m == null || m.Whole == whole) return;
        m.Whole = whole;
        m.Version++;
    }

    public static void Set(VehicleObject vo, PartMaterials m)
    {
        if (vo == null || m == null) return;
        var e = Get(vo);
        if (e == null)
        {
            if (m.IsDefault) return;
            e = Ensure(vo);
        }
        e.Whole = m.Whole;
        e.Faces = m.Faces != null ? (int[])m.Faces.Clone() : null;
        e.FacesPending = m.FacesPending;
        e.PendingSince = m.PendingSince;
        e.Version++;
    }

    
    public static List<(VehicleObject Vo, PartMaterials M)> Snapshot()
    {
        var list = new List<(VehicleObject, PartMaterials)>();
        List<IntPtr> dead = null;
        foreach (var kv in All)
        {
            if (Alive(kv.Value.Vo)) list.Add((kv.Value.Vo, kv.Value.M));
            else (dead ??= new List<IntPtr>()).Add(kv.Key);
        }
        if (dead != null) foreach (var k in dead) All.Remove(k);
        return list;
    }

    private static bool Alive(VehicleObject vo)
    {
        try { return vo != null && !vo.WasCollected && vo.transform != null; }
        catch { return false; }
    }
}
