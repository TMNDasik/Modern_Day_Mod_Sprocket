using System;

namespace Munitions;


internal static class ModernDayTags
{
    public const string Eo = "modernEo", Aps = "modernAps", Thermal = "modernThermal", Other = "modernOther", Guns = "modernGuns";

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
}
