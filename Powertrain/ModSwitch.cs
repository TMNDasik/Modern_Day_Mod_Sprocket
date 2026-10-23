using System;
using System.IO;
using BepInEx;

namespace Powertrain;



internal static class ModSwitch
{
    public static bool IsDisabled(string guid)
    {
        try
        {
            var path = Path.Combine(Paths.ConfigPath, "sprocket.modmenu.disabled.txt");
            if (!File.Exists(path)) return false;
            foreach (var line in File.ReadAllLines(path))
                if (string.Equals(line.Trim(), guid, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }
}
