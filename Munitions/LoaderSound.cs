using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Munitions;













internal sealed class LoaderVoice
{
    
    private const float AzLoopLevel = 0.8f, MzLoopLevel = 0.9f, MzStartLevel = 1f, LoadLevel = 1f;
    private const float BeltLoopLevel = 0.75f, BeltTickLevel = 0.45f, BeltEndLevel = 0.9f;
    private const float AzLoopFadeIn = 0.3f, BeltLoopFadeIn = 0.2f;
    private const float StartToLoop = 0.25f;
    private const float MzLoopEntry = 0.838f;
    private const float LoadFadeInDry = 0.02f;
    
    
    private const float RotationCut = 0.02f, LoadAttack = 0.005f;
    private const float StopFade = 0.45f, AbortFade = 0.25f;
    private const float WaitHum = 0.35f;   
    private const float Silent = 0.0005f;

    private static readonly System.Random Rng = new();

    private readonly LoaderType _type;
    private readonly bool _mz, _belt;
    private GameObject _go;
    private Transform _parent;
    private AudioSource _loop, _start, _load;
    private AudioSource _end, _tickA, _tickB;   
    private bool _tickFlip;
    private float _beltPos = float.NaN, _lastAngle; 

    
    private float _rotT0 = -1f;           
    private float _handT0 = -1f;          
    private float _handLoop, _handStart;  
    private float _handDuration = RotationCut;
    private float _stopT0 = -1f;          
    private float _loadT0 = -1f, _loadFadeIn;
    private float _abortT0 = -1f, _abortLoop, _abortStart, _abortLoad;
    private float _loopEnv, _startEnv, _loadEnv;
    private float _pitch = 1f, _lastSpeed;

    public LoaderVoice(LoaderType type)
    {
        _type = type;
        _mz = type == LoaderType.Mz;
        _belt = type == LoaderType.Belt;
    }

    public LoaderType Type => _type;

    
    private static int _nextId;
    private readonly int _id = ++_nextId;
    private bool _external, _stopSent;
    private float _extStart;

    
    public float LoadLength
    {
        get
        {
            var external = _belt ? 0f : SoundBridge.CarouselLoadLength(_mz);
            if (external > 0.1f) return external;
            var clip = _belt ? Fx.BeltLoadClip : _mz ? Fx.MzLoadClip : Fx.AzLoadClip;
            try { return clip != null ? clip.length : 0f; }
            catch { return 0f; }
        }
    }

    

    
    public void OnRotationStart(Transform parent)
    {
        if (!_belt && SoundBridge.Carousel(_id, 0, parent, _mz, 0f))
        {
            _external = true;
            _stopSent = false;
            _extStart = Time.time;
            SilenceOwn();
            return;
        }
        _external = false;
        if (!Ensure(parent)) return;
        var now = Time.time;
        _rotT0 = now;
        _handT0 = -1f;
        _stopT0 = -1f;
        _abortT0 = -1f;
        _lastSpeed = 0f;
        var loopClip = _belt ? Fx.BeltLoopClip : _mz ? Fx.MzLoopClip : Fx.AzLoopClip;
        if (loopClip != null)
        {
            _loop.Stop();
            _loop.clip = loopClip;
            _loop.loop = true;
            _loop.volume = 0f;
            var n = loopClip.samples;
            if (n > 0)
            {
                if (_mz)
                {
                    
                    var lead = Mathf.Max(0f, StartLength - StartToLoop);
                    _loop.timeSamples = Wrap((int)((MzLoopEntry - lead) * loopClip.frequency), n);
                }
                else _loop.timeSamples = Rng.Next(0, n);
            }
            _pitch = _mz ? 1f : _belt ? 0.86f : 0.9f;
            _loop.pitch = _pitch;
            if(_loop.isActiveAndEnabled)_loop.Play();
        }
        else if (_belt) Plugin.Log.LogWarning("Belt loader: belt sound missing in munitions.bundle (old bundle?)");
        if (_mz && Fx.MzStartClip != null)
        {
            _start.Stop();
            _start.clip = Fx.MzStartClip;
            _start.loop = false;
            _start.pitch = 1f;
            _start.timeSamples = 0;
            _start.volume = 0f;
            if(_start.isActiveAndEnabled)_start.Play();
        }
        _speedLevel = _mz ? 1f : 0.6f;
        _loopEnv = 0f;
        _startEnv = _mz ? 1f : 0f;
        Apply();
    }

    
    public void OnLoadStart(Transform parent)
    {
        if (!_belt && SoundBridge.Carousel(_id, 2, parent, _mz, 0f))
        {
            _external = true;
            _stopSent = true;
            SilenceOwn();
            return;
        }
        _external = false;
        if (!Ensure(parent)) return;
        var now = Time.time;
        _abortT0 = -1f;
        
        var rotationAudible = _loopEnv > 0.02f || _startEnv > 0.02f;
        if (_rotT0 >= 0f || rotationAudible)
        {
            _handT0 = now;
            _handLoop = _loopEnv;
            _handStart = _startEnv;
        }
        _loadFadeIn = rotationAudible ? LoadAttack : LoadFadeInDry;
        _handDuration = RotationCut;
        _loadT0 = now;
        var clip = _belt ? Fx.BeltLoadClip : _mz ? Fx.MzLoadClip : Fx.AzLoadClip;
        if (clip != null)
        {
            _load.Stop();
            _load.clip = clip;
            _load.loop = false;
            _load.pitch = 1f;
            _load.timeSamples = 0;
            _load.volume = 0f;
            if(_load.isActiveAndEnabled)_load.Play();
        }
        Apply();
    }

    
    public void OnAbort()
    {
        if (_external)
        {
            SoundBridge.Carousel(_id, 3, null, _mz, 0f);
            _external = false;
            return;
        }
        if (_abortT0 >= 0f) return;
        _abortT0 = Time.time;
        _abortLoop = _loopEnv;
        _abortStart = _startEnv;
        _abortLoad = _loadEnv;
    }

    

    public void Update(Carousel c, float dt)
    {
        if (_external)
        {
            
            
            
            
            var v = c.DegSpeed > 1f ? Mathf.Clamp01(Mathf.Abs(c.Velocity) / c.DegSpeed) : 0f;
            SoundBridge.Carousel(_id, 4, null, _mz, v);
            return;
        }
        if (_go == null || _go.WasCollected || !_go) return;
        var now = Time.time;

        
        
        var inside = PlayerTracker.InControl && PlayerTracker.Scoped && c.Rack.VehicleKey != 0 && c.Rack.VehicleKey == PlayerTracker.VehicleKey;
        var placeTarget = inside ? 1f : Mathf.Clamp01(Plugin.AutoloaderOutsideVolume.Value);
        _place = _placeInit ? _place + (placeTarget - _place) * (1f - Mathf.Exp(-Mathf.Max(0f, Time.unscaledDeltaTime) / 0.15f)) : placeTarget;
        _placeInit = true;
        var speed = c.DegSpeed > 1f ? Mathf.Clamp01(Mathf.Abs(c.Velocity) / c.DegSpeed) : 0f;
        var decelerating = speed < _lastSpeed - 1e-4f;
        _lastSpeed = speed;
        if (_belt) CellTicks(c);

        
        float loopEnv = 0f, startEnv = 0f;
        if (_rotT0 >= 0f)
        {
            var t = now - _rotT0;
            if (_mz)
            {
                var x = t - (StartLength - StartToLoop);
                var u = Mathf.Clamp01(x / StartToLoop);
                loopEnv = x <= 0f ? 0f : Mathf.Sin(u * Mathf.PI * 0.5f);
                startEnv = x <= 0f ? 1f : Mathf.Cos(u * Mathf.PI * 0.5f);
                if (t > StartLength) startEnv = 0f;
            }
            else loopEnv = Mathf.Sin(Mathf.Clamp01(t / (_belt ? BeltLoopFadeIn : AzLoopFadeIn)) * Mathf.PI * 0.5f);

            
            
            var stopped = c.Phase == AutoloaderPhase.Rotating && Mathf.Abs(c.Velocity) < 0.01f && t > 0.15f;
            if (stopped && _handT0 < 0f)
            {
                if (_stopT0 < 0f) _stopT0 = now;
                var u = Mathf.Clamp01((now - _stopT0) / StopFade);
                var k = Mathf.Lerp(1f, WaitHum, 0.5f - 0.5f * Mathf.Cos(u * Mathf.PI));
                loopEnv *= k;
                startEnv *= Mathf.Cos(u * Mathf.PI * 0.5f);
            }
        }
        
        if (_handT0 >= 0f)
        {
            var k = Mathf.Cos(Mathf.Clamp01((now - _handT0) / _handDuration) * Mathf.PI * 0.5f);
            loopEnv = _handLoop * k;
            startEnv = _handStart * k;
            if (k <= 0f)
            {
                _handT0 = -1f;
                _rotT0 = -1f;
            }
        }

        
        var loadEnv = 0f;
        if (_loadT0 >= 0f)
        {
            loadEnv = Mathf.Sin(Mathf.Clamp01((now - _loadT0) / Mathf.Max(0.001f, _loadFadeIn)) * Mathf.PI * 0.5f);
            if (_load != null && !_load.isPlaying && now - _loadT0 > 0.1f)
            {
                _loadT0 = -1f;
                
                if (_belt && _abortT0 < 0f) PlayOnce(_end, Fx.BeltEndClip, BeltEndLevel, 1f);
            }
        }

        
        if (_abortT0 >= 0f)
        {
            var k = Mathf.Cos(Mathf.Clamp01((now - _abortT0) / AbortFade) * Mathf.PI * 0.5f);
            loopEnv = _abortLoop * k;
            startEnv = _abortStart * k;
            loadEnv = _abortLoad * k;
            if (k <= 0f)
            {
                _abortT0 = -1f;
                _rotT0 = _handT0 = _loadT0 = -1f;
            }
        }

        _loopEnv = loopEnv;
        _startEnv = startEnv;
        _loadEnv = loadEnv;

        
        
        var sq = Mathf.Sqrt(speed);
        var slowing = decelerating || _handT0 >= 0f || _stopT0 >= 0f;
        var pitchTarget = _mz ? (slowing ? 0.92f + 0.08f * sq : 1f) : _belt ? 0.86f + 0.16f * sq : 0.9f + 0.1f * sq;
        var levelTarget = _mz && !slowing ? 1f : 0.6f + 0.4f * sq;
        var k2 = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / 0.08f);
        _pitch += (pitchTarget - _pitch) * k2;
        _speedLevel += (levelTarget - _speedLevel) * k2;
        Apply();
    }

    private float _speedLevel = 1f;
    private float _place = 1f;            
    private bool _placeInit;

    private void Apply()
    {
        var master = Mathf.Min(1f, Mathf.Max(0f, Plugin.AutoloaderVolume.Value)) * _place;
        if (_loop != null && !_loop.WasCollected)
        {
            
            
            var v = master * (_mz ? MzLoopLevel : _belt ? BeltLoopLevel : AzLoopLevel) * _loopEnv * _speedLevel;
            _loop.volume = Mathf.Clamp01(v);
            _loop.pitch = _pitch;
            if (_loopEnv <= Silent && _rotT0 < 0f && _handT0 < 0f && _loop.isPlaying) _loop.Stop();
        }
        if (_start != null && !_start.WasCollected)
        {
            _start.volume = Mathf.Clamp01(master * MzStartLevel * _startEnv);
            if (_startEnv <= Silent && (_rotT0 < 0f || Time.time - _rotT0 > StartLength) && _start.isPlaying) _start.Stop();
        }
        if (_load != null && !_load.WasCollected)
        {
            _load.volume = Mathf.Clamp01(master * LoadLevel * _loadEnv);
            if (_loadT0 < 0f && _abortT0 < 0f && _load.isPlaying && _loadEnv <= Silent) _load.Stop();
        }
    }

    
    
    private void CellTicks(Carousel c)
    {
        var moving = c.Phase == AutoloaderPhase.Rotating;
        if (!moving || float.IsNaN(_beltPos))
        {
            _beltPos = c.Angle / c.Step;
            _lastAngle = c.Angle;
            return;
        }
        var before = _beltPos;
        var after = before + Mathf.DeltaAngle(_lastAngle, c.Angle) / c.Step;
        _lastAngle = c.Angle;
        if (Mathf.Abs(after - Mathf.Round(after)) < 1e-3f) after = Mathf.Round(after);
        _beltPos = after;
        var crossed = after > before ? Mathf.FloorToInt(after) - Mathf.FloorToInt(before)
            : Mathf.CeilToInt(before) - Mathf.CeilToInt(after);
        if (crossed <= 0) return;
        _tickFlip = !_tickFlip;
        PlayOnce(_tickFlip ? _tickA : _tickB, Fx.BeltTickClip, BeltTickLevel * (0.85f + 0.15f * _speedLevel), _pitch * (0.96f + 0.08f * (float)Rng.NextDouble()));
    }

    
    private void PlayOnce(AudioSource src, AudioClip clip, float level, float pitch)
    {
        if (src == null || src.WasCollected || clip == null) return;
        try
        {
            src.Stop();
            src.clip = clip;
            src.loop = false;
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(Mathf.Min(1f, Mathf.Max(0f, Plugin.AutoloaderVolume.Value)) * _place * level);
            src.timeSamples = 0;
            if(src.isActiveAndEnabled)src.Play();
        }
        catch { }
    }

    private static float StartLength
    {
        get
        {
            try { return Fx.MzStartClip != null ? Fx.MzStartClip.length : 0f; }
            catch { return 0f; }
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    
    private bool Ensure(Transform parent)
    {
        if (parent == null || parent.WasCollected || !parent.gameObject.activeInHierarchy) return false;
        if (_go != null && !_go.WasCollected && _go && _parent == parent) return true;
        Destroy();
        try
        {
            _parent = parent;
            _go = new GameObject("Munitions loader sound");
            _go.transform.SetParent(parent, false);
            _loop = Fx.NewSource(_go, 4f);
            _start = Fx.NewSource(_go, 4f);
            _load = Fx.NewSource(_go, 4f);
            if (_belt)
            {
                _end = Fx.NewSource(_go, 4f);
                _tickA = Fx.NewSource(_go, 3f);
                _tickB = Fx.NewSource(_go, 3f);
            }
            return _loop != null && _start != null && _load != null;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Loader sound failed: " + e.Message);
            Destroy();
            return false;
        }
    }

    
    private void SilenceOwn()
    {
        _rotT0 = _handT0 = _stopT0 = _loadT0 = _abortT0 = -1f;
        _loopEnv = _startEnv = _loadEnv = 0f;
        try
        {
            if (_loop != null && !_loop.WasCollected) _loop.Stop();
            if (_start != null && !_start.WasCollected) _start.Stop();
            if (_load != null && !_load.WasCollected) _load.Stop();
        }
        catch { }
    }

    public void Destroy()
    {
        if (_external)
        {
            SoundBridge.Carousel(_id, 3, null, _mz, 0f);
            _external = false;
        }
        try { if (_go != null && !_go.WasCollected && _go) Object.Destroy(_go); } catch { }
        _go = null;
        _parent = null;
        _loop = _start = _load = null;
        _end = _tickA = _tickB = null;
        _beltPos = float.NaN;
        _rotT0 = _handT0 = _stopT0 = _loadT0 = _abortT0 = -1f;
        _loopEnv = _startEnv = _loadEnv = 0f;
    }
}
