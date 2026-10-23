using System;
using System.Linq;
using System.Reflection;

namespace ThermalSight;






internal static class ColdWarThermal
{
    private static bool _searched;
    private static FieldInfo _selected, _active, _toggle;
    private static PropertyInfo _pressed;
    private static int _yields;

    
    internal static bool OwnsToggle()
    {
        Find();
        if (_active == null) return false;
        try
        {
            if (!(bool)_active.GetValue(null) && _selected.GetValue(null) == null) return false;
            
            var binding = _toggle?.GetValue(null);
            if (binding != null && _pressed != null && !(bool)_pressed.GetValue(binding)) return false;
            if (_yields++ < 3) Plugin.Log.LogInfo("Thermal Sight: the Cold War thermal sight is in use — its toggle handles this key press");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Cold War thermal sight state unreadable, ignoring it: " + e.Message);
            _active = null;
            return false;
        }
    }

    private static void Find()
    {
        if (_searched) return;
        _searched = true;
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "SprocketThermalSight")
                .Select(a => a.GetType("SprocketThermalSight.Runtime", false))
                .FirstOrDefault(t => t != null);
            if (type == null) return;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _selected = type.GetField("selected", flags);
            _active = type.GetField("active", flags);
            _toggle = type.GetField("toggleBinding", flags);
            _pressed = _toggle?.FieldType.GetProperty("WasPressedThisFrame");
            if (_selected == null || _active == null || _active.FieldType != typeof(bool))
            {
                _selected = _active = null;
                Plugin.Log.LogWarning("Cold War thermal sight found, but its state is unknown (other version?) — both thermal sights react to their keys");
                return;
            }
            Plugin.Log.LogInfo("Cold War thermal sight found: its own thermal sights keep their key, ours work as before");
        }
        catch (Exception e) { Plugin.Log.LogWarning("Cold War thermal sight check failed: " + e.Message); }
    }
}
