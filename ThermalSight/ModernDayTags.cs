using System;

namespace ThermalSight;


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
}
