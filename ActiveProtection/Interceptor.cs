using System;
using System.Collections.Generic;
using HarmonyLib;
using Sprocket.DamageModelling;
using Sprocket.Vehicles;
using UnityEngine;

namespace ActiveProtection;












internal static unsafe class Interceptor
{
    private const int PoolOffset = 0x38, ElemSize = 0x50, ElemBase = 0x20;
    private const int EDef = 0x08, EId = 0x18, ESpawn = 0x1C, EPos = 0x28, EVel = 0x34, ELayer = 0x40, EFlags = 0x48;
    private const int FlagActive = 1, FlagRelease = 2;

    internal sealed class Track
    {
        public int Id, Index;
        public IntPtr Def;
        public DefInfo Info;
        public Vector3 Pos, Vel;
        public float Speed, CheckSpeed;
        public float SeenAt;
        public bool Threat;
        public float HitAt;                                      
        public readonly Dictionary<IntPtr, float> Detected = new();  
        public bool Resolved;
        public int Attempts;
        public float PredictedAt = -100f;                        
        public Vector3 PredictedDir;
    }

    internal sealed class Group
    {
        public Vehicle V;
        public IntPtr Ptr;
        public readonly List<Module> All = new();
        public readonly List<Module> Detectors = new();
        public readonly List<Module> Launchers = new();
        public readonly List<Module> Warners = new();
        public readonly List<Module> Smokes = new();
        public readonly List<Module> Jammers = new();
        public readonly List<Module> Eras = new();
        public readonly List<Module> Panels = new();
        public readonly List<EraHost> Hosts = new();             
        public readonly List<CompoundStructure> Structures = new();
        public readonly HashSet<IntPtr> StructurePtrs = new();
        public float StructuresAt = -100f;
        public Vector3 Center;
        public float Radius = 8f, MaxRange;
        public readonly Dictionary<int, Track> Tracks = new();
    }

    private static readonly Dictionary<IntPtr, Group> Groups = new();
    internal static Dictionary<IntPtr, Group>.ValueCollection AllGroups => Groups.Values;
    private static readonly System.Random Rng = new();
    private static int _errors;

    

    public static void Refresh()
    {
        var seen = new HashSet<IntPtr>();
        foreach (var m in Modules.Snapshot())
        {
            var v = Modules.VehicleOf(m);
            var battle = false;
            try { battle = v != null && v.BehaviourEnabled; } catch { }
            if (battle && !m.InBattle)
            {
                m.InBattle = true;
                m.ChargesLeft = m.S.Charges;
                m.Destroyed = false;
                m.LastShot = -100f;
                Era.Reset(m);
                Blowoff.Reset(m);
            }
            else if (!battle && m.InBattle)
            {
                m.InBattle = false;
                m.ChargesLeft = m.S.Charges;
                m.Destroyed = false;
                Era.Reset(m);
                Blowoff.Reset(m);
            }
            if (!battle)
            {
                if (m.Vented) Blowoff.Reset(m); 
                continue;
            }
            var g = GroupFor(v, seen);
            g.All.Add(m);
            (m.Kind switch
            {
                ModuleKind.Detector => g.Detectors,
                ModuleKind.Launcher => g.Launchers,
                ModuleKind.Warner => g.Warners,
                ModuleKind.Smoke => g.Smokes,
                ModuleKind.Era => g.Eras,
                ModuleKind.Panel => g.Panels,
                _ => g.Jammers
            }).Add(m);
        }

        
        foreach (var h in EraHosts.Snapshot())
        {
            var v = EraHosts.VehicleOf(h);
            var battle = false;
            try { battle = v != null && v.BehaviourEnabled; } catch { }
            if (battle != h.InBattle)
            {
                h.InBattle = battle;
                Era.Reset(h);
            }
            if (!battle || !h.C.On || !h.C.HasGeometry || h.C.Count(true) == 0) continue;
            GroupFor(v, seen).Hosts.Add(h);
        }

        List<IntPtr> gone = null;
        foreach (var kv in Groups)
            if (!seen.Contains(kv.Key)) (gone ??= new List<IntPtr>()).Add(kv.Key);
        if (gone != null) foreach (var k in gone) Groups.Remove(k);

        foreach (var g in Groups.Values)
        {
            try
            {
                var t = g.V.transform;
                Rigidbody rb = null;
                try { rb = g.V.Rigidbody; } catch { }
                g.Center = rb != null ? rb.worldCenterOfMass : t.position;
                var radius = 6f;
                var range = 0f;
                foreach (var m in g.All)
                {
                    radius = Mathf.Max(radius, (m.Center - g.Center).magnitude + 3f);
                    if (m.Kind == ModuleKind.Detector) range = Mathf.Max(range, m.S.Range);
                }
                foreach (var h in g.Hosts)
                    try { radius = Mathf.Max(radius, (h.WorldCenter - g.Center).magnitude + 3f); }
                    catch { }
                g.Radius = radius;
                g.MaxRange = range;
                if (Time.unscaledTime - g.StructuresAt > 2f) FindStructures(g, rb);
            }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogWarning("APS vehicle refresh failed: " + e.Message); }
        }
    }

    
    private static Group GroupFor(Vehicle v, HashSet<IntPtr> seen)
    {
        if (!Groups.TryGetValue(v.Pointer, out var g))
        {
            g = new Group { V = v, Ptr = v.Pointer };
            Groups[v.Pointer] = g;
        }
        if (seen.Add(v.Pointer))
        {
            g.All.Clear();
            g.Detectors.Clear();
            g.Launchers.Clear();
            g.Warners.Clear();
            g.Smokes.Clear();
            g.Jammers.Clear();
            g.Eras.Clear();
            g.Panels.Clear();
            g.Hosts.Clear();
        }
        return g;
    }

    
    private static void FindStructures(Group g, Rigidbody body)
    {
        g.StructuresAt = Time.unscaledTime;
        g.Structures.Clear();
        g.StructurePtrs.Clear();
        foreach (var cs in g.V.GetComponentsInChildren<CompoundStructure>(true))
            if (cs != null && g.StructurePtrs.Add(cs.Pointer)) g.Structures.Add(cs);
        if (g.Structures.Count > 0 || body == null) return;
        try
        {
            var models = DamageModel.models;
            if (models == null) return;
            for (var i = 0; i < models.Count; i++)
            {
                var cs = models[i]?.TryCast<CompoundStructure>();
                var rb = cs != null ? cs.RigidBody : null;
                if (rb != null && rb.Pointer == body.Pointer && g.StructurePtrs.Add(cs.Pointer)) g.Structures.Add(cs);
            }
        }
        catch { }
    }

    

    [HarmonyPatch(typeof(ProjectileRegister), nameof(ProjectileRegister.FixedUpdate))]
    private static class StepPatch
    {
        
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ProjectileRegister __instance)
        {
            if (Groups.Count == 0) return;
            try { Step(__instance.Pointer); }
            catch (Exception e) { if (_errors++ < 10) Plugin.Log.LogError("APS step failed: " + e); }
        }
    }

    private static void Step(IntPtr reg)
    {
        var pool = *(IntPtr*)(reg + PoolOffset);
        if (pool == IntPtr.Zero) return;
        var len = *(int*)(pool + 0x18);
        var now = Time.time;
        var dt = Time.fixedDeltaTime;
        var gravity = Physics.gravity;
        var destructible = Plugin.ModulesDestructible.Value;

        for (var i = 0; i < len; i++)
        {
            var e = pool + ElemBase + i * ElemSize;
            var flags = *(int*)(e + EFlags);
            if ((flags & FlagActive) == 0 || (flags & FlagRelease) != 0) continue;
            var pos = *(Vector3*)(e + EPos);
            var vel = *(Vector3*)(e + EVel);
            var speed = vel.magnitude;
            if (speed < 1f) continue;

            
            if (EoSystem.WatchLaunches)
            {
                var info0 = ThreatClass.Of(*(IntPtr*)(e + EDef));
                if (info0 != null && info0.Threat && info0.Type == ThreatTypes.Atgm)
                    EoSystem.SeeMissile(*(int*)(e + EId), *(Vector3*)(e + ESpawn), pos);
            }

            foreach (var g in Groups.Values)
            {
                var toCenter = g.Center - pos;
                var dist = toCenter.magnitude;
                var stepLen = speed * dt;
                if (dist > g.MaxRange + g.Radius + stepLen) continue;

                if (destructible && dist < g.Radius + stepLen + 1f) CheckModuleHits(g, pos, vel * dt);

                var defPtr = *(IntPtr*)(e + EDef);
                var info = ThreatClass.Of(defPtr);

                
                if ((g.Eras.Count > 0 || g.Hosts.Count > 0) && dist < g.Radius + stepLen + 1f && info != null && info.Type != ThreatTypes.None &&
                    (*(Vector3*)(e + ESpawn) - g.Center).sqrMagnitude >= g.Radius * g.Radius)
                {
                    try
                    {
                        if (g.Eras.Count > 0) Era.Check(g, e, pos, vel, dt, info);
                        if (g.Hosts.Count > 0) Era.CheckHosts(g, e, pos, vel, dt, info);
                    }
                    catch (Exception ex) { if (_errors++ < 10) Plugin.Log.LogError("ERA check failed: " + ex); }
                }

                if (info == null || !info.Threat) continue;
                if (g.Detectors.Count == 0 || g.Launchers.Count == 0) continue;
                
                if ((*(Vector3*)(e + ESpawn) - g.Center).sqrMagnitude < g.Radius * g.Radius) continue;

                var id = *(int*)(e + EId);
                if (!g.Tracks.TryGetValue(id, out var tr) || tr.Def != defPtr)
                {
                    tr = new Track { Id = id, Def = defPtr, Info = info };
                    g.Tracks[id] = tr;
                }
                tr.Index = i;
                tr.Pos = pos;
                tr.Vel = vel;
                tr.Speed = speed;
                tr.SeenAt = now;
                
                
                if (tr.CheckSpeed <= 0f || speed < tr.CheckSpeed * 1.25f) tr.CheckSpeed = speed;
                if (tr.Resolved) continue;

                
                
                if (dist > g.Radius && Vector3.Dot(vel, toCenter) <= 0f) { tr.Threat = false; continue; }
                var dirNow = vel / speed;
                float tHit;
                if (now - tr.PredictedAt < 0.1f && now >= tr.PredictedAt && Vector3.Dot(dirNow, tr.PredictedDir) > 0.9998f)
                {
                    if (!tr.Threat) continue;
                    tHit = tr.HitAt - now;
                    if (tHit <= 0f) continue;
                }
                else
                {
                    tHit = NearPath(g, pos, vel, gravity, dist) ? PredictHit(g, pos, vel, gravity, dist + g.Radius + 2f, *(int*)(e + ELayer)) : -1f;
                    tr.PredictedAt = now;
                    tr.PredictedDir = dirNow;
                }
                if (tHit < 0f) { tr.Threat = false; continue; }
                tr.Threat = true;
                tr.HitAt = now + tHit;

                
                foreach (var d in g.Detectors)
                {
                    if (d.Destroyed || (d.S.Types & info.Type) == 0 || tr.Detected.ContainsKey(d.Ptr)) continue;
                    var t = FirstInside(d, pos, vel, gravity, 0f, Mathf.Min(dt, tHit), 0f, d.S.Range, 1f);
                    if (t < 0f) continue;
                    tr.Detected[d.Ptr] = now + t;
                    if (Plugin.DebugLogging.Value)
                        Plugin.Log.LogInfo($"APS: {ThreatInfo.Name(info.Type)} ({info.Caliber:0} mm, {speed:0} m/s) detected " +
                                           $"{(PathPoint(pos, vel, gravity, t) - d.Center).magnitude:0} m from the detector, impact in {tHit:0.000} s");
                }
            }
        }

        foreach (var g in Groups.Values)
        {
            if (g.Tracks.Count == 0) continue;
            try { Engage(g, pool, len, now, dt, gravity); }
            catch (Exception ex) { if (_errors++ < 10) Plugin.Log.LogError("APS engagement failed: " + ex); }
            List<int> old = null;
            foreach (var kv in g.Tracks)
                if (now - kv.Value.SeenAt > 1f) (old ??= new List<int>()).Add(kv.Key);
            if (old != null) foreach (var k in old) g.Tracks.Remove(k);
        }
    }

    private static Vector3 PathPoint(Vector3 pos, Vector3 vel, Vector3 gravity, float t) => pos + vel * t + gravity * (0.5f * t * t);

    
    private static bool NearPath(Group g, Vector3 pos, Vector3 vel, Vector3 gravity, float dist)
    {
        var speed2 = vel.sqrMagnitude;
        if (speed2 < 1e-4f) return false;
        var total = (dist + g.Radius + 2f) / Mathf.Sqrt(speed2);
        var t = Mathf.Clamp(Vector3.Dot(g.Center - pos, vel) / speed2, 0f, total);
        return (PathPoint(pos, vel, gravity, t) - g.Center).sqrMagnitude < (g.Radius + 4f) * (g.Radius + 4f);
    }

    
    private static float PredictHit(Group g, Vector3 pos, Vector3 vel, Vector3 gravity, float maxPath, int layerMask)
    {
        if (g.Structures.Count == 0) return -1f;
        var speed = vel.magnitude;
        var total = maxPath / speed;
        var segments = Mathf.Clamp(Mathf.CeilToInt(maxPath / 25f), 1, 12);
        var t0 = 0f;
        var p0 = pos;
        for (var s = 1; s <= segments; s++)
        {
            var t1 = total * s / segments;
            var p1 = PathPoint(pos, vel, gravity, t1);
            var seg = p1 - p0;
            var segLen = seg.magnitude;
            if (segLen < 1e-4f) break;
            var dir = seg / segLen;
            var best = float.MaxValue;
            foreach (var cs in g.Structures)
            {
                try
                {
                    if (cs == null || cs.WasCollected) continue;
                    if (cs.Raycast(p0, dir, segLen, out float d) && d < best) best = d;
                }
                catch { }
            }
            if (best <= segLen)
            {
                if (Blocked(g, p0, dir, best, layerMask)) return -1f;
                return t0 + (t1 - t0) * (best / segLen);
            }
            
            try
            {
                if (Physics.Raycast(p0, dir, out var hit, segLen, layerMask) && hit.rigidbody == null) return -1f;
            }
            catch { }
            t0 = t1;
            p0 = p1;
        }
        return -1f;
    }

    
    private static bool Blocked(Group g, Vector3 from, Vector3 dir, float distance, int layerMask)
    {
        try
        {
            if (distance > 0.5f && Physics.Raycast(from, dir, out var hit, distance - 0.3f, layerMask) && hit.rigidbody == null) return true;
        }
        catch { }
        return false;
    }

    
    private static float FirstInside(Module m, Vector3 pos, Vector3 vel, Vector3 gravity, float t0, float t1, float rNear, float rFar, float stepMetres)
    {
        if (t1 <= t0) return -1f;
        var c = m.Center;
        
        var p0 = PathPoint(pos, vel, gravity, t0);
        var p1 = PathPoint(pos, vel, gravity, t1);
        var d = p1 - p0;
        var a = Vector3.Dot(d, d);
        var f = p0 - c;
        var b = 2f * Vector3.Dot(f, d);
        var cc = Vector3.Dot(f, f) - (rFar + 0.5f) * (rFar + 0.5f);
        float u0 = 0f, u1 = 1f;
        if (a > 1e-8f)
        {
            var disc = b * b - 4f * a * cc;
            if (disc < 0f) return -1f;
            var sq = Mathf.Sqrt(disc);
            u0 = Mathf.Max(0f, (-b - sq) / (2f * a));
            u1 = Mathf.Min(1f, (-b + sq) / (2f * a));
            if (u0 > u1) return -1f;
        }
        else if (cc > 0f) return -1f;

        var ta = Mathf.Lerp(t0, t1, u0);
        var tb = Mathf.Lerp(t0, t1, u1);
        var length = (tb - ta) * vel.magnitude;
        var n = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(0.05f, stepMetres)), 1, 160);
        var rot = Quaternion.Inverse(m.Rotation);
        for (var k = 0; k <= n; k++)
        {
            var t = Mathf.Lerp(ta, tb, k / (float)n);
            var rel = PathPoint(pos, vel, gravity, t) - c;
            var r = rel.magnitude;
            if (r < rNear || r > rFar) continue;
            if (m.S.InSector(rot * rel)) return t;
        }
        return -1f;
    }

    

    private struct Option
    {
        public Track T;
        public float At;
    }

    private static void Engage(Group g, IntPtr pool, int len, float now, float dt, Vector3 gravity)
    {
        var threats = new List<Track>();
        foreach (var t in g.Tracks.Values)
            if (t.Threat && !t.Resolved && t.Detected.Count > 0 && t.HitAt > now && t.SeenAt >= now - 1e-4f) threats.Add(t);
        if (threats.Count == 0) return;

        
        var options = new Dictionary<Module, List<Option>>();
        foreach (var l in g.Launchers)
        {
            if (l.Destroyed || l.ChargesLeft <= 0) continue;
            var d = Modules.DetectorOf(l, g.All);
            if (d == null || d.Destroyed) continue;
            foreach (var t in threats)
            {
                if ((l.S.Types & t.Info.Type) == 0) continue;
                if (t.CheckSpeed < l.S.MinSpeed || t.CheckSpeed > l.S.MaxSpeed) continue;
                if (!t.Detected.TryGetValue(d.Ptr, out var detectedAt)) continue;
                var start = Mathf.Max(now, Mathf.Max(detectedAt + l.S.Reaction, l.LastShot + l.S.Pause));
                var end = t.HitAt - 0.0005f;
                if (start >= end) continue;
                var at = FirstInside(l, t.Pos, t.Vel, gravity, start - now, end - now, l.S.MinDist, l.S.MaxDist, 0.25f);
                if (at < 0f) continue;
                if (!options.TryGetValue(l, out var list)) options[l] = list = new List<Option>();
                list.Add(new Option { T = t, At = now + at });
            }
        }
        if (options.Count == 0) return;

        
        foreach (var kv in options)
        {
            var l = kv.Key;
            kv.Value.Sort((x, y) => Score(l, x).CompareTo(Score(l, y)));
        }
        var taken = new Dictionary<Track, (Module L, float At)>();
        var matched = new HashSet<Module>();
        for (var round = 0; round < 8; round++)
        {
            var changed = false;
            foreach (var kv in options)
            {
                var l = kv.Key;
                if (matched.Contains(l)) continue;
                foreach (var o in kv.Value)
                {
                    if (!taken.TryGetValue(o.T, out var holder))
                    {
                        taken[o.T] = (l, o.At);
                        matched.Add(l);
                        changed = true;
                        break;
                    }
                    if (o.At < holder.At - 1e-4f)
                    {
                        matched.Remove(holder.L);
                        taken[o.T] = (l, o.At);
                        matched.Add(l);
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed) break;
        }

        
        foreach (var kv in taken)
        {
            var (l, at) = kv.Value;
            if (at >= now + dt) continue;
            Fire(g, l, kv.Key, at, now, pool, len, gravity);
        }
    }

    
    private static float Score(Module l, Option o) => l.S.Priority switch
    {
        TargetPriority.Nearest => (o.T.Pos - l.Center).magnitude,
        TargetPriority.Dangerous => -ThreatClass.Danger(o.T.Info, o.T.CheckSpeed),
        _ => o.T.HitAt
    };

    

    private static void Fire(Group g, Module l, Track t, float at, float now, IntPtr pool, int len, Vector3 gravity)
    {
        var n = Math.Min(l.S.PerTarget, l.ChargesLeft);
        if (n <= 0) return;
        l.ChargesLeft -= n;
        l.LastShot = at;
        l.LastFlash = Time.time;

        var chance = Mathf.Clamp01(ThreatClass.HitChance(t.Info.Type));
        var success = false;
        for (var k = 0; k < n && !success; k++) success = Rng.NextDouble() < chance;

        var tau = Mathf.Max(0f, at - now);
        var point = PathPoint(t.Pos, t.Vel, gravity, tau);
        var dir = t.Vel.sqrMagnitude > 1e-4f ? t.Vel.normalized : Vector3.forward;
        var kinetic = ThreatClass.Kinetic(t.Info.Type);

        if (success && t.Index >= 0 && t.Index < len)
        {
            var e = pool + ElemBase + t.Index * ElemSize;
            if (*(int*)(e + EId) == t.Id && *(IntPtr*)(e + EDef) == t.Def)
            {
                if (kinetic)
                {
                    
                    var left = Mathf.Clamp(Plugin.KineticPenetrationLeft.Value, 0.05f, 1f) * (0.85f + 0.3f * (float)Rng.NextDouble());
                    var speedFactor = Mathf.Pow(Mathf.Clamp01(left), 1f / 1.43f);
                    var axis = Vector3.Cross(dir, UnityEngine.Random.onUnitSphere);
                    if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(dir, Vector3.up);
                    var angle = Mathf.Max(0f, Plugin.KineticDeflection.Value) * (0.4f + 0.6f * (float)Rng.NextDouble());
                    var newVel = Quaternion.AngleAxis(angle, axis.normalized) * (t.Vel + gravity * tau) * speedFactor;
                    
                    *(Vector3*)(e + EPos) = point - newVel * tau;
                    *(Vector3*)(e + EVel) = newVel;
                }
                else
                {
                    *(Vector3*)(e + EPos) = point;
                    *(Vector3*)(e + EVel) = dir * 0.01f;
                    *(int*)(e + EFlags) |= FlagRelease;
                }
                t.Resolved = true;
            }
        }
        if (!success) t.Attempts++;

        Fx.Shot(l, point, dir, kinetic, success);
        Plugin.Log.LogInfo($"APS: launcher fired {n} charge(s) at {ThreatInfo.Name(t.Info.Type)} ({t.Info.Caliber:0} mm, {t.CheckSpeed:0} m/s) " +
                           $"{(point - l.Center).magnitude:0.0} m away — {(success ? kinetic ? "deflected" : "destroyed" : "missed")}, " +
                           $"{l.ChargesLeft} left");
    }

    
    
    
    
    

    private sealed class DroneTrack
    {
        public readonly Dictionary<IntPtr, float> Detected = new();
        public float SeenAt;
        public bool Resolved;
    }

    private static readonly Dictionary<(IntPtr Vehicle, int Drone), DroneTrack> DroneTracks = new();
    private static float _droneCleanupAt;

    
    public static int Drone(int id, Vector3 pos, Vector3 vel, float killChance)
    {
        if (Groups.Count == 0) return 0;
        var now = Time.time;
        var dt = Time.fixedDeltaTime;
        var speed = vel.magnitude;
        var result = 0;
        foreach (var g in Groups.Values)
        {
            if (g.Detectors.Count == 0 || g.Launchers.Count == 0) continue;
            var toCenter = g.Center - pos;
            var dist = toCenter.magnitude;
            if (dist > g.MaxRange + g.Radius) continue;
            if (speed < 2f || Vector3.Dot(vel, toCenter) <= 0f) continue; 
            var closest = pos + vel * (Vector3.Dot(toCenter, vel) / (speed * speed));
            if ((closest - g.Center).magnitude > g.Radius) continue;      

            var key = (g.Ptr, id);
            if (!DroneTracks.TryGetValue(key, out var tr)) DroneTracks[key] = tr = new DroneTrack();
            tr.SeenAt = now;
            if (tr.Resolved) continue;

            foreach (var d in g.Detectors)
            {
                if (d.Destroyed || (d.S.Types & ThreatTypes.Atgm) == 0 || tr.Detected.ContainsKey(d.Ptr)) continue;
                var rel = pos - d.Center;
                if (rel.magnitude > d.S.Range || !d.S.InSector(Quaternion.Inverse(d.Rotation) * rel)) continue;
                tr.Detected[d.Ptr] = now;
                Plugin.Log.LogInfo($"APS: FPV drone detected {rel.magnitude:0} m from the detector ({speed:0} m/s)");
            }

            foreach (var l in g.Launchers)
            {
                if (l.Destroyed || l.ChargesLeft <= 0 || (l.S.Types & ThreatTypes.Atgm) == 0) continue;
                var det = Modules.DetectorOf(l, g.All);
                if (det == null || det.Destroyed || !tr.Detected.TryGetValue(det.Ptr, out var seenAt)) continue;
                if (now < seenAt + l.S.Reaction || now < l.LastShot + l.S.Pause) continue;
                var at = FirstInside(l, pos, vel, Vector3.zero, 0f, dt, l.S.MinDist, l.S.MaxDist, 0.1f);
                if (at < 0f) continue;
                var r = FireAtDrone(l, pos + vel * at, vel, killChance);
                if (r == 1) tr.Resolved = true;
                result = Math.Max(result, r);
                break; 
            }
            if (result == 1) break;
        }

        if (now - _droneCleanupAt > 2f)
        {
            _droneCleanupAt = now;
            List<(IntPtr, int)> old = null;
            foreach (var kv in DroneTracks)
                if (now - kv.Value.SeenAt > 2f || now < kv.Value.SeenAt) (old ??= new List<(IntPtr, int)>()).Add(kv.Key);
            if (old != null) foreach (var k in old) DroneTracks.Remove(k);
        }
        return result;
    }

    private static int FireAtDrone(Module l, Vector3 point, Vector3 vel, float chance)
    {
        var n = Math.Min(l.S.PerTarget, l.ChargesLeft);
        if (n <= 0) return 0;
        l.ChargesLeft -= n;
        l.LastShot = Time.time;
        l.LastFlash = Time.time;
        chance = Mathf.Clamp01(chance);
        var success = false;
        for (var k = 0; k < n && !success; k++) success = Rng.NextDouble() < chance;
        var dir = vel.sqrMagnitude > 1e-4f ? vel.normalized : Vector3.forward;
        Fx.Shot(l, point, dir, false, success);
        Plugin.Log.LogInfo($"APS: launcher fired {n} charge(s) at an FPV drone {(point - l.Center).magnitude:0.0} m away — " +
                           $"{(success ? "destroyed" : "missed")}, {l.ChargesLeft} left");
        return success ? 1 : 2;
    }

    

    private static void CheckModuleHits(Group g, Vector3 p0, Vector3 step)
    {
        foreach (var m in g.All)
        {
            if (m.Destroyed || m.Kind is ModuleKind.Era or ModuleKind.Panel) continue;
            var half = m.Size * 0.5f + new Vector3(0.02f, 0.02f, 0.02f);
            var inv = Quaternion.Inverse(m.Rotation);
            var a = inv * (p0 - m.Center);
            var d = inv * step;
            if (!SegmentHitsBox(a, d, half)) continue;
            m.Destroyed = true;
            Fx.ModuleDestroyed(m);
            Plugin.Log.LogInfo($"APS: {(m.Kind == ModuleKind.Detector ? "detector" : "launcher")} destroyed by a hit");
        }
    }

    private static bool SegmentHitsBox(Vector3 a, Vector3 d, Vector3 half)
    {
        float tMin = 0f, tMax = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = a[axis];
            var v = d[axis];
            var h = half[axis];
            if (Mathf.Abs(v) < 1e-9f)
            {
                if (o < -h || o > h) return false;
                continue;
            }
            var t1 = (-h - o) / v;
            var t2 = (h - o) / v;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        return true;
    }
}
