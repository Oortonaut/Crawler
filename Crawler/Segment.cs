namespace Crawler;

public enum SegmentKind {
    Power,
    Traction,
    Offense,
    Defense,
}

// Power scaling for segments uses tiers or levels for each value which map onto the
// actual value. The ExpPowerScaling uses a baseline of tier 1
public record PowerScaling(float Min, Tier Tier, string badName, string goodName) {
    float numSizes = 5;
    float numUpgrades = 3;
    public const int NA = -1000;
    public float At(float size, float quality) {
        double l = Math.Exp((size - 1) * Math.Log(Tier.Size) / numSizes);
        double u = Math.Exp(quality * Math.Log(Tier.Quality) / numUpgrades);
        double Y = Min * l * u;
        return ( float ) Y;
    }
    public float this[Tier tier] => tier.Size <= NA / 2.0f ? 0 : At(tier.Size, tier.Quality);
}

public record struct Tier(float Size, float Quality) {
    public Tier(float size) : this(size, 0) {}
    public static Tier operator +(Tier tier, Tier bias) => new(tier.Size + bias.Size, tier.Quality + bias.Quality);
    public static Tier operator -(Tier tier, Tier bias) => new(tier.Size - bias.Size, tier.Quality - bias.Quality);
    public static Tier operator +(Tier tier, float qualityBias) => new(tier.Size, tier.Quality + qualityBias);
    public static Tier operator -(Tier tier, float qualityBias) => new(tier.Size, tier.Quality - qualityBias);
    public static Tier PlusLevel(Tier tier, float levelBias) => new(tier.Size + levelBias, tier.Quality);
    public static Tier MinusLevel(Tier tier, float levelBias) => new(tier.Size - levelBias, tier.Quality);
    public static Tier NA => new(PowerScaling.NA, 0);
    public static implicit operator Tier(float size) => new(size);
    public static implicit operator Tier((float size, float quality) a) => new(a.size, a.quality);
    public override string ToString() => $"S{Size}Q{Quality}";
}

// Segment defs can be leveled by adding the difference from the reference tier to
// all other tiered values.
// So a reference tier 3 segment might be leveled up to tier 5 by adding 2 to all
public record SegmentDef(
    char Symbol,
    Tier ReferenceTier,
    string Name,
    SegmentKind SegmentKind,
    Tier WeightTier,
    Tier DrainTier,
    Tier CostTier,
    Tier MaxHitsTier) {
    public virtual Segment NewSegment() => new(this, null);

    public float Weight => Tuning.Segments.WeightTiers[WeightTier];
    public float Drain => Tuning.Segments.DrainTiers[DrainTier];
    public float Cost => Tuning.Segments.CostTiers[CostTier];
    public int MaxHits => ( int ) Math.Round(Tuning.Segments.MaxHitsTiers[MaxHitsTier]);
    public virtual char ClassCode => '?';

    public virtual SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return this with {
            ReferenceTier = ReferenceTier + delta,
            WeightTier = WeightTier + delta,
            DrainTier = DrainTier + delta,
            CostTier = CostTier + delta,
            MaxHitsTier = MaxHitsTier + delta,
        };
    }
    public SegmentDef UpgradeWeight(int Upgrade) => this with {
        WeightTier = WeightTier + new Tier(0, Upgrade)
    };
    public SegmentDef UpgradeDrain(int Upgrade) => this with {
        DrainTier = DrainTier + new Tier(0, Upgrade)
    };
    public SegmentDef UpgradeCost(int Upgrade) => this with {
        CostTier = CostTier + new Tier(0, Upgrade)
    };
    public SegmentDef UpgradeMaxHits(int Upgrade) => this with {
        MaxHitsTier = MaxHitsTier + new Tier(0, Upgrade)
    };
}
public class Segment(SegmentDef segmentDef, IActor? Owner) {
    public SegmentDef SegmentDef { get; } = segmentDef;
    public IActor? Owner { get; set; } = Owner;
    public string Name => SegmentDef.Name;
    public char Symbol => SegmentDef.Symbol;
    public SegmentKind SegmentKind => SegmentDef.SegmentKind;
    public float Weight => SegmentDef.Weight;
    public float Drain => SegmentDef.Drain;
    public float Cost => SegmentDef.Cost;
    public int MaxHits => SegmentDef.MaxHits;
    public char ClassCode => SegmentDef.ClassCode;
    public virtual string Report => $"{Name} {GetStyle()}";

    public virtual void Tick() { }

    int _hits = 0;
    public int Hits {
        get => _hits;
        set => _hits = Math.Clamp(value, 0, SegmentDef.MaxHits);
    }

    public bool Packaged { get; set; } = true;
    public bool Activated { get; set; } = true;
    // Returns amount of damage sunk (not necessarily dealt)
    public virtual (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (hitType is HitType.Misses) {
            return (0, "");
        }
        string msg = Name;
        if (State is Working.Packaged) {
            int soaked = delta / 2;
            // takes 1/2 damage
            delta -= soaked;
            msg += $", packaging soaked {soaked}";
        }
        var start = Hits;
        Hits += delta;
        int Taken = Hits - start;
        if (Taken > 0) {
            msg += $", took {Taken}{StateString}";
        }
        var remaining = delta - Taken;
        return (remaining, msg);
    }
    public enum Working {
        Active,
        Disabled,
        Destroyed,
        Deactivated,
        Packaged,
    }
    public Working State =>
        Health <= 0 ? Working.Destroyed :
        !Packaged ? Working.Packaged :
        !Activated ? Working.Deactivated :
        Health <= MaxHits / 2 ? Working.Disabled :
        Working.Active;
    public bool IsActive => State == Working.Active;
    public string StateString => State switch { Working.Active => "", _ => $" ({State})" };
    public string StateName => Name + StateString;

    public int Health => MaxHits - Hits;

    protected Style GetStyle() {
        Style result = Style.None;
        switch (State) {
        case Working.Active:
            result = Style.SegmentActive;
            break;
        case Working.Packaged:
            result = Style.SegmentActive;
            break;
        case Working.Deactivated:
            result = Style.SegmentDeactivated;
            break;
        case Working.Disabled:
            result = Style.SegmentDisabled;
            break;
        case Working.Destroyed:
            result = Style.SegmentDestroyed;
            break;
        }
        return result;
    }
    public StyledString Colored(Location Location) => new(GetStyle(), StatusLine(Location));
    public string StatusLine(Location Location) => $"{ClassCode}{Symbol}{levelCode(Health),1}{StateCode}{ReportCode(Location)}";
    public char StateCode => State switch {
        Working.Active => '+',
        Working.Disabled => '-',
        Working.Deactivated => '_',
        Working.Destroyed => 'x',
        Working.Packaged => '/',
        _ => '?',
    };
    public virtual char ReportCode(Location Location) => ' ';
    static string levels = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    protected static char levelCode(int x) => x < 0 || x >= levels.Length ? '-' : levels[x];
    static string fracLevels = " ░▒▓█";
    protected static char fracCode(int x, int n) => fracLevels[Math.Clamp((x * fracLevels.Length) / n, 0, fracLevels.Length - 1)];
    protected static char fracCode(float x, float n = 1.0f) => fracLevels[(Math.Clamp(( int ) ((x * fracLevels.Length) / n), 0, fracLevels.Length - 1))];
}

public record OffenseDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, ReferenceTier, Name, SegmentKind.Offense, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override OffenseSegment NewSegment() => new(this, null);
    public override char ClassCode => 'O';
}
public class OffenseSegment(OffenseDef OffenseDef, IActor? Owner): Segment(OffenseDef, Owner) {
}
public record WeaponDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : OffenseDef(Symbol, ReferenceTier, Name, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override WeaponSegment NewSegment() => new(this, null);

    public float Damage => Tuning.Segments.DamageTiers[DamageTier];
    public float Rate => Tuning.Segments.RateTiers[RateTier];
    public float Shots => Tuning.Segments.VolleyTiers[ShotsTier];
    public float Aim => Tuning.Segments.AimTiers[AimTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( WeaponDef ) base.Resize(Size)) with {
            DamageTier = DamageTier + delta,
            RateTier = RateTier + delta,
            ShotsTier = ShotsTier + delta,
            AimTier = AimTier + delta,
        };
    }
}
public class WeaponSegment(WeaponDef weaponDef, IActor? Owner): OffenseSegment(weaponDef, Owner) {
    public float Damage => weaponDef.Damage;
    public float Rate => weaponDef.Rate;
    public float Shots => weaponDef.Shots;
    public float Aim => weaponDef.Aim;
    public virtual IEnumerable<HitRecord> GenerateFire(int aim) {
        if (IsActive) {
            for (int i = 0; i < Shots; i++) {
                yield return new(this, Damage, Aim + aim);
            }
        }
    }
    public virtual IEnumerable<HitRecord> Adjust(HitRecord hit) {
        yield return hit;
    }
}
public record GunDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, ReferenceTier, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
    public override GunSegment NewSegment() => new(this, null);
}
public class GunSegment(GunDef GunDef, IActor? Owner): WeaponSegment(GunDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit) {
        if (Random.Shared.NextDouble() < 0.2) {
            yield return hit;
        }
        yield return hit;
    }
}
public record LaserDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, ReferenceTier, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
    public override LaserSegment NewSegment() => new(this, null);
}
public class LaserSegment(LaserDef LaserDef, IActor? Owner): WeaponSegment(LaserDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit) {
        if (Random.Shared.NextDouble() < 0.2) {
            hit = hit with {
                Damage = hit.Damage * 2
            };
        }
        yield return hit;
    }
}
public record MissileDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, ReferenceTier, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
    public override MissileSegment NewSegment() => new(this, null);
}
public class MissileSegment(MissileDef MissileDef, IActor? Owner): WeaponSegment(MissileDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit) {
        if (Random.Shared.NextDouble() < 0.2) {
            hit = hit with {
                Aim = hit.Aim + 2
            };
        }
        yield return hit;
    }
}
public record PowerDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, ReferenceTier, Name, SegmentKind.Power, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override PowerSegment NewSegment() => new(this, null);
    public override char ClassCode => 'R';
}
public class PowerSegment(PowerDef PowerDef, IActor? Owner): Segment(PowerDef, Owner) {
}
public record ReactorDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier CapacityTier, Tier ChargerTier)
    : PowerDef(Symbol, ReferenceTier, Name, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ReactorSegment NewSegment() => new(this, null);
    public float Capacity => Tuning.Segments.CapacityTiers[CapacityTier];
    public float Generation => Tuning.Segments.GenerationTiers[ChargerTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( ReactorDef ) base.Resize(Size)) with {
            CapacityTier = CapacityTier + delta,
            ChargerTier = ChargerTier + delta,
        };
    }
}
public class ReactorSegment(ReactorDef reactorDef, IActor? Owner): PowerSegment(reactorDef, Owner) {
    public float Capacity => reactorDef.Capacity;
    public float Generation => reactorDef.Generation;
    public override char ReportCode(Location _) => fracCode(Charge, Capacity);

    float _charge = 0;
    public float Charge {
        get => _charge;
        set => _charge = Math.Clamp(value, 0, Capacity);
    }
    // Returns remaining generation
    public float Generate() {
        if (IsActive) {
            float Expected = Charge + Generation;
            Charge = Expected;
            return Expected - Charge;
        } else {
            return 0;
        }
    }
}
public record ChargerDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier ChargeTier)
    : PowerDef(Symbol, ReferenceTier, Name, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ChargerSegment NewSegment() => new(this, null);
    public float Charge => Tuning.Segments.ChargerTiers[ChargeTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( ChargerDef ) base.Resize(Size)) with {
            ChargeTier = ChargeTier + delta,
        };
    }
}
public class ChargerSegment(ChargerDef ChargerDef, IActor? Owner): PowerSegment(ChargerDef, Owner) {
    public float Generation => ChargerDef.Charge;

    // Returns remaining generation
    public float Generate() {
        if (IsActive) {
            return Generation;
        }
        return 0;
    }
}
public record TractionDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier LiftTier, Tier SpeedTier, TerrainType TerrainLimit)
    : SegmentDef(Symbol, ReferenceTier, Name, SegmentKind.Traction, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override TractionSegment NewSegment() => new(this, null);
    public override char ClassCode => 'T';
    public float Lift => Tuning.Segments.LiftTiers[LiftTier];
    public float Speed => Tuning.Segments.SpeedTiers[SpeedTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( TractionDef ) base.Resize(Size)) with {
            LiftTier = LiftTier + delta,
            SpeedTier = SpeedTier + delta,
        };
    }
}
public class TractionSegment(TractionDef tractionDef, IActor? Owner): Segment(tractionDef, Owner) {
    public float Lift => tractionDef.Lift;
    public float Speed => tractionDef.Speed;
    public TerrainType TerrainLimit => tractionDef.TerrainLimit;
    public override char ReportCode(Location Location) => CapCode(Location.Terrain);
    public char CapCode(TerrainType terrain) => fracCode(Penalty(terrain, TerrainSpeedPenalty));

    const float TerrainLiftPenalty = 0.8f;
    const float TerrainSpeedPenalty = 0.7f;
    const float TerrainPowerPenalty = 1.3f;
    float Penalty(TerrainType terrain, float penalty) =>
        terrain < TerrainLimit ? 1 :
        terrain == TerrainLimit ? penalty : 0;

    public float LiftOn(TerrainType terrain) => Lift * Penalty(terrain, TerrainLiftPenalty);
    public float SpeedOn(TerrainType terrain) => Speed * Penalty(terrain, TerrainSpeedPenalty);
    public float DrainOn(TerrainType terrain) => Drain * Penalty(terrain, TerrainPowerPenalty);
}
public record DefenseDef(char Symbol, Tier ReferenceTier, string Name, SegmentKind SegmentKind, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, ReferenceTier, Name, SegmentKind, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override DefenseSegment NewSegment() => new(this, null);
    public override char ClassCode => 'D';
}
public class DefenseSegment(DefenseDef defenseDef, IActor? Owner): Segment(defenseDef, Owner) {
}
public record ArmorDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier ReductionTier)
    : DefenseDef(Symbol, ReferenceTier, Name, SegmentKind.Defense, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ArmorSegment NewSegment() => new(this, null);

    public int Reduction => ( int ) Math.Round(Tuning.Segments.ReductionTiers[ReductionTier]);

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( ArmorDef ) base.Resize(Size)) with {
            ReductionTier = ReductionTier + delta,
        };
    }
}
public class ArmorSegment(ArmorDef armorDef, IActor? Owner): DefenseSegment(armorDef, Owner) {
    virtual public int Reduction => armorDef.Reduction;
    // returns damage sunk
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (State is Working.Active or Working.Disabled && hitType is HitType.Hits) {
            int reduction = Reduction;
            if (State is Working.Disabled) {
                reduction = (reduction + 1) / 2;
            }
            if (delta > reduction) {
                var start = Hits;
                var (rem, _) = base.AddDmg(hitType, delta - reduction);
                var msg = $" {Name}, soaked {reduction}";
                if (Hits > start) {
                    msg += $", took {Hits - start}";
                }
                msg += StateString;
                return (rem, msg);
            } else {
                return (0, $" {Name} soaked {delta}{StateString}");
            }
        }
        return base.AddDmg(hitType, delta);
    }
}
public record PlatingDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier MitigationTier)
    : DefenseDef(Symbol, ReferenceTier, Name, SegmentKind.Defense, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override PlatingSegment NewSegment() => new(this, null);
    public float Mitigation => Tuning.Segments.MitigationTiers[MitigationTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( PlatingDef ) base.Resize(Size)) with {
            MitigationTier = MitigationTier + delta,
        };
    }
}
public class PlatingSegment(PlatingDef PlatingDef, IActor? Owner): DefenseSegment(PlatingDef, Owner) {
    public float Mitigation => PlatingDef.Mitigation;
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        string msg = $" {Name}";
        if (State is Working.Active or Working.Disabled && hitType is HitType.Hits) {
            int remaining = (delta * (1 - Mitigation)).StochasticInt();
            if (remaining != delta) {
                msg += $", {delta - remaining} absorbed";
            }
        }
        if (delta > 0) {
            string desc;
            (delta, desc) = base.AddDmg(hitType, delta);
            msg += desc;
        }
        return (delta, msg);
    }
}
public record ShieldDef(char Symbol, Tier ReferenceTier, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier CapacityTier, Tier ChargeTier)
    : DefenseDef(Symbol, ReferenceTier, Name, SegmentKind.Defense, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override ShieldSegment NewSegment() => new(this, null);

    public int Capacity => ( int ) Math.Round(Tuning.Segments.ShieldCapacityTiers[CapacityTier]);
    public int Charge => ( int ) Math.Round(Tuning.Segments.ShieldChargeTiers[ChargeTier]);

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - ReferenceTier.Size);
        return (( ShieldDef ) base.Resize(Size)) with {
            CapacityTier = CapacityTier + delta,
            ChargeTier = ChargeTier + delta,
        };
    }
}
public class ShieldSegment(ShieldDef shieldDef, IActor? Owner): DefenseSegment(shieldDef, Owner) {
    public int Capacity => shieldDef.Capacity;
    public int Charge => shieldDef.Charge;
    public override char ReportCode(Location _) => fracCode(ShieldLeft, Capacity);

    public override void Tick() {
        if (IsActive) {
            ShieldLeft += Charge;
        }
    }

    int _shieldLeft = 0;
    public int ShieldLeft {
        get => _shieldLeft;
        set => _shieldLeft = Math.Clamp(value, 0, Capacity);
    }
    // Returns amount of source damage remaining
    public (int, string) AddShieldDmg(int delta) {
        var start = ShieldLeft;
        ShieldLeft -= delta;
        var taken = start - ShieldLeft;
        var rem = delta - taken;
        if (taken > 0) {
            return (rem, $" {Name}, soaked {taken}");
        } else {
            return (rem, $" {Name}");
        }
    }
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (State is Working.Active or Working.Disabled && hitType is not HitType.Misses) {
            var (rem, txt) = AddShieldDmg(delta);
            if (rem > 0) {
                var start = Hits;
                (rem, _) = base.AddDmg(hitType, rem);
                if (Hits > start) {
                    txt += $", took {Hits - start}";
                }
            }
            txt += StateString;
            return (rem, txt);
        }
        return base.AddDmg(hitType, delta);
    }
}
public static class SegmentEx {
    // Becase leveling a segment by a level causes an increase in all its tiers,
    // Each individual tier can vary from the reference tier by one or two as long as the
    // average comes to [tier -0.5 (shitty items) to  tier + 1.5 (best in class items).
    public static List<WeaponDef> BaseWeaponDefs = [
        new GunDef('g', 1, "Guns", 1, 1, 1, 1, 1, 1, 1, 1),
        new LaserDef('l', 1, "Lasers", 1, 1, 1, 1, 1, 1, 1, 1),
        new MissileDef('m', 1, "Missiles", 1, 1, 1, 1, 1, 1, 1, 1),
    ];

    public static List<WeaponDef> WeaponDefs = Variations(BaseWeaponDefs).ToList();

    public static List<OffenseDef> OffenseDefs = [
        .. WeaponDefs,
    ];

    // Power definitions (Amount = Capacity, Rate = Refill per round)
    public static List<PowerDef> PowerDefs = [
        new ReactorDef('r', 1, "Reactor", 1, 1, 1, 1, 2),
        new ReactorDef('b', 1, "Battery", 1, 1, 1, 2, 0.5f),
        new ChargerDef('c', 1, "Charger", 1, 1, 1, 1.5f),
    ];

    public static IEnumerable<PowerDef> CorePowerDefs => PowerDefs.OfType<ReactorDef>();

    // Traction definitions (Amount = Terrain handling, Rate = Move speed)
    public static List<TractionDef> TractionDefs = [
        new TractionDef('-', 1, "Legs", 1, 1, 1.5f, 1, 0.5f, 1.0f, TerrainType.Shattered),
        new TractionDef('O', 1, "Wheels", 1, 1, 1, 1, 1.0f, 1.5f, TerrainType.Rough),
        new TractionDef('%', 1, "Treads", 1, 1, 2.0f, 1, 1.5f, 0.5f, TerrainType.Broken),
    ];

    public static IEnumerable<TractionDef> CoreTractionDefs => TractionDefs;

    // Defense definitions (Amount = Damage soaked, Rate = Hits before destruction)
    public static List<DefenseDef> DefenseDefs = [
        new ArmorDef('a', 1, "Armor", 1, 1, 1, 1),
        new PlatingDef('p', 1, "Plating", 1, 1, 1.5f, 1),
        new ShieldDef('s', 1, "Shields", 1, 1, 1, 1, 1, 1),
    ];

    public static IEnumerable<DefenseDef> CoreDefenseDefs => DefenseDefs;

    public static List<SegmentDef> AllDefs = [
        .. OffenseDefs,
        .. PowerDefs,
        .. TractionDefs,
        .. DefenseDefs,
    ];

    // public static Dictionary<char, SegmentDef> Lookup = AllDefs.ToDictionary(s => s.Symbol);
    public static Dictionary<string, SegmentDef> NameLookup = AllDefs.ToDictionary(s => s.Name);

    public static EArray<SegmentKind, SegmentDef[]> SegmentClassLookup = AllDefs
        .GroupBy(s => s.SegmentKind)
        .Select(g => (g.Key, g.ToArray()))
        .ToEArray();

    public static WeaponDef Heavy(this WeaponDef weapon, float qualityBias = 1.0f) => weapon with {
        Name = $"Heavy {weapon.Name}",
        DamageTier = weapon.DamageTier + qualityBias * 2,
        RateTier = weapon.RateTier - qualityBias / 2,
        DrainTier = weapon.DrainTier - qualityBias / 2,
        CostTier = weapon.CostTier + qualityBias, // this should be == net quality
    };

    public static WeaponDef Rapid(this WeaponDef weapon, float qualityBias = 1.0f) => weapon with {
        Name = $"Rapid {weapon.Name}",
        DamageTier = weapon.DamageTier - qualityBias / 2,
        RateTier = weapon.RateTier + qualityBias * 2,
        DrainTier = weapon.DrainTier - qualityBias / 2,
        CostTier = weapon.CostTier + qualityBias,
    };

    public static IEnumerable<WeaponDef> Variations(IEnumerable<WeaponDef> weapons) {
        foreach (var weapon in weapons) {
            yield return weapon;
            yield return weapon.Heavy();
            yield return weapon.Rapid();
        }
    }
}
