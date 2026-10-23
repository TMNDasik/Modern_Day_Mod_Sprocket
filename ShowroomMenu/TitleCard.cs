using System;
using Sprocket.ContinuousTracks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShowroomMenu;









internal sealed class TitleCard
{
    private const float Depth = 12f;          
    private const float CenterX = 0.745f;     
    private const float Top = 0.935f;         
    private const float WidthFraction = 0.34f, MaxHeightFraction = 0.2f;
    private const float Flat = 0.06f;         
    private const int KnownSubmesh = 31;      

    private Mesh _mesh;
    private Material _material;
    private int _submesh = -1, _layer;
    private Vector3 _titlePos;
    private Quaternion _titleRot;
    private float _titleScale = 1f;
    private Bounds _layout;                   
    private float _alpha = 1f;

    private Transform _belt, _beltParent;
    private Vector3 _beltLocalPos, _beltLocalScale, _beltWorldPos, _beltWorldScale;
    private Quaternion _beltLocalRot, _beltWorldRot;
    
    
    private GameObject _rig, _rigInner;

    public bool Valid { get; private set; }
    public Transform BeltRoot => _belt;
    public Rect ViewRect { get; private set; } = Rect.MinMaxRect(0.575f, 0.75f, 0.915f, 0.935f);
    public float Alpha => _alpha;

    public void Capture(Scene menuScene)
    {
        try
        {
            MeshRenderer title = null;
            TrackBeltTester belt = null;
            foreach (var root in menuScene.GetRootGameObjects())
            {
                if (belt == null) belt = root.GetComponentInChildren<TrackBeltTester>(true);
                if (title != null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mat = mr.sharedMaterial;
                    if (mr.gameObject.name == "SprocketTitle" || (mat != null && mat.name.StartsWith("SprocketTitle", StringComparison.Ordinal)))
                    {
                        title = mr;
                        break;
                    }
                }
            }
            if (title == null)
            {
                Plugin.Log.LogWarning("SPROCKET title not found in the main menu");
                return;
            }
            _mesh = title.GetComponent<MeshFilter>()?.sharedMesh;
            _material = title.sharedMaterial;
            _layer = title.gameObject.layer;
            var t = title.transform;
            _titlePos = t.position;
            _titleRot = t.rotation;
            _titleScale = Mathf.Max(1e-4f, t.lossyScale.x);
            var worldToTitle = t.localToWorldMatrix.inverse;
            var world = title.bounds;
            _layout = Transform(worldToTitle, world);
            _submesh = FindSubmesh(_mesh, world);
            Valid = _mesh != null && _material != null && _submesh >= 0 && _layout.extents.x > 0.01f && _layout.extents.y > 0.01f;
            Plugin.Log.LogInfo($"Title: mesh '{_mesh?.name}' ({_mesh?.subMeshCount} submeshes), submesh {_submesh}, letters {_layout.size}");

            
            if (Valid && belt != null)
            {
                _belt = belt.transform;
                _beltParent = _belt.parent;
                _beltLocalPos = _belt.localPosition;
                _beltLocalRot = _belt.localRotation;
                _beltLocalScale = _belt.localScale;
                _beltWorldPos = _belt.position;
                _beltWorldRot = _belt.rotation;
                _beltWorldScale = _belt.lossyScale;
                foreach (var r in _belt.GetComponentsInChildren<Renderer>(true))
                    _layout.Encapsulate(Transform(worldToTitle, r.bounds));
                Plugin.Log.LogInfo($"Title sprocket found, whole logo {_layout.size}");
            }
            if (!Valid) Plugin.Log.LogWarning("SPROCKET title could not be prepared for drawing");
        }
        catch (Exception e)
        {
            Valid = false;
            _belt = null;
            Plugin.Log.LogWarning("SPROCKET title unavailable: " + e.Message);
        }
    }

    
    public void Attach(Scene menuScene)
    {
        if (!Valid || _belt == null) return;
        try
        {
            _rig = new GameObject("ShowroomTitleRig");
            SceneManager.MoveGameObjectToScene(_rig, menuScene);
            _rigInner = new GameObject("FactoryToTitle");
            _rigInner.transform.SetParent(_rig.transform, false);
            var inv = Quaternion.Inverse(_titleRot);
            _rigInner.transform.localRotation = inv;
            _rigInner.transform.localPosition = -(inv * _titlePos);
            _belt.SetParent(_rigInner.transform, false);
            _belt.localPosition = _beltWorldPos;
            _belt.localRotation = _beltWorldRot;
            _belt.localScale = _beltWorldScale;
            foreach (var r in _belt.GetComponentsInChildren<Renderer>(true))
            {
                r.lightProbeUsage = LightProbeUsage.Off;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Title sprocket could not be moved: " + e.Message);
            RestoreBelt();
        }
    }

    
    private static int FindSubmesh(Mesh mesh, Bounds world)
    {
        if (mesh == null) return -1;
        var count = mesh.subMeshCount;
        int best = -1;
        var bestErr = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var b = mesh.GetSubMesh(i).bounds;
            var err = (b.center - world.center).magnitude + (b.extents - world.extents).magnitude;
            if (err < bestErr)
            {
                bestErr = err;
                best = i;
            }
        }
        if (bestErr < 0.1f) return best;
        if (KnownSubmesh < count)
        {
            
            var known = mesh.GetSubMesh(KnownSubmesh).bounds;
            if (known.size == Vector3.zero || (known.center - world.center).magnitude < 1f) return KnownSubmesh;
        }
        Plugin.Log.LogWarning($"Title submesh match is poor ({bestErr:0.00} m)");
        return bestErr < 0.6f ? best : -1;
    }

    private static Bounds Transform(Matrix4x4 m, Bounds b)
    {
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        for (var i = 0; i < 8; i++)
        {
            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var p = m.MultiplyPoint3x4(c);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var r = new Bounds();
        r.SetMinMax(min, max);
        return r;
    }

    public void Draw(Camera cam, bool visible)
    {
        _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 5f);
        if (!Valid) return;
        var camT = cam.transform;
        var tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var tanX = tanY * (cam.aspect > 0.1f ? cam.aspect : 16f / 9f);

        
        var scale = WidthFraction * Depth * tanX / _layout.extents.x;
        scale = Mathf.Min(scale, MaxHeightFraction * Depth * tanY / _layout.extents.y);
        var cy = Top - _layout.extents.y * scale / (Depth * tanY) * 0.5f;

        
        Vector3 pos;
        Quaternion rot;
        Rect rect = default;
        for (var pass = 0; pass < 3; pass++)
        {
            Place(camT, tanX, tanY, cy, scale, out pos, out rot);
            rect = Project(cam, pos, rot, scale);
            if (pass == 2) break;
            var k = Mathf.Min(WidthFraction / Mathf.Max(1e-4f, rect.width), MaxHeightFraction / Mathf.Max(1e-4f, rect.height));
            scale *= k;
            cy = Top - (rect.yMax - cy) * k;
        }
        ViewRect = rect;

        
        var eased = _alpha * _alpha * (3f - 2f * _alpha);
        cy += (1f - eased) * (rect.height + 0.12f);
        Place(camT, tanX, tanY, cy, scale, out pos, out rot);

        
        var outerScale = new Vector3(scale, scale, scale * Flat) / _titleScale;
        var inv = Quaternion.Inverse(_titleRot);
        var matrix = Matrix4x4.TRS(pos, rot, outerScale) * Matrix4x4.TRS(-(inv * _titlePos), inv, Vector3.one);
        if (_rig != null)
        {
            
            _rig.transform.SetPositionAndRotation(pos, rot);
            _rig.transform.localScale = outerScale;
        }
        if (_alpha <= 0.01f) return;
        
        Graphics.DrawMesh(_mesh, matrix, _material, _layer, cam, _submesh, null, ShadowCastingMode.Off, true,
            null, LightProbeUsage.Off, null);
    }

    
    
    private void Place(Transform camT, float tanX, float tanY, float cy, float scale, out Vector3 pos, out Quaternion rot)
    {
        var camCenter = new Vector3((2f * CenterX - 1f) * Depth * tanX, (2f * cy - 1f) * Depth * tanY, Depth);
        var centerWorld = camT.position + camT.rotation * camCenter;
        rot = camT.rotation * Quaternion.Euler(0f, 180f, 0f);
        pos = centerWorld - rot * Vector3.Scale(_layout.center, new Vector3(scale, scale, scale * Flat));
    }

    
    private Rect Project(Camera cam, Vector3 pos, Quaternion rot, float scale)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        var s = new Vector3(scale, scale, scale * Flat);
        for (var i = 0; i < 8; i++)
        {
            var c = _layout.center + Vector3.Scale(_layout.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var v = cam.WorldToViewportPoint(pos + rot * Vector3.Scale(c, s));
            minX = Mathf.Min(minX, v.x);
            maxX = Mathf.Max(maxX, v.x);
            minY = Mathf.Min(minY, v.y);
            maxY = Mathf.Max(maxY, v.y);
        }
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void RestoreBelt()
    {
        if (_belt == null || _belt.WasCollected || !_belt) return;
        if (_beltParent != null && !_beltParent.WasCollected && _beltParent) _belt.SetParent(_beltParent, false);
        else _belt.SetParent(null, false);
        _belt.localPosition = _beltLocalPos;
        _belt.localRotation = _beltLocalRot;
        _belt.localScale = _beltLocalScale;
    }

    public void Dispose(bool restoreMenu)
    {
        if (restoreMenu) RestoreBelt();
        if (_rig != null && !_rig.WasCollected && _rig) Object.Destroy(_rig);
        _rig = null;
        Valid = false;
        _mesh = null;
        _material = null;
    }
}
