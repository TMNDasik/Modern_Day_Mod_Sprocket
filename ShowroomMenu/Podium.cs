using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShowroomMenu;




internal sealed class Podium
{
    public const float BaseTop = 0.16f, DeckTop = 0.34f, BaseExtra = 0.3f;
    private const int Segments = 160;

    private static AssetBundle _bundle;
    private static bool _bundleTried;
    private static Material _deckMat, _metalMat, _baseMat, _glowMat;

    private GameObject _root, _deck;
    private readonly List<Mesh> _meshes = new();
    public float Radius { get; private set; }
    public float BaseRadius => Radius + BaseExtra;

    public bool Create(Scene scene, float radius)
    {
        if (!LoadMaterials()) return false;
        _root = new GameObject("ShowroomPodium");
        SceneManager.MoveGameObjectToScene(_root, scene);
        Build(radius);
        return true;
    }

    public void Resize(float radius)
    {
        if (_root == null || Mathf.Abs(radius - Radius) < 0.05f) return;
        var yaw = _deck != null ? _deck.transform.localEulerAngles.y : 0f;
        Clear();
        Build(radius);
        SetYaw(yaw);
    }

    public void SetYaw(float yaw)
    {
        if (_deck != null) _deck.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public void Dispose()
    {
        Clear();
        if (_root != null && !_root.WasCollected && _root) Object.Destroy(_root);
        _root = null;
    }

    private void Clear()
    {
        if (_root != null && !_root.WasCollected && _root)
            for (var i = _root.transform.childCount - 1; i >= 0; i--) Object.Destroy(_root.transform.GetChild(i).gameObject);
        foreach (var m in _meshes)
            if (m != null && !m.WasCollected && m) Object.Destroy(m);
        _meshes.Clear();
        _deck = null;
    }

    private void Build(float radius)
    {
        Radius = radius;
        var rb = BaseRadius;
        const float tau = Mathf.PI * 2f;

        
        var b = new MeshBuilder();
        b.Band(rb, 0f, BaseTop - 0.02f, 0f, tau, Segments, 1f);
        b.Cone(rb, BaseTop - 0.02f, rb - 0.02f, BaseTop, Segments);
        b.Annulus(radius - 0.06f, rb - 0.02f, BaseTop, Segments);
        Part("Base", _root.transform, b, _baseMat, true);

        
        var g = new MeshBuilder();
        const int dashes = 60;
        for (var i = 0; i < dashes; i++)
        {
            var a0 = tau * i / dashes;
            g.Band(rb + 0.004f, 0.05f, 0.075f, a0, a0 + tau / dashes * 0.6f, 4, 1f);
        }
        Part("BaseLights", _root.transform, g, _glowMat, false);

        
        _deck = new GameObject("Deck");
        _deck.transform.SetParent(_root.transform, false);
        var side = new MeshBuilder();
        side.Band(radius, BaseTop, DeckTop - 0.025f, 0f, tau, Segments, 1f);
        side.Cone(radius, DeckTop - 0.025f, radius - 0.025f, DeckTop, Segments);
        Part("DeckSide", _deck.transform, side, _metalMat, true);

        var top = new MeshBuilder();
        top.Disc(radius - 0.025f, DeckTop, Segments);
        Part("DeckTop", _deck.transform, top, _deckMat, true);

        
        var lights = new MeshBuilder();
        const int segs = 16;
        for (var i = 0; i < segs; i++)
        {
            var a0 = tau * i / segs;
            lights.Band(radius + 0.004f, 0.23f, 0.258f, a0, a0 + tau / segs * 0.7f, 10, 1f);
        }
        Part("DeckLights", _deck.transform, lights, _glowMat, false);
    }

    private void Part(string name, Transform parent, MeshBuilder builder, Material material, bool shadows)
    {
        var mesh = builder.ToMesh(name);
        _meshes.Add(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        r.receiveShadows = true;
        
        r.lightProbeUsage = LightProbeUsage.Off;
    }

    

    private static bool Alive(Object o) => o != null && !o.WasCollected && o;

    private static bool LoadMaterials()
    {
        if (Alive(_deckMat) && Alive(_metalMat) && Alive(_baseMat) && Alive(_glowMat)) return true;
        if (!_bundleTried)
        {
            _bundleTried = true;
            try
            {
                var path = Path.Combine(Plugin.ModFolder(Plugin.PluginDir, "ShowroomMenu"), "showroommenu.bundle");
                if (File.Exists(path)) _bundle = BundleLoader.LoadFromFile(path);
                else Plugin.Log.LogWarning("Podium materials not found: " + path);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Podium bundle failed: " + e.Message); }
        }
        if (_bundle != null)
        {
            _deckMat = Keep(BundleLoader.LoadAsset<Material>(_bundle, "SM_Deck"));
            _metalMat = Keep(BundleLoader.LoadAsset<Material>(_bundle, "SM_Metal"));
            _baseMat = Keep(BundleLoader.LoadAsset<Material>(_bundle, "SM_Base"));
            _glowMat = Keep(BundleLoader.LoadAsset<Material>(_bundle, "SM_Glow"));
        }
        if (!Alive(_deckMat) || !Alive(_metalMat) || !Alive(_baseMat) || !Alive(_glowMat))
        {
            var lit = Shader.Find("HDRP/Lit");
            if (lit == null)
            {
                Plugin.Log.LogWarning("No material for the podium (HDRP/Lit missing)");
                return false;
            }
            _deckMat ??= Simple(lit, new Color(0.2f, 0.2f, 0.21f), 0.7f, 0.42f, Color.black);
            _metalMat ??= Simple(lit, new Color(0.2f, 0.205f, 0.215f), 0.85f, 0.6f, Color.black);
            _baseMat ??= Simple(lit, new Color(0.13f, 0.135f, 0.14f), 0.6f, 0.38f, Color.black);
            _glowMat ??= Simple(lit, new Color(0.02f, 0.02f, 0.02f), 0f, 0.75f, new Color(4f, 1.28f, 0.18f));
        }
        return true;
    }

    private static Material Keep(Material m)
    {
        if (m != null) m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return m;
    }

    private static Material Simple(Shader shader, Color color, float metallic, float smoothness, Color emissive)
    {
        var m = new Material(shader) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        m.SetVector("_EmissiveColor", new Vector4(emissive.r, emissive.g, emissive.b, 1f));
        m.SetFloat("_EmissiveExposureWeight", 0f);
        return m;
    }
}


internal sealed class MeshBuilder
{
    private readonly List<Vector3> _v = new();
    private readonly List<Vector3> _n = new();
    private readonly List<Vector2> _uv = new();
    private readonly List<int> _t = new();

    
    public void Band(float r, float y0, float y1, float a0, float a1, int segs, float tile)
    {
        var start = _v.Count;
        for (var i = 0; i <= segs; i++)
        {
            var a = Mathf.Lerp(a0, a1, (float)i / segs);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var u = a * r / tile;
            _v.Add(dir * r + Vector3.up * y0); _n.Add(dir); _uv.Add(new Vector2(u, y0 / tile));
            _v.Add(dir * r + Vector3.up * y1); _n.Add(dir); _uv.Add(new Vector2(u, y1 / tile));
        }
        for (var i = 0; i < segs; i++)
        {
            int bl = start + i * 2, tl = bl + 1, br = bl + 2, tr = bl + 3;
            
            _t.Add(tl); _t.Add(tr); _t.Add(br);
            _t.Add(tl); _t.Add(br); _t.Add(bl);
        }
    }

    
    public void Cone(float r0, float y0, float r1, float y1, int segs)
    {
        var start = _v.Count;
        var nr = y1 - y0;
        var ny = r0 - r1;
        var len = Mathf.Sqrt(nr * nr + ny * ny);
        nr /= len;
        ny /= len;
        for (var i = 0; i <= segs; i++)
        {
            var a = Mathf.PI * 2f * i / segs;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var n = dir * nr + Vector3.up * ny;
            _v.Add(dir * r0 + Vector3.up * y0); _n.Add(n); _uv.Add(new Vector2(a * r0, y0));
            _v.Add(dir * r1 + Vector3.up * y1); _n.Add(n); _uv.Add(new Vector2(a * r1, y1));
        }
        for (var i = 0; i < segs; i++)
        {
            int bl = start + i * 2, tl = bl + 1, br = bl + 2, tr = bl + 3;
            _t.Add(tl); _t.Add(tr); _t.Add(br);
            _t.Add(tl); _t.Add(br); _t.Add(bl);
        }
    }

    
    public void Disc(float r, float y, int segs)
    {
        var center = _v.Count;
        _v.Add(new Vector3(0f, y, 0f)); _n.Add(Vector3.up); _uv.Add(new Vector2(0.5f, 0.5f));
        for (var i = 0; i <= segs; i++)
        {
            var a = Mathf.PI * 2f * i / segs;
            var x = Mathf.Cos(a);
            var z = Mathf.Sin(a);
            _v.Add(new Vector3(x * r, y, z * r)); _n.Add(Vector3.up); _uv.Add(new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f));
        }
        
        for (var i = 0; i < segs; i++)
        {
            _t.Add(center); _t.Add(center + i + 2); _t.Add(center + i + 1);
        }
    }

    
    public void Annulus(float r0, float r1, float y, int segs)
    {
        var start = _v.Count;
        for (var i = 0; i <= segs; i++)
        {
            var a = Mathf.PI * 2f * i / segs;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _v.Add(dir * r0 + Vector3.up * y); _n.Add(Vector3.up); _uv.Add(new Vector2(a * r0, r0));
            _v.Add(dir * r1 + Vector3.up * y); _n.Add(Vector3.up); _uv.Add(new Vector2(a * r1, r1));
        }
        for (var i = 0; i < segs; i++)
        {
            int in0 = start + i * 2, out0 = in0 + 1, in1 = in0 + 2, out1 = in0 + 3;
            _t.Add(in0); _t.Add(in1); _t.Add(out1);
            _t.Add(in0); _t.Add(out1); _t.Add(out0);
        }
    }

    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = "ShowroomPodium_" + name };
        mesh.vertices = _v.ToArray();
        mesh.normals = _n.ToArray();
        mesh.uv = _uv.ToArray();
        mesh.triangles = _t.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        mesh.UploadMeshData(true);
        return mesh;
    }
}
