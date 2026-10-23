using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace Munitions;

[BepInPlugin(Guid, "Munitions", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "sprocket.munitions";
    public const string Version = "1.26.1";

    internal new static ManualLogSource Log;
    internal static string PluginDir;

    internal static ConfigEntry<string> KeyApfsdsConfig, KeyHeConfig, KeyAtgmConfig, KeyHeatConfig, KeyApConfig, KeyApheConfig, KeyHeshConfig;
    internal static ConfigEntry<float> HeatShellPenetration, ApPenetration, AphePenetration, ApheFuseDistance, RocketHeatPenetration;
    internal static ConfigEntry<bool> ManageUnconfigured;
    internal static ConfigEntry<int> MinCaliber;
    internal static ConfigEntry<int> ApfsdsConstant;
    internal static ConfigEntry<float> HePowerPerKg;
    internal static ConfigEntry<float> HePenetration;
    internal static ConfigEntry<float> AtgmPenetration;
    internal static ConfigEntry<int> AtgmMinCaliber;
    internal static ConfigEntry<float> AtgmLaunchSpeed, AtgmCruiseSpeed, AtgmMaxFlight, AtgmLag, AtgmMaxAccel;
    internal static ConfigEntry<float> AtgmSoundVolume;
    internal static ConfigEntry<float> LauncherDelay, LauncherLaunchSpeed, LauncherCruiseSpeed, LauncherMaxFlight;
    internal static ConfigEntry<float> LauncherPenetration, LauncherSoundVolume, LauncherMass;
    internal static ConfigEntry<float> AtgmWireLength, AtgmWireLifetime;
    internal static ConfigEntry<float> RocketSpeed, RocketBurnTime, RocketDispersion, RocketRipple, ThermobaricPower, ThermobaricFireChance;
    internal static ConfigEntry<float> RocketSoundVolume;
    internal static ConfigEntry<string> AutoloaderKeyConfig;
    internal static ConfigEntry<float> AutoloaderVolume, CasingLifetime, AutoloaderEjectTime, CasingSoundVolume, AutoloaderOutsideVolume;
    internal static ConfigEntry<bool> AutoloaderEnabled, CasingsEnabled;
    internal static Key AutoloaderKey = Key.R;
    internal static ConfigEntry<bool> EffectsEnabled, Tracers;
    internal static ConfigEntry<float> EffectDensity, AutoSmoke;
    internal static ConfigEntry<int> MaxParticles;
    internal static ConfigEntry<bool> ShowHud, ShowTankPanel, HideVanillaDamage;
    
    internal static ConfigEntry<bool> HudShells, HudSmoke, HudEngineSmoke, HudExtinguisher, HudBelt, HudLaunchers, HudStatus;
    internal static ConfigEntry<bool> PanelBackground, PanelTopView, PanelDegrees, PanelAps, PanelStab;
    internal static ConfigEntry<bool> PanelEngine, PanelTurret, PanelGun, PanelTracks, PanelCrew, PanelAmmo;
    internal static ConfigEntry<bool> DebugLogging;
    internal static ConfigEntry<bool> CrewDamageEnabled;
    internal static ConfigEntry<float> CrewToughness, CrewBlast;

    internal static readonly Key[] KindKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };

    public override void Load()
    {
        Log = base.Log;
        PluginDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        if (ModSwitch.IsDisabled(Guid))
        {
            Log.LogInfo($"Munitions {Version} is turned off in the Mod menu");
            return;
        }

        KeyApfsdsConfig = Config.Bind("Controls", "APFSDS key", "Digit1", "Selects APFSDS for the next load (Unity Input System key name). Press twice to unload the current round and load it now.");
        KeyHeConfig = Config.Bind("Controls", "HE key", "Digit2", "Selects HE for the next load.");
        KeyAtgmConfig = Config.Bind("Controls", "ATGM key", "Digit4", "Selects ATGM for the next load.");
        KeyHeatConfig = Config.Bind("Controls", "HEAT key", "Digit3", "Selects HEAT for the next load.");
        KeyApConfig = Config.Bind("Controls", "AP key", "Digit5", "Selects AP (solid armour-piercing shot) for the next load.");
        KeyApheConfig = Config.Bind("Controls", "APHE key", "Digit7", "Selects APHE (armour-piercing high-explosive) for the next load. 6 is the fire extinguisher.");
        KeyHeshConfig = Config.Bind("Controls", "HESH key", "Digit8", "Selects HESH (high-explosive squash head) for the next load.");
        
        if (KeyAtgmConfig.Value == "Digit3" && KeyHeatConfig.Value == "Digit4")
        {
            KeyAtgmConfig.Value = "Digit4";
            KeyHeatConfig.Value = "Digit3";
        }

        ManageUnconfigured = Config.Bind("Ammunition", "Racks without settings carry APFSDS", true,
            "Ammo racks that were never set up in the designer (including AI vehicles) are filled with APFSDS. Off = such racks keep the vanilla shells.");
        MinCaliber = Config.Bind("Ammunition", "Minimum caliber (mm)", 20,
            "Guns smaller than this (machine guns) keep vanilla ammunition.");
        ApfsdsConstant = Config.Bind("Ammunition", "APFSDS penetrator constant", 3000,
            "Penetrator constant of the tungsten long rod. Lower = more penetration. 3000 gives about 450 mm (105 mm), 600 mm (120 mm), 650 mm (125 mm).");
        HePowerPerKg = Config.Bind("Ammunition", "HE blast power per kg of explosive", 150f,
            "Explosive power of the HE filler (13% of the shell mass). Blast radius is sqrt(power / 10) m.");
        HePenetration = Config.Bind("Ammunition", "HE penetration (calibers)", 0.2f,
            "Armour an HE shell can still punch through before bursting (thin-walled shell). 0.2 = 24 mm for a 120 mm shell.");
        AtgmPenetration = Config.Bind("Ammunition", "ATGM penetration (calibers)", 6.4f,
            "HEAT warhead penetration. 6.4 calibers = about 800 mm for 125 mm and 970 mm for 152 mm (Kornet class).");
        AtgmMinCaliber = Config.Bind("Ammunition", "ATGM minimum caliber (mm)", 90,
            "Smallest gun that can fire a gun-launched ATGM.");
        HeatShellPenetration = Config.Bind("Ammunition", "HEAT shell penetration (calibers)", 4.5f,
            "Penetration of a gun-fired HEAT shell, the same at any range. 4.5 calibers = 540 mm for 120 mm, 135 mm for 30 mm.");
        ApPenetration = Config.Bind("Ammunition", "AP penetration (calibers)", 1.9f, new ConfigDescription(
            "Penetration of a full-bore solid AP shot at 800 m/s (faster guns more, slower less, and it drops with range). " +
            "1.9 calibers = 190 mm for 100 mm.", new AcceptableValueRange<float>(0.5f, 4f)));
        AphePenetration = Config.Bind("Ammunition", "APHE penetration (calibers)", 1.6f, new ConfigDescription(
            "Penetration of an APHE shell at 800 m/s. The bursting charge makes it a bit weaker than solid AP, but it explodes behind the armour.",
            new AcceptableValueRange<float>(0.5f, 4f)));
        ApheFuseDistance = Config.Bind("Ammunition", "APHE fuse distance (m)", 1.2f, new ConfigDescription(
            "How far behind the penetrated armour an APHE shell explodes (sooner if it hits something inside first).",
            new AcceptableValueRange<float>(0.2f, 4f)));
        RocketHeatPenetration = Config.Bind("Rockets", "HEAT rocket penetration (calibers)", 5f, new ConfigDescription(
            "Penetration of an unguided rocket with a HEAT warhead, the same at any range. 5 calibers = 400 mm for 80 mm (S-8KOM class).",
            new AcceptableValueRange<float>(1f, 10f)));

        CrewDamageEnabled = Config.Bind("Crew", "Realistic crew damage", true,
            "Crew members are no longer killed by every splinter or by any HE burst on the hull. Spall wounds, the round itself " +
            "kills whoever it hits, and a blast hurts by the distance to each crew member. Damage to engines, guns and other parts " +
            "stays as in the game. Off = the game's own crew damage.");
        CrewToughness = Config.Bind("Crew", "Crew toughness", 1f, new ConfigDescription(
            "Divides the damage crew members take from rounds, spall and blasts. 2 = twice as tough, 0.5 = twice as fragile.",
            new AcceptableValueRange<float>(0.25f, 4f)));
        CrewBlast = Config.Bind("Crew", "Blast damage to crew", 0.25f, new ConfigDescription(
            "Part of an HE blast that reaches the crew inside the vehicle (the game applies the full blast through the armour). " +
            "It also falls with the distance from the burst to each crew member.", new AcceptableValueRange<float>(0f, 1f)));

        AtgmLaunchSpeed = Config.Bind("ATGM", "Launch speed (m/s)", 280f, "Speed when the missile leaves the barrel.");
        AtgmCruiseSpeed = Config.Bind("ATGM", "Cruise speed (m/s)", 350f, "Speed after the sustainer motor has accelerated the missile.");
        AtgmMaxFlight = Config.Bind("ATGM", "Max flight time (s)", 17f, "The missile self-destructs after this time (about 5 km).");
        AtgmLag = Config.Bind("ATGM", "Guidance lag (s)", 0.18f, "How late the guidance reacts to the movement of the sight.");
        AtgmMaxAccel = Config.Bind("ATGM", "Max lateral acceleration (m/s²)", 80f, "How hard the missile can turn to follow the line of sight.");
        AtgmSoundVolume = Config.Bind("ATGM", "Launch sound volume", 1f, "0 = silent.");

        LauncherDelay = Config.Bind("ATGM launcher", "Launch delay (s)", 1.2f,
            "Time from pulling the trigger to the missile leaving the tube (matches the launch sound).");
        LauncherLaunchSpeed = Config.Bind("ATGM launcher", "Launch speed (m/s)", 60f, "Speed given by the ejection charge before the motor ignites.");
        LauncherCruiseSpeed = Config.Bind("ATGM launcher", "Cruise speed (m/s)", 300f, "Kornet class: about 300 m/s.");
        LauncherMaxFlight = Config.Bind("ATGM launcher", "Max flight time (s)", 20f, "Self-destruct after this time (about 5.5 km).");
        LauncherPenetration = Config.Bind("ATGM launcher", "Penetration (calibers)", 7.5f,
            "Tandem HEAT warhead. 7.5 calibers = about 1140 mm for 152 mm (Kornet class).");
        LauncherSoundVolume = Config.Bind("ATGM launcher", "Launch sound volume", 1f, "0 = silent.");
        LauncherMass = Config.Bind("ATGM launcher", "Mass at 152 mm (kg)", 30f,
            "Mass of a loaded tube at 152 mm; other calibers scale with the cube of the caliber.");
        AtgmWireLength = Config.Bind("ATGM launcher", "Wire length (m)", 4000f, new ConfigDescription(
            "Wire-guided missiles (Control link = Wire-guided): the missile can't be steered farther than this — the wire snaps (TOW, Konkurs: 3.75-4 km).",
            new AcceptableValueRange<float>(500f, 8000f)));
        AtgmWireLifetime = Config.Bind("ATGM launcher", "Wire stays on the ground (s)", 120f, new ConfigDescription(
            "How long a laid guidance wire stays visible after the missile is gone, then it fades away.", new AcceptableValueRange<float>(5f, 600f)));

        RocketSpeed = Config.Bind("Rockets", "Default rocket speed (m/s)", 600f, new ConfigDescription(
            "Speed of an unguided rocket after its motor has burnt out, for launchers where it was not set in the part panel (S-8 about 600, Grad about 690).",
            new AcceptableValueRange<float>(RocketSettings.MinSpeed, RocketSettings.MaxSpeed)));
        RocketBurnTime = Config.Bind("Rockets", "Motor burn time (s)", 1f, new ConfigDescription(
            "While the motor burns the rocket accelerates and flies straight along the tube; after that it falls like a shell.",
            new AcceptableValueRange<float>(0.2f, 4f)));
        RocketDispersion = Config.Bind("Rockets", "Dispersion (degrees)", 0.4f, new ConfigDescription(
            "Random deviation of each rocket from the tube axis (unguided rockets are not precise). 0.4 = about 7 m at 1 km.",
            new AcceptableValueRange<float>(0f, 5f)));
        RocketRipple = Config.Bind("Rockets", "Ripple interval (s)", 0.25f, new ConfigDescription(
            "Holding the trigger fires the tubes of the fire group one after another with this interval (TOS-1A: 24 rockets in about 6 s). " +
            "A short click fires one rocket.", new AcceptableValueRange<float>(0.05f, 3f)));
        ThermobaricPower = Config.Bind("Rockets", "Thermobaric blast power", 2.5f, new ConfigDescription(
            "Blast of a thermobaric warhead relative to an HE warhead of the same caliber (blast radius grows with its square root).",
            new AcceptableValueRange<float>(1f, 10f)));
        ThermobaricFireChance = Config.Bind("Rockets", "Thermobaric fire chance", 0.7f, new ConfigDescription(
            "Chance that a vehicle in the centre of the fireball catches fire; falls to 0 at the edge of the fireball. 0 = never.",
            new AcceptableValueRange<float>(0f, 1f)));
        RocketSoundVolume = Config.Bind("Rockets", "Sound volume", 1f, "Volume of the rocket launch and of the thermobaric blast. 0 = silent.");

        AutoloaderEnabled = Config.Bind("Autoloader", "Enable autoloader", true,
            "Ammo racks marked as \"Autoloader (AZ)\", \"Mechanized loader (MZ)\" or \"Belt loader\" in the designer work as a loading carousel / belt. Off = they are ordinary racks.");
        AutoloaderKeyConfig = Config.Bind("Autoloader", "Load key", "R",
            "Loads the selected round (number keys) from the carousel or the loading belt (Unity Input System key name).");
        AutoloaderVolume = Config.Bind("Autoloader", "Sound volume", 1f, "Volume of the AZ/MZ carousel, belt loader and loading sounds. 0 = silent.");
        AutoloaderOutsideVolume = Config.Bind("Autoloader", "Volume outside the vehicle", 0.4f, new ConfigDescription(
            "AZ/MZ/belt loader sounds heard from outside (third-person camera, other vehicles) relative to inside (your own vehicle's sight). 0.4 = 60% quieter.",
            new AcceptableValueRange<float>(0f, 1f)));
        AutoloaderEjectTime = Config.Bind("Autoloader", "Case stub ejection time (s)", 5f,
            "When the stub of the last shot is thrown out of the ejection port, counted from the start of the loading cycle (the moment R is pressed).");
        CasingsEnabled = Config.Bind("Casings", "Ejected cases", true,
            "Spent cases (autoloader: case stubs) fly out of a \"Casing Ejection Port\" part, if the vehicle has one.");
        CasingLifetime = Config.Bind("Casings", "Case lifetime before fading (s)", 30f,
            "Ejected cases stay physical (roll, can be pushed around) for this long, then smoothly vanish. Entering the vehicle editor removes them at once.");
        CasingSoundVolume = Config.Bind("Casings", "Case drop sound volume", 1f,
            "Clink of a case hitting the ground or the armour (louder for harder hits, higher pitch for smaller calibres). 0 = silent.");

        EffectsEnabled = Config.Bind("Effects", "Enable effects", true,
            "Sabot discard, ATGM launch cloud, spark showers, HE and HEAT explosions, smoke and dirt.");
        Tracers = Config.Bind("Effects", "Tracers", true, "Red tracer on APFSDS, AP and APHE, orange on HE and HESH, yellow on HEAT (burns for about 3.5 s).");
        EffectDensity = Config.Bind("Effects", "Effect density", 1f, "Multiplier for the number of sparks, smoke puffs and debris (0.1 - 4).");
        AutoSmoke = Config.Bind("Effects", "Autocannon / MG smoke", 0.06f, new ConfigDescription(
            "Muzzle smoke of autocannons and machine guns, relative to a single cannon shot of that size (1 = as much as the game makes). " +
            "In a burst the game's muzzle effect (flash and smoke) shows at most once a second, the other shots get a short flash without smoke.",
            new AcceptableValueRange<float>(0f, 1f)));
        
        if (Math.Abs(AutoSmoke.Value - 0.15f) < 0.001f) AutoSmoke.Value = 0.06f;
        MaxParticles = Config.Bind("Effects", "Max particles", 4000, "Upper limit of particles at the same time for each kind (glowing / smoke).");

        ShowHud = Config.Bind("Display", "Show ammo panel", true,
            "The ammo panel at the bottom centre (like War Thunder): rounds per type (1-4, 5, 7, 8), smoke grenades (G) and engine smoke (H).");
        ShowTankPanel = Config.Bind("Display", "Show vehicle panel", true,
            "The vehicle panel in the bottom left corner: top view of the vehicle with a degree scale and the APS sectors " +
            "(green = working, red = out of charges or broken, with charges left), damage of engine / turret drive / gun / tracks, " +
            "stabilizer (STAB), crew and ammo of the selected weapon.");
        
        HideVanillaDamage = Config.Bind("Display", "Hide the game damage list", true,
            "While the vehicle panel is shown, the game's own list of damaged components is hidden (the panel shows the damage).");
        const string hud = "HUD elements";
        HudShells = Config.Bind(hud, "Ammo panel shells (1-4)", true, "Ammo panel: the shell cells APFSDS / HE / HEAT / ATGM / AP / APHE / HESH.");
        HudSmoke = Config.Bind(hud, "Ammo panel smoke grenades (G)", true, "Ammo panel: the smoke grenade cell.");
        HudEngineSmoke = Config.Bind(hud, "Ammo panel engine smoke (H)", true, "Ammo panel: the engine smoke (TDA) cell.");
        HudExtinguisher = Config.Bind(hud, "Ammo panel fire extinguisher (6)", true, "Ammo panel: the fire extinguisher cell.");
        HudBelt = Config.Bind(hud, "Ammo panel belt weapon", true, "Ammo panel: the row with the rounds left of an autocannon or machine gun.");
        HudLaunchers = Config.Bind(hud, "Ammo panel launchers", true, "Ammo panel: the rows of the ATGM and rocket launcher tubes.");
        HudStatus = Config.Bind(hud, "Ammo panel status line", true, "Ammo panel: the line above it (LOADING, EMPTY, FIRE!, autoloader state, messages).");
        PanelBackground = Config.Bind(hud, "Vehicle panel background", true, "Vehicle panel: the dark square and its frame.");
        PanelTopView = Config.Bind(hud, "Vehicle panel top view", true, "Vehicle panel: the top view of the vehicle with the turret and gun.");
        PanelDegrees = Config.Bind(hud, "Vehicle panel degree scale", true, "Vehicle panel: the degree scale around the top view.");
        PanelAps = Config.Bind(hud, "Vehicle panel APS sectors", true, "Vehicle panel: the APS sectors and their charge numbers.");
        PanelStab = Config.Bind(hud, "Vehicle panel STAB", true, "Vehicle panel: the stabilizer label.");
        PanelEngine = Config.Bind(hud, "Vehicle panel engine", true, "Vehicle panel: the engine damage icon.");
        PanelTurret = Config.Bind(hud, "Vehicle panel turret drive", true, "Vehicle panel: the turret drive damage icon.");
        PanelGun = Config.Bind(hud, "Vehicle panel gun", true, "Vehicle panel: the gun damage icon.");
        PanelTracks = Config.Bind(hud, "Vehicle panel tracks", true, "Vehicle panel: the tracks damage icon.");
        PanelCrew = Config.Bind(hud, "Vehicle panel crew", true, "Vehicle panel: the crew icon and how many are alive.");
        PanelAmmo = Config.Bind(hud, "Vehicle panel ammo", true, "Vehicle panel: the ammo icon and rounds of the selected weapon.");
        DebugLogging = Config.Bind("Debug", "Verbose logging", false, "Extra log output.");

        ParseKeys();
        
        foreach (var k in new[] { KeyApfsdsConfig, KeyHeConfig, KeyAtgmConfig, KeyHeatConfig, KeyApConfig, KeyApheConfig, KeyHeshConfig, AutoloaderKeyConfig })
            k.SettingChanged += (_, _) => ParseKeys();

        var harmony = new Harmony(Guid);
        foreach (var type in typeof(Plugin).Assembly.GetTypes())
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception e) { Log.LogError($"Patch {type.Name} failed: {e.Message}"); }
        }
        foreach (var m in harmony.GetPatchedMethods())
            Log.LogInfo($"Patched {m.DeclaringType?.Name}.{m.Name}");

        AddComponent<Controller>();
        Log.LogInfo($"Munitions {Version} loaded");
    }

    private static void ParseKeys()
    {
        Controller.Keys = new[]
        {
            ParseKey(KeyApfsdsConfig.Value, Key.Digit1), ParseKey(KeyHeConfig.Value, Key.Digit2), ParseKey(KeyAtgmConfig.Value, Key.Digit4),
            ParseKey(KeyHeatConfig.Value, Key.Digit3), ParseKey(KeyApConfig.Value, Key.Digit5), ParseKey(KeyApheConfig.Value, Key.Digit7),
            ParseKey(KeyHeshConfig.Value, Key.Digit8)
        };
        AutoloaderKey = ParseKey(AutoloaderKeyConfig.Value, Key.R);
    }

    private static Key ParseKey(string name, Key fallback)
    {
        if (Enum.TryParse<Key>(name, true, out var key)) return key;
        Log.LogWarning($"Unknown key '{name}', using {fallback}");
        return fallback;
    }
}
