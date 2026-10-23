using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Munitions;


internal static class Fx
{
    private static AssetBundle _bundle;
    private static bool _loadFailed;
    private static Material _flare, _smoke, _fxAdd, _fxAlpha, _smokeLit;
    private static AudioClip _launch, _kornet, _azLoad, _azLoop, _mzLoad, _mzLoop, _mzStart, _casingDrop;
    private static AudioClip _beltLoad, _beltEnd, _beltTick, _beltLoop;
    private static AudioClip _thermobaric;
    private static readonly AudioClip[] _rocketLaunch = new AudioClip[3];
    private static Texture2D _launcherIcon, _ejectorIcon, _rocketIcon;
    private static Texture2D _boreBase, _boreMask, _boreNormal;
    private static Material _casing;
    private static AudioMixerGroup _mixer;
    private static bool _mixerSearched;
    private static Mesh _quad;

    public static Material FlareMat => Ensure() ? Alive(_flare) : null;
    public static Material SmokeMat => Ensure() ? Alive(_smoke) : null;
    public static AudioClip LaunchClip => Ensure() ? Alive(_launch) : null;
    public static AudioClip KornetClip => Ensure() ? Alive(_kornet) : null;
    public static Texture2D LauncherIcon => Ensure() ? Alive(_launcherIcon) : null;
    public static Texture2D EjectorIcon => Ensure() ? Alive(_ejectorIcon) : null;
    
    public static Texture2D RocketIcon => Ensure() ? Alive(_rocketIcon) : null;
    public static AudioClip ThermobaricClip => Ensure() ? Alive(_thermobaric) : null;

    public static AudioClip RocketLaunchClip
    {
        get
        {
            if (!Ensure()) return null;
            var start = UnityEngine.Random.Range(0, _rocketLaunch.Length);
            for (var i = 0; i < _rocketLaunch.Length; i++)
            {
                var c = Alive(_rocketLaunch[(start + i) % _rocketLaunch.Length]);
                if (c != null) return c;
            }
            return null;
        }
    }
    
    public static AudioClip AzLoadClip => Ensure() ? Alive(_azLoad) : null;
    public static AudioClip AzLoopClip => Ensure() ? Alive(_azLoop) : null;
    public static AudioClip MzLoadClip => Ensure() ? Alive(_mzLoad) : null;
    public static AudioClip MzLoopClip => Ensure() ? Alive(_mzLoop) : null;
    public static AudioClip MzStartClip => Ensure() ? Alive(_mzStart) : null;
    
    public static AudioClip BeltLoadClip => Ensure() ? Alive(_beltLoad) : null;
    public static AudioClip BeltEndClip => Ensure() ? Alive(_beltEnd) : null;
    public static AudioClip BeltTickClip => Ensure() ? Alive(_beltTick) : null;
    public static AudioClip BeltLoopClip => Ensure() ? Alive(_beltLoop) : null;
    public static AudioClip CasingDropClip => Ensure() ? Alive(_casingDrop) : null;
    public static Material CasingMat => Ensure() ? Alive(_casing) : null;
    public static Material FxAddMat => Ensure() ? Alive(_fxAdd) : null;
    public static Material FxAlphaMat => Ensure() ? Alive(_fxAlpha) : null;
    
    public static Material SmokeLitMat => Ensure() ? Alive(_smokeLit) : null;
    
    public static Texture2D BoreBase => Ensure() ? Alive(_boreBase) : null;
    public static Texture2D BoreMask => Ensure() ? Alive(_boreMask) : null;
    public static Texture2D BoreNormal => Ensure() ? Alive(_boreNormal) : null;

    private static T Alive<T>(T o) where T : Object => o != null && !o.WasCollected ? o : null;

    private static bool Ensure()
    {
        if (_bundle != null && Alive(_flare) != null && Alive(_smoke) != null) return true;
        if (_loadFailed) return false;
        try
        {
            if (_bundle == null)
            {
                string path = null;
                foreach (var p in new[]
                         {
                             Path.Combine(Plugin.PluginDir, "munitions.bundle"),
                             Path.Combine(Plugin.PluginDir, "MDMunitions", "munitions.bundle"),
                             Path.Combine(Plugin.PluginDir, "Munitions", "munitions.bundle")
                         })
                    if (File.Exists(p)) { path = p; break; }
                if (path == null)
                {
                    _loadFailed = true;
                    Plugin.Log.LogWarning("munitions.bundle not found — ATGM flare, smoke and launch sound disabled");
                    return false;
                }
                _bundle = BundleLoader.LoadFromFile(path);
                if (_bundle == null) throw new InvalidOperationException("bundle load returned null");
            }
            _flare = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/flaremat.mat"));
            _smoke = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/smokemat.mat"));
            _launch = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/atgm_launch.wav"));
            _kornet = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/kornet_launch.wav"));
            _launcherIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/textures/launchericon.png"));
            _ejectorIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/textures/ejectoricon.png"));
            _azLoad = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/az_load.wav"));
            _azLoop = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/az_rotate_loop.wav"));
            _mzLoad = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/mz_load.wav"));
            _mzLoop = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/mz_rotate_loop.wav"));
            _mzStart = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/mz_rotate_start.wav"));
            _casingDrop = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/casing_drop.wav"));
            _beltLoad = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/belt_load.wav"));
            _beltEnd = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/belt_end.wav"));
            _beltTick = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/belt_tick.wav"));
            _beltLoop = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/belt_loop.wav"));
            for (var i = 0; i < _rocketLaunch.Length; i++)
                _rocketLaunch[i] = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, $"assets/munitions/audio/rocket_launch_{i + 1}.wav"));
            _thermobaric = Keep(BundleLoader.LoadAsset<AudioClip>(_bundle, "assets/munitions/audio/thermobaric_blast.wav"));
            _rocketIcon = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/textures/rocketicon.png"));
            _casing = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/casingmat.mat"));
            _fxAdd = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/fxaddmat.mat"));
            _fxAlpha = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/fxalphamat.mat"));
            _smokeLit = Keep(BundleLoader.LoadAsset<Material>(_bundle, "assets/munitions/smokelitmat.mat"));
            _boreBase = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/bore/smoothcannonbore_basecolor.png"));
            _boreMask = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/bore/smoothcannonbore_mask.png"));
            _boreNormal = Keep(BundleLoader.LoadAsset<Texture2D>(_bundle, "assets/munitions/bore/smoothcannonbore_normal.png"));
            if (_flare == null || _smoke == null) throw new InvalidOperationException("materials missing in bundle");
            Plugin.Log.LogInfo($"Effects loaded (flare shader supported: {_flare.shader != null && _flare.shader.isSupported}, " +
                               $"particles: {_fxAdd != null && _fxAlpha != null}, lit smoke: {_smokeLit != null && _smokeLit.shader != null && _smokeLit.shader.isSupported}, " +
                               $"sound: {_launch != null}, smooth bore: {_boreBase != null && _boreMask != null && _boreNormal != null}, " +
                               $"loader sounds: AZ {_azLoad != null && _azLoop != null}, MZ {_mzLoad != null && _mzLoop != null && _mzStart != null}, " +
                               $"belt {_beltLoad != null && _beltEnd != null && _beltTick != null && _beltLoop != null}, " +
                               $"rockets {_rocketLaunch[0] != null && _thermobaric != null && _rocketIcon != null})");
            return true;
        }
        catch (Exception e)
        {
            _loadFailed = true;
            Plugin.Log.LogError("Effects bundle failed: " + e);
            return false;
        }
    }

    private static T Keep<T>(T o) where T : Object
    {
        if (o != null) o.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return o;
    }

    public static Mesh Quad
    {
        get
        {
            if (_quad != null && !_quad.WasCollected) return _quad;
            _quad = new Mesh { name = "MunitionsFlareQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _quad.RecalculateBounds();
            _quad.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object.DontDestroyOnLoad(_quad);
            return _quad;
        }
    }

    
    public static void PlayLaunch(Vector3 position, Transform part = null)
    {
        if (SoundBridge.Play("atgm.gun.fire", position, part, Plugin.AtgmSoundVolume.Value)) return;
        Play(LaunchClip, Plugin.AtgmSoundVolume.Value, null, position);
    }

    
    
    public static bool PlayKornet(Transform parent, Vector3 position, bool wire)
    {
        if (SoundBridge.Play(wire ? "atgm.wire.pre" : "atgm.beam.pre", position, parent, Plugin.LauncherSoundVolume.Value)) return true;
        Play(KornetClip, Plugin.LauncherSoundVolume.Value, parent, position);
        return false;
    }

    public static void PlayTubeFire(Transform parent, Vector3 position, bool wire) =>
        SoundBridge.Play(wire ? "atgm.wire.fire" : "atgm.beam.fire", position, parent, Plugin.LauncherSoundVolume.Value);

    
    public static void PlayRocketLaunch(Transform parent, Vector3 position)
    {
        var volume = Plugin.RocketSoundVolume.Value;
        var pitch = UnityEngine.Random.Range(0.95f, 1.05f);
        if (SoundBridge.Play("rocket.fire", position, parent, volume, pitch)) return;
        Play(RocketLaunchClip, volume, parent, position, 15f, pitch);
    }

    
    
    public static void PlayThermobaric(Vector3 position, int caliber)
    {
        if (SoundBridge.Active) return;
        var pitch = Mathf.Clamp(1.12f - caliber / 900f, 0.82f, 1.08f) * UnityEngine.Random.Range(0.96f, 1.04f);
        Play(ThermobaricClip, Plugin.RocketSoundVolume.Value, null, position, 40f, pitch);
    }

    
    public static void PlayCasingDrop(Transform parent, Vector3 position, float volume, float pitch, bool first)
    {
        
        if (SoundBridge.Active)
        {
            if (first) SoundBridge.Play("casing.drop", position, parent, volume, pitch);
            return;
        }
        var src = Play(CasingDropClip, volume, parent, position, 3f, pitch);
        if (src != null) src.priority = 96;   
    }

    
    public static AudioSource NewSource(GameObject go, float minDistance)
    {
        var src = go.AddComponent<AudioSource>();
        Setup(src, minDistance);
        src.playOnAwake = false;
        src.volume = 0f;
        return src;
    }

    private static void Setup(AudioSource src, float minDistance)
    {
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = 3000f;
        src.dopplerLevel = 0f;
        src.priority = 32;
        var mixer = Mixer();
        if (mixer != null) src.outputAudioMixerGroup = mixer;
    }

    private static AudioSource Play(AudioClip clip, float volumeSetting, Transform parent, Vector3 position, float minDistance = 12f, float pitch = 1f)
    {
        try
        {
            var volume = Mathf.Clamp(volumeSetting, 0f, 4f);
            if (clip == null || volume <= 0f) return null;
            var go = new GameObject("Munitions sound");
            go.transform.position = position;
            if (parent != null) go.transform.SetParent(parent, true);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            Setup(src, minDistance);
            src.volume = Mathf.Min(1f, volume);
            src.pitch = pitch;
            if(src.isActiveAndEnabled)src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
            return src;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Sound failed: " + e.Message);
            return null;
        }
    }

    
    private static AudioMixerGroup Mixer()
    {
        if (_mixer != null && !_mixer.WasCollected) return _mixer;
        if (_mixerSearched) return null;
        _mixerSearched = true;
        try
        {
            AudioMixerGroup any = null;
            var counts = new Dictionary<IntPtr, (AudioMixerGroup Group, int Count)>();
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                var g = s != null ? s.outputAudioMixerGroup : null;
                if (g == null) continue;
                var n = (g.name ?? "").ToLowerInvariant();
                if (n.Contains("music") || n.Contains("ui") || n.Contains("voice") || n.Contains("ambien")) continue;
                if (n.Contains("sfx") || n.Contains("effect") || n.Contains("weapon") || n.Contains("combat") || n.Contains("explos"))
                {
                    _mixer = g;
                    break;
                }
                counts[g.Pointer] = counts.TryGetValue(g.Pointer, out var c) ? (g, c.Count + 1) : (g, 1);
            }
            if (_mixer == null)
            {
                var best = 0;
                foreach (var c in counts.Values)
                    if (c.Count > best) { best = c.Count; any = c.Group; }
                _mixer = any;
            }
            if (Plugin.DebugLogging.Value) Plugin.Log.LogInfo($"ATGM sound mixer group: {(_mixer != null ? _mixer.name : "none")}");
            if (_mixer == null) _mixerSearched = false;
        }
        catch (Exception e)
        {
            if (Plugin.DebugLogging.Value) Plugin.Log.LogWarning("Mixer search failed: " + e.Message);
        }
        return _mixer;
    }
}


internal sealed class MissileFx
{
    private const float SmokeLife = 2.8f;
    private const float PointSpacing = 1.5f;

    private GameObject _flare, _trail;
    private Mesh _mesh;
    private readonly List<Vector3> _points = new();
    private readonly List<float> _times = new();
    private Vector3 _head, _headVel;
    private bool _hasHead, _emitting = true;
    private readonly float _seed = UnityEngine.Random.value * 100f;

    public static MissileFx Create(Vector3 position)
    {
        var fx = new MissileFx { _head = position, _hasHead = true };
        try
        {
            var flareMat = Fx.FlareMat;
            var smokeMat = Fx.SmokeMat;
            if (flareMat != null)
            {
                fx._flare = new GameObject("ATGM flare");
                fx._flare.transform.position = position;
                fx._flare.AddComponent<MeshFilter>().sharedMesh = Fx.Quad;
                var r = fx._flare.AddComponent<MeshRenderer>();
                r.sharedMaterial = flareMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (smokeMat != null)
            {
                fx._trail = new GameObject("ATGM smoke");
                fx._mesh = new Mesh { name = "ATGM smoke" };
                fx._mesh.MarkDynamic();
                fx._trail.AddComponent<MeshFilter>().sharedMesh = fx._mesh;
                var r = fx._trail.AddComponent<MeshRenderer>();
                r.sharedMaterial = smokeMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("ATGM visuals failed: " + e.Message); }
        return fx;
    }

    public void SetHead(Vector3 position, Vector3 velocity)
    {
        _head = position;
        _headVel = velocity;
        _hasHead = true;
        
        
        if (_flare != null && !_flare.WasCollected)
            _flare.transform.position = position - (velocity.sqrMagnitude > 1f ? velocity.normalized : Vector3.forward) * .3f;
    }

    public void StopEmitting()
    {
        _emitting = false;
        if (_flare != null && !_flare.WasCollected) _flare.SetActive(false);
        StopFlightSound(0.4f);
    }

    
    private GameObject _sound;
    private int _soundLoop;

    public void StartFlightSound(float volume)
    {
        if (!SoundBridge.Active) return;
        try
        {
            _sound = new GameObject("ATGM sound");
            _sound.transform.position = _head;
            _soundLoop = SoundBridge.Loop("atgm.flight", _sound.transform, volume);
            if (_soundLoop == 0) { Object.Destroy(_sound); _sound = null; }
        }
        catch { _sound = null; }
    }

    private void StopFlightSound(float fade)
    {
        if (_soundLoop == 0) return;
        SoundBridge.StopLoop(_soundLoop, fade);
        _soundLoop = 0;
    }

    
    public bool Update(Camera cam)
    {
        var now = Time.time;
        var dir = _headVel.sqrMagnitude > 1f ? _headVel.normalized : Vector3.forward;
        var nozzle = _head - dir * 0.8f;
        if (_sound != null && !_sound.WasCollected) _sound.transform.position = nozzle;

        if (_emitting && _hasHead)
        {
            if (_points.Count == 0 || (nozzle - _points[_points.Count - 1]).sqrMagnitude > PointSpacing * PointSpacing)
            {
                _points.Add(nozzle);
                _times.Add(now);
            }
        }
        var removed = 0;
        while (removed < _points.Count && now - _times[removed] > SmokeLife) removed++;
        if (removed > 0)
        {
            _points.RemoveRange(0, removed);
            _times.RemoveRange(0, removed);
        }

        if (cam == null) return _emitting || _points.Count > 0;
        var camPos = cam.transform.position;

        if (_flare != null && !_flare.WasCollected && _emitting)
        {
            var p = _head - dir * 0.3f;
            var dist = (p - camPos).magnitude;
            var flicker = 0.8f + 0.4f * Mathf.PerlinNoise(now * 17f, _seed);
            var size = Mathf.Max(1.6f, dist * 0.0045f) * flicker;
            var t = _flare.transform;
            t.position = p;
            t.rotation = cam.transform.rotation;
            t.localScale = new Vector3(size, size, size);
        }

        if (_mesh != null && !_mesh.WasCollected) Rebuild(camPos, now, _emitting ? nozzle : (Vector3?)null);
        return _emitting || _points.Count > 0;
    }

    private void Rebuild(Vector3 camPos, float now, Vector3? head)
    {
        var n = _points.Count + (head.HasValue ? 1 : 0);
        _mesh.Clear();
        if (n < 2) return;

        var verts = new Vector3[n * 2];
        var uvs = new Vector2[n * 2];
        var colors = new Color[n * 2];
        var tris = new int[(n - 1) * 6];
        var pts = new Vector3[n];
        var ages = new float[n];
        for (var i = 0; i < _points.Count; i++)
        {
            ages[i] = now - _times[i];
            
            pts[i] = _points[i] + Vector3.up * (0.35f * ages[i]);
        }
        if (head.HasValue)
        {
            pts[n - 1] = head.Value;
            ages[n - 1] = 0f;
        }

        for (var i = 0; i < n; i++)
        {
            var p = pts[i];
            var tangent = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
            var side = Vector3.Cross(tangent, camPos - p);
            side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.up;
            var u = Mathf.Clamp01(ages[i] / SmokeLife);
            var width = Mathf.Lerp(0.3f, 2.6f, Mathf.Sqrt(u));
            var alpha = Mathf.Clamp01(ages[i] / 0.06f + 0.15f) * Mathf.Sqrt(1f - u);
            verts[i * 2] = p - side * (width * 0.5f);
            verts[i * 2 + 1] = p + side * (width * 0.5f);
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
            colors[i * 2] = colors[i * 2 + 1] = new Color(1f, 1f, 1f, alpha);
        }
        for (var i = 0; i < n - 1; i++)
        {
            var v = i * 2;
            var t = i * 6;
            tris[t] = v;
            tris[t + 1] = v + 1;
            tris[t + 2] = v + 2;
            tris[t + 3] = v + 1;
            tris[t + 4] = v + 3;
            tris[t + 5] = v + 2;
        }
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.colors = colors;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
    }

    public void Destroy()
    {
        try
        {
            StopFlightSound(0.05f);
            if (_sound != null && !_sound.WasCollected) Object.Destroy(_sound, 0.5f);
            if (_flare != null && !_flare.WasCollected) Object.Destroy(_flare);
            if (_trail != null && !_trail.WasCollected) Object.Destroy(_trail);
            if (_mesh != null && !_mesh.WasCollected) Object.Destroy(_mesh);
        }
        catch { }
    }
}
