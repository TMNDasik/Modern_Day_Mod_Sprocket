using System.Collections.Generic;

namespace CombatEffects;





internal static class ImpactSelector
{
    public const int HE = 0, Heat = 1, Kinetic = 2, Ricochet = 4;
    public const int Bullet = 0, Autocannon = 1, MainGun = 2, Missile = 3, Artillery = 4, Mortar = 5, Rocket = 6;
    public const int Steel = 2, Dirt = 5;
    public const int Small = 4, Medium = 8, Large = 16, VerySmall = 32, VeryLarge = 64, Huge = 128;
    public const int RicNone = 0, RicNormal = 2, RicLarge = 3, RicSabot = 4, RicAutocannon = 8;

    private const int VeryLow = 0, Low = 1, MediumStrict = 2, High = 3, Exact = 4;

    public struct Descriptor
    {
        public int Category, Size, Fuzed, Flags, Ricochet;
        public override string ToString() => $"cat {Category}, size {Size}, fuze {Fuzed}, flags {Flags}, ricochet {Ricochet}";
    }

    
    
    public static Descriptor Describe(int caliber, int kind, bool explosion, bool automatic)
    {
        var d = new Descriptor();
        var missile = kind == 2 || kind == 3;
        var rocket = kind == 5 || kind == 6;
        d.Category = !explosion ? Kinetic : kind == 4 || missile ? Heat : HE;
        d.Fuzed = explosion ? 1 : 0;
        if (missile) d.Size = Missile;
        else if (rocket) d.Size = Rocket;
        else if (caliber < 15) d.Size = Bullet;
        else if (caliber < 45 || automatic) d.Size = Autocannon;
        else if (explosion && caliber >= 150) d.Size = Artillery;
        else d.Size = MainGun;
        d.Flags = d.Size switch
        {
            Bullet => caliber < 9 ? Medium : caliber < 13.6f ? Large : Huge,
            Autocannon => caliber < 25 ? Small : caliber < 35 ? Medium : Large,
            Missile => caliber < 110 ? Small : caliber < 140 ? Medium : caliber < 170 ? Large : VeryLarge,
            Rocket => caliber < 70 ? VerySmall : caliber < 100 ? Small : caliber < 150 ? Medium : Large,
            Artillery => Large,
            _ => caliber < 76 ? VerySmall : caliber < 100 ? Small : caliber < 118 ? Medium : Large
        };
        d.Ricochet = caliber < 15 ? RicNormal : caliber < 45 || automatic ? RicAutocannon : kind == 0 || caliber >= 90 ? RicSabot : RicLarge;
        return d;
    }

    public static ImpactEntry Pick(Descriptor d, int surface, bool ricochet)
    {
        var all = Library.Impacts;
        if (ricochet)
        {
            if (d.Ricochet == RicNone) return null;
            foreach (var e in all) if (e.Effect != null && e.Category == Ricochet && e.Ricochet == d.Ricochet) return e;
            foreach (var e in all) if (e.Effect != null && e.Category == Ricochet) return e;
            return null;
        }
        for (var strict = Exact; strict >= VeryLow; strict--)
        {
            var list = new List<ImpactEntry>();
            foreach (var e in all)
                if (e.Effect != null && Passes(e, d, surface, strict)) list.Add(e);
            if (list.Count == 0) continue;
            if (strict == Exact) return list[0];
            if (d.Flags != 0)
            {
                var best = -1;
                ImpactEntry pick = null;
                foreach (var e in list)
                {
                    var n = CountBits(e.Flags & d.Flags);
                    if (n > best) { best = n; pick = e; }
                }
                if (best > 0) return pick;
            }
            if (strict <= MediumStrict) foreach (var e in list) if (e.Surface == surface) return e;
            if (strict <= Low) foreach (var e in list) if (e.Fuzed == d.Fuzed) return e;
            if (strict <= VeryLow) foreach (var e in list) if (e.Size == d.Size) return e;
            if (strict == VeryLow) return list[0];
        }
        return null;
    }

    private static bool Passes(ImpactEntry e, Descriptor d, int surface, int strict)
    {
        if (e.Category != d.Category) return false;
        if (strict >= Low && e.Size != d.Size) return false;
        if (strict >= MediumStrict && e.Fuzed != d.Fuzed) return false;
        if (strict >= High && e.Surface != surface) return false;
        if (strict >= Exact && e.Flags != d.Flags) return false;
        return true;
    }

    private static int CountBits(int x)
    {
        var n = 0;
        while (x != 0) { n += x & 1; x >>= 1; }
        return n;
    }
}
