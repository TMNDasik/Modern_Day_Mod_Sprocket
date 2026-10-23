using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Cannons;
using Sprocket.Vehicles.UI;
using Sprocket.Vehicles.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Munitions;












internal sealed class TankPanel
{
    
    private const float W = 300f, H = 300f, Margin = 20f;
    private static readonly Vector2 RingCenter = new(170f, 158f);
    private const float Disp = 212f;          
    private const float TrackHalf = 92f;      
    private const float TrackStroke = 7f;     
    private const float Corner = 6f;          
    private const int Tex = 288;              
    private const float SchemeBox = 112f;     

    private static readonly Color PanelBg = new(0.06f, 0.08f, 0.09f, 0.55f);
    private static readonly Color PanelBorder = new(0.3f, 0.36f, 0.4f, 0.75f);
    private static readonly Color Gold = new(0.95f, 0.8f, 0.38f, 1f);
    private static readonly Color Green = new(0.36f, 0.9f, 0.4f, 1f);
    private static readonly Color Red = new(0.95f, 0.3f, 0.25f, 1f);
    private static readonly Color Amber = new(1f, 0.75f, 0.25f, 1f);
    private static readonly Color IconOk = new(1f, 1f, 1f, 0.28f);
    private static readonly Color Scheme = new(0.82f, 0.86f, 0.88f, 0.9f);

    private enum Part { Engine, Turret, Gun, Tracks, Count }
    private static readonly string[] PartIcons =
        { "engine_state_indicator.png", "turret_gear_state_indicator.png", "gun_state_indicator.png", "track_state_indicator.png" };

    private GameObject _canvasGo;
    private RectTransform _root;
    private bool _failed;
    private TMP_FontAsset _font;
    private Sprite _white;
    private readonly Dictionary<string, Texture2D> _tex = new();

    private RawImage _ticks, _ring;
    private Texture2D _ringTex;
    private Il2CppStructArray<Color32> _ringBuf;
    private int[] _ringIdx;
    private float[] _ringCov, _ringAng;
    private string _ringKey;

    private Image _hull, _turret, _barrel;
    private RawImage _silHull, _silTurret;
    private Silhouette _building, _shown;
    private IntPtr _silVehicle;
    private float _silSince;
    private int _silPass;
    private TextMeshProUGUI _stab, _crewText, _ammoText;
    private Image _bg, _border;
    private RawImage _crewIcon, _ammoIcon;
    private readonly RawImage[] _parts = new RawImage[(int)Part.Count];
    private readonly List<TextMeshProUGUI> _labels = new();

    public void Hide()
    {
        if (_canvasGo != null && !_canvasGo.WasCollected && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        ShowVanillaDamage(true);
    }

    public void Update(bool inControl, PlayerView view, List<Launchers.GroupInfo> tubes)
    {
        if (!inControl || !Plugin.ShowTankPanel.Value || PlayerTracker.VehicleGo == null) { Hide(); return; }
        if (!Ensure()) return;
        if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
        ShowVanillaDamage(!Plugin.HideVanillaDamage.Value);

        if (Plugin.PanelTopView.Value) UpdateScheme();
        if (Plugin.PanelAps.Value) UpdateSegments();
        UpdateDamage();
        
        if (Plugin.PanelStab.Value) UpdateStab();
        UpdateAmmo(view, tubes);
        ApplyElements();
    }

    
    private void ApplyElements()
    {
        Show(_bg, Plugin.PanelBackground.Value);
        Show(_border, Plugin.PanelBackground.Value);
        Show(_ticks, Plugin.PanelDegrees.Value);
        var aps = Plugin.PanelAps.Value;
        Show(_ring, aps);
        if (!aps) foreach (var t in _labels) Show(t, false);
        var top = Plugin.PanelTopView.Value;
        Show(_hull, top);
        Show(_turret, top);
        Show(_barrel, top);
        Show(_silHull, top);
        Show(_silTurret, top);
        if (!Plugin.PanelStab.Value) Show(_stab, false);
        Show(_parts[(int)Part.Engine], Plugin.PanelEngine.Value);
        Show(_parts[(int)Part.Turret], Plugin.PanelTurret.Value);
        Show(_parts[(int)Part.Gun], Plugin.PanelGun.Value);
        Show(_parts[(int)Part.Tracks], Plugin.PanelTracks.Value);
        Show(_crewIcon, Plugin.PanelCrew.Value);
        Show(_crewText, Plugin.PanelCrew.Value);
        Show(_ammoIcon, Plugin.PanelAmmo.Value);
        Show(_ammoText, Plugin.PanelAmmo.Value);
    }

    private static void Show(Component c, bool on)
    {
        if (c == null || c.WasCollected) return;
        var go = c.gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }

    

    private sealed class Footprint
    {
        public IntPtr Vehicle;
        public float MinX, MaxX, MinZ, MaxZ;
        public float Since, NextMeasure;
        public int Measures;
    }

    private Footprint _fp;

    private static Transform HullOf(GameObject go)
    {
        try
        {
            var v = go.GetComponentInParent<Vehicle>() ?? go.GetComponentInChildren<Vehicle>();
            return v != null ? v.transform : go.transform;
        }
        catch { return go.transform; }
    }

    
    
    private void Measure(Transform hull)
    {
        var now = Time.unscaledTime;
        if (_fp == null || _fp.Vehicle != hull.Pointer)
            _fp = new Footprint { Vehicle = hull.Pointer, Since = now, NextMeasure = now };
        if (_fp.Measures >= 3 || now < _fp.NextMeasure) return;
        _fp.Measures++;
        _fp.NextMeasure = _fp.Since + (_fp.Measures == 1 ? 3f : 8f);
        var m = hull.worldToLocalMatrix;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        var used = 0;
        foreach (var r in hull.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled || r.forceRenderingOff) continue;
            var b = r.bounds;
            var c = b.center;
            var e = b.extents;
            if (e.sqrMagnitude < 1e-6f || e.sqrMagnitude > 400f) continue;   
            var lx = m.m00 * c.x + m.m01 * c.y + m.m02 * c.z + m.m03;
            var lz = m.m20 * c.x + m.m21 * c.y + m.m22 * c.z + m.m23;
            var ex = Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z;
            var ez = Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z;
            minX = Mathf.Min(minX, lx - ex); maxX = Mathf.Max(maxX, lx + ex);
            minZ = Mathf.Min(minZ, lz - ez); maxZ = Mathf.Max(maxZ, lz + ez);
            used++;
        }
        if (used == 0 || maxX - minX < 0.3f || maxZ - minZ < 0.3f) return;
        _fp.MinX = minX; _fp.MaxX = maxX; _fp.MinZ = minZ; _fp.MaxZ = maxZ;
        if (_fp.Measures == 1 || Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo($"Tank panel: vehicle {maxX - minX:0.0} × {maxZ - minZ:0.0} m from {used} renderer(s)");
    }

    private void UpdateScheme()
    {
        var hull = HullOf(PlayerTracker.VehicleGo);
        if (UpdateSilhouette(hull)) return;
        
        _silHull.enabled = _silTurret.enabled = false;
        Measure(hull);
        if (_fp == null || _fp.MaxX <= _fp.MinX) { _hull.enabled = _turret.enabled = _barrel.enabled = false; return; }
        var wid = _fp.MaxX - _fp.MinX;
        var len = _fp.MaxZ - _fp.MinZ;
        var cx = (_fp.MinX + _fp.MaxX) * 0.5f;
        var cz = (_fp.MinZ + _fp.MaxZ) * 0.5f;
        var s = Mathf.Min(SchemeBox / len, SchemeBox * 0.62f / wid);
        Vector2 ToPanel(Vector3 world)
        {
            var l = hull.InverseTransformPoint(world);
            return RingCenter + new Vector2((l.x - cx) * s, (l.z - cz) * s);
        }
        _hull.enabled = true;
        var rt = _hull.rectTransform;
        rt.anchoredPosition = RingCenter;
        rt.sizeDelta = new Vector2(Mathf.Max(18f, wid * s), Mathf.Max(24f, len * s));

        
        var cannon = PlayerTracker.Cannon;
        if (cannon == null || cannon.WasCollected) { _turret.enabled = _barrel.enabled = false; return; }
        Vector2 pivot;
        Transform ring = null;
        try { ring = cannon.HorizontalAimingMechanism?.TryCast<TurretBehaviour>()?.transform; } catch { }
        Transform weapon = null;
        try { weapon = cannon.Behaviour != null ? cannon.Behaviour.transform : cannon.transform; } catch { weapon = cannon.transform; }
        if (ring != null && !ring.WasCollected)
        {
            pivot = ToPanel(ring.position);
            _turret.enabled = true;
            var d = Mathf.Clamp(0.62f * wid, 1.2f, 3.2f) * s;
            _turret.rectTransform.anchoredPosition = pivot;
            _turret.rectTransform.sizeDelta = new Vector2(d, d);
        }
        else
        {
            pivot = ToPanel(weapon.position);
            _turret.enabled = false;
        }
        
        var fwd = hull.InverseTransformDirection(weapon.forward);
        var yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        var length = 0.45f * len;
        try
        {
            var barrel = cannon.Barrel?.TryCast<CannonBarrelModel>();
            if (barrel != null)
            {
                var muzzle = ToPanel(barrel.transform.TransformPoint(barrel.fireLocalPosition));
                var m = (muzzle - pivot).magnitude / s;
                if (m > 0.3f && m < 15f) length = m;
            }
        }
        catch { }
        _barrel.enabled = true;
        var br = _barrel.rectTransform;
        br.anchoredPosition = pivot;
        
        var bdir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
        var room = RayToTrack(pivot - RingCenter, bdir) - TrackStroke * 0.5f - 26f;
        br.sizeDelta = new Vector2(3f, Mathf.Clamp(length * s, 10f, Mathf.Max(10f, room)));
        br.localEulerAngles = new Vector3(0f, 0f, -yaw);
    }

    
    
    private bool UpdateSilhouette(Transform hull)
    {
        var now = Time.unscaledTime;
        if (hull.Pointer != _silVehicle)
        {
            _silVehicle = hull.Pointer;
            _silSince = now;
            _silPass = 0;
            _building = null;
            Release(_shown);
            _shown = null;
        }
        if (_building == null && _silPass < 2 && now - _silSince >= (_silPass == 0 ? 1f : 6f))
        {
            
            _building = new Silhouette(hull.Pointer, hull, Silhouette.MainTurret(hull, PlayerTracker.Cannon),
                TrackHalf - TrackStroke * 0.5f - 2f, 1.4f * SchemeBox);
            _silPass++;
        }
        if (_building != null && _building.Step(3f))
        {
            var b = _building;
            _building = null;
            if (b.Ready && b.HullTex != null)
            {
                Release(_shown);
                _shown = b;
                Plugin.Log.LogInfo($"Tank panel: silhouette from {b.RendererCount} renderer(s) ({b.BakedOriginals} merged-away originals; " +
                                   $"skipped {b.SkippedCombined} combined/shadow, {b.SkippedHidden} hidden, {b.SkippedCrew} crew, {b.SkippedFar} far) — " +
                                   $"{b.Triangles} triangle(s), {b.Boxes} box(es), scale {b.Scale:0.0} px/m; " +
                                   $"hull {b.HullMax.x - b.HullMin.x:0.0} × {b.HullMax.y - b.HullMin.y:0.0} m, " +
                                   (b.TurretTex != null ? $"turret {b.TurretMax.x - b.TurretMin.x:0.0} × {b.TurretMax.y - b.TurretMin.y:0.0} m " +
                                                          $"({b.TurretByTransform} renderer(s) under the turret, {b.TurretByParts} by part hierarchy)"
                                       : b.Turret != null ? "turret found but no renderers on it — drawn with the hull" : "no turret"));
            }
            else Plugin.Log.LogWarning("Tank panel: vehicle silhouette could not be built — simple outline shown");
        }
        var s0 = _shown;
        if (s0 == null || !s0.Ready || s0.HullTex == null || s0.Hull == null || s0.Hull.WasCollected) return false;

        _hull.enabled = _turret.enabled = _barrel.enabled = false;
        
        var s = s0.Scale;
        var turret = s0.TurretTex != null && s0.Turret != null && !s0.Turret.WasCollected;
        var origin = s0.TurretOrigin();

        _silHull.enabled = true;
        _silHull.texture = s0.HullTex;
        var hr = _silHull.rectTransform;
        hr.pivot = new Vector2(0.5f, 0.5f);
        hr.anchoredPosition = RingCenter;
        hr.sizeDelta = new Vector2(s0.HullSpan * s, s0.HullSpan * s);

        _silTurret.enabled = turret;
        if (turret)
        {
            _silTurret.texture = s0.TurretTex;
            var tr = _silTurret.rectTransform;
            
            tr.pivot = new Vector2((0f - (s0.TurretCenter.x - s0.TurretSpan * 0.5f)) / s0.TurretSpan,
                                   (0f - (s0.TurretCenter.y - s0.TurretSpan * 0.5f)) / s0.TurretSpan);
            tr.anchoredPosition = RingCenter + (origin - s0.HullCenter) * s;
            tr.sizeDelta = new Vector2(s0.TurretSpan * s, s0.TurretSpan * s);
            
            tr.localEulerAngles = new Vector3(0f, 0f, -Mathf.DeltaAngle(s0.Yaw0, s0.TurretYaw()));
        }
        return true;
    }

    private static void Release(Silhouette s)
    {
        if (s == null) return;
        try { if (s.HullTex != null && !s.HullTex.WasCollected) Object.Destroy(s.HullTex); } catch { }
        try { if (s.TurretTex != null && !s.TurretTex.WasCollected) Object.Destroy(s.TurretTex); } catch { }
    }

    

    private void UpdateSegments()
    {
        var data = HudBridge.ApsSegments() ?? Array.Empty<float>();
        var n = data.Length / 5;
        
        var key = n.ToString();
        for (var i = 0; i < n; i++)
            key += $"|{Mathf.Round(data[i * 5] * 2f)},{Mathf.Round(data[i * 5 + 1] * 2f)},{data[i * 5 + 2]},{data[i * 5 + 4]}";
        if (key != _ringKey)
        {
            _ringKey = key;
            DrawRing(data, n);
        }
        
        
        var groups = ChargeGroups(data, n, HudBridge.ApsSegmentFrames());
        while (_labels.Count < groups.Count) _labels.Add(NewText("Charges", _root, 15f, TextAlignmentOptions.Center, Color.white, new Vector2(30f, 20f)));
        var placed = new List<(Vector2 Pos, bool Inner)>();
        for (var i = 0; i < _labels.Count; i++)
        {
            var t = _labels[i];
            if (i >= groups.Count) { if (t.gameObject.activeSelf) t.gameObject.SetActive(false); continue; }
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            var g = groups[i];
            var dir = new Vector2(Mathf.Sin(g.Angle * Mathf.Deg2Rad), Mathf.Cos(g.Angle * Mathf.Deg2Rad));
            
            var inset = g.Inner ? 33f : 15f;
            var pos = dir * (BoundaryDistance(dir) - TrackStroke * 0.5f - inset);
            
            
            foreach (var p in placed)
            {
                var d = (p.Pos - pos).magnitude;
                if (p.Inner == g.Inner && d < 16f) pos = dir * (pos.magnitude - (16f - d));
            }
            placed.Add((pos, g.Inner));
            t.rectTransform.anchoredPosition = RingCenter + pos;
            t.text = g.Charges.ToString();
            t.color = g.Working ? Color.white : new Color(1f, 0.6f, 0.55f, 1f);
        }
    }

    
    
    
    
    
    
    
    private struct ChargeGroup
    {
        public float Angle;     
        public int Charges;     
        public bool Working;
        public bool Inner;      
    }

    
    private readonly Dictionary<int, float> _frontOf = new();
    private IntPtr _frontVehicle;
    private float _frontAt = -100f;

    private List<ChargeGroup> ChargeGroups(float[] data, int n, float[] frames)
    {
        var groups = new List<ChargeGroup>();
        if (n == 0) return groups;
        var hasFrames = frames != null && frames.Length == n * 2;
        var family = new int[n];
        var frameAng = new float[n];
        var hullAng = new float[n];
        for (var i = 0; i < n; i++)
        {
            hullAng[i] = Mathf.Repeat(data[i * 5], 360f);
            family[i] = hasFrames ? (int)frames[i * 2] : 0;
            frameAng[i] = hasFrames ? Mathf.Repeat(frames[i * 2 + 1], 360f) : hullAng[i];
        }

        
        var hull = HullOf(PlayerTracker.VehicleGo);
        var now = Time.unscaledTime;
        if (hull.Pointer != _frontVehicle || now - _frontAt > 1f || now < _frontAt)
        {
            _frontVehicle = hull.Pointer;
            _frontOf.Clear();
        }
        _frontAt = now;
        float? gun = null;

        var families = new List<int>();
        foreach (var f in family) if (!families.Contains(f)) families.Add(f);
        families.Sort();
        foreach (var f in families)
        {
            
            float ox = 0f, oy = 0f;
            for (var i = 0; i < n; i++)
            {
                if (family[i] != f) continue;
                var d = (hullAng[i] - frameAng[i]) * Mathf.Deg2Rad;
                ox += Mathf.Sin(d);
                oy += Mathf.Cos(d);
            }
            var offset = Mathf.Atan2(ox, oy) * Mathf.Rad2Deg;
            var front = 0f;
            if (f != 0 && !_frontOf.TryGetValue(f, out front))
            {
                gun ??= GunYaw(hull);
                front = 0f;
                if (gun.HasValue) _frontOf[f] = front = Mathf.Repeat(gun.Value - offset, 360f);
            }

            var charges = new int[4];
            var all = new int[4];
            var count = new int[4];
            var working = new bool[4];
            for (var i = 0; i < n; i++)
            {
                if (family[i] != f) continue;
                var side = 0;
                if (data[i * 5 + 1] < 359f)
                {
                    var rel = Mathf.Repeat(frameAng[i] - front, 360f);
                    if (Mathf.Abs(Mathf.DeltaAngle(rel, 0f)) <= 45.5f) side = 0;
                    else if (Mathf.Abs(Mathf.DeltaAngle(rel, 180f)) <= 45.5f) side = 2;
                    else side = rel < 180f ? 1 : 3;
                }
                var left = (int)data[i * 5 + 2];
                count[side]++;
                all[side] += left;
                if (data[i * 5 + 4] > 0.5f) { working[side] = true; charges[side] += left; }
            }
            for (var side = 0; side < 4; side++)
            {
                if (count[side] == 0) continue;
                groups.Add(new ChargeGroup
                {
                    Angle = Mathf.Repeat(offset + front + side * 90f, 360f),
                    Charges = working[side] ? charges[side] : all[side],
                    Working = working[side],
                    Inner = f != 0
                });
            }
        }
        return groups;
    }

    
    private static float? GunYaw(Transform hull)
    {
        try
        {
            var cannon = PlayerTracker.Cannon;
            if (cannon == null || cannon.WasCollected) return null;
            Vector3 dir;
            var barrel = cannon.Barrel?.TryCast<CannonBarrelModel>();
            if (barrel != null && (barrel.transform.TransformPoint(barrel.fireLocalPosition) - barrel.transform.position).sqrMagnitude > 0.04f)
                dir = barrel.transform.TransformPoint(barrel.fireLocalPosition) - barrel.transform.position;
            else
                dir = (cannon.Behaviour != null ? cannon.Behaviour.transform : cannon.transform).forward;
            var local = hull.InverseTransformDirection(dir);
            if (local.x * local.x + local.z * local.z < 1e-4f) return null;
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }
        catch { return null; }
    }

    private void DrawRing(float[] data, int n)
    {
        var baseCol = new Color32(255, 255, 255, 30);
        for (var j = 0; j < _ringIdx.Length; j++)
        {
            var ang = _ringAng[j];
            var state = 0;   
            for (var i = 0; i < n; i++)
            {
                var width = data[i * 5 + 1];
                var start = data[i * 5] - width * 0.5f;
                if (width < 359f && Mathf.Repeat(ang - start, 360f) > width) continue;
                state = Math.Max(state, data[i * 5 + 4] > 0.5f ? 2 : 1);
                if (state == 2) break;
            }
            var cov = _ringCov[j];
            Color32 c;
            if (state == 0) c = new Color32(baseCol.r, baseCol.g, baseCol.b, (byte)(baseCol.a * cov));
            else
            {
                var col = state == 2 ? Green : Red;
                c = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), (byte)(240 * cov));
            }
            _ringBuf[_ringIdx[j]] = c;
        }
        _ringTex.SetPixels32(_ringBuf);
        _ringTex.Apply(false);
    }

    
    private static float Sdf(Vector2 p) => p.magnitude - TrackHalf;

    
    private static float BoundaryDistance(Vector2 dir)
    {
        float lo = 0f, hi = TrackHalf * 1.5f;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (Sdf(dir * mid) < 0f) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    
    private static float RayToTrack(Vector2 from, Vector2 dir)
    {
        float lo = 0f, hi = TrackHalf * 3f;
        if (Sdf(from) >= 0f) return 0f;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (Sdf(from + dir * mid) < 0f) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    private static Vector2 Normal(Vector2 p)
    {
        const float e = 0.25f;
        var n = new Vector2(Sdf(p + new Vector2(e, 0f)) - Sdf(p - new Vector2(e, 0f)), Sdf(p + new Vector2(0f, e)) - Sdf(p - new Vector2(0f, e)));
        return n.sqrMagnitude > 1e-8f ? n.normalized : p.normalized;
    }

    
    private void BuildRingTexture()
    {
        var k = Disp / Tex;
        var idx = new List<int>();
        var cov = new List<float>();
        var ang = new List<float>();
        for (var y = 0; y < Tex; y++)
        for (var x = 0; x < Tex; x++)
        {
            var p = new Vector2((x + 0.5f) * k - Disp * 0.5f, (y + 0.5f) * k - Disp * 0.5f);
            var c = Mathf.Clamp01((TrackStroke * 0.5f - Mathf.Abs(Sdf(p))) / k + 0.5f);
            if (c <= 0f) continue;
            idx.Add(y * Tex + x);
            cov.Add(c);
            ang.Add(Mathf.Repeat(Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg, 360f));
        }
        _ringIdx = idx.ToArray();
        _ringCov = cov.ToArray();
        _ringAng = ang.ToArray();
        _ringTex = NewTexture(Tex, Tex);
        _ringBuf = new Il2CppStructArray<Color32>(Tex * Tex);
        for (var i = 0; i < _ringBuf.Length; i++) _ringBuf[i] = new Color32(255, 255, 255, 0);
    }

    
    private Texture2D BuildTicksTexture()
    {
        var k = Disp / Tex;
        var px = new Color32[Tex * Tex];
        for (var deg = 0; deg < 360; deg += 10)
        {
            var dir = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), Mathf.Cos(deg * Mathf.Deg2Rad));
            var p = dir * BoundaryDistance(dir);
            var n = Normal(p);
            var major = deg % 90 == 0;
            var mid = deg % 30 == 0;
            var len = deg == 0 ? 11f : major ? 9f : mid ? 6f : 3.5f;
            var th = deg == 0 ? 3f : major ? 2.2f : mid ? 1.8f : 1.4f;
            var col = deg == 0 ? Gold : new Color(1f, 1f, 1f, major ? 0.8f : mid ? 0.6f : 0.4f);
            var a = p + n * (TrackStroke * 0.5f + 2f);
            var b = a + n * len;
            Stamp(px, a, b, th, col, k);
        }
        var t = NewTexture(Tex, Tex);
        t.SetPixels32(px);
        t.Apply(false);
        return t;
    }

    
    private static void Stamp(Color32[] px, Vector2 a, Vector2 b, float thickness, Color col, float k)
    {
        var half = Disp * 0.5f;
        var minX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - thickness + half) / k));
        var maxX = Mathf.Min(Tex - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + thickness + half) / k));
        var minY = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - thickness + half) / k));
        var maxY = Mathf.Min(Tex - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + thickness + half) / k));
        var ab = b - a;
        var l2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var p = new Vector2((x + 0.5f) * k - half, (y + 0.5f) * k - half);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            var d = (p - (a + ab * t)).magnitude;
            var c = Mathf.Clamp01((thickness * 0.5f - d) / k + 0.5f);
            if (c <= 0f) continue;
            var i = y * Tex + x;
            var alpha = (byte)(255 * col.a * c);
            if (alpha <= px[i].a) continue;
            px[i] = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), alpha);
        }
    }

    

    private static VehicleComponentHealthDisplayer _displayer;
    private static bool _vanillaHidden;

    [HarmonyPatch(typeof(VehicleComponentHealthDisplayer), nameof(VehicleComponentHealthDisplayer.SetTargetVehicle))]
    private static class DisplayerPatch
    {
        private static void Postfix(VehicleComponentHealthDisplayer __instance) => _displayer = __instance;
    }

    [HarmonyPatch(typeof(VehicleComponentHealthDisplayer), nameof(VehicleComponentHealthDisplayer.OnComponentHealthChange))]
    private static class DisplayerChangePatch
    {
        private static void Postfix(VehicleComponentHealthDisplayer __instance) => _displayer = __instance;
    }

    
    private static void ShowVanillaDamage(bool show)
    {
        try
        {
            var d = _displayer != null && !_displayer.WasCollected ? _displayer.display : null;
            var go = d != null && !d.WasCollected ? d.gameObject : null;
            if (go == null) return;
            if (!show)
            {
                if (go.activeSelf) { go.SetActive(false); _vanillaHidden = true; }
            }
            else if (_vanillaHidden)
            {
                go.SetActive(true);
                _vanillaHidden = false;
            }
        }
        catch { }
    }

    private readonly int[] _partState = new int[(int)Part.Count];   
    private int _crewAlive, _crewTotal;
    private float _nextDamage;
    private static readonly Dictionary<IntPtr, int> Category = new();  
    private IntPtr _loggedVehicle;

    private void UpdateDamage()
    {
        var now = Time.unscaledTime;
        if (now >= _nextDamage)
        {
            _nextDamage = now + 0.25f;
            ReadDamage();
        }
        for (var i = 0; i < (int)Part.Count; i++)
        {
            var img = _parts[i];
            if (img == null) continue;
            img.color = _partState[i] switch { 2 => Red, 1 => Amber, _ => IconOk };
        }
        _crewText.text = _crewTotal > 0 ? _crewAlive.ToString() : "-";
        _crewText.color = _crewTotal > 0 && _crewAlive < _crewTotal ? (_crewAlive <= 1 ? Red : Amber) : Color.white;
    }

    private void ReadDamage()
    {
        for (var i = 0; i < _partState.Length; i++) _partState[i] = 0;
        _crewAlive = _crewTotal = 0;
        try
        {
            var health = _displayer != null && !_displayer.WasCollected ? _displayer.health : null;
            var pools = health?.ComponentHealthPools;
            if (pools == null) return;
            var count = pools.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<VehicleComponentHealth>>().Count;
            var counts = new int[(int)Part.Count];
            for (var i = 0; i < count; i++)
            {
                var e = pools[i];
                var owner = e?.Owner;
                var pool = e?.HealthPool;
                if (owner == null || pool == null) continue;
                var cat = CategoryOf(owner);
                if (cat < 0) continue;
                var info = pool.HealthInfo;
                if (cat == 9)
                {
                    _crewTotal++;
                    if (info.Current > 0f) _crewAlive++;
                    continue;
                }
                counts[cat]++;
                var state = info.Current <= 0f ? 2 : info.Max > 0f && info.Current < info.Max * 0.98f ? 1 : 0;
                if (state > _partState[cat]) _partState[cat] = state;
            }
            var vehicle = PlayerTracker.VehicleGo != null ? PlayerTracker.VehicleGo.Pointer : IntPtr.Zero;
            if (vehicle != _loggedVehicle)
            {
                _loggedVehicle = vehicle;
                Plugin.Log.LogInfo($"Tank panel: {count} health pools — engine {counts[0]}, turret drive {counts[1]}, gun {counts[2]}, " +
                                   $"tracks {counts[3]}, crew {_crewTotal}");
            }
        }
        catch (Exception e)
        {
            if (_damageErrors++ < 3) Plugin.Log.LogWarning("Tank panel damage read failed: " + e.Message);
        }
    }

    private int _damageErrors;

    private static int CategoryOf(VehicleComponent owner)
    {
        if (Category.TryGetValue(owner.Pointer, out var c)) return c;
        string name;
        try { name = owner.GetIl2CppType().Name; }
        catch { name = ""; }
        c = name switch
        {
            "CombustionEngine" or "TransmissionBlock" => (int)Part.Engine,
            "TurretRing" or "TraverseMotor" or "LayingDrive" => (int)Part.Turret,
            "Cannon" or "CannonBreech" or "CannonBarrelSegment" => (int)Part.Gun,
            "TrackBelt" or "TrackAssembly" or "Suspension" or "WheelArray" or "WheelMount" or "Axle" => (int)Part.Tracks,
            "CrewSeat" => 9,
            _ => -1
        };
        if (Category.Count > 4096) Category.Clear();
        Category[owner.Pointer] = c;
        return c;
    }

    

    private void UpdateStab()
    {
        var st = HudBridge.StabState();
        if (st < 0) { if (_stab.gameObject.activeSelf) _stab.gameObject.SetActive(false); return; }
        if (!_stab.gameObject.activeSelf) _stab.gameObject.SetActive(true);
        HudBridge.TakeOverStab();
        _stab.color = st switch
        {
            3 => new Color(0.95f, 0.97f, 0.98f, 1f),
            2 => Amber,
            4 => Red,
            _ => new Color(1f, 1f, 1f, 0.28f)
        };
    }

    private void UpdateAmmo(PlayerView view, List<Launchers.GroupInfo> tubes)
    {
        string text = "-";
        var empty = false;
        if (view.Valid)
        {
            int n;
            if (view.Launcher)
            {
                n = 0;
                if (tubes != null) foreach (var t in tubes) if (t.Group == view.LauncherGroup) n += t.Ready;
            }
            else if (view.AutoVanilla) n = view.VanillaRounds;
            else n = Kind.Sum(view.Counts);
            text = n.ToString();
            empty = n == 0;
        }
        _ammoText.text = text;
        _ammoText.color = empty ? Red : Color.white;
    }

    

    private bool Ensure()
    {
        if (_root != null && !_root.WasCollected) return true;
        if (_failed) return false;
        try
        {
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null) foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { _font = f; break; }
            var wt = NewTexture(4, 4);
            var wp = new Color32[16];
            for (var i = 0; i < 16; i++) wp[i] = new Color32(255, 255, 255, 255);
            wt.SetPixels32(wp);
            wt.Apply();
            _white = Sprite.Create(wt, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            _white.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            _canvasGo = new GameObject("MunitionsTankPanel");
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            _root = Rect("Panel", _canvasGo.transform, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin), new Vector2(W, H));
            
            _bg = Img("Background", _root, RoundedSprite(64, Corner, 0f, true), PanelBg);
            _bg.type = Image.Type.Sliced;
            Stretch(_bg.rectTransform);
            _border = Img("Border", _root, RoundedSprite(64, Corner, 1.6f, false), PanelBorder);
            _border.type = Image.Type.Sliced;
            Stretch(_border.rectTransform);

            
            _ticks = Raw("Degrees", _root, BuildTicksTexture());
            Place(_ticks.rectTransform, RingCenter, new Vector2(Disp, Disp));
            BuildRingTexture();
            _ring = Raw("APS", _root, _ringTex);
            Place(_ring.rectTransform, RingCenter, new Vector2(Disp, Disp));
            DrawRing(Array.Empty<float>(), 0);

            
            _hull = Img("Hull", _root, RoundedSprite(64, 10f, 2.6f, true, 0.22f), Scheme);
            _hull.type = Image.Type.Sliced;
            Place(_hull.rectTransform, RingCenter, new Vector2(50f, 100f));
            _turret = Img("Turret", _root, CircleSprite(64, 3.2f), Scheme);
            Place(_turret.rectTransform, RingCenter, new Vector2(34f, 34f));
            _barrel = Img("Barrel", _root, _white, Scheme);
            _barrel.rectTransform.anchorMin = _barrel.rectTransform.anchorMax = Vector2.zero;
            _barrel.rectTransform.pivot = new Vector2(0.5f, 0f);
            
            _silHull = Raw("Silhouette hull", _root, null);
            Place(_silHull.rectTransform, RingCenter, new Vector2(100f, 100f));
            _silHull.color = Scheme;
            _silHull.enabled = false;
            _silTurret = Raw("Silhouette turret", _root, null);
            Place(_silTurret.rectTransform, RingCenter, new Vector2(60f, 60f));
            _silTurret.color = Scheme;
            _silTurret.enabled = false;

            
            _stab = NewText("STAB", _root, 19f, TextAlignmentOptions.Center, Color.white, new Vector2(120f, 24f));
            _stab.text = "STAB";
            _stab.rectTransform.anchoredPosition = new Vector2(RingCenter.x, H - 18f);

            
            for (var i = 0; i < (int)Part.Count; i++)
            {
                _parts[i] = Raw(PartIcons[i], _root, Icon(PartIcons[i]));
                Place(_parts[i].rectTransform, new Vector2(30f, 236f - i * 46f), new Vector2(38f, 38f));
            }

            
            _crewIcon = Raw("Crew", _root, Icon("crew.png"));
            Place(_crewIcon.rectTransform, new Vector2(30f, 28f), new Vector2(36f, 36f));
            _crewText = NewText("CrewCount", _root, 20f, TextAlignmentOptions.Left, Color.white, new Vector2(50f, 24f));
            _crewText.rectTransform.pivot = new Vector2(0f, 0.5f);
            _crewText.rectTransform.anchoredPosition = new Vector2(52f, 26f);
            _ammoIcon = Raw("Ammo", _root, Icon("icon_weapons_relocation_in_progress.png"));
            Place(_ammoIcon.rectTransform, new Vector2(W - 32f, 28f), new Vector2(38f, 38f));
            _ammoText = NewText("AmmoCount", _root, 20f, TextAlignmentOptions.Right, Color.white, new Vector2(70f, 24f));
            _ammoText.rectTransform.pivot = new Vector2(1f, 0.5f);
            _ammoText.rectTransform.anchoredPosition = new Vector2(W - 54f, 26f);
            Plugin.Log.LogInfo("Tank panel created (bottom left)");
            return true;
        }
        catch (Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning("Tank panel unavailable: " + e);
            return false;
        }
    }

    
    private static Sprite RoundedSprite(int size, float radius, float stroke, bool fill, float fillAlpha = 1f)
    {
        var px = new Color32[size * size];
        var half = size * 0.5f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
            var qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
            var d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
            var inside = Mathf.Clamp01(0.5f - d);
            var a = 0f;
            if (fill) a = inside * (stroke > 0f ? fillAlpha : 1f);
            if (stroke > 0f) a = Mathf.Max(a, inside * Mathf.Clamp01(stroke + 0.5f + d));
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        var t = NewTexture(size, size);
        t.SetPixels32(px);
        t.Apply(false);
        var b = radius + stroke + 2f;
        var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        s.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return s;
    }

    private static Sprite CircleSprite(int size, float stroke)
    {
        var px = new Color32[size * size];
        var half = size * 0.5f;
        var r = half - 1f;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude - r;
            var inside = Mathf.Clamp01(0.5f - d);
            var a = Mathf.Max(inside * 0.3f, inside * Mathf.Clamp01(stroke + 0.5f + d));
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * a));
        }
        var t = NewTexture(size, size);
        t.SetPixels32(px);
        t.Apply(false);
        var s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return s;
    }

    private static Texture2D NewTexture(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return t;
    }

    private Texture2D Icon(string file)
    {
        if (_tex.TryGetValue(file, out var t) && t != null && !t.WasCollected) return t;
        t = null;
        try
        {
            var path = Path.Combine(Plugin.PluginDir, "MDMunitions", "Hud", file);
            if (!File.Exists(path)) path = Path.Combine(Plugin.PluginDir, "Munitions", "Hud", file);
            if (!File.Exists(path)) path = Path.Combine(Plugin.PluginDir, "Hud", file);
            if (File.Exists(path))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Tank panel " + file, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (!ImageConversion.LoadImage(t, new Il2CppStructArray<byte>(File.ReadAllBytes(path)))) { Object.Destroy(t); t = null; }
                else t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            else Plugin.Log.LogWarning("Tank panel icon missing: " + path);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Tank panel icon {file} failed: {e.Message}"); }
        _tex[file] = t;
        return t;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    
    private static void Place(RectTransform rt, Vector2 center, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Image Img(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static RawImage Raw(string name, Transform parent, Texture tex)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI NewText(string name, Transform parent, float size, TextAlignmentOptions align, Color color, Vector2 box)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = box;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t.font = _font;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.richText = false;
        try
        {
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 220);
        }
        catch { }
        return t;
    }
}
