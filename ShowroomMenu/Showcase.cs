using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sprocket.TechTrees;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Il2CppCts = Il2CppSystem.Threading.CancellationTokenSource;
using GatewayTask = Il2CppSystem.Threading.Tasks.Task<Sprocket.Vehicles.IVehicleEditGateway>;

namespace ShowroomMenu;



internal sealed class Showcase
{
    
    
    private static readonly List<(GatewayTask Task, GameObject Root, float Since)> Pending = new();

    private const float DefaultRadius = 3.2f, DefaultHeight = 2.6f;

    private Scene _scene;
    private Podium _podium;
    private float _yaw;

    private GatewayTask _task;
    private Il2CppCts _cts;
    private GameObject _building;   
    private IVehicleEditGateway _gateway;
    private Vehicle _vehicle;
    private Transform _vt;
    private Vector3 _localCenter;
    private float _localBottom;
    private float _vehRadius = DefaultRadius, _vehHeight = DefaultHeight, _footRadius = DefaultRadius;
    private float _spawnTime;
    private int _measures, _crewPasses;

    
    
    private static readonly float[] CrewPassTimes = { 0.3f, 1f, 2f, 4f, 8f, 15f };
    private static readonly float[] MeasureTimes = { 0.6f, 2.5f, 6f };

    private bool _started;
    private float _readySince = -1f;
    private string _factionDir;
    private float _nextDirCheck;

    public float Yaw => _yaw;
    public float BaseRadius => _podium != null ? _podium.BaseRadius : DefaultRadius + 0.45f + Podium.BaseExtra;
    public float VehicleRadius => _vehRadius;       
    public float FootprintRadius => _footRadius;    
    public float TopY => Podium.DeckTop + _vehHeight;
    public float MidY => Podium.DeckTop + _vehHeight * 0.5f;

    public void Begin(Scene scene)
    {
        _scene = scene;
        _podium = new Podium();
        if (!_podium.Create(scene, DefaultRadius + 0.45f)) _podium = null;
    }

    public void Rebuild()
    {
        DestroyVehicle();
        _started = false;
        _readySince = Time.unscaledTime;
    }

    public void Update(float dt, bool stageReady)
    {
        _yaw = Mathf.Repeat(_yaw + Plugin.TurntableSpeed.Value * dt, 360f);
        _podium?.SetYaw(_yaw);

        if (stageReady && _readySince < 0f) _readySince = Time.unscaledTime;

        if (Alive(_vehicle))
        {
            var since = Time.unscaledTime - _spawnTime;
            if (_crewPasses < CrewPassTimes.Length && since > CrewPassTimes[_crewPasses])
            {
                _crewPasses++;
                HideCrew();
            }
            if (_measures >= 1 && _measures - 1 < MeasureTimes.Length && since > MeasureTimes[_measures - 1]) Measure();
            Place();
        }
        else if (_vehicle != null)
        {
            _vehicle = null;
            _vt = null;
            _gateway = null;
        }
        else if (_task != null) Poll();
        else if (!_started && stageReady && Plugin.ShowVehicle.Value && Time.unscaledTime - _readySince > 0.15f)
            StartBuild();

        
        if (Time.unscaledTime > _nextDirCheck)
        {
            _nextDirCheck = Time.unscaledTime + 2f;
            if (_started && string.IsNullOrWhiteSpace(Plugin.VehicleName.Value))
            {
                var dir = BlueprintLocator.CurrentDir();
                if (dir != null && _factionDir != null && !string.Equals(dir, _factionDir, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo("Faction changed, showing its last vehicle");
                    Rebuild();
                }
            }
        }
    }

    

    private void StartBuild()
    {
        _started = true;
        _factionDir = BlueprintLocator.CurrentDir();
        var path = BlueprintLocator.Find(Plugin.VehicleName.Value);
        if (path == null)
        {
            Plugin.Log.LogInfo("No saved vehicle to show on the turntable");
            return;
        }
        try
        {
            var blueprint = IVehicleBlueprintSerializer.Serializer.LoadFromPath(path);
            if (blueprint == null)
            {
                Plugin.Log.LogWarning("Could not read the blueprint " + path);
                return;
            }
            var header = blueprint.Header;
            var tech = ITechFrameFactory.Instance.GetTechFrameAtDate(header.CreationDate);
            var factory = IVehicleFactory.Factory;
            if (factory == null || tech == null)
            {
                Plugin.Log.LogWarning("The vehicle factory is not ready");
                return;
            }
            _cts = new Il2CppCts();
            
            var factories = VehicleFactories.instance;
            VehicleContext previous = null;
            if (factories != null)
            {
                previous = factories.context;
                factories.context = VehicleContext.Default;
            }
            
            var before = new HashSet<int>();
            try { foreach (var v in Object.FindObjectsOfType<Vehicle>()) if (v != null) before.Add(v.gameObject.GetInstanceID()); }
            catch { }
            
            
            try { _task = factory.Create(blueprint, tech, VehicleSpawnFlags.Editable, _cts.Token); }
            finally
            {
                if (factories != null) factories.context = previous;
            }
            _building = null;
            try
            {
                foreach (var v in Object.FindObjectsOfType<Vehicle>())
                    if (v != null && !before.Contains(v.gameObject.GetInstanceID())) { _building = v.gameObject; break; }
                
                
                if (_building != null) Object.DontDestroyOnLoad(_building);
            }
            catch { _building = null; }
            Plugin.Log.LogInfo($"Building '{header.Name}' for the turntable ({Path.GetFileName(path)})");
        }
        catch (Exception e)
        {
            _task = null;
            Plugin.Log.LogWarning("Could not build the vehicle for the turntable: " + e.Message);
        }
    }

    private void Poll()
    {
        if (!_task.IsCompleted) return;
        var task = _task;
        _task = null;
        if (task.IsFaulted || task.IsCanceled)
        {
            Plugin.Log.LogWarning("Turntable vehicle build failed: " + (task.Exception?.ToString() ?? "cancelled"));
            return;
        }
        _building = null;
        _gateway = task.Result;
        _vehicle = _gateway?.TryCast<Vehicle>();
        if (_vehicle == null)
        {
            Plugin.Log.LogWarning("Turntable vehicle build returned nothing");
            DestroyVehicle();
            return;
        }
        _vt = _vehicle.transform;
        _vt.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _spawnTime = Time.unscaledTime;
        _measures = 0;
        _crewPasses = 0;
        var crew = HideCrew();
        Measure();
        Place();
        Plugin.Log.LogInfo($"Turntable vehicle ready: {_vehRadius * 2f:0.0} m across, {_vehHeight:0.0} m high, crew hidden: {crew}");
    }

    
    private int HideCrew()
    {
        var count = 0;
        foreach (var a in _vehicle.GetComponentsInChildren<Sprocket.CrewSystems.UnityPostureArmature>(true))
        {
            if (a == null) continue;
            count++;
            try { a.SetVisible(false); } catch { }
            try
            {
                var model = a.model;
                var go = model?.gameObject;
                if (go != null && go) go.SetActive(false);
            }
            catch { }
            try
            {
                var armature = a.armatureInstance;
                if (armature != null && armature) armature.gameObject.SetActive(false);
            }
            catch { }
            try
            {
                var animator = a.animator;
                if (animator != null && animator) animator.enabled = false;
            }
            catch { }
        }
        return count;
    }

    
    
    private void Measure()
    {
        _measures++;
        _vt.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var boxes = new List<(Bounds b, string name)>();
        foreach (var r in _vehicle.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !r.enabled) continue;
            r.lightProbeUsage = LightProbeUsage.Off;
            if (r.TryCast<SkinnedMeshRenderer>() != null) continue;     
            var b = r.bounds;
            if (b.size.sqrMagnitude < 1e-8f || b.size.sqrMagnitude > 3600f) continue;
            boxes.Add((b, r.gameObject.name));
        }
        if (boxes.Count == 0)
        {
            Place();
            return;
        }

        var median = new Vector3(
            Median(boxes.Select(x => x.b.center.x)),
            Median(boxes.Select(x => x.b.center.y)),
            Median(boxes.Select(x => x.b.center.z)));
        var limit = Mathf.Max(6f, 4f * Median(boxes.Select(x => (x.b.center - median).magnitude)));
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        var dropped = new List<string>();
        var kept = new List<Bounds>();
        foreach (var (b, name) in boxes)
        {
            if ((b.center - median).magnitude > limit)
            {
                dropped.Add($"{name} at {b.center}");
                continue;
            }
            kept.Add(b);
            min = Vector3.Min(min, b.min);
            max = Vector3.Max(max, b.max);
        }
        Place();
        if (kept.Count == 0) return;
        var size = max - min;

        
        
        var footMin = Vector3.positiveInfinity;
        var footMax = Vector3.negativeInfinity;
        foreach (var b in kept)
        {
            if (b.min.y > min.y + 0.45f) continue;
            footMin = Vector3.Min(footMin, b.min);
            footMax = Vector3.Max(footMax, b.max);
        }
        if (footMin.x > footMax.x)
        {
            footMin = min;
            footMax = max;
        }
        var cx = (footMin.x + footMax.x) * 0.5f;
        var cz = (footMin.z + footMax.z) * 0.5f;
        var footHalfDiagonal = 0.5f * Mathf.Sqrt((footMax.x - footMin.x) * (footMax.x - footMin.x) + (footMax.z - footMin.z) * (footMax.z - footMin.z));

        
        var sweep = 0f;
        foreach (var b in kept)
        {
            var dx = Mathf.Max(Mathf.Abs(b.min.x - cx), Mathf.Abs(b.max.x - cx));
            var dz = Mathf.Max(Mathf.Abs(b.min.z - cz), Mathf.Abs(b.max.z - cz));
            sweep = Mathf.Max(sweep, Mathf.Sqrt(dx * dx + dz * dz));
        }

        
        var topY = float.MinValue;
        foreach (var b in kept)
            if (b.size.x >= 0.12f || b.size.z >= 0.12f) topY = Mathf.Max(topY, b.max.y);
        if (topY <= min.y) topY = max.y;

        _localCenter = new Vector3(cx, (min.y + max.y) * 0.5f, cz);
        _localBottom = min.y;
        _vehRadius = Mathf.Max(0.8f, sweep);
        _footRadius = Mathf.Max(0.8f, footHalfDiagonal);
        _vehHeight = Mathf.Max(0.5f, topY - min.y);
        _podium?.Resize(Mathf.Clamp(footHalfDiagonal + 0.5f, 2.4f, 14f));
        Plugin.Log.LogInfo($"Turntable vehicle measured ({_measures}): {kept.Count} parts, size {size}, bottom {min.y:0.00}, " +
                           $"running gear {footMax - footMin}, sweep radius {sweep:0.0}" +
                           (dropped.Count > 0 ? $", ignored {dropped.Count} far parts: {string.Join("; ", dropped.Take(6))}" : ""));
    }

    private static float Median(IEnumerable<float> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return 0f;
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) * 0.5f;
    }

    
    private void Place()
    {
        var rot = Quaternion.Euler(0f, _yaw, 0f);
        var offset = new Vector3(_localCenter.x, _localBottom, _localCenter.z);
        var pos = new Vector3(0f, Podium.DeckTop + 0.005f, 0f) - rot * offset;
        _vt.SetPositionAndRotation(pos, rot);
    }

    private void DestroyVehicle()
    {
        if (_task != null)
        {
            
            
            if (_building != null && Alive(_building))
            {
                try { _building.transform.position = new Vector3(0f, -5000f, 0f); } catch { }
                Plugin.Log.LogInfo("Left the main menu while the turntable vehicle was still building: it is finished out of sight and then removed");
            }
            else try { _cts?.Cancel(); } catch { }   
            Pending.Add((_task, _building, Time.unscaledTime));
            _task = null;
        }
        _building = null;
        if (_gateway != null || Alive(_vehicle)) Release(_gateway, _vehicle);
        _gateway = null;
        _vehicle = null;
        _vt = null;
        _vehRadius = DefaultRadius;
        _footRadius = DefaultRadius;
        _vehHeight = DefaultHeight;
        _localCenter = Vector3.zero;
        _localBottom = 0f;
    }

    private static void Release(IVehicleEditGateway gateway, Vehicle vehicle)
    {
        try
        {
            if (gateway != null)
            {
                IVehicleFactory.Factory.Destroy(gateway);
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("Vehicle factory could not remove the turntable vehicle: " + e.Message); }
        if (Alive(vehicle)) Object.Destroy(vehicle.gameObject);
    }

    
    public int CountRenderers()
    {
        if (!Alive(_vehicle)) return -1;
        var n = 0;
        foreach (var r in _vehicle.GetComponentsInChildren<Renderer>(false))
            if (r != null && r.enabled) n++;
        return n;
    }

    public void Dispose()
    {
        DestroyVehicle();
        _podium?.Dispose();
        _podium = null;
    }

    public static void ProcessPending()
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var (t, root, since) = Pending[i];
            if (t != null && !t.IsCompleted)
            {
                if (Time.unscaledTime - since < 30f) continue;
                Pending.RemoveAt(i);
                Plugin.Log.LogWarning("Abandoned turntable vehicle build did not finish in 30 s — removed");
                try { if (Alive(root)) Object.Destroy(root); } catch { }
                continue;
            }
            Pending.RemoveAt(i);
            try
            {
                if (t == null || t.IsFaulted || t.IsCanceled)
                {
                    if (Alive(root)) Object.Destroy(root);
                    continue;
                }
                var gateway = t.Result;
                if (gateway != null) Release(gateway, gateway.TryCast<Vehicle>());
                else if (Alive(root)) Object.Destroy(root);
            }
            catch (Exception e) { Plugin.Debug("Abandoned vehicle cleanup: " + e.Message); }
        }
    }

    private static bool Alive(Object o) => o != null && !o.WasCollected && o;
}


internal static class BlueprintLocator
{
    private const string Extension = ".blueprint";
    private const string Autosave = "Autosave";

    public static string CurrentDir()
    {
        try { return VehicleBlueprintFiler.GetVehicleBlueprintDirectory(Sprocket.GameDataDirectory.CurrentFaction); }
        catch { return null; }
    }

    public static string Find(string wanted)
    {
        var current = CurrentDir();
        var dirs = new List<string>();
        if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) dirs.Add(current);
        var factions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Sprocket", "Factions");
        try
        {
            if (Directory.Exists(factions))
                foreach (var f in Directory.GetDirectories(factions))
                {
                    var d = Path.Combine(f, "Blueprints", "Vehicles");
                    if (Directory.Exists(d) && !dirs.Any(x => string.Equals(Path.GetFullPath(x), Path.GetFullPath(d), StringComparison.OrdinalIgnoreCase)))
                        dirs.Add(d);
                }
        }
        catch { }
        if (dirs.Count == 0) return null;

        wanted = (wanted ?? "").Trim();
        if (wanted.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) wanted = wanted[..^Extension.Length];
        if (wanted.Length > 0)
        {
            foreach (var d in dirs)
            {
                var p = Path.Combine(d, wanted + Extension);
                if (File.Exists(p)) return p;
            }
            Plugin.Log.LogWarning($"Blueprint '{wanted}' not found, showing the last saved vehicle instead");
        }

        
        foreach (var group in new[] { dirs.Take(1), dirs })
        {
            string best = null;
            var bestTime = DateTime.MinValue;
            foreach (var d in group)
            {
                string[] files;
                try { files = Directory.GetFiles(d, "*" + Extension); }
                catch { continue; }
                foreach (var f in files)
                {
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), Autosave, StringComparison.OrdinalIgnoreCase)) continue;
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t <= bestTime) continue;
                    bestTime = t;
                    best = f;
                }
            }
            if (best != null) return best;
        }
        return null;
    }
}
