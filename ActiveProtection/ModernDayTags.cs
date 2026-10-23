using System;

namespace ActiveProtection;


internal static class ModernDayTags
{
    public const string Eo = "modernEo", Aps = "modernAps", Thermal = "modernThermal", Other = "modernOther";

    private static bool? _active;

    public static bool Active
    {
        get
        {
            if (_active != null) return _active.Value;
            var active = false;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "ModernDay") continue;
                    var p = a.GetType("ModernDay.Api")?.GetProperty("Active");
                    active = p?.GetValue(null) is true;
                    break;
                }
            }
            catch { }
            _active = active;
            return active;
        }
    }

    
    public static string Or(string modernTag) => Active ? modernTag : "cannon";

    
    
    private static Func<bool> _visible;
    private static bool _visibleSearched;

    public static bool PartsVisible
    {
        get
        {
            if (!_visibleSearched)
            {
                _visibleSearched = true;
                try
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (a.GetName().Name != "ModernDay") continue;
                        var getter = a.GetType("ModernDay.Api")?.GetProperty("PartsVisible")?.GetGetMethod();
                        if (getter != null) _visible = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                        break;
                    }
                }
                catch { }
            }
            try { return _visible == null || _visible(); }
            catch { return true; }
        }
    }

    
    private static Func<IntPtr, bool> _hidden;
    private static bool _hiddenSearched;

    public static bool HiddenInBattle(IntPtr vehicleObject)
    {
        if (!_hiddenSearched)
        {
            _hiddenSearched = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "ModernDay") continue;
                    var m = a.GetType("ModernDay.Api")?.GetMethod("HiddenInBattle", new[] { typeof(IntPtr) });
                    if (m != null) _hidden = (Func<IntPtr, bool>)Delegate.CreateDelegate(typeof(Func<IntPtr, bool>), m);
                    break;
                }
            }
            catch { }
        }
        try { return _hidden != null && vehicleObject != IntPtr.Zero && _hidden(vehicleObject); }
        catch { return false; }
    }

    
    public static string Era
    {
        get
        {
            if (!Active) return "cannon";
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "ModernDay") continue;
                    var f = a.GetType("ModernDay.Api")?.GetField("TagEra");
                    if (f?.GetValue(null) is string tag && tag.Length > 0) return tag;
                    break;
                }
            }
            catch { }
            return Other;
        }
    }
}
