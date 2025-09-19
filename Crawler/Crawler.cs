namespace Crawler;

public static partial class CrawlerEx {
    public static int Length<ENUM>() where ENUM : struct, Enum => Enum.GetValues<ENUM>().Length;
    public static ENUM ChooseRandom<ENUM>() where ENUM : struct, Enum => Enum.GetValues<ENUM>()[Random.Shared.Next(0, Length<ENUM>() - 1)];
    public static double Roll(this int Count, double sides) {
        double result = 0;
        for (int i = 0; i < Count; i++) {
            result += Random.Shared.NextDouble() * sides;
        }
        return result;
    }
    public static double RollLow(this int Count, int Take, double sides) {
        // Roll Count dice and Take the lowest. Converges to log-normal distributions.
        double[] rolls = new double[Count];
        for (int i = 0; i < Count; i++) {
            rolls[i] = Random.Shared.NextDouble() * sides;
        }
        Array.Sort(rolls);
        return rolls.Take(Take).Sum();
    }
}
public enum SegmentType: byte {
    Guns,
    Lasers, //
    Missiles, // slow, strong alpha

    Reactor, // Provides power to move and fire weapons
    Battery, // Single use reactor
    Charger, // Increases fuel efficiency

    Legs,
    Wheels,
    Treads,

    Armor, // soaks up to amount; damaged when over, several HP
    Shield, // soaks up to amount but doesn't recharge until end of combat
    Ablative, // low armor, high HP
}
public enum SegmentClass: byte {
    // Core segments provide power and movement
    Core = 0,
    Power = Core,
    Traction,
    Offense,
    Defense,

    // Aux segments provide bonuses and improvements to core segments
    Aux = 128,
    AuxPower = Aux,
    AuxTraction,
    AuxOffense,
    AuxDefense,

}
public record Segment(string Name, SegmentType Type, int Amount, int Rate, int Weight, int Power, int Cost, int HP, int Dmg = 0, int State = 0) {
    public override string ToString() => $"{Type} {Name} AMT:{Amount,2} RATE:{Rate,2} TONS:{Weight,2} MW:{Power,2} COST:{Cost,3} HP:{HP,2} DMG:{Dmg,2}";

    public SegmentClass SegmentClass => Type switch {
        SegmentType.Reactor => SegmentClass.Power,
        SegmentType.Battery => SegmentClass.AuxPower,
        SegmentType.Charger => SegmentClass.AuxPower,
        SegmentType.Legs => SegmentClass.Traction,
        SegmentType.Wheels => SegmentClass.Traction,
        SegmentType.Treads => SegmentClass.Traction,
        SegmentType.Guns => SegmentClass.Offense,
        SegmentType.Lasers => SegmentClass.Offense,
        SegmentType.Missiles => SegmentClass.Offense,
        SegmentType.Armor => SegmentClass.Defense,
        SegmentType.Shield => SegmentClass.Defense,
        SegmentType.Ablative => SegmentClass.Defense,
        _ => SegmentClass.Core
    };
    public bool IsDestroyed => Dmg >= HP; // 0% HP = 1 2 3 4 5 6
    public bool IsActive => Dmg < (HP + 1) / 2; // 50% HP = 1 1 2 2 3 3 etc.
    public bool IsDisabled => !IsActive && !IsDestroyed;

    public int HPLeft => HP - Dmg;
    public int StateLeft => Amount - State;

    public string StateString => IsActive ? "(Active)" : IsDestroyed ? "(Destroyed)" : "(Disabled)";
    public Segment Recharged(int Count) => IsActive
        ? Type switch {
            SegmentType.Shield => AddState(Rate * Count),
            SegmentType.Reactor => AddState(Rate * Count),
            SegmentType.Battery => AddState(Rate * Count),
            SegmentType.Charger => AddState(Rate * Count),
            _ => this,
        }
        : this;
    public Segment AddDamage(int dmg) => this with {
        Dmg = Math.Clamp(Dmg + dmg, 0, HP)
    };
    public Segment AddState(int state) => this with {
        State = Math.Clamp(State + state, 0, Amount)
    };

    public string Report {
        get {
            string result = Style.Em.Format(Name.Substring(2).PadRight(20));
            result += GetStyle().Format($"\t\tAR:{Amount,2}/{Rate,2} WT:{Weight,2} MW:{Power,2} COST:{Cost,3} HP:{HP,2} DMG:{Dmg,2} STT:{State,2}");
            return result;
        }
    }
    Style GetStyle() {
        Style result = Style.None;
        if (IsDestroyed) {
            result = Style.SegmentDestroyed;
        } else if (IsDisabled) {
            result = Style.SegmentDisabled;
        } else {
            result = Style.SegmentActive;
        }
        return result;
    }
    public StyledString Colored {
        get {
            int hpLeft = HPLeft;
            int stateLeft = StateLeft;
            char code = SegmentClass switch {
                SegmentClass.Power => 'P',
                SegmentClass.Traction => 'T',
                SegmentClass.Offense => 'O',
                SegmentClass.Defense => 'X',
                SegmentClass.AuxPower => 'p',
                SegmentClass.AuxTraction => 't',
                SegmentClass.AuxOffense => 'o',
                SegmentClass.AuxDefense => 'x',
                _ => '?',
            };
            string result = $"{code}{Name[0]}{levelCode(hpLeft),1}{levelCode(stateLeft),1}";
            Style style = GetStyle();
            return new(style, result);
        }
    }
    string levels = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    char levelCode(int x) => x < 0 || x >= levels.Length ? '-' : levels[x];

    public static List<Segment> Prototypes = [
        // For guns, amount is the damage per shot, rate is number of shots per combat round
        new Segment("g:Basic Guns", SegmentType.Guns, 2, 1, 3, 1, 100, 3),
        new Segment("G:Heavy Guns", SegmentType.Guns, 3, 1, 3, 1, 150, 3),
        new Segment("l:Basic Lasers", SegmentType.Lasers, 2, 1, 2, 2, 120, 3),
        new Segment("L:Heavy Lasers", SegmentType.Lasers, 2, 2, 2, 2, 170, 3),
        new Segment("m:Basic Missiles", SegmentType.Missiles, 2, 1, 4, 1, 110, 3),
        new Segment("M:Heavy Missiles", SegmentType.Missiles, 3, 1, 4, 1, 160, 3),

        // For power segments, amount is battery capacity, rate is refill per combat round
        new Segment("r:Small Reactor", SegmentType.Reactor, 4, 4, 5, 0, 120, 7),
        new Segment("R:Large Reactor", SegmentType.Reactor, 6, 6, 2, 0, 180, 8),
        new Segment("b:Small Battery", SegmentType.Battery, 10, 2, 1, 0, 150, 2),
        new Segment("B:Large Battery", SegmentType.Battery, 15, 3, 1, 0, 250, 2),
        new Segment("c:Charger", SegmentType.Charger, 0, 3, 1, 0, 80, 4),
        new Segment("C:Supercharger", SegmentType.Charger, 0, 4, 1, 0, 120, 4),

        // For movement sections, amount is terrain handling, rate is move speed
        new Segment("-:Legs", SegmentType.Legs, 4, 30, -2, 1, 110, 3),
        new Segment("=:Heavy Legs", SegmentType.Legs, 4, 30, -3, 1, 140, 3),
        new Segment("+:Fast Legs", SegmentType.Legs, 4, 40, -2, 1, 140, 3),

        new Segment("O:Wheels", SegmentType.Wheels, 2, 40, -3, 1, 90, 2),
        new Segment("@:Balloon", SegmentType.Wheels, 3, 30, -2, 1, 120, 2),
        new Segment("Q:High Speed", SegmentType.Wheels, 2, 50, -3, 2, 130, 2),

        new Segment("%:Treads", SegmentType.Treads, 3, 30, -4, 1, 110, 4),
        new Segment("$:Flex Treads", SegmentType.Treads, 3, 30, -3, 1, 140, 4),
        new Segment("#:Heavy Treads", SegmentType.Treads, 3, 25, -5, 1, 150, 5),

        // For armor sections, amount is the damage soaked, rate is the number of hits t2e piece can take before destruction.
        new Segment("p:Light Plating", SegmentType.Armor, 1, 2, 1, 0, 120, 5),
        new Segment("P:Heavy Plating", SegmentType.Armor, 2, 2, 1, 0, 200, 8),
        new Segment("D:Duraplate", SegmentType.Ablative, 1, 4, 2, 0, 210, 10),
        new Segment("s:Light Shields", SegmentType.Shield, 5, 1, 2, 0, 210, 2),
        new Segment("I:Quick Shields", SegmentType.Shield, 3, 2, 2, 0, 210, 2),
        new Segment("S:Heavy Shields", SegmentType.Shield, 8, 1, 2, 0, 210, 2),
    ];

    public static Dictionary<char, Segment> Lookup = Prototypes.ToDictionary(s => s.Name[0]);
    public static List<Segment> Create(string segmentCodestring) => segmentCodestring.Select(s => Lookup[s] with {
    }).ToList();
    public static char RandomPreset() => Prototypes[Random.Shared.Next(0, Prototypes.Count - 1)].Name[0];
    public static string AllSegmentCodes() => string.Join("", Prototypes.Select(s => s.Name[0]));

    public static string AllWeaponSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Offense or SegmentClass.AuxOffense).Select(s => s.Name[0]));
    public static string AllPowerSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Power or SegmentClass.AuxPower).Select(s => s.Name[0]));
    public static string AllTractionSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Traction or SegmentClass.AuxTraction).Select(s => s.Name[0]));
    public static string AllDefenseSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Defense or SegmentClass.AuxDefense).Select(s => s.Name[0]));
    public static string CoreWeaponSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Offense).Select(s => s.Name[0]));
    public static string CorePowerSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Power).Select(s => s.Name[0]));
    public static string CoreTractionSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Traction).Select(s => s.Name[0]));
    public static string CoreDefenseSegmentCodes() => string.Join("", Prototypes.Where(p => p.SegmentClass is SegmentClass.Defense).Select(s => s.Name[0]));
}
public enum Faction {
    Player, // eg a Player crawler
    Bandit, // a Bandit crawler
    Hero,
    Trade,
    Mercenary,
    Civilian,
}
public class Crawler: IActor {
    public static Crawler NewRandom(Location here, double LocWealthScale, double CommoditiesFraction, double[] segmentClassWeights) {
        var newInv = new Inventory();
        double Budget = here.Wealth * LocWealthScale;
        newInv.AddRandomInventory(here, Budget, CommoditiesFraction, true, segmentClassWeights);
        return new Crawler(here, newInv);
    }
    public Crawler(Location location, Inventory inventory) {
        Inv = inventory;
        Location = location;
    }
    public Crawler(Location location, string segmentCodes = "gr%") {
        Location = location;
        List<Segment> segments = new();
        try {
            segments = Segment.Create(segmentCodes);
        } catch (Exception e) {
            Console.WriteLine($"Error parsing segment codes: {e.Message}");
        }
        Inv = new Inventory([1000, 50, 100, 16, 20], segments);
    }
    public string Name { get; set; } = "Neophyte";
    public string Brief(int detail) {
        var C = Colored().Split('\n');
        if (FailState is not null) {
            C[0] += " " + Style.SegmentDestroyed.Format(FailState);
        }
        if (IsDepowered) {
            C[0] += " (Depowered)";
        }
        if (IsStranded) {
            C[0] += " (Stranded)";
        }
        detail = 2;
        if (detail >= 1) {
            var CurrentTerrain = Location.Terrain;
            int terrainRate = TotalPullOn(CurrentTerrain);
            int fuelEfficiency = 100 - ( int ) (Math.Sqrt(TotalRecharge) * 10);

            C[1] += $" MW {TotalPower,3}/{TotalReactor,3}+{TotalBattery,3}";
            C[1] += $" MV {TotalWeight,3}@{terrainRate,3}/{TotalCarry,3}";
        }
        if (detail >= 2) {
            C[2] += " " + Style.None.Format(Inv.Brief());
        }
        if (detail <= 0) {
            C = C.Take(3).ToArray();
        }
        return string.Join("\n", C);
    }
    public Location Location { get; set; }

    long Hour = 300000000;
    long Day => Hour / 25;
    long HOD => Hour % 25;
    long Week => Day / 10;
    long DOW => Day % 10;

    double CycleFrac(long duration, long offset = 0) => ((Hour - offset) % duration) / ( double ) duration;
    double CycleWave(long duration, long offset = 0) => Math.Sin(Math.PI * 2 * CycleFrac(duration, offset));

    double ShortWave => CycleWave(3180);
    double LongWave => CycleWave(10813);

    // + factions are good or defensive; neutral with other good factions, oppose evel
    // - factions are evil or aggro, oppose all factions except their own
    public Faction Faction { get; set; } = 0;
    // Called on YOUR Crawler
    public IEnumerable<MenuItem> MenuItems(IActor other) {
        List<MenuItem> result = new();
        switch (other.Faction) {
        case Faction.Bandit:
            result.Add(new ActionMenuItem("BF", $"Flee the bandit crawler {other.Name}", _ => Flee(other)));
            result.Add(new ActionMenuItem("BA", $"Attack the bandit crawler {other.Name}", _ => Attack(other)));
            break;
        case Faction.Hero: break;
        case Faction.Player: break;
        case Faction.Trade:
            result.Add(new ActionMenuItem("TR", $"Trade with {other.Name}", _ => Trade(other)));
            break;
        case Faction.Mercenary: break;
        case Faction.Civilian: break;
        }
        return result;
    }
    int Flee(IActor other) {
        Console.WriteLine($"Fleeing {other.Name}");
        return 1;
    }
    int Trade(IActor other) {
        var (tradeResult, args) = CrawlerEx.Menu($"{other.Name} Trade Menu", Menu_OffersFrom(other).ToArray());
        if (tradeResult.IsSeparator || tradeResult.IsCancel) {
            return 0;
            // cancelled, no cost
        }
        if (tradeResult is ActionMenuItem tradeAction) {
            return tradeAction.TryRun(args);
        };
        return 0;
    }
    public IEnumerable<TradeOffer> TradeOffers(IActor other) => Offers;
    IEnumerable<MenuItem> Menu_OffersFrom(IActor Seller) {
        List<MenuItem> result = new();
        result.Add(MenuItem.Sep);
        result.Add(new ActionMenuItem("X", "Cancel", null));
        int Index = 0;
        foreach (var offer in Seller.TradeOffers(this)) {
            if (Index % 3 == 0) {
                result.Add(MenuItem.Sep);
            }
            ++Index;

            var lambda = (string args) => {
                int count = int.Parse(args);
                if (count <= 1) {
                    count = 1;
                }
                int transferred = 0;
                while (transferred < count) {
                    if (offer.CanPerform(Inv, Seller.Inv)) {
                        offer.Perform(Inv, Seller.Inv);
                        ++transferred;
                    } else {
                        break;
                    }
                }
                return transferred > 0 ? 1 : 0;
            };
            bool canPerform = offer.CanPerform(Inv, Seller.Inv);
            result.Add(new ActionMenuItem($"T{Index}", offer.ToString(), canPerform ? lambda : null));
        }
        return result;

    }
    public void Tick(IEnumerable<IActor> Actors) {
        ++Hour;
        Recharge(1);
        Rations -= CrawlerEx.StochasticInt(Crew * 0.03);
        if (Rations <= 0) {
            Crew = CrawlerEx.StochasticInt(Crew * 0.97);
            Rations = 0;
        }
        if (IsDepowered) {
            Morale -= CrawlerEx.StochasticInt(-0.05);
        }
        if (Faction is Faction.Bandit) {
            TickBandit(Actors);
        }
    }
    protected void TickBandit(IEnumerable<IActor> Actors) {
        if (IsDepowered) {
            Console.WriteLine($"{Name} is radio silent.");
            return;
        }
        foreach (var actor in Actors) {
            if (actor.Faction == Faction.Player) {
                Attack(actor);
                break;
            }
        }
    }

    public Inventory Inv { get; set; } = Inventory.Empty;
    public List<TradeOffer> Offers { get; set; } = new();

    public int TotalPower => ActiveSegments.Sum(s => s.Power);
    public int TotalWeight => Segments.Sum(s => s.Weight > 0 ? s.Weight : 0);
    public int TotalCarry => ActiveSegments.Sum(s => s.Weight < 0 ? -s.Weight : 0);
    public int TotalPullOn(TerrainType t) => ActiveSegments.Sum(s => s.SegmentClass is SegmentClass.Traction && s.Amount >= ( int ) t ? s.Rate : 0);
    public int TotalPull => TotalPullOn(Location.Terrain);
    public int TotalReactor => ActiveSegments.Sum(s => s.Type is SegmentType.Reactor ? s.Amount : 0);
    public int TotalRecharge => ActiveSegments.Sum(s => s.Type is SegmentType.Charger ? s.Amount : 0);
    public int TotalBattery => ActiveSegments.Sum(s => s.Type is SegmentType.Battery ? s.Amount : 0);

    public string SegmentString => string.Join("", Segments.Select(s => s.Name[0]));
    public string Report() {
        var CurrentTerrain = Location.Terrain;
        int terrainRate = TotalPull;
        int fuelEfficiency = 100 - ( int ) (Math.Sqrt(TotalRecharge) * 10);

        string result = Style.Name.Format(Name) + $" on {CurrentTerrain} terrain.  {Scrap,7}¢\n";
        result += $" MW  :{TotalPower,3}/{TotalReactor,3} BAT:{TotalBattery}\n";
        result += $"TONS:{TotalWeight,3}/{TotalCarry,3}\n";
        result += $"PULL:{terrainRate,3}   FUEL:{Fuel,3} EFF:{fuelEfficiency,3}\n";
        result += $"CREW:{Crew,3}  FOOD:{Rations,3}  MOOD:{Morale,3}\n";
        result += string.Join("\n", Segments.Select(s => s.Report));
        result += $"\nINV:{Inv}";
        return result;
    }
    public string Colored() {
        IEnumerable<StyledString> coloredSegments = [
            new StyledString(Style.SegmentNone, " | "),
            new StyledString(Style.SegmentNone, "/ \\"),
            .. Segments.Select(s => s.Colored),
            new StyledString(Style.SegmentNone, "   "),
            new StyledString(Style.SegmentNone, "|<|"),
        ];
        string result = coloredSegments.TransposeJoinStyled();
        result = Style.Name.Format(Name) + "\n" + result;
        return result;
    }

    public List<Segment> Segments => Inv.Segments.ToList();
    public List<Segment> ActiveSegments => Segments.Where(s => s.IsActive).ToList();
    public List<Segment> UndestroyedSegments => Segments.Where(s => !s.IsDestroyed).ToList();
    public int Scrap {
        get => Inv[Commodity.Scrap];
        set => Inv[Commodity.Scrap] = value;
    }
    public int Fuel {
        get => Inv[Commodity.Fuel];
        set => Inv[Commodity.Fuel] = value;
    }
    public int Rations {
        get => Inv[Commodity.Ration];
        set => Inv[Commodity.Ration] = value;
    }
    public int Crew {
        get => Inv[Commodity.Crew];
        set => Inv[Commodity.Crew] = value;
    }
    public int Morale {
        get => Inv[Commodity.Morale];
        set => Inv[Commodity.Morale] = value;
    }
    //public string? FailState => IsDepowered ? "Depowered" : IsStranded ? "Stranded" : null;
    public string? FailState => Crew <= 0 ? (Rations <= 0 ? "Starved" : "Killed") : Morale <= 0 ? "Revolt" : null;
    public bool IsDepowered => TotalReactor <= 0 && TotalBattery <= 0;
    public bool IsStranded => TotalCarry <= 0 || TotalPull <= 0;

    public override string ToString() => $"{Name} ({Faction})";
    int Attack(IActor Other) {
        if (IsDepowered) {
            Console.WriteLine($" {Name} Can't attack {Other.Name}: No power");
            return 0;
        }
        Console.WriteLine($" {Name} Attacking {Other.Name}");
        Console.WriteLine(Brief(Faction == Faction.Player ? 1 : 0));
        List<HitRecord> fire = new();
        foreach (Segment segment in Segments) {
            double amount = segment.Amount;
            double rate = segment.Rate;
            int aim = 0;
            double critChance = 0.2;
            switch (segment.Type) {
            case SegmentType.Guns:
                if (Random.Shared.NextDouble() < critChance) {
                    rate = rate * 2;
                }
                break;
            case SegmentType.Lasers:
                if (Random.Shared.NextDouble() < critChance) {
                    amount = amount * 2;
                }
                break;
            case SegmentType.Missiles:
                if (Random.Shared.NextDouble() < critChance) {
                    aim += 1;
                }
                break;
            default:
                continue;
            }
            for (int i = 0; i < rate; ++i) {
                int damage = amount.StochasticInt();
                fire.Add(new(damage, aim));
            }
        }
        if (fire.Any()) {
            Other.ReceiveFire(this, fire);
        }
        Console.WriteLine(Other.Brief(Other.Faction == Faction.Player ? 1 : 0));
        return 1;
    }
    public void Recharge(int Count) {
        var RechargedSegments = Inv.Segments.Select(s => s.Recharged(Count)).ToList();
        Inv.Segments.Clear();
        Inv.Segments.AddRange(RechargedSegments);
    }
    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        if (!Inv.Segments.Any()) {
            return;
        }
        foreach (var hit in fire) {
            int damage = hit.Damage;
            int soaked = 0;
            var armorSegments = Segments.Index().Where(si => si.Item.SegmentClass is SegmentClass.Defense).Select(si => si.Index).ToArray();
            var nonArmorSegments = ActiveSegments.Index().Where(si => si.Item.SegmentClass is not SegmentClass.Defense).Select(si => si.Index).ToArray();
            string armorSegmentName = string.Empty;
            var hitType = hit.Hit;
            if (hitType is HitType.Misses) {
                damage = 0;
            }
            string msg = $"{from.Name} {hitType} {Name}";
            if (damage > 0) {
                msg += $" for {damage}";
            }
            if (hitType is HitType.Hits && armorSegments.Any()) {
                var armorSegmentIndex = Random.Shared.Next(armorSegments.Length);
                var segmentIndex = armorSegments[armorSegmentIndex];
                var armorSegment = Inv.Segments[segmentIndex];
                armorSegmentName = armorSegment.Name;
                switch (armorSegment.Type) {
                case SegmentType.Armor:
                    // Damage reduction, half if disabled, none if destroyed
                    if (armorSegment.IsActive) {
                        soaked = armorSegment.Amount;
                        //damage = Math.Max(0, damage - armorSegment.Amount);
                    } else if (armorSegment.IsDisabled) {
                        soaked = armorSegment.Amount / 2;
                    }
                    break;
                case SegmentType.Shield:
                    // Limited damage reduction
                    if (armorSegment.IsActive) {
                        var shieldLeft = armorSegment.StateLeft;
                        soaked = Math.Min(damage, shieldLeft);
                        armorSegment = armorSegment.AddState(-soaked);
                    }
                    break;
                case SegmentType.Ablative:
                    // Half damage and lots of hit points
                    if (armorSegment.IsActive) {
                        soaked = (damage + armorSegment.Amount) / 2;
                    } else if (armorSegment.IsDisabled) {
                        soaked = (damage + armorSegment.Amount) / 3;
                    }
                    break;
                default:
                    break;
                }
                soaked = Math.Clamp(soaked, 0, damage);
                // Armor damage is always applied before non-armor damage ( so we can't take more than we')
                var armorDamage = damage;
                armorDamage = Math.Max(0, armorDamage - soaked);
                armorDamage = Math.Min(armorDamage, armorSegment.HPLeft);
                armorSegment = armorSegment.AddDamage(armorDamage);
                Inv.Segments[segmentIndex] = armorSegment;
                var startDamage = damage;
                damage -= soaked + armorDamage;
                if (soaked > 0) {
                    msg += $", -{soaked} from {armorSegmentName}";
                }
                if (armorDamage > 0) {
                    msg += $", {armorDamage} to {armorSegmentName} {armorSegment.StateString}";
                }
            } // else penetrating
            if (damage > 0) {
                var hitSegmentIndex = Random.Shared.Next(nonArmorSegments.Length);
                var hitIndex = nonArmorSegments[hitSegmentIndex];
                var hitSegment = Inv.Segments[hitIndex];
                var segmentDamage = Math.Min(damage, hitSegment.HPLeft);
                hitSegment = hitSegment.AddDamage(segmentDamage);
                damage -= segmentDamage;
                hitSegment = hitSegment.AddDamage(damage);
                Inv.Segments[hitIndex] = hitSegment;
                msg += $", {segmentDamage} to {hitSegment.Name} {hitSegment.StateString}";
            }
            if (damage > 0) {
                LoseCrew(damage);
                msg += $", {damage} to crew";
                damage = 0;
            }
            Console.WriteLine(msg);

        }
    }

    void LoseCrew(int damage) {
        Inv[Commodity.Crew] -= damage;
    }
}
