using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Weapons.Cannons;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace ActiveProtection;








internal static class EoSystem
{
    

    private sealed class Cloud
    {
        public Vector3 Center;
        public Vector3 Drift;    
        public float Radius, HalfHeight, Born, Build, Duration;

        public Vector3 CenterAt(float now) => Center + Drift * Mathf.Max(0f, now - Born);
    }

    private const float Attenuation = 0.35f;   
    private const float FadeOutTime = 4f;
    private const float GrenadeRadius = 10f, GrenadeHalfHeight = 5.5f;   
    private static readonly List<Cloud> Clouds = new();

    private static float Density(Cloud c, float now)
    {
        var age = now - c.Born;
        if (age <= 0f) return 0f;
        var build = Mathf.Clamp01(age / Mathf.Max(0.1f, c.Build));
        build = build * build * (3f - 2f * build);
        var end = c.Build + c.Duration;
        var fade = age > end ? Mathf.Clamp01(1f - (age - end) / FadeOutTime) : 1f;
        return build * fade;
    }

    private static float RadiusAt(Cloud c, float now)
    {
        var f = Mathf.Clamp01((now - c.Born) / Mathf.Max(0.1f, c.Build));
        return c.Radius * (0.35f + 0.65f * f);
    }

    
    private static bool Chord(Cloud c, float now, Vector3 a, Vector3 b, out float s0, out float s1)
    {
        s0 = s1 = 0f;
        var r = RadiusAt(c, now);
        var k = c.Radius / Mathf.Max(0.5f, c.HalfHeight);
        var center = c.CenterAt(now);
        var pa = a - center;
        var pb = b - center;
        pa.y *= k;
        pb.y *= k;
        var d = pb - pa;
        var aa = Vector3.Dot(d, d);
        if (aa < 1e-8f) return false;
        var bb = 2f * Vector3.Dot(pa, d);
        var cc = Vector3.Dot(pa, pa) - r * r;
        var disc = bb * bb - 4f * aa * cc;
        if (disc <= 0f) return false;
        var sq = Mathf.Sqrt(disc);
        s0 = Mathf.Max(0f, (-bb - sq) / (2f * aa));
        s1 = Mathf.Min(1f, (-bb + sq) / (2f * aa));
        return s1 > s0;
    }

    
    public static bool Blocks(Vector3 a, Vector3 b)
    {
        if (Clouds.Count == 0) return false;
        var now = Time.time;
        var len = (b - a).magnitude;
        var tau = 0f;
        foreach (var c in Clouds)
        {
            var dens = Density(c, now);
            if (dens <= 0.05f || !Chord(c, now, a, b, out var s0, out var s1)) continue;
            tau += (s1 - s0) * len * Attenuation * dens;
            if (tau >= 1f) return true;
        }
        return false;
    }

    
    private static bool ScreenedSoon(Vector3 a, Vector3 b)
    {
        var len = (b - a).magnitude;
        if (len < 1f) return false;
        var tau = 0f;
        var now = Time.time;
        foreach (var c in Clouds)
        {
            if (now - c.Born > c.Build + c.Duration - 2f) continue; 
            var full = new Cloud { Center = c.CenterAt(now), Radius = c.Radius, HalfHeight = c.HalfHeight, Born = now - 100f, Build = 1f, Duration = 1000f };
            if (Chord(full, now, a, b, out var s0, out var s1)) tau += (s1 - s0) * len * Attenuation;
        }
        foreach (var gr in Grenades)
        {
            var full = new Cloud { Center = gr.Point, Radius = GrenadeRadius, HalfHeight = GrenadeHalfHeight, Born = now - 100f, Build = 1f, Duration = 1000f };
            if (Chord(full, now, a, b, out var s0, out var s1)) tau += (s1 - s0) * len * Attenuation;
        }
        return tau >= 1f;
    }

    
    public static float SmokeDistance(Vector3 origin, Vector3 dir, float max)
    {
        if (Clouds.Count == 0 || max <= 0f) return -1f;
        var now = Time.time;
        var end = origin + dir.normalized * max;
        var best = -1f;
        foreach (var c in Clouds)
        {
            var dens = Density(c, now);
            if (dens <= 0.05f || !Chord(c, now, origin, end, out var s0, out var s1)) continue;
            var depth = 1f / (Attenuation * dens);
            var chord = (s1 - s0) * max;
            if (chord < depth) continue;
            var d = s0 * max + depth;
            if (best < 0f || d < best) best = d;
        }
        return best;
    }

    
    
    [HarmonyPatch(typeof(Physics), nameof(Physics.Linecast), new[] { typeof(Vector3), typeof(Vector3), typeof(int) })]
    private static class LineOfSightPatch
    {
        private static void Postfix(Vector3 start, Vector3 end, ref bool __result)
        {
            if (__result || Clouds.Count == 0) return;
            try { if (Blocks(start, end)) __result = true; }
            catch { }
        }
    }

    

    private static bool _filterSearched;
    private static readonly Dictionary<long, bool> JamRolls = new();
    private static readonly Dictionary<long, float> BeamWarned = new();
    private static readonly System.Random Rng = new();

    private static void RegisterGuidanceFilter()
    {
        if (_filterSearched) return;
        _filterSearched = true;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name is not ("Munitions" or "MDMunitions")) continue;
                var api = asm.GetType("Munitions.Api");
                var m = api?.GetMethod("SetGuidanceFilter", BindingFlags.Public | BindingFlags.Static);
                if (m == null) continue;
                m.Invoke(null, new object[] { (Func<int, Vector3, Vector3, Vector3, int, int>)GuidanceFilter });
                Plugin.Log.LogInfo("EO countermeasures linked to Munitions missile guidance");
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Munitions guidance hook failed: " + e.Message); }
    }

    
    private static int GuidanceFilter(int id, Vector3 origin, Vector3 dir, Vector3 missile, int guide)
    {
        try
        {
            if (Clouds.Count > 0 && Blocks(origin, missile)) return 1;
            var now = Time.time;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            foreach (var g in Interceptor.AllGroups)
            {
                if (g.Warners.Count == 0 && g.Jammers.Count == 0) continue;
                if ((origin - g.Center).sqrMagnitude < g.Radius * g.Radius) continue; 
                var rel = g.Center - origin;
                var along = Vector3.Dot(rel, dir);
                if (along <= 0f || (rel - dir * along).magnitude > g.Radius + 1.5f) continue;
                var key = ((long)id << 32) ^ g.Ptr.ToInt64();
                if (guide == 0)
                {
                    
                    if (!BeamWarned.TryGetValue(key, out var last) || now - last > 1f)
                    {
                        BeamWarned[key] = now;
                        Threat(g, origin, EoThreats.Beam);
                    }
                    continue;
                }
                
                foreach (var j in g.Jammers)
                {
                    if (!j.JamOn || j.Destroyed) continue;
                    var toLauncher = origin - j.Center;
                    if (toLauncher.magnitude > j.S.Range || !j.S.InSector(j.ToLocalDir(toLauncher))) continue;
                    var jk = key ^ ((long)j.Ptr.ToInt64() << 7);
                    if (!JamRolls.TryGetValue(jk, out var jammed))
                    {
                        jammed = Rng.NextDouble() < j.S.JamChance;
                        JamRolls[jk] = jammed;
                        Plugin.Log.LogInfo($"EO: IR jammer vs SACLOS missile {(origin - j.Center).magnitude:0} m away — {(jammed ? "guidance broken" : "missile held")}");
                    }
                    if (jammed) return 2;
                }
            }
        }
        catch { }
        return 0;
    }

    

    
    public static bool WatchLaunches;
    private static readonly HashSet<int> SeenMissiles = new();

    public static void SeeMissile(int id, Vector3 spawn, Vector3 pos)
    {
        if (!SeenMissiles.Add(id)) return;
        if (SeenMissiles.Count > 2000) SeenMissiles.Clear();
        if ((pos - spawn).sqrMagnitude > 300f * 300f) return; 
        foreach (var g in Interceptor.AllGroups)
        {
            if (g.Warners.Count == 0) continue;
            if ((spawn - g.Center).sqrMagnitude < g.Radius * g.Radius) continue;
            Threat(g, spawn, EoThreats.Launch);
        }
    }

    
    private static void Threat(Interceptor.Group g, Vector3 source, EoThreats type)
    {
        var now = Time.time;
        List<Module> seeing = null;
        foreach (var w in g.Warners)
        {
            if (w.Destroyed || (w.S.Eo & type) == 0) continue;
            var rel = source - w.Center;
            if (rel.magnitude > w.S.Range || !w.S.InSector(w.ToLocalDir(rel))) continue;
            (seeing ??= new List<Module>()).Add(w);
        }
        if (seeing == null) return;

        var warn = false;
        foreach (var w in seeing) warn |= w.S.Warn;
        if (warn) EoHud.Warn(g.V, type, source);

        bool Linked(Module m)
        {
            foreach (var w in seeing) if (m.S.Detector == w.Vuid) return true;
            return false;
        }

        foreach (var j in g.Jammers)
            if (j.S.Mode == JammerMode.OnWarning && Linked(j)) j.JamUntil = Mathf.Max(j.JamUntil, now + j.S.Duration);

        
        Module best = null;
        var bestAngle = float.MaxValue;
        var screened = ScreenedSoon(g.Center, source);
        foreach (var s in g.Smokes)
        {
            if (screened) break;
            if (s.Destroyed || s.ChargesLeft <= 0 || (s.S.Eo & type) == 0 || !Linked(s)) continue;
            if (now < s.LastShot + s.S.Pause || Pending(s)) continue;
            var angle = AngleOff(s, source);
            if (angle < 0f || angle >= bestAngle) continue;
            best = s;
            bestAngle = angle;
        }
        if (best != null) Schedule(best, source, now + best.S.Reaction);
        if (Plugin.DebugLogging.Value || best != null)
            Plugin.Log.LogInfo($"EO: {EoInfo.Short(type)} from {(source - g.Center).magnitude:0} m" +
                               (best != null ? $" — aerosol salvo in {best.S.Reaction:0.0#} s" : ""));
    }

    
    private static float AngleOff(Module s, Vector3 target)
    {
        var local = s.ToLocalDir(target - s.Center);
        if (new Vector2(local.x, local.z).sqrMagnitude < 1e-6f) return 0f;
        var az = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        var off = Mathf.Abs(Mathf.DeltaAngle(s.S.Azimuth, az));
        if (!s.S.Full360 && off > s.S.HalfH) return -1f;
        return off;
    }

    

    private sealed class Salvo
    {
        public Module Launcher;
        public Vector3 Target;
        public float At;
    }

    private sealed class Grenade
    {
        public Vector3 Point;
        public float At, Build, Duration;
    }

    private static readonly List<Salvo> Salvos = new();
    private static readonly List<Grenade> Grenades = new();

    private static bool Pending(Module s)
    {
        foreach (var x in Salvos) if (x.Launcher == s) return true;
        return false;
    }

    private static void Schedule(Module s, Vector3 target, float at)
    {
        s.LastShot = Time.time;
        Salvos.Add(new Salvo { Launcher = s, Target = target, At = at });
    }

    private static void FireSalvo(Module s, Vector3 target)
    {
        if (s.Destroyed || !s.Alive || s.ChargesLeft <= 0) return;
        var n = Math.Min(s.S.PerTarget, s.ChargesLeft);
        s.ChargesLeft -= n;
        s.LastShot = Time.time;

        
        var local = s.ToLocalDir(target - s.Center);
        var az = new Vector2(local.x, local.z).sqrMagnitude > 1e-6f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : s.S.Azimuth;
        if (!s.S.Full360) az = s.S.Azimuth + Mathf.Clamp(Mathf.DeltaAngle(s.S.Azimuth, az), -s.S.HalfH, s.S.HalfH);
        var dir = s.Rotation * ApsSettings.Dir(az, 0f);
        dir.y = 0f;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        var side = Vector3.Cross(Vector3.up, dir).normalized;
        var gravity = Physics.gravity;
        var muzzle = s.T.position + s.Rotation * new Vector3(0f, 0f, s.Size.z * 1.3f);

        for (var k = 0; k < n; k++)
        {
            var point = s.Center + dir * s.S.ScreenDist + side * ((k - (n - 1) * 0.5f) * GrenadeRadius);
            point.y = GroundBelow(point, s.Center.y - 2f) + GrenadeHalfHeight * 0.7f;
            var dist = (point - muzzle).magnitude;
            var t = Mathf.Clamp(dist / 65f, 0.4f, 2.5f);
            var vel = (point - muzzle) / t - gravity * (0.5f * t);
            MunitionsBridge.SmokeLaunch(muzzle, vel, t);
            Grenades.Add(new Grenade { Point = point, At = Time.time + t, Build = s.S.BuildTime, Duration = s.S.Duration });
        }
        if (!SoundBridge.Play("gl.launch", muzzle, s.T, 0.9f, 0.95f + 0.1f * UnityEngine.Random.value))
            Assets.Play(Assets.GrenadeClip, muzzle, 0.9f, 8f, 0.95f + 0.1f * UnityEngine.Random.value);
        EoHud.Smoke(Modules.VehicleOf(s), s.ChargesLeft);
        Plugin.Log.LogInfo($"EO: aerosol launcher fired {n} grenade(s), {s.ChargesLeft} left");
    }

    
    private static float GroundBelow(Vector3 p, float fallback)
    {
        try
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var best = float.MaxValue;
            var y = fallback;
            foreach (var h in hits)
            {
                if (h.rigidbody != null || h.distance >= best) continue;
                best = h.distance;
                y = h.point.y;
            }
            return y;
        }
        catch { return fallback; }
    }

    private static void Burst(Grenade gr)
    {
        Clouds.Add(new Cloud
        {
            Center = gr.Point, Drift = MunitionsBridge.SmokeDrift(), Radius = GrenadeRadius, HalfHeight = GrenadeHalfHeight,
            Born = Time.time, Build = gr.Build, Duration = gr.Duration
        });
        MunitionsBridge.SmokeCloud(gr.Point, GrenadeRadius, gr.Build, gr.Duration);
        ThermalBridge.AddObscurant(gr.Point, GrenadeRadius, gr.Build, gr.Duration);
        var pitch = 0.95f + 0.1f * UnityEngine.Random.value;
        if (!SoundBridge.Play("smoke.burst", gr.Point, null, 0.9f, pitch))
            Assets.Play(Assets.GrenadeBurstClip, gr.Point, 0.9f, 15f, pitch);
    }

    
    public static void AddCloud(Vector3 center, float radius, float halfHeight, float build, float duration)
    {
        if (Clouds.Count > 400) Clouds.RemoveAt(0);
        Clouds.Add(new Cloud
        {
            Center = center, Drift = MunitionsBridge.SmokeDrift(), Radius = Mathf.Clamp(radius, 1f, 40f),
            HalfHeight = Mathf.Clamp(halfHeight, 0.5f, 20f), Born = Time.time, Build = Mathf.Max(0.1f, build), Duration = Mathf.Max(1f, duration)
        });
    }

    
    public static Vector4 PlayerSmokeStatus()
    {
        if (!EoPlayer.InControl) return Vector4.zero;
        var vehicle = EoPlayer.Vehicle;
        if (vehicle == null) return Vector4.zero;
        Interceptor.Group group = null;
        foreach (var g in Interceptor.AllGroups)
            if (g.Ptr == vehicle.Pointer) { group = g; break; }
        if (group == null || group.Smokes.Count == 0) return Vector4.zero;
        int left = 0, total = 0;
        var busy = false;
        var now = Time.time;
        foreach (var s in group.Smokes)
        {
            total += Math.Max(0, s.S.Charges);
            if (s.Destroyed) continue;
            left += Math.Max(0, s.ChargesLeft);
            if (Pending(s) || now < s.LastShot + Mathf.Min(1f, s.S.Pause)) busy = true;
        }
        return new Vector4(left, total, busy ? 1f : 0f, 1f);
    }

    

    private static float _nextGunScan, _nextGunList;
    private static readonly List<(Sprocket.Vehicles.Cannons.Cannon Gun, Vehicle Owner)> Guns = new();
    private static readonly Dictionary<long, float> AimedTime = new();
    private static readonly Dictionary<long, float> LasedAt = new();

    public static void Tick()
    {
        RegisterGuidanceFilter();
        var now = Time.time;

        var anyWarners = false;
        foreach (var g in Interceptor.AllGroups) anyWarners |= g.Warners.Count > 0;
        WatchLaunches = anyWarners;

        
        foreach (var g in Interceptor.AllGroups)
            foreach (var j in g.Jammers)
                j.JamOn = !j.Destroyed && (j.S.Mode == JammerMode.AlwaysOn || (j.S.Mode == JammerMode.OnWarning && now < j.JamUntil));
        foreach (var m in Modules.Snapshot())
            if (m.Kind == ModuleKind.Jammer && !m.InBattle) m.JamOn = false;

        
        for (var i = Salvos.Count - 1; i >= 0; i--)
        {
            if (now < Salvos[i].At) continue;
            var s = Salvos[i];
            Salvos.RemoveAt(i);
            try { FireSalvo(s.Launcher, s.Target); }
            catch (Exception e) { Plugin.Log.LogWarning("Aerosol salvo failed: " + e.Message); }
        }
        for (var i = Grenades.Count - 1; i >= 0; i--)
        {
            if (now < Grenades[i].At) continue;
            var gr = Grenades[i];
            Grenades.RemoveAt(i);
            try { Burst(gr); }
            catch (Exception e) { Plugin.Log.LogWarning("Aerosol burst failed: " + e.Message); }
        }
        for (var i = Clouds.Count - 1; i >= 0; i--)
        {
            var c = Clouds[i];
            if (now - c.Born > c.Build + c.Duration + FadeOutTime || now < c.Born - 1f) Clouds.RemoveAt(i);
        }

        
        ManualSmoke();

        if (!anyWarners)
        {
            EoHud.Update();
            return;
        }

        PollThermalLaser();
        if (Plugin.GunnersLase.Value) ScanGuns(now);
        EoHud.Update();

        if (JamRolls.Count > 4000) JamRolls.Clear();
        if (BeamWarned.Count > 4000) BeamWarned.Clear();
    }

    
    private static int _laserSeen = -1;

    private static void PollThermalLaser()
    {
        if (!ThermalBridge.TryLaser(out var count, out var origin, out var dir, out var range)) return;
        if (_laserSeen < 0) { _laserSeen = count; return; }
        if (count == _laserSeen) return;
        _laserSeen = count;
        var end = origin + dir * (range > 0f ? range + 2f : 8000f);
        foreach (var g in Interceptor.AllGroups)
        {
            if (g.Warners.Count == 0) continue;
            if ((origin - g.Center).sqrMagnitude < g.Radius * g.Radius) continue;
            if (DistanceToSegment(g.Center, origin, end) > g.Radius * 0.8f) continue;
            Threat(g, origin, EoThreats.Rangefinder);
        }
    }

    
    private static void ScanGuns(float now)
    {
        if (Time.unscaledTime >= _nextGunList)
        {
            _nextGunList = Time.unscaledTime + 2f;
            Guns.Clear();
            try
            {
                foreach (var gun in Object.FindObjectsOfType<Sprocket.Vehicles.Cannons.Cannon>())
                {
                    if (gun == null) continue;
                    var cal = 0f;
                    try { cal = gun.Blueprint != null ? gun.Blueprint.Caliber : 0f; } catch { }
                    if (cal < Plugin.MinThreatCaliber.Value) continue;
                    Guns.Add((gun, gun.GetComponentInParent<Vehicle>()));
                }
            }
            catch { }
        }
        if (now < _nextGunScan) return;
        const float step = 0.2f;
        _nextGunScan = now + step;
        var lase = Mathf.Max(0.2f, Plugin.LaseTime.Value);
        foreach (var (gun, owner) in Guns)
        {
            if (gun == null || gun.WasCollected) continue;
            Transform t;
            try
            {
                
                var behaviour = gun.Behaviour;
                t = behaviour != null ? behaviour.transform : null;
            }
            catch { continue; }
            if (t == null) continue;
            var pos = t.position;
            var fwd = t.forward;
            foreach (var g in Interceptor.AllGroups)
            {
                if (g.Warners.Count == 0 || (owner != null && owner.Pointer == g.Ptr)) continue;
                var key = gun.Pointer.ToInt64() ^ (g.Ptr.ToInt64() << 1);
                var rel = g.Center - pos;
                var along = Vector3.Dot(rel, fwd);
                var aimed = along > 30f && along < 6000f && (rel - fwd * along).magnitude < g.Radius * 0.7f;
                if (!aimed) { AimedTime.Remove(key); continue; }
                AimedTime.TryGetValue(key, out var time);
                time += step;
                AimedTime[key] = time;
                if (time < lase || (LasedAt.TryGetValue(key, out var last) && now - last < 6f)) continue;
                LasedAt[key] = now;
                Threat(g, pos, EoThreats.Rangefinder);
            }
        }
        if (AimedTime.Count > 2000) AimedTime.Clear();
        if (LasedAt.Count > 2000) LasedAt.Clear();
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (a + ab * t - p).magnitude;
    }

    
    
    
    private static bool _smokeKeyWasDown;

    private static void ManualSmoke()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        bool down, pressed;
        try
        {
            var key = kb[Plugin.SmokeKey];
            down = key.isPressed;
            pressed = key.wasPressedThisFrame || (down && !_smokeKeyWasDown);
        }
        catch { return; }
        _smokeKeyWasDown = down;
        if (!pressed || !EoPlayer.InControl) return;
        var cam = EoPlayer.Camera;
        var vehicle = EoPlayer.Vehicle;
        if (cam == null || cam.WasCollected || vehicle == null) return;

        Interceptor.Group group = null;
        foreach (var g in Interceptor.AllGroups)
            if (g.Ptr == vehicle.Pointer) { group = g; break; }
        if (group == null || group.Smokes.Count == 0)
        {
            EoHud.Message("NO AEROSOL LAUNCHERS");
            return;
        }

        var now = Time.time;
        var target = cam.transform.position + cam.transform.forward * 1000f;
        var ready = new List<(Module S, float Outside)>();
        int empty = 0, busy = 0, destroyed = 0;
        foreach (var s in group.Smokes)
        {
            if (s.Destroyed) { destroyed++; continue; }
            if (s.ChargesLeft <= 0) { empty++; continue; }
            if (Pending(s) || now < s.LastShot + Mathf.Min(1f, s.S.Pause)) { busy++; continue; }
            ready.Add((s, AngleOutside(s, target)));
        }
        if (ready.Count == 0)
        {
            EoHud.Message(busy > 0 ? "AEROSOL LAUNCHERS RELOADING" : empty > 0 ? "AEROSOL GRENADES USED UP" : "AEROSOL LAUNCHERS DESTROYED");
            return;
        }

        
        ready.Sort((a, b) => a.Outside.CompareTo(b.Outside));
        var best = ready[0].Outside;
        var fired = 0;
        foreach (var (s, outside) in ready)
        {
            if (best <= 0f ? outside > 0f : outside > best + 20f) continue;
            Schedule(s, target, now + 0.1f);
            fired++;
        }
        EoHud.Message(best <= 0f ? "AEROSOL SCREEN" : "AEROSOL SCREEN  (nearest launchers)");
        Plugin.Log.LogInfo($"EO: aerosol key — {fired} launcher(s){(best > 0f ? $", none covers the view, nearest {best:0}° off" : "")}");
    }

    
    private static float AngleOutside(Module s, Vector3 target)
    {
        var local = s.ToLocalDir(target - s.Center);
        if (new Vector2(local.x, local.z).sqrMagnitude < 1e-6f) return 0f;
        if (s.S.Full360) return 0f;
        var az = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        var off = Mathf.Abs(Mathf.DeltaAngle(s.S.Azimuth, az));
        return Mathf.Max(0f, off - s.S.HalfH);
    }

    public static void Clear()
    {
        Clouds.Clear();
        Salvos.Clear();
        Grenades.Clear();
    }
}


internal static class ThermalBridge
{
    private static bool _searched;
    private static FieldInfo _count, _origin, _dir, _range;
    private static Action<Vector3, float, float, float> _obscurant;

    private static void Find()
    {
        if (_searched) return;
        _searched = true;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name is not ("ThermalSight" or "MDThermalSight")) continue;
                var api = asm.GetType("ThermalSight.Api");
                if (api == null) continue;
                const BindingFlags f = BindingFlags.Public | BindingFlags.Static;
                _count = api.GetField("LaserCount", f);
                _origin = api.GetField("LaserOrigin", f);
                _dir = api.GetField("LaserDirection", f);
                _range = api.GetField("LaserRange", f);
                var m = api.GetMethod("AddObscurant", f, null, new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) }, null);
                if (m != null) _obscurant = (Action<Vector3, float, float, float>)Delegate.CreateDelegate(typeof(Action<Vector3, float, float, float>), m);
            }
        }
        catch { }
    }

    public static bool TryLaser(out int count, out Vector3 origin, out Vector3 dir, out float range)
    {
        Find();
        count = 0; origin = dir = default; range = 0f;
        if (_count == null) return false;
        try
        {
            count = (int)_count.GetValue(null);
            origin = (Vector3)_origin.GetValue(null);
            dir = (Vector3)_dir.GetValue(null);
            range = (float)_range.GetValue(null);
            return true;
        }
        catch { return false; }
    }

    public static void AddObscurant(Vector3 center, float radius, float build, float duration)
    {
        Find();
        try { _obscurant?.Invoke(center, radius, build, duration); }
        catch { }
    }
}
