using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Properties;
using Sprocket.UI;
using Sprocket.VehicleControl;
using Sprocket.VehicleDesigner.Modules;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Serialization;
using Sprocket.Vehicles.UI;
using Sprocket.Vehicles.Weapons;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace ThermalSight;






internal static class Reticles
{
    private const string Key = "reticle";
    private const int MaxBytes = 4 * 1024 * 1024;
    public const float MinScale = 0.25f, MaxScale = 3f;

    public const float MaxOffset = 0.5f;   

    internal sealed class Data
    {
        public string Name;
        public byte[] Png;
        public float Scale = 1f;
        public float OffsetX, OffsetY;      
        public Texture2D Tex;
        public float TexOffsetX, TexOffsetY; 
        public bool Failed;
        
        public byte[] Pixels;
        public int Width, Height;
    }

    private static readonly Dictionary<IntPtr, Data> ByObject = new();

    public static string Folder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Sprocket", "Reticles");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    private static bool IsSight(VehicleObject vo)
    {
        if (vo == null) return false;
        try
        {
            foreach (var c in vo.behaviours)
                if (c != null && c.TryCast<GunnerSight>() != null) return true;
        }
        catch { }
        return false;
    }

    private static Data Of(VehicleObject vo)
    {
        if (vo == null) return null;
        return ByObject.TryGetValue(vo.Pointer, out var d) ? d : null;
    }

    

    
    private static string Encode(Data d)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return "v2|" + d.Scale.ToString("0.###", ci) + "|" + d.OffsetX.ToString("0.####", ci) + "|" + d.OffsetY.ToString("0.####", ci) + "|" +
               (d.Name ?? "reticle").Replace("|", "/") + "|" + Convert.ToBase64String(d.Png);
    }

    private static Data Decode(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('|');
        var v2 = token.StartsWith("v2|");
        if (!v2 && !token.StartsWith("v1|")) return null;
        if (parts.Length < (v2 ? 6 : 4)) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float F(string s, float def) => float.TryParse(s, System.Globalization.NumberStyles.Float, ci, out var f) && !float.IsNaN(f) ? f : def;
        try
        {
            var d = new Data
            {
                Scale = Mathf.Clamp(F(parts[1], 1f), MinScale, MaxScale),
                Name = parts[v2 ? 4 : 2],
                Png = Convert.FromBase64String(parts[v2 ? 5 : 3])
            };
            if (v2)
            {
                d.OffsetX = Mathf.Clamp(F(parts[2], 0f), -MaxOffset, MaxOffset);
                d.OffsetY = Mathf.Clamp(F(parts[3], 0f), -MaxOffset, MaxOffset);
            }
            return d.Png.Length > 0 ? d : null;
        }
        catch { return null; }
    }

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ToBlueprint))]
    private static class SavePatch
    {
        private static void Postfix(VehicleObject obj, VehicleObjectBlueprint __result)
        {
            try
            {
                var d = Of(obj);
                var state = __result?.State;
                if (d?.Png == null || state == null || state.FindElement(Key) >= 0) return;
                state.AddValue(Key, new Il2CppSystem.Object(IL2CPP.ManagedStringToIl2Cpp(Encode(d))));
            }
            catch (Exception e) { Plugin.Log.LogError("Reticle save failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(VehicleObjectSerialization), nameof(VehicleObjectSerialization.ApplyBaseObjectState))]
    private static class LoadPatch
    {
        private static void Postfix(VehicleObject obj, Il2CppSystem.Runtime.Serialization.SerializationInfo state)
        {
            try
            {
                if (obj == null || state == null || state.FindElement(Key) < 0) return;
                var d = Decode(state.GetString(Key));
                if (d != null) ByObject[obj.Pointer] = d;
                if (ByObject.Count > 512) Cleanup();
            }
            catch (Exception e) { Plugin.Log.LogError("Reticle load failed: " + e.Message); }
        }
    }

    private static void Cleanup()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in ByObject)
        {
            try
            {
                var vo = new VehicleObject(kv.Key);
                if (vo == null || !vo) dead.Add(kv.Key);
            }
            catch { dead.Add(kv.Key); }
        }
        foreach (var k in dead) ByObject.Remove(k);
    }

    

    [HarmonyPatch(typeof(VehicleObjectEditor), nameof(VehicleObjectEditor.OnGUI))]
    private static class PanelPatch
    {
        private static void Postfix(VehicleObjectEditor __instance, IGUILayout layout)
        {
            try
            {
                var target = __instance.target;
                if (target == null || !IsSight(target)) return;
                var drawer = layout.TryCast<IGUIElementDrawer>();
                if (drawer == null) return;
                Draw(__instance, layout, drawer, target);
            }
            catch (Exception e) { Plugin.Log.LogError("Reticle panel failed: " + e); }
        }
    }

    private static void Draw(VehicleObjectEditor editor, IGUILayout layout, IGUIElementDrawer drawer, VehicleObject target)
    {
        var current = Of(target);
        var files = new List<string>();
        try
        {
            foreach (var f in Directory.GetFiles(Folder, "*.png")) files.Add(f);
            files.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { }

        var options = new Il2CppSystem.Collections.Generic.List<string>();
        options.Add("Game default");
        var selected = 0;
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            options.Add(name);
            if (current != null && string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase)) selected = options.Count - 1;
        }
        var embeddedOnly = current != null && selected == 0;
        if (embeddedOnly)
        {
            options.Add(current.Name + " (in this tank)");
            selected = options.Count - 1;
        }

        drawer.Header("Reticle");
        UnityAction<int> onSelect = (Action<int>)(index =>
        {
            try
            {
                if (index <= 0) Clear(target);
                else if (index - 1 < files.Count) Load(target, files[index - 1]);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Reticle change failed: " + e); }
        });
        drawer.Dropdown("Reticle", options.Cast<Il2CppSystem.Collections.Generic.IReadOnlyList<string>>(), selected, onSelect,
            "The aiming reticle of THIS sight. Game default, or a PNG image from Documents\\My Games\\Sprocket\\Reticles. " +
            "The image is stored inside the tank file, so the tank keeps its reticle on another computer.");

        if (current != null)
        {
            
            var tipEdit = new UITooltip("Edit reticle",
                "Opens the view through this sight: drag the reticle to move it, mouse wheel or the corner handles to resize, then Save.");
            UnityAction onEdit = (Action)(() =>
            {
                try { ReticleEditor.Open(target, current, () => { try { editor.RequestRedraw(); } catch { } }); }
                catch (Exception e) { Plugin.Log.LogError("Reticle editor failed: " + e); }
            });
            drawer.Button("Edit reticle", onEdit, ref tipEdit);
            var moved = Mathf.Abs(current.OffsetX) > 1e-4f || Mathf.Abs(current.OffsetY) > 1e-4f;
            drawer.InfoField($"Size {current.Scale * 100f:0}%" + (moved
                ? $", moved {current.OffsetX * 100f:+0.0;-0.0}% right / {current.OffsetY * 100f:+0.0;-0.0}% up."
                : ", centred."), 1);
        }

        var tipFile = new UITooltip("Choose file", "Pick any PNG file; it is copied into the Reticles folder and used for this sight.");
        UnityAction onFile = (Action)(() =>
        {
            try
            {
                var path = FileDialog.OpenPng(Folder);
                if (string.IsNullOrEmpty(path)) return;
                var dest = Path.Combine(Folder, Path.GetFileName(path));
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    File.Copy(path, dest, true);
                Load(target, dest);
                editor.RequestRedraw();
            }
            catch (Exception e) { Plugin.Log.LogError("Reticle file failed: " + e); }
        });
        drawer.Button("Choose file...", onFile, ref tipFile);

        var tipFolder = new UITooltip("Open folder", "Opens the Reticles folder: put your PNG reticles there.");
        UnityAction onFolder = (Action)(() =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Folder + "\""); }
            catch (Exception e) { Plugin.Log.LogWarning("Can't open the Reticles folder: " + e.Message); }
        });
        drawer.Button("Open reticles folder", onFolder, ref tipFolder);

        var info = current == null
            ? "This sight uses the game's reticle."
            : $"'{current.Name}' ({current.Png.Length / 1024} KB, saved with the tank). " + (current.Failed ? "The image can't be read!" : "");
        drawer.InfoField(info + " PNG with transparency, square works best; the image centre is the aim point.", 3);
    }

    private static void Clear(VehicleObject target)
    {
        if (ByObject.TryGetValue(target.Pointer, out var d) && d.Tex != null) try { Object.Destroy(d.Tex); } catch { }
        ByObject.Remove(target.Pointer);
        Plugin.Log.LogInfo("Sight reticle: game default");
    }

    private static void Load(VehicleObject target, string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length > MaxBytes) throw new InvalidOperationException($"{Path.GetFileName(file)} is larger than {MaxBytes / 1024 / 1024} MB");
        var old = Of(target);
        var d = new Data
        {
            Name = Path.GetFileNameWithoutExtension(file), Png = bytes, Scale = old?.Scale ?? 1f,
            OffsetX = old?.OffsetX ?? 0f, OffsetY = old?.OffsetY ?? 0f
        };
        if (!EnsureTexture(d)) throw new InvalidOperationException(Path.GetFileName(file) + " is not a readable PNG image");
        if (old?.Tex != null) try { Object.Destroy(old.Tex); } catch { }
        ByObject[target.Pointer] = d;
        Plugin.Log.LogInfo($"Sight reticle: {d.Name} ({d.Tex.width}x{d.Tex.height})");
    }

    private static bool EnsureTexture(Data d)
    {
        var alive = false;
        try { alive = d.Tex != null && !d.Tex.WasCollected && d.Tex; } catch { }
        var sameShift = Mathf.Abs(d.TexOffsetX - d.OffsetX) < 1e-4f && Mathf.Abs(d.TexOffsetY - d.OffsetY) < 1e-4f;
        if (alive && sameShift) return true;
        if (d.Failed || d.Png == null) return false;
        try
        {
            Texture2D tex;
            if (Mathf.Abs(d.OffsetX) < 1e-4f && Mathf.Abs(d.OffsetY) < 1e-4f)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(d.Png)))
                {
                    d.Failed = true;
                    Object.Destroy(tex);
                    return false;
                }
            }
            else
            {
                if (d.Pixels == null && !ReadPixels(d)) { d.Failed = true; return false; }
                tex = Shifted(d);
            }
            tex.name = "Reticle " + d.Name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (alive) try { Object.Destroy(d.Tex); } catch { }
            d.Tex = tex;
            d.TexOffsetX = d.OffsetX;
            d.TexOffsetY = d.OffsetY;
            return true;
        }
        catch (Exception e)
        {
            d.Failed = true;
            Plugin.Log.LogWarning($"Reticle '{d.Name}' can't be read: {e.Message}");
            return false;
        }
    }

    
    private static bool ReadPixels(Data d)
    {
        var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(tmp, new Il2CppStructArray<byte>(d.Png))) return false;
            int w = tmp.width, h = tmp.height;
            if (w <= 0 || h <= 0) return false;
            var px = tmp.GetPixels32();
            if (px == null || px.Length < w * h) return false;
            var bytes = new byte[w * h * 4];
            Marshal.Copy(px.Pointer + 0x20, bytes, 0, bytes.Length); 
            d.Pixels = bytes;
            d.Width = w;
            d.Height = h;
            return true;
        }
        finally { Object.Destroy(tmp); }
    }

    
    
    
    private static unsafe Texture2D Shifted(Data d)
    {
        int w = d.Width, h = d.Height;
        var dx = Mathf.RoundToInt(d.OffsetX * w);
        var dy = Mathf.RoundToInt(d.OffsetY * h);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var mips = Math.Max(1, tex.mipmapCount);

        var sizes = new (int W, int H, int Offset)[mips];
        var total = 0;
        for (var i = 0; i < mips; i++)
        {
            var mw = Math.Max(1, w >> i);
            var mh = Math.Max(1, h >> i);
            sizes[i] = (mw, mh, total);
            total += mw * mh * 4;
        }
        var all = new byte[total];
        var src = d.Pixels;
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Clamp(y - dy, 0, h - 1);
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Clamp(x - dx, 0, w - 1);
                var s = (sy * w + sx) * 4;
                var o = (y * w + x) * 4;
                all[o] = src[s];
                all[o + 1] = src[s + 1];
                all[o + 2] = src[s + 2];
                all[o + 3] = src[s + 3];
            }
        }
        for (var i = 1; i < mips; i++)
        {
            var (pw, ph, po) = sizes[i - 1];
            var (cw, ch, co) = sizes[i];
            for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (var k = 0; k < 4; k++)
                {
                    var sx = Math.Min(pw - 1, x * 2 + (k & 1));
                    var sy = Math.Min(ph - 1, y * 2 + (k >> 1));
                    var s = po + (sy * pw + sx) * 4;
                    var sa = all[s + 3];
                    r += all[s] * sa;
                    g += all[s + 1] * sa;
                    b += all[s + 2] * sa;
                    a += sa;
                }
                var o = co + (y * cw + x) * 4;
                if (a > 0f)
                {
                    all[o] = (byte)Mathf.Clamp(Mathf.RoundToInt(r / a), 0, 255);
                    all[o + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(g / a), 0, 255);
                    all[o + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(b / a), 0, 255);
                }
                all[o + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(a / 4f), 0, 255);
            }
        }
        fixed (byte* p = all) tex.LoadRawTextureData((IntPtr)p, all.Length);
        tex.Apply(false, true);
        return tex;
    }

    

    private static ScopeOverlayVehicleControllerPlugin _plugin;
    private static float _nextFind;
    private static IntPtr _scopePtr;
    private static Texture _origTex;
    private static float _origRadius;
    private static bool _swapped;
    private static bool _lookRecorded;
    private static bool _hooked;
    private static int _errors;

    public static void EnsureRenderHook()
    {
        if (_hooked) return;
        _hooked = true;
        try
        {
            UnityAction cb = (Action)Render;
            Application.add_onBeforeRender(cb);
        }
        catch (Exception e) { Plugin.Log.LogWarning("Reticle render hook failed: " + e.Message); }
    }

    
    private static void Render()
    {
        try
        {
            if (!_lookRecorded) RecordLook();
            if (ByObject.Count == 0 && !_swapped) return;
            var scope = ScopeEffect();
            if (scope == null) return;
            if (scope.Pointer != _scopePtr) { _scopePtr = scope.Pointer; _swapped = false; }

            Data wanted = null;
            var sc = ThermalController.CurrentState != null ? ThermalController.CurrentState.scopeController : null;
            if (sc != null && sc.scoped)
            {
                var active = sc.activeSight;
                if (active != null)
                {
                    var vo = new GunnerSight(active.Pointer).GetComponent<VehicleObject>();
                    wanted = Of(vo);
                    if (wanted != null && !EnsureTexture(wanted)) wanted = null;
                }
            }

            
            if (DynamicSightCompat.HidesReticle(scope.overlay.value))
            {
                if (_swapped)
                {
                    scope.overlayRadius.value = _origRadius;
                    _swapped = false;
                }
                return;
            }

            if (wanted != null)
            {
                if (!_swapped)
                {
                    _origTex = scope.overlay.value;
                    _origRadius = scope.overlayRadius.value;
                    _swapped = true;
                }
                scope.overlay.overrideState = true;
                if (scope.overlay.value == null || scope.overlay.value.Pointer != wanted.Tex.Pointer) scope.overlay.value = wanted.Tex;
                scope.overlayRadius.value = _origRadius * wanted.Scale;
            }
            else if (_swapped)
            {
                scope.overlay.value = _origTex;
                scope.overlayRadius.value = _origRadius;
                _swapped = false;
            }
            
            else if (_origTex != null && IsOurs(scope.overlay.value))
            {
                scope.overlay.value = _origTex;
                scope.overlayRadius.value = _origRadius;
            }
        }
        catch (Exception e) { if (_errors++ < 5) Plugin.Log.LogWarning("Reticle swap failed: " + e.Message); }
    }

    private static bool IsOurs(Texture t)
    {
        if (t == null) return false;
        var ptr = t.Pointer;
        foreach (var d in ByObject.Values)
            if (d?.Tex != null && d.Tex.Pointer == ptr) return true;
        return false;
    }

    
    
    private static void RecordLook()
    {
        try
        {
            var sc = ThermalController.CurrentState != null ? ThermalController.CurrentState.scopeController : null;
            if (sc == null || !sc.scoped || _swapped) return;
            var scope = ScopeEffect();
            if (scope == null) return;
            _lookRecorded = true;
            ReticleEditor.Remember(scope.radius.value, scope.overlayRadius.value, scope.softness.value);
        }
        catch { _lookRecorded = true; }
    }

    
    internal static bool LensCircle(out Vector2 centre, out float radius)
    {
        centre = new Vector2(0.5f, 0.5f);
        radius = 0.45f;
        try
        {
            var scope = ScopeEffect();
            if (scope == null) return false;
            centre = scope.centre.value;
            radius = scope.radius.value;
            return radius > 0.01f;
        }
        catch { return false; }
    }

    private static Scope ScopeEffect()
    {
        if (_plugin == null || _plugin.WasCollected || !_plugin)
        {
            _plugin = null;
            if (Time.unscaledTime < _nextFind) return null;
            _nextFind = Time.unscaledTime + 1f;
            _plugin = Object.FindObjectOfType<ScopeOverlayVehicleControllerPlugin>();
            if (_plugin == null) return null;
        }
        return _plugin.postProcess;
    }
}


internal static class FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public static string OpenPng(string initialDir)
    {
        const int size = 4096;
        var buffer = Marshal.AllocHGlobal(size * 2);
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = GetActiveWindow(),
                lpstrFilter = "PNG images (*.png)\0*.png\0\0",
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = size,
                lpstrInitialDir = initialDir,
                lpstrTitle = "Choose a reticle image",
                Flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008 
            };
            if (!GetOpenFileNameW(ref ofn)) return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
