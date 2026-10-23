using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.VehicleControl;
using Sprocket.Vehicles.AttachedBehaviours;
using UnityEngine;
using UnityEngine.UI;

namespace ThermalSight;














internal static unsafe class OpticsDamage
{
    private sealed class State
    {
        public float Health = 1f;                        
        public readonly List<Vector3> Impacts = new();   
        public Texture2D Glass, Display;
        public int GlassImpacts = -1, DisplayImpacts = -1;
        public bool Thermal;
        public string Name, Vehicle;
        public int Seed;
        public ThermalDamage Gpu;                        
        public float Worst => Thermal ? Mathf.Min(Gpu.HeadHealth, Gpu.DisplayHealth) : Health;
    }

    
    private static readonly Dictionary<int, State> States = new();
    private static readonly List<(GunnerSight Sight, int Id)> Sights = new();
    private static readonly HashSet<int> Seen = new();
    private static float _nextScan;
    private static int _logged;

    
    internal static ThermalDamage Current { get; private set; }

    public static bool Destroyed(GunnerSight s) => s != null && States.TryGetValue(s.GetInstanceID(), out var st) && st.Worst <= 0f;

    

    public static void Update(VehicleControlPlayerState state)
    {
        try
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                Rescan();
            }
            var sc = state != null ? state.scopeController : null;
            State st = null;
            if (sc != null && sc.scoped && sc.activeSight != null)
            {
                var active = sc.activeSight.TryCast<GunnerSight>();
                if (active != null) States.TryGetValue(active.GetInstanceID(), out st);
            }
            Current = st != null && st.Thermal && st.Gpu.Any ? st.Gpu : null;
            Overlay.Draw(st);
        }
        catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Optics damage: " + e.Message); }
    }

    
    private static void Rescan()
    {
        Sights.Clear();
        Seen.Clear();
        foreach (var s in UnityEngine.Object.FindObjectsOfType<GunnerSight>())
        {
            if (s == null) continue;
            var id = s.GetInstanceID();
            Sights.Add((s, id));
            Seen.Add(id);
            
            if (!Seats.ContainsKey(id)) Seat(s, id);
        }
        if (Sights.Count == 0) _logged = 0;
        List<int> gone = null;
        foreach (var kv in States)
            if (!Seen.Contains(kv.Key)) (gone ??= new List<int>()).Add(kv.Key);
        if (gone != null)
            foreach (var id in gone)
            {
                Release(States[id]);
                States.Remove(id);
            }
        
        List<int> stale = null;
        foreach (var k in Seats.Keys) if (!Seen.Contains(k)) (stale ??= new List<int>()).Add(k);
        if (stale != null) foreach (var k in stale) Seats.Remove(k);
    }

    private static int _errors;

    
    
    
    
    private static bool Geometry(GunnerSight s, int id, out Vector3 lens, out Vector3 eye, out Quaternion rot)
    {
        lens = eye = default;
        rot = Quaternion.identity;
        try
        {
            var parent = s.SightParent;
            if (parent != null)
            {
                lens = parent.TransformPoint(s.ObjectiveLensLocalPosition);
                eye = parent.TransformPoint(s.EyepieceLocalPosition);
                rot = parent.rotation;
            }
            else { lens = eye = s.transform.position; rot = s.transform.rotation; }
            var seat = Seat(s, id);
            if (seat != null) eye = seat.position + seat.up * HeadHeight;
            return true;
        }
        catch { return false; }
    }

    private const float HeadHeight = 0.65f;   
    private static readonly Dictionary<int, Transform> Seats = new();

    
    private static Transform Seat(GunnerSight s, int id)
    {
        if (Seats.TryGetValue(id, out var cached))
        {
            if (cached == null) return null;
            try { if (cached) return cached; }
            catch { }
        }
        Transform best = null;
        try
        {
            var vehicle = s.GetComponentInParent<Sprocket.Vehicles.Vehicle>();
            if (vehicle != null)
            {
                var weaponId = -1;
                try
                {
                    var w = s.LinkedWeapon;
                    var cannon = w != null ? w.GetComponentInParent<Sprocket.Vehicles.Cannons.Cannon>() : null;
                    if (cannon != null) weaponId = cannon.VUID.Value;
                }
                catch { }
                var pos = s.transform.position;
                var bestScore = float.MaxValue;
                foreach (var seat in vehicle.GetComponentsInChildren<Sprocket.Vehicles.CrewSystems.CrewSeat>(true))
                {
                    if (seat == null) continue;
                    var dist = Vector3.Distance(seat.transform.position, pos);
                    var score = dist;
                    if (weaponId >= 0)
                    {
                        try
                        {
                            var ids = seat.SeatBlueprint?.OperatedBehaviourIDs;
                            if (ids != null)
                                for (var i = 0; i < ids.Length; i++)
                                    if (ids[i].Value == weaponId) { score -= 100f; break; }
                        }
                        catch { }
                    }
                    else if (dist > 3f) continue;
                    if (score < bestScore) { bestScore = score; best = seat.transform; }
                }
            }
        }
        catch { }
        Seats[id] = best;
        if (Plugin.DebugLogging.Value)
            Plugin.Log.LogInfo(best != null ? $"Optics: '{s.gameObject.name}' display at the crew seat '{best.name}'" : $"Optics: '{s.gameObject.name}' has no crew seat — display at the sight");
        return best;
    }

    private static string VehicleName(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.GetComponent<Sprocket.Vehicles.Vehicle>() != null) return t.name;
        return "?";
    }

    

    
    
    
    
    
    
    
    private static float HeadDamage(float impact, float path, float radius, float speed, bool blast)
    {
        if (blast) return impact < 6f ? 0.45f * (1f - impact / 6f) : 0f;
        if (path < radius && impact < 3f) return speed > 900f ? 0.5f : 0.35f;
        if (impact < 0.6f) return 0.15f * (0.6f - impact) / 0.6f;
        return 0f;
    }

    
    private static float PathDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var len2 = ab.sqrMagnitude;
        if (len2 < 1e-8f) return Vector3.Distance(p, a);
        var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }

    
    private static float HeadRadius(GunnerSight s)
    {
        try
        {
            var sc = s.transform.lossyScale;
            return 0.22f * Mathf.Clamp(Mathf.Max(Mathf.Abs(sc.x), Mathf.Max(Mathf.Abs(sc.y), Mathf.Abs(sc.z))), 0.5f, 3f);
        }
        catch { return 0.22f; }
    }

    
    
    private static float InnerDamage(Vector3 point, Vector3 eye, Vector3 dir, bool blast, bool penetrated)
    {
        var v = eye - point;
        var dist = v.magnitude;
        if (blast) return dist < 3f ? 0.5f * (1f - dist / 3f) : 0f;
        if (!penetrated) return dist < 0.3f ? 0.12f : 0f;
        if (dist > 3.2f) return 0f;
        if (dist < 0.05f) return 1f;
        var along = Vector3.Dot(v, dir);
        if (along > -0.2f && (v - dir * along).magnitude < 0.3f) return 0.9f;
        var cone = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.92f, along / dist));
        return 0.85f * cone * Mathf.Pow(1f - dist / 3.2f, 1.3f);
    }

    
    private static Vector2 PlaceOnGlass(Vector3 point, Vector3 origin, Quaternion rot)
    {
        var local = Quaternion.Inverse(rot) * (point - origin);
        var uv = new Vector2(local.x, local.y);
        uv = uv.sqrMagnitude > 1e-4f ? uv.normalized * Mathf.Min(0.33f, 0.12f + uv.magnitude * 0.12f) : Vector2.zero;
        uv += new Vector2(UnityEngine.Random.Range(-0.08f, 0.08f), UnityEngine.Random.Range(-0.08f, 0.08f));
        return new Vector2(Mathf.Clamp(uv.x, -0.4f, 0.4f), Mathf.Clamp(uv.y, -0.4f, 0.4f));
    }

    private static void Hit(Vector3 point, Vector3 from, Vector3 velocity, bool blast, bool penetrated)
    {
        if (Plugin.OpticsDamage == null || !Plugin.OpticsDamage.Value) return;
        var speed = velocity.magnitude;
        var dir = speed > 1f ? velocity / speed : Vector3.zero;
        foreach (var (sight, id) in Sights)
        {
            if (sight == null || sight.WasCollected || !Geometry(sight, id, out var lens, out var eye, out var rot)) continue;
            var dLens = Vector3.Distance(point, lens);
            var path = blast ? float.MaxValue : PathDistance(lens, from, point);
            var head = HeadDamage(dLens, path, HeadRadius(sight), speed, blast);
            var inner = dir == Vector3.zero && !blast ? 0f : InnerDamage(point, eye, dir, blast, penetrated);
            if (head < 0.02f && inner < 0.02f) continue;

            if (!States.TryGetValue(id, out var st))
            {
                var thermal = ThermalController.SightConfigOf(sight).Gen != 0;
                st = new State
                {
                    Name = sight.gameObject.name, Vehicle = VehicleName(sight.transform), Thermal = thermal,
                    Seed = unchecked(id * 7919 ^ Environment.TickCount), Gpu = thermal ? new ThermalDamage() : null
                };
                States[id] = st;
            }
            if (st.Worst <= 0f && (!st.Thermal || (st.Gpu.HeadHealth <= 0f && st.Gpu.DisplayHealth <= 0f))) continue;

            string what;
            if (st.Thermal)
            {
                var g = st.Gpu;
                var parts = new List<string>(2);
                if (head >= 0.02f && g.HeadHealth > 0f)
                {
                    var uv = PlaceOnGlass(point, lens, rot);
                    g.AddHead(uv + new Vector2(0.5f, 0.5f), head * 1.6f);
                    g.HeadHealth = Mathf.Max(0f, g.HeadHealth - head);
                    st.Impacts.Add(new Vector3(uv.x, uv.y, Mathf.Clamp01(head * 1.6f)));
                    parts.Add($"sight head {g.HeadHealth * 100f:0}%");
                }
                if (inner >= 0.02f && g.DisplayHealth > 0f)
                {
                    var uv = PlaceOnGlass(point, eye, rot);
                    g.AddDisplay(uv + new Vector2(0.5f, 0.5f), inner * 1.4f);
                    g.DisplayHealth = Mathf.Max(0f, g.DisplayHealth - inner);
                    st.Impacts.Add(new Vector3(uv.x, uv.y, Mathf.Clamp01(inner * 1.4f)));
                    parts.Add($"display {g.DisplayHealth * 100f:0}%");
                }
                if (parts.Count == 0) continue;
                what = string.Join(", ", parts) + " left";
            }
            else
            {
                
                var dmg = Mathf.Max(head, inner * 0.8f);
                if (dmg < 0.02f || st.Health <= 0f) continue;
                var uv = head >= inner * 0.8f ? PlaceOnGlass(point, lens, rot) : PlaceOnGlass(point, eye, rot);
                st.Impacts.Add(new Vector3(uv.x, uv.y, Mathf.Clamp01(dmg * 1.6f)));
                st.Health = Mathf.Max(0f, st.Health - dmg);
                what = st.Health <= 0f ? "destroyed" : $"{st.Health * 100f:0}% left";
            }
            
            if (_logged++ < 60)
                Plugin.Log.LogInfo($"Optics: '{st.Name}' on '{st.Vehicle}' hit by {(blast ? "an explosion" : penetrated ? "a penetration" : "a hit")} " +
                                   $"{dLens:0.0} m from the lens{(path < 50f ? $" (shot passed {path:0.00} m from it)" : "")}, " +
                                   $"{Vector3.Distance(point, eye):0.0} m from the gunner's eyepiece — {what}");
        }
    }

    
    private const int ElemSize = 0x50, ElemBase = 0x20, PoolOffset = 0x38;
    private const int EEvent = 0x10, EId = 0x18, ESpawn = 0x1C, EPos = 0x28, EVel = 0x34, EFlags = 0x48;
    
    private static readonly Dictionary<int, (IntPtr Event, Vector3 Velocity, Vector3 Pos)> Tracked = new();
    private static readonly HashSet<int> Alive = new();
    private static readonly List<int> Gone = new();

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class HitScan
    {
        private static void Postfix(ProjectileRegister __instance)
        {
            if (Sights.Count == 0 || Plugin.OpticsDamage == null || !Plugin.OpticsDamage.Value) return;
            try { Scan(__instance); }
            catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Optics hit scan: " + e.Message); }
        }
    }

    private static void Scan(ProjectileRegister register)
    {
        var pool = *(IntPtr*)(register.Pointer + PoolOffset);
        if (pool == IntPtr.Zero) return;
        var len = *(int*)(pool + 0x18);
        Alive.Clear();
        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            if ((*(int*)(e + EFlags) & 1) == 0) continue;
            var id = *(int*)(e + EId);
            Alive.Add(id);
            var ev = *(IntPtr*)(e + EEvent);
            var vel = *(Vector3*)(e + EVel);
            var pos = *(Vector3*)(e + EPos);
            if (!Tracked.TryGetValue(id, out var t))
            {
                Tracked[id] = (ev, vel, pos);
                if (ev != IntPtr.Zero) OnEvent(ev, vel, *(Vector3*)(e + ESpawn));
                continue;
            }
            
            if (ev != IntPtr.Zero && ev != t.Event) OnEvent(ev, t.Velocity, t.Pos);
            Tracked[id] = (ev, vel, pos);
        }
        Gone.Clear();
        foreach (var kv in Tracked) if (!Alive.Contains(kv.Key)) Gone.Add(kv.Key);
        if (Gone.Count == 0) return;
        var history = register.projectileHistory;
        foreach (var id in Gone)
        {
            var t = Tracked[id];
            Tracked.Remove(id);
            try
            {
                if (history != null && history.TryGetValue(id, out var last) && last != null && last.Pointer != t.Event) OnEvent(last.Pointer, t.Velocity, t.Pos);
            }
            catch { }
        }
        if (Tracked.Count > 4096) Tracked.Clear();
    }

    private static void OnEvent(IntPtr ptr, Vector3 velocity, Vector3 from)
    {
        var ev = new ProjectileEvent(ptr);
        var dm = ev.TryCast<HitDamageModelProjectileEvent>();
        if (dm != null)
        {
            var penetrated = ((int)dm.Type & (int)HitDamageModelProjectileEvent.HitType.Penetrated) != 0;
            Hit(dm.Position, from, velocity, false, penetrated);
            return;
        }
        var gr = ev.TryCast<HitGroundProjectileEvent>();
        if (gr != null) Hit(gr.Position, from, velocity, false, false);
    }

    
    [HarmonyPatch(typeof(Sprocket.Gameplay.ExplosionEffects), nameof(Sprocket.Gameplay.ExplosionEffects.Play), new[] { typeof(float) })]
    private static class ExplosionPatch
    {
        private static void Postfix(Sprocket.Gameplay.ExplosionEffects __instance)
        {
            if (Sights.Count == 0) return;
            try { var p = __instance.transform.position; Hit(p, p, Vector3.zero, true, false); }
            catch { }
        }
    }

    private static void Release(State st)
    {
        if (st.Glass != null) UnityEngine.Object.Destroy(st.Glass);
        if (st.Display != null) UnityEngine.Object.Destroy(st.Display);
        st.Glass = st.Display = null;
        st.Gpu?.Release();
        if (Current == st.Gpu) Current = null;
    }

    

    private static class Overlay
    {
        private static GameObject _go;
        private static RawImage _image;
        private static readonly Image[] Bars = new Image[5];
        private static TMPro.TextMeshProUGUI _text;
        private static float _nextGlitch;

        private enum Mode { Glass, CpuDisplay, Shader }

        public static void Draw(State st)
        {
            if (st == null || st.Worst >= 0.999f || !Place(st, out var rect, out var mode))
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
                return;
            }
            Ensure();
            if (!_go.activeSelf) _go.SetActive(true);
            if (mode == Mode.Shader)
            {
                
                _image.enabled = false;
                _text.text = st.Gpu.HeadHealth <= 0f && st.Gpu.DisplayHealth > 0f ? "NO SIGNAL" : "";
                _text.rectTransform.anchoredPosition = rect.center;
                Glitch(false, st, rect);
                return;
            }
            _image.enabled = true;
            var display = mode == Mode.CpuDisplay;
            var tex = display ? DisplayTexture(st) : GlassTexture(st);
            if (_image.texture == null || _image.texture.Pointer != tex.Pointer) _image.texture = tex;
            var rt = _image.rectTransform;
            rt.anchoredPosition = rect.position;
            rt.sizeDelta = rect.size;
            _text.text = st.Worst <= 0f ? (display ? "NO SIGNAL" : "") : "";
            _text.rectTransform.anchoredPosition = rect.center;
            Glitch(display, st, rect);
        }

        
        private static bool Place(State st, out Rect rect, out Mode mode)
        {
            mode = Mode.Glass;
            var view = ThermalController.ActiveView;
            if (view != null)
            {
                
                
                if (view is not NightVisionView && st.Thermal)
                    mode = view is GpuThermalView && GpuResources.DisplayDamageShader != null ? Mode.Shader : Mode.CpuDisplay;
                return view.ScreenRect(out rect);
            }
            rect = default;
            if (!Reticles.LensCircle(out var centre, out var radius)) return false;
            var side = 2f * radius * Screen.height;
            rect = new Rect(centre.x * Screen.width - side * 0.5f, centre.y * Screen.height - side * 0.5f, side, side);
            return true;
        }

        private static void Ensure()
        {
            if (_go != null) return;
            _go = new GameObject("OpticsDamageOverlay");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            var canvas = _go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Plugin.CanvasSortingOrder.Value + 10;   
            _image = new GameObject("Damage").AddComponent<RawImage>();
            _image.transform.SetParent(_go.transform, false);
            _image.raycastTarget = false;
            Pixels(_image.rectTransform);
            for (var i = 0; i < Bars.Length; i++)
            {
                Bars[i] = new GameObject("Glitch").AddComponent<Image>();
                Bars[i].transform.SetParent(_go.transform, false);
                Bars[i].raycastTarget = false;
                Pixels(Bars[i].rectTransform);
                Bars[i].gameObject.SetActive(false);
            }
            _text = new GameObject("DamageText").AddComponent<TMPro.TextMeshProUGUI>();
            _text.transform.SetParent(_go.transform, false);
            var font = TMPro.TMP_Settings.defaultFontAsset;
            if (font == null)
                foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) { font = f; break; }
            if (font != null) _text.font = font;
            _text.fontSize = 34;
            _text.color = new Color(0.9f, 0.9f, 0.9f, 0.55f);
            _text.alignment = TMPro.TextAlignmentOptions.Center;
            _text.raycastTarget = false;
            var trt = _text.rectTransform;
            trt.anchorMin = trt.anchorMax = Vector2.zero;
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(600, 80);
        }

        
        private static void Pixels(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
        }

        
        private static void Glitch(bool display, State st, Rect rect)
        {
            var now = Time.unscaledTime;
            if (!display) { foreach (var b in Bars) if (b.gameObject.activeSelf) b.gameObject.SetActive(false); _image.color = Color.white; return; }
            if (now < _nextGlitch) return;
            var broken = 1f - st.Worst;
            _nextGlitch = now + UnityEngine.Random.Range(0.04f, 0.25f);
            for (var i = 0; i < Bars.Length; i++)
            {
                var on = UnityEngine.Random.value < 0.25f + 0.5f * broken && i < 2 + (int)(broken * 3f);
                Bars[i].gameObject.SetActive(on);
                if (!on) continue;
                var h = UnityEngine.Random.Range(2f, 6f + 24f * broken);
                var rt = Bars[i].rectTransform;
                rt.anchoredPosition = new Vector2(rect.x, rect.y + UnityEngine.Random.value * (rect.height - h));
                rt.sizeDelta = new Vector2(rect.width, h);
                var g = UnityEngine.Random.value;
                Bars[i].color = g < 0.4f ? new Color(0f, 0f, 0f, 0.85f) : g < 0.7f ? new Color(1f, 1f, 1f, 0.35f) :
                    new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, 0.4f);
            }
            _image.color = UnityEngine.Random.value < 0.06f * broken ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
        }
    }

    

    private static Texture2D GlassTexture(State st)
    {
        if (st.Glass != null && st.GlassImpacts == st.Impacts.Count) return st.Glass;
        if (st.Glass != null) UnityEngine.Object.Destroy(st.Glass);
        st.Glass = Painter.Glass(st.Impacts, st.Worst, st.Seed);
        st.GlassImpacts = st.Impacts.Count;
        return st.Glass;
    }

    private static Texture2D DisplayTexture(State st)
    {
        if (st.Display != null && st.DisplayImpacts == st.Impacts.Count) return st.Display;
        if (st.Display != null) UnityEngine.Object.Destroy(st.Display);
        st.Display = Painter.Display(st.Impacts, st.Worst, st.Seed);
        st.DisplayImpacts = st.Impacts.Count;
        return st.Display;
    }

    internal static class Painter
    {
        private static float[] _white, _dark;
        private static int _w, _h;

        
        internal static float[] White => _white;
        internal static float[] Dark => _dark;

        
        
        public static Texture2D Glass(List<Vector3> impacts, float health, int seed)
        {
            const int size = 512;
            Begin(size, size);
            var rng = new System.Random(seed ^ impacts.Count * 7919);
            foreach (var imp in impacts) Impact(rng, new Vector2((imp.x + 0.5f) * size, (imp.y + 0.5f) * size), imp.z, size, 1f);
            if (health <= 0f)
            {
                for (var i = 0; i < 6; i++)
                    Impact(rng, new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size), 0.9f, size, 0.8f);
                for (var i = 0; i < _white.Length; i++)
                {
                    var n = (float)rng.NextDouble();
                    _white[i] += 0.42f + 0.18f * n;
                    _dark[i] += 0.18f * n * n;
                }
            }
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = y * size + x;
                var dx = (x + 0.5f) / size - 0.5f;
                var dy = (y + 0.5f) / size - 0.5f;
                var mask = Mathf.Clamp01((0.5f - Mathf.Sqrt(dx * dx + dy * dy)) * 60f);   
                var w = Mathf.Min(_white[i], 1f);
                var d = Mathf.Min(_dark[i], 1f);
                var a = Mathf.Clamp01(w + d) * mask;
                if (a <= 0.003f) continue;
                var l = Mathf.Clamp01(w / Mathf.Max(w + d, 1e-4f)) * 235f + 10f;
                px[i] = new Color32((byte)l, (byte)l, (byte)Mathf.Min(255f, l + 6f), (byte)(a * 255f));
            }
            return Finish(px, size, size);
        }

        
        public static Texture2D Display(List<Vector3> impacts, float health, int seed)
        {
            const int w = 768, h = 384;
            Begin(w, h);
            var rng = new System.Random(seed ^ impacts.Count * 104729);
            var px = new Color32[w * h];
            var broken = 1f - health;
            foreach (var imp in impacts)
            {
                var c = new Vector2((imp.x + 0.5f) * w, (imp.y + 0.5f) * h);
                Blot(px, w, h, rng, c, h * (0.08f + 0.22f * imp.z));
                Impact(rng, c, imp.z * 0.6f, h, 0.7f);
                
                var columns = 1 + (int)(imp.z * 5f);
                for (var k = 0; k < columns; k++)
                {
                    var x = Mathf.Clamp((int)(c.x + (rng.NextDouble() - 0.5) * h * 0.6), 0, w - 1);
                    Column(px, w, h, x, 1 + rng.Next(3), StuckColour(rng));
                }
            }
            if (broken > 0.4f) for (var k = 0; k < 1 + (int)(broken * 3f); k++) Row(px, w, h, rng.Next(h), 1 + rng.Next(2), StuckColour(rng));
            if (health <= 0f)
                for (var i = 0; i < px.Length; i++)
                    if (px[i].a < 235) px[i] = new Color32(0, 0, 0, 235);
            
            for (var i = 0; i < px.Length; i++)
            {
                var a = Mathf.Clamp01(_white[i] * 0.9f);
                if (a <= 0.01f) continue;
                var p = px[i];
                var pa = p.a / 255f;
                var oa = a + pa * (1f - a);
                var l = 225f;
                px[i] = new Color32((byte)((l * a + p.r * pa * (1f - a)) / oa), (byte)((l * a + p.g * pa * (1f - a)) / oa),
                    (byte)((l * a + p.b * pa * (1f - a)) / oa), (byte)(oa * 255f));
            }
            return Finish(px, w, h);
        }

        private static Color32 StuckColour(System.Random rng) => rng.Next(6) switch
        {
            0 => new Color32(255, 40, 220, 220),
            1 => new Color32(40, 255, 255, 220),
            2 => new Color32(60, 255, 60, 220),
            3 => new Color32(255, 255, 255, 220),
            4 => new Color32(255, 230, 40, 220),
            _ => new Color32(10, 10, 10, 240),
        };

        internal static void Begin(int w, int h)
        {
            _w = w;
            _h = h;
            if (_white == null || _white.Length != w * h) { _white = new float[w * h]; _dark = new float[w * h]; }
            else { Array.Clear(_white, 0, _white.Length); Array.Clear(_dark, 0, _dark.Length); }
        }

        private static Texture2D Finish(Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        internal static void Impact(System.Random rng, Vector2 c, float strength, int size, float scale)
        {
            strength = Mathf.Clamp01(strength);
            var reach = size * (0.18f + 0.42f * strength) * scale;
            
            var crush = size * (0.012f + 0.03f * strength) * scale;
            Disc(c, crush * 3.2f, 0.16f, 0.05f, rng);
            Disc(c, crush, 0.75f, 0.1f, rng);
            var rays = 8 + (int)(10f * strength);
            var angles = new float[rays];
            for (var i = 0; i < rays; i++)
            {
                var a = Mathf.PI * 2f * (i + (float)rng.NextDouble() * 0.6f) / rays;
                angles[i] = a;
                Crack(rng, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * crush * 0.6f, a, reach * (0.45f + 0.55f * (float)rng.NextDouble()), 1.3f, 0);
            }
            
            var rings = 1 + (int)(3f * strength);
            for (var k = 1; k <= rings; k++)
            {
                var r0 = crush * 1.5f + reach * 0.12f * k * (0.8f + 0.4f * (float)rng.NextDouble());
                for (var i = 0; i < rays; i++)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    var a0 = angles[i];
                    var a1 = angles[(i + 1) % rays] + (i + 1 == rays ? Mathf.PI * 2f : 0f);
                    var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0 * (0.9f + 0.2f * (float)rng.NextDouble());
                    var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r0 * (0.9f + 0.2f * (float)rng.NextDouble());
                    var mid = (p0 + p1) * 0.5f + (mid0(p0, p1, c)) * (float)(rng.NextDouble() * 0.15);
                    Line(p0, mid, 0.9f);
                    Line(mid, p1, 0.9f);
                }
            }
        }

        private static Vector2 mid0(Vector2 a, Vector2 b, Vector2 c) => ((a + b) * 0.5f - c);

        internal static void Crack(System.Random rng, Vector2 p, float angle, float length, float width, int depth)
        {
            const float step = 7f;
            var travelled = 0f;
            while (travelled < length)
            {
                angle += (float)(rng.NextDouble() - 0.5) * 0.36f;
                var next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * step;
                Line(p, next, width * (1f - 0.5f * travelled / length));
                p = next;
                travelled += step;
                if (depth < 2 && rng.NextDouble() < 0.045)
                    Crack(rng, p, angle + (rng.NextDouble() < 0.5 ? -1f : 1f) * (0.35f + (float)rng.NextDouble() * 0.5f),
                        (length - travelled) * 0.5f, width * 0.7f, depth + 1);
                if (p.x < 0 || p.y < 0 || p.x >= _w || p.y >= _h) break;
            }
        }

        
        internal static void Line(Vector2 a, Vector2 b, float width)
        {
            var pad = width * 3f + 1f;
            int x0 = Mathf.Max(0, (int)(Mathf.Min(a.x, b.x) - pad)), x1 = Mathf.Min(_w - 1, (int)(Mathf.Max(a.x, b.x) + pad));
            int y0 = Mathf.Max(0, (int)(Mathf.Min(a.y, b.y) - pad)), y1 = Mathf.Min(_h - 1, (int)(Mathf.Max(a.y, b.y) + pad));
            var ab = b - a;
            var len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                var d = Vector2.Distance(p, a + ab * t);
                if (d > pad) continue;
                var i = y * _w + x;
                var core = Mathf.Clamp01(1f - d / Mathf.Max(width, 0.5f));
                _white[i] = Mathf.Max(_white[i], core * 0.9f);
                var edge = Mathf.Exp(-(d * d) / (width * width * 4f));
                _dark[i] = Mathf.Max(_dark[i], edge * 0.3f);
            }
        }

        internal static void Disc(Vector2 c, float r, float white, float dark, System.Random rng)
        {
            int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(_w - 1, (int)(c.x + r));
            int y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(_h - 1, (int)(c.y + r));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / r;
                if (d > 1f) continue;
                var k = (1f - d) * (0.6f + 0.4f * (float)rng.NextDouble());
                var i = y * _w + x;
                _white[i] = Mathf.Max(_white[i], k * white);
                _dark[i] = Mathf.Max(_dark[i], k * dark);
            }
        }

        
        private static void Blot(Color32[] px, int w, int h, System.Random rng, Vector2 c, float radius)
        {
            var lobes = new float[7];
            for (var k = 0; k < lobes.Length; k++) lobes[k] = (float)rng.NextDouble() * Mathf.PI * 2f;
            var pad = radius * 1.6f;
            int x0 = Mathf.Max(0, (int)(c.x - pad)), x1 = Mathf.Min(w - 1, (int)(c.x + pad));
            int y0 = Mathf.Max(0, (int)(c.y - pad)), y1 = Mathf.Min(h - 1, (int)(c.y + pad));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var v = new Vector2(x - c.x, y - c.y);
                var ang = Mathf.Atan2(v.y, v.x);
                var edge = radius * (1f + 0.28f * Mathf.Sin(ang * 3f + lobes[0]) + 0.16f * Mathf.Sin(ang * 5f + lobes[1]) + 0.1f * Mathf.Sin(ang * 9f + lobes[2]));
                var d = v.magnitude;
                var i = y * w + x;
                if (d < edge) px[i] = new Color32(0, 0, 0, 255);
                else if (d < edge * 1.25f)
                {
                    var k = 1f - (d - edge) / (edge * 0.25f);
                    var a = (byte)(k * 220f);
                    if (a > px[i].a) px[i] = new Color32((byte)(40 * k), 0, (byte)(60 * k), a);
                }
            }
        }

        private static void Column(Color32[] px, int w, int h, int x, int width, Color32 c)
        {
            for (var dx = 0; dx < width && x + dx < w; dx++)
                for (var y = 0; y < h; y++) px[y * w + x + dx] = c;
        }

        private static void Row(Color32[] px, int w, int h, int y, int height, Color32 c)
        {
            for (var dy = 0; dy < height && y + dy < h; dy++)
                for (var x = 0; x < w; x++) px[(y + dy) * w + x] = c;
        }
    }
}
