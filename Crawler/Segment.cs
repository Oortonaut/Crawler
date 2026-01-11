namespace Crawler;

public enum SegmentKind {
    Power,
    Traction,
    Offense,
    Defense,
    Industry,
    Storage,
    Harvest,
    Habitat,
}

public enum HarvestType {
    Mining,      // Extracts: Ore, Silicates
    Biomass,     // Extracts: Biomass
    Crystal,     // Extracts: Silicates, Gems
    Isotope,     // Extracts: Isotopes
}

public enum HabitatType {
    Cabin,      // Basic crew quarters for mobile crawlers
    Quarters,   // Standard living space
    Barracks,   // Military/utilitarian - high capacity, lower morale
    Suite,      // Luxury - lower capacity, higher morale bonus
    Dome,       // Large settlement structures (sizes 6-9)
}

// Segment defs can be leveled by adding the difference from the reference tier to
// all other tiered values.
// So a reference tier 3 segment might be leveled up to tier 5 by adding 2 to all
public record SegmentDef(
    char Symbol,
    Tier Size,
    string Name,
    SegmentKind SegmentKind,
    Tier WeightTier,
    Tier DrainTier,
    Tier CostTier,
    Tier MaxHitsTier,
    Tier? FactorySizeTier = null) {

    /// <summary>Factory size for manufacturing. Defaults to Size if not specified.</summary>
    public Tier FactorySize => FactorySizeTier ?? Size;

    public virtual Segment NewSegment(ulong seed) => new(seed, this, null);
    public override string ToString() => $"{Symbol} {Name} {Size} {SegmentKind} {WeightTier} {DrainTier} {CostTier} {MaxHitsTier}";
    public string NameSize => $"{Name} " + (int)Math.Round(Size.Size) switch {
        -1 => "Micro",
        0 => "Mini",
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        10 => "X",
        var x => $"{x}",
    } + (Size.Quality == 0 ? "" : $" {Size.Quality:0.00}");

    public float Weight => Tuning.Segments.WeightTiers[WeightTier];
    public float Drain => Tuning.Segments.DrainTiers[DrainTier];
    public float Cost => Tuning.Segments.CostTiers[CostTier];
    public float Length => Tuning.Segments.LengthTiers[Size];
    public int MaxHits => ( int ) Math.Round(Tuning.Segments.MaxHitsTiers[MaxHitsTier]);
    public virtual char ClassCode => '?';

public virtual SegmentDef Resize(int Size) {
        Tier delta = new(Size - this.Size.Size);
        // FactorySize scales with Size if explicitly set, otherwise stays null (defaults to Size)
        Tier? newFactorySize = FactorySizeTier.HasValue
            ? FactorySizeTier.Value + delta
            : null;
        return this with {
            Size = this.Size + delta,
            WeightTier = WeightTier + delta,
            DrainTier = DrainTier + delta,
            CostTier = CostTier + delta,
            MaxHitsTier = MaxHitsTier + delta,
            FactorySizeTier = newFactorySize,
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
public class Segment(ulong seed, SegmentDef segmentDef, IActor? Owner) {
    public ulong Seed { get; } = seed;
    public SegmentDef SegmentDef { get; } = segmentDef;
    public IActor? Owner { get; set; } = Owner;
    public string Name => SegmentDef.Name;
    public string NameSize => SegmentDef.NameSize;
    public char Symbol => SegmentDef.Symbol;
    public SegmentKind SegmentKind => SegmentDef.SegmentKind;
    public float Length => SegmentDef.Length;
    public float Weight => SegmentDef.Weight;
    public float Drain => SegmentDef.Drain;
    public float Cost => SegmentDef.Cost;
    public int MaxHealth => SegmentDef.MaxHits;
    public char ClassCode => SegmentDef.ClassCode;
    public TimeDuration Cycle { get; set; } = TimeDuration.Zero;
    public XorShift Rng = new(seed);
    public virtual string Report => $"{Name} {GetStyle()}";

    long lastTick = 0;
    public void TickUntil(long time) => Tick(new TimeDuration(time - lastTick));
    public virtual void Tick(TimeDuration elapsed) {
        lastTick += elapsed.TotalSeconds;
        Cycle = Cycle > elapsed ? Cycle - elapsed : TimeDuration.Zero;
    }

    int _hits = 0;
    public int Hits {
        get => _hits;
        set => _hits = Math.Clamp(value, 0, SegmentDef.MaxHits);
    }

    public bool Packaged { get; set; } = true;  // Default to packaged for safety
    public bool Activated { get; set; } = true;
    // Returns amount of damage sunk (not necessarily dealt)
    public virtual (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (hitType is HitType.Misses) {
            return (0, "");
        }
        string msg = Name + " ";
        if (IsPackaged) {
            int soaked = delta / 2;
            // takes 1/2 damage
            delta -= soaked;
            msg += $"packaging soaked {soaked}, ";
        }
        var start = Hits;
        Hits += delta;
        int taken = Hits - start;
        if (taken > 0) {
            msg += $"took {taken}, ";
        } else {
            msg += "no damage.";
        }
        var remaining = delta - taken;
        return (remaining, msg);
    }
    public enum Working {
        Pristine, // Active with no damage
        Running, // Active with some damage
        Damaged, // Damaged such that functionality is impaired
        Destroyed, // Destroyed, cannot be used or repaired
        Deactivated, // Turned off the choose power distribution
        Packaged, // Packed up tight for trade purposes; cannot be used or repaired
    }
    public Working State =>
        Hits >= MaxHealth ? Working.Destroyed :
        Packaged ? Working.Packaged :
        !Activated ? Working.Deactivated :
        Hits > MaxHealth / 2 ? Working.Damaged :
        Hits > 0 ? Working.Running :
        Working.Pristine;
    // Active segments are usable until they take half damage, rounded down
    public bool IsActive => State is Working.Pristine or Working.Running;
    // IsActiveReady combines the usable, running active segments with those that are ready to fire
    public bool IsReadyToFire => IsActive && !Cycle.IsPositive;
    // The cycling state indicates that the weapon is somewhere in the charge cycle.
    public bool IsCycling => IsActive && Cycle.IsPositive;
    public void CycleStart() => Cycle = CycleLength;
    public virtual TimeDuration CycleLength => TimeDuration.Zero;
    public bool IsPristine => State is Working.Pristine;
    public bool IsRunning => State is Working.Running;

    public bool IsDamaged => State is Working.Damaged;

    // Passive segments remain usable until they are destroyed
    public bool IsUsable => State is not Working.Destroyed and not Working.Packaged;
    public bool IsPackaged => State == Working.Packaged;

    public bool IsDestroyed => State == Working.Destroyed;
    public bool IsDeactivated => State == Working.Deactivated;

    public string StateString => $" ({State})";
    public string StateName => Name + StateString;

    public int Health => MaxHealth - Hits;

    protected Style GetStyle() {
        Style result = Style.None;
        switch (State) {
        case Working.Pristine:
        case Working.Running:
            result = Style.SegmentActive;
            break;
        case Working.Packaged:
            result = Style.SegmentPackaged;
            break;
        case Working.Deactivated:
            result = Style.SegmentDeactivated;
            break;
        case Working.Damaged:
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
        Working.Pristine => '*',
        Working.Running => '+',
        Working.Damaged => '~',
        Working.Deactivated => '-',
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

    public virtual Segment Clone() {
        var clone = new Segment(Seed, SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }

    protected void CopyBaseTo(Segment target) {
        target.Hits = this.Hits;
        target.Cycle = this.Cycle;
        target.Packaged = this.Packaged;
        target.Activated = this.Activated;
    }

    // Data structure for serialization
    public record class Data {
        public ulong Seed { get; set; }
        public string DefName { get; set; } = "";
        public int Hits { get; set; }
        public bool Packaged { get; set; }
        public bool Activated { get; set; }
    }

    public virtual Data ToData() {
        return new Data {
            Seed = this.Seed,
            DefName = this.SegmentDef.Name,
            Hits = this.Hits,
            Packaged = this.Packaged,
            Activated = this.Activated
        };
    }

    public virtual void FromData(Data data) {
        Hits = data.Hits;
        Packaged = data.Packaged;
        Activated = data.Activated;
    }
}

public record OffenseDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, Size, Name, SegmentKind.Offense, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override OffenseSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'O';
}
public class OffenseSegment(ulong seed, OffenseDef OffenseDef, IActor? Owner): Segment(seed, OffenseDef, Owner) {
    public override Segment Clone() {
        var clone = new OffenseSegment(Seed, (OffenseDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record WeaponDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier, Commodity? AmmoType = null, Tier AmmoUseTier = default)
    : OffenseDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override WeaponSegment NewSegment(ulong seed) => new(seed, this, null);

    public float Damage => (float)Math.Round(Tuning.Segments.DamageTiers[DamageTier]);
    public float Rate => Tuning.Segments.RateTiers[RateTier];
    public float Shots => (float)Math.Round(Tuning.Segments.ShotsTiers[ShotsTier]);
    public float Aim => Tuning.Segments.AimTiers[AimTier];
    public float AmmoPerShot => AmmoType.HasValue ? Tuning.Segments.AmmoUseTiers[AmmoUseTier] : 0;
    public TimeDuration CycleLength => TimeDuration.FromSeconds(60 / Rate);

    /// <summary>Whether this weapon requires ammunition to fire</summary>
    public bool RequiresAmmo => AmmoType.HasValue;

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( WeaponDef ) base.Resize(Size)) with {
            DamageTier = DamageTier + delta,
            RateTier = RateTier + delta,
            ShotsTier = ShotsTier + delta,
            AimTier = AimTier + delta,
            AmmoUseTier = AmmoUseTier + delta,
        };
    }
}
public class WeaponSegment(ulong seed, WeaponDef weaponDef, IActor? Owner): OffenseSegment(seed, weaponDef, Owner) {
    public float Damage => weaponDef.Damage;
    public float Rate => weaponDef.Rate;
    public float Shots => weaponDef.Shots;
    public float Aim => weaponDef.Aim;
    public Commodity? AmmoType => weaponDef.AmmoType;
    public float AmmoPerShot => weaponDef.AmmoPerShot;
    public bool RequiresAmmo => weaponDef.RequiresAmmo;
    public override TimeDuration CycleLength => weaponDef.CycleLength;

    /// <summary>Check if weapon has enough ammo to fire</summary>
    public bool HasAmmo {
        get {
            if (!RequiresAmmo) return true;
            if (Owner is not Crawler crawler) return false;
            float ammoNeeded = Shots * AmmoPerShot;
            return crawler.Supplies[AmmoType!.Value] >= ammoNeeded;
        }
    }

    /// <summary>Total ammo consumption per firing cycle</summary>
    public float AmmoPerCycle => Shots * AmmoPerShot;

    public virtual IEnumerable<HitRecord> GenerateFire(ulong seed, float aim) {
        if (!IsReadyToFire) yield break;

        // Check and consume ammo if required
        if (RequiresAmmo) {
            if (Owner is not Crawler crawler) yield break;
            float ammoNeeded = Shots * AmmoPerShot;
            if (crawler.Supplies[AmmoType!.Value] < ammoNeeded) yield break;
            crawler.Supplies.Remove(AmmoType!.Value, ammoNeeded);
        }

        var rng = new XorShift(seed);
        for (int i = 0; i < Shots; i++) {
            yield return new(rng.Seed(), this, Damage, Aim + aim);
        }
        CycleStart();
    }
    public virtual IEnumerable<HitRecord> Adjust(HitRecord hit, ulong seed) {
        yield return hit;
    }
    public override Segment Clone() {
        var clone = new WeaponSegment(Seed, (WeaponDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record GunDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier, Tier AmmoTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier, Commodity.Slugs, AmmoTier) {
    public override GunSegment NewSegment(ulong seed) => new(seed, this, null);
}
public class GunSegment(ulong seed, GunDef GunDef, IActor? Owner): WeaponSegment(seed, GunDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit, ulong seed) {
        var rng = new XorShift(seed);
        if (rng.NextDouble() < 0.2) {
            yield return hit;
        }
        yield return hit;
    }
    public override Segment Clone() {
        var clone = new GunSegment(Seed, (GunDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record LaserDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier, Tier AmmoTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier, Commodity.Cells, AmmoTier) {
    public override LaserSegment NewSegment(ulong seed) => new(seed, this, null);
}
public class LaserSegment(ulong seed, LaserDef LaserDef, IActor? Owner): WeaponSegment(seed, LaserDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit, ulong seed) {
        var rng = new XorShift(seed);
        if (rng.NextDouble() < 0.2) {
            hit = hit with {
                Damage = hit.Damage * 2
            };
        }
        yield return hit;
    }
    public override Segment Clone() {
        var clone = new LaserSegment(Seed, (LaserDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record MissileDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier, Tier AmmoTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier, Commodity.Rockets, AmmoTier) {
    public override MissileSegment NewSegment(ulong seed) => new(seed, this, null);
}
public class MissileSegment(ulong seed, MissileDef MissileDef, IActor? Owner): WeaponSegment(seed, MissileDef, Owner) {
    public override IEnumerable<HitRecord> Adjust(HitRecord hit, ulong seed) {
        var rng = new XorShift(seed);
        if (rng.NextDouble() < 0.2) {
            hit = hit with {
                Aim = hit.Aim + 2
            };
        }
        yield return hit;
    }
    public override Segment Clone() {
        var clone = new MissileSegment(Seed, (MissileDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record PowerDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, Size, Name, SegmentKind.Power, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override PowerSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'R';
}
public class PowerSegment(ulong seed, PowerDef PowerDef, IActor? Owner): Segment(seed, PowerDef, Owner) {
    public override Segment Clone() {
        var clone = new PowerSegment(Seed, (PowerDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record ReactorDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier CapacityTier, Tier ChargerTier)
    : PowerDef(Symbol, Size, Name, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ReactorSegment NewSegment(ulong seed) => new(seed, this, null);
    public float Capacity => Tuning.Segments.CapacityTiers[CapacityTier];
    public float Generation => Tuning.Segments.GenerationTiers[ChargerTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( ReactorDef ) base.Resize(Size)) with {
            CapacityTier = CapacityTier + delta,
            ChargerTier = ChargerTier + delta,
        };
    }
}
public class ReactorSegment(ulong seed, ReactorDef reactorDef, IActor? Owner): PowerSegment(seed, reactorDef, Owner) {
    public float Capacity => reactorDef.Capacity;
    public float Generation => reactorDef.Generation;
    public override char ReportCode(Location _) => fracCode(Charge, Capacity);

    float _charge = reactorDef.Capacity;
    public float Charge {
        get => _charge;
        set => _charge = Math.Clamp(value, 0, Capacity);
    }
    // Returns remaining generation
    public float Generate(float durationHrs) {
        if (IsActive) {
            float Expected = Charge + Generation * durationHrs;
            Charge = Expected;
            return Expected - Charge;
        } else {
            return 0;
        }
    }
    public override Segment Clone() {
        var clone = new ReactorSegment(Seed, (ReactorDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        clone.Charge = this.Charge;
        return clone;
    }

    // Extended Data for ReactorSegment
    public new record class Data : Segment.Data {
        public float Charge { get; set; }
    }

    public override Segment.Data ToData() {
        return new Data {
            Seed = this.Seed,
            DefName = this.SegmentDef.Name,
            Hits = this.Hits,
            Packaged = this.Packaged,
            Activated = this.Activated,
            Charge = this.Charge
        };
    }

    public override void FromData(Segment.Data data) {
        base.FromData(data);
        if (data is Data reactorData) {
            Charge = reactorData.Charge;
        }
    }
}
public record ChargerDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier ChargeTier)
    : PowerDef(Symbol, Size, Name, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ChargerSegment NewSegment(ulong seed) => new(seed, this, null);
    public float Generation => Tuning.Segments.ChargerTiers[ChargeTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( ChargerDef ) base.Resize(Size)) with {
            ChargeTier = ChargeTier + delta,
        };
    }
}
public class ChargerSegment(ulong seed, ChargerDef ChargerDef, IActor? Owner): PowerSegment(seed, ChargerDef, Owner) {
    public float Generation => ChargerDef.Generation;

    // Returns remaining generation
    public float Generate(float durationHrs) {
        if (IsActive) {
            return Generation * durationHrs;
        }
        return 0;
    }
    public override Segment Clone() {
        var clone = new ChargerSegment(Seed, (ChargerDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record TractionDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier LiftTier, Tier SpeedTier, TerrainType TerrainLimit)
    : SegmentDef(Symbol, Size, Name, SegmentKind.Traction, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override TractionSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'T';
    public float Lift => Tuning.Segments.LiftTiers[LiftTier];
    public float Speed => Tuning.Segments.SpeedTiers[SpeedTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - base.Size.Size);
        return (( TractionDef ) base.Resize(Size)) with {
            LiftTier = LiftTier + delta,
            SpeedTier = SpeedTier + delta,
        };
    }
}
public class TractionSegment(ulong seed, TractionDef tractionDef, IActor? Owner): Segment(seed, tractionDef, Owner) {
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
    public override Segment Clone() {
        var clone = new TractionSegment(Seed, (TractionDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record DefenseDef(char Symbol, Tier Size, string Name, SegmentKind SegmentKind, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier)
    : SegmentDef(Symbol, Size, Name, SegmentKind, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override DefenseSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'D';
}
public class DefenseSegment(ulong seed, DefenseDef defenseDef, IActor? Owner): Segment(seed, defenseDef, Owner) {
    public override Segment Clone() {
        var clone = new DefenseSegment(Seed, (DefenseDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record ArmorDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier ReductionTier)
    : DefenseDef(Symbol, Size, Name, SegmentKind.Defense, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override ArmorSegment NewSegment(ulong seed) => new(seed, this, null);

    public int Reduction => ( int ) Math.Round(Tuning.Segments.ReductionTiers[ReductionTier]);

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( ArmorDef ) base.Resize(Size)) with {
            ReductionTier = ReductionTier + delta,
        };
    }
}
public class ArmorSegment(ulong seed, ArmorDef armorDef, IActor? Owner): DefenseSegment(seed, armorDef, Owner) {
    virtual public int Reduction => armorDef.Reduction;
    // returns damage sunk
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (IsUsable && hitType is HitType.Hits) {
            int reduction = Reduction;
            if (IsDamaged) {
                reduction = (reduction + 1) / 2;
            }
            reduction = Math.Min(reduction, delta);
            delta -= reduction;
            var start = Hits;
            var msg = $"{Name} ";
            if (reduction > 0) {
                msg += $"soaked {reduction}, ";
            }
            var (rem, baseMsg) = base.AddDmg(hitType, delta - reduction);
            msg += baseMsg + " " + StateString;
            return (rem, msg);
        }
        return base.AddDmg(hitType, delta);
    }
    public override Segment Clone() {
        var clone = new ArmorSegment(Seed, (ArmorDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record PlatingDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier CostTier, Tier MaxHitsTier, Tier MitigationTier)
    : DefenseDef(Symbol, Size, Name, SegmentKind.Defense, WeightTier, Tier.NA, CostTier, MaxHitsTier) {
    public override PlatingSegment NewSegment(ulong seed) => new(seed, this, null);
    public float Mitigation => 1 - Tuning.Segments.MitigationTiers[MitigationTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( PlatingDef ) base.Resize(Size)) with {
            MitigationTier = MitigationTier + delta,
        };
    }
}
public class PlatingSegment(ulong seed, PlatingDef PlatingDef, IActor? Owner): DefenseSegment(seed, PlatingDef, Owner) {
    public float Mitigation => PlatingDef.Mitigation;
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        string msg = $"{Name} ";
        if (IsUsable && hitType is HitType.Hits) {
            int absorbed = (delta * Mitigation).StochasticInt(ref Rng);
            if (absorbed != 0) {
                msg += $", mitigated {absorbed}";
                delta -= absorbed;
            }
        }
        if (delta > 0) {
            string desc;
            (delta, desc) = base.AddDmg(hitType, delta);
            msg += " " + desc;
        }
        msg += " " + StateString;
        return (delta, msg);
    }
    public override Segment Clone() {
        var clone = new PlatingSegment(Seed, (PlatingDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}
public record ShieldDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier CapacityTier, Tier ChargeTier)
    : DefenseDef(Symbol, Size, Name, SegmentKind.Defense, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override ShieldSegment NewSegment(ulong seed) => new(seed, this, null);

    public int Capacity => ( int ) Math.Round(Tuning.Segments.ShieldCapacityTiers[CapacityTier]);
    public float Charge => Tuning.Segments.ShieldChargeTiers[ChargeTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( ShieldDef ) base.Resize(Size)) with {
            CapacityTier = CapacityTier + delta,
            ChargeTier = ChargeTier + delta,
        };
    }
}
public class ShieldSegment(ulong seed, ShieldDef shieldDef, IActor? Owner): DefenseSegment(seed, shieldDef, Owner) {
    public int Capacity => shieldDef.Capacity;
    public float Charge => shieldDef.Charge;
    public override char ReportCode(Location _) => fracCode(ShieldLeft, Capacity);

    public override void Tick(TimeDuration elapsed) {
        base.Tick(elapsed);
        if (IsActive) {
            ShieldLeft += Charge * (float)elapsed.TotalHours;
        } else if (IsUsable) {
            // Hold charge but don't generate new charge
        } else {
            ShieldLeft = 0;
        }
    }

    float _shieldLeft = 0;
    public float ShieldLeft {
        get => _shieldLeft;
        set => _shieldLeft = Math.Clamp(value, 0, Capacity);
    }
    // Returns amount of source damage remaining
    public (int, string) AddShieldDmg(int delta) {
        var start = (int)ShieldLeft;
        ShieldLeft -= delta;
        var taken = start - (int)ShieldLeft;
        var rem = delta - taken;
        if (taken > 0) {
            return (rem, $"{Name} soaked {taken}, ");
        } else {
            return (rem, $"{Name}");
        }
    }
    public override (int remaining, string desc) AddDmg(HitType hitType, int delta) {
        if (IsActive || State is Working.Damaged && hitType is not HitType.Misses) {
            var (rem, msg) = AddShieldDmg(delta);
            if (rem > 0) {
                var start = Hits;
                string baseMsg;
                (rem, baseMsg) = base.AddDmg(hitType, rem);
                msg += " " + baseMsg;
            }
            msg += " " + StateString;
            return (rem, msg);
        }
        return base.AddDmg(hitType, delta);
    }
    public override Segment Clone() {
        var clone = new ShieldSegment(Seed, (ShieldDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        clone.ShieldLeft = this.ShieldLeft;
        return clone;
    }

    // Extended Data for ShieldSegment
    public new record class Data : Segment.Data {
        public float ShieldLeft { get; set; }
    }

    public override Segment.Data ToData() {
        return new Data {
            Seed = this.Seed,
            DefName = this.SegmentDef.Name,
            Hits = this.Hits,
            Packaged = this.Packaged,
            Activated = this.Activated,
            ShieldLeft = this.ShieldLeft
        };
    }

    public override void FromData(Segment.Data data) {
        base.FromData(data);
        if (data is Data shieldData) {
            ShieldLeft = shieldData.ShieldLeft;
        }
    }
}

// ============================================
// INDUSTRY SEGMENTS
// ============================================

public record IndustryDef(
    char Symbol,
    Tier Size,
    string Name,
    Tier WeightTier,
    Tier DrainTier,
    Tier CostTier,
    Tier MaxHitsTier,
    Tier ThroughputTier,
    Tier EfficiencyTier,
    Tier ActivateChargeTier,
    Production.IndustryType IndustryType
) : SegmentDef(Symbol, Size, Name, SegmentKind.Industry, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override IndustrySegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'I';

    /// <summary>Production speed multiplier (1.0 = normal speed)</summary>
    public float Throughput => Tuning.Segments.ThroughputTiers[ThroughputTier];

    /// <summary>Output efficiency multiplier (1.0 = no waste bonus)</summary>
    public float Efficiency => Tuning.Segments.EfficiencyTiers[EfficiencyTier];

    /// <summary>Reactor charge consumed per production cycle</summary>
    public float ActivateCharge => Tuning.Segments.ActivateChargeTiers[ActivateChargeTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - base.Size.Size);
        return ((IndustryDef)base.Resize(Size)) with {
            ThroughputTier = ThroughputTier + delta,
            EfficiencyTier = EfficiencyTier + delta,
            ActivateChargeTier = ActivateChargeTier + delta,
        };
    }
}

public class IndustrySegment(ulong seed, IndustryDef industryDef, IActor? Owner) : Segment(seed, industryDef, Owner) {
    public Production.IndustryType IndustryType => industryDef.IndustryType;
    public float Throughput => industryDef.Throughput;
    public float Efficiency => industryDef.Efficiency;
    public float ActivateCharge => industryDef.ActivateCharge;

    /// <summary>Currently executing recipe, or null if idle</summary>
    public Production.ProductionRecipe? CurrentRecipe { get; set; }

    /// <summary>Progress through current production cycle (0.0 to 1.0)</summary>
    public float ProductionProgress { get; set; } = 0;

    /// <summary>Whether production is currently stalled (missing inputs, power, crew)</summary>
    public bool IsStalled { get; set; } = false;

    public override char ReportCode(Location _) => CurrentRecipe != null ? fracCode(ProductionProgress) : '-';

    public override Segment Clone() {
        var clone = new IndustrySegment(Seed, (IndustryDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        clone.CurrentRecipe = CurrentRecipe;
        clone.ProductionProgress = ProductionProgress;
        clone.IsStalled = IsStalled;
        return clone;
    }

    public new record class Data : Segment.Data {
        public string? RecipeName { get; set; }
        public float ProductionProgress { get; set; }
    }

    public override Segment.Data ToData() {
        return new Data {
            Seed = this.Seed,
            DefName = this.SegmentDef.Name,
            Hits = this.Hits,
            Packaged = this.Packaged,
            Activated = this.Activated,
            RecipeName = this.CurrentRecipe?.Name,
            ProductionProgress = this.ProductionProgress,
        };
    }

    public override void FromData(Segment.Data data) {
        base.FromData(data);
        if (data is Data industryData) {
            CurrentRecipe = Production.RecipeEx.FindByName(industryData.RecipeName ?? "");
            ProductionProgress = industryData.ProductionProgress;
        }
    }
}

// ============================================
// STORAGE SEGMENTS
// ============================================

public enum StorageType {
    General,      // Standard cargo
    Refrigerated, // For perishables (higher power drain)
    Hazardous,    // For dangerous goods (reinforced)
    Bulk,         // Higher capacity, lower value density
}

public record StorageDef(
    char Symbol,
    Tier Size,
    string Name,
    Tier WeightTier,
    Tier DrainTier,
    Tier CostTier,
    Tier MaxHitsTier,
    Tier CapacityTier,
    StorageType StorageType
) : SegmentDef(Symbol, Size, Name, SegmentKind.Storage, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override StorageSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'S';

    /// <summary>Volume capacity in cubic meters</summary>
    public float Capacity => Tuning.Segments.StorageCapacityTiers[CapacityTier];

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - base.Size.Size);
        return ((StorageDef)base.Resize(Size)) with {
            CapacityTier = CapacityTier + delta,
        };
    }
}

public class StorageSegment(ulong seed, StorageDef storageDef, IActor? Owner) : Segment(seed, storageDef, Owner) {
    public float Capacity => storageDef.Capacity;
    public StorageType StorageType => storageDef.StorageType;

    public override char ReportCode(Location _) => fracCode(UsedCapacity, Capacity);

    /// <summary>Current used volume (tracked by crawler's inventory system)</summary>
    public float UsedCapacity { get; set; } = 0;

    public float AvailableCapacity => Capacity - UsedCapacity;

    public override Segment Clone() {
        var clone = new StorageSegment(Seed, (StorageDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        clone.UsedCapacity = UsedCapacity;
        return clone;
    }

    public new record class Data : Segment.Data {
        public float UsedCapacity { get; set; }
    }

    public override Segment.Data ToData() {
        return new Data {
            Seed = this.Seed,
            DefName = this.SegmentDef.Name,
            Hits = this.Hits,
            Packaged = this.Packaged,
            Activated = this.Activated,
            UsedCapacity = this.UsedCapacity,
        };
    }

    public override void FromData(Segment.Data data) {
        base.FromData(data);
        if (data is Data storageData) {
            UsedCapacity = storageData.UsedCapacity;
        }
    }
}

// ========================
// Harvest Segments
// ========================

public record HarvestDef(
    char Symbol,
    Tier Size,
    string Name,
    Tier WeightTier,
    Tier DrainTier,
    Tier CostTier,
    Tier MaxHitsTier,
    Tier YieldTier,
    HarvestType Type
) : SegmentDef(Symbol, Size, Name, SegmentKind.Harvest, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override HarvestSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'H';

    /// <summary>Extraction yield multiplier</summary>
    public float Yield => Tuning.Segments.HarvestYieldTiers[YieldTier];

    /// <summary>Commodities this harvester can extract</summary>
    public Commodity[] ExtractableCommodities => Type switch {
        HarvestType.Mining => [Commodity.Ore, Commodity.Silicates],
        HarvestType.Biomass => [Commodity.Biomass],
        HarvestType.Crystal => [Commodity.Silicates, Commodity.Gems],
        HarvestType.Isotope => [Commodity.Isotopes],
        _ => []
    };

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - base.Size.Size);
        return ((HarvestDef)base.Resize(Size)) with {
            YieldTier = YieldTier + delta,
        };
    }
}

public class HarvestSegment(ulong seed, HarvestDef harvestDef, IActor? Owner) : Segment(seed, harvestDef, Owner) {
    public float Yield => harvestDef.Yield;
    public HarvestType HarvestType => harvestDef.Type;
    public Commodity[] ExtractableCommodities => harvestDef.ExtractableCommodities;

    public override char ReportCode(Location _) => HarvestType switch {
        HarvestType.Mining => 'M',
        HarvestType.Biomass => 'B',
        HarvestType.Crystal => 'X',
        HarvestType.Isotope => 'I',
        _ => '?'
    };

    public override Segment Clone() {
        var clone = new HarvestSegment(Seed, (HarvestDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}

// ========================
// Habitat Segments
// ========================

public record HabitatDef(
    char Symbol,
    Tier Size,
    string Name,
    Tier WeightTier,
    Tier DrainTier,    // Life support power drain
    Tier CostTier,
    Tier MaxHitsTier,
    Tier CrewCapacityTier,
    Tier MoraleBonusTier,
    HabitatType Type,
    Tier? FactorySizeTier = null
) : SegmentDef(Symbol, Size, Name, SegmentKind.Habitat, WeightTier, DrainTier, CostTier, MaxHitsTier, FactorySizeTier) {
    public override HabitatSegment NewSegment(ulong seed) => new(seed, this, null);
    public override char ClassCode => 'Q';  // 'H' is taken by Harvest, 'Q' for Quarters

    /// <summary>Maximum crew this habitat can support</summary>
    public float CrewCapacity => Tuning.Segments.CrewCapacityTiers[CrewCapacityTier];

    /// <summary>Morale bonus per hour when crew is housed</summary>
    public float MoraleBonus => Tuning.Segments.MoraleBonusTiers[MoraleBonusTier];

    /// <summary>Whether this is a Dome (size 6+)</summary>
    public bool IsDome => Type == HabitatType.Dome || Size.Size >= 6;

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - base.Size.Size);
        // FactorySizeTier is handled by base.Resize()
        return ((HabitatDef)base.Resize(Size)) with {
            CrewCapacityTier = CrewCapacityTier + delta,
            MoraleBonusTier = MoraleBonusTier + delta,
        };
    }
}

public class HabitatSegment(ulong seed, HabitatDef habitatDef, IActor? Owner) : Segment(seed, habitatDef, Owner) {
    public float CrewCapacity => habitatDef.CrewCapacity;
    public float MoraleBonus => habitatDef.MoraleBonus;
    public HabitatType HabitatType => habitatDef.Type;
    public bool IsDome => habitatDef.IsDome;

    public override char ReportCode(Location _) => HabitatType switch {
        HabitatType.Cabin => 'C',
        HabitatType.Quarters => 'Q',
        HabitatType.Barracks => 'B',
        HabitatType.Suite => 'S',
        HabitatType.Dome => 'D',
        _ => '?'
    };

    public override Segment Clone() {
        var clone = new HabitatSegment(Seed, (HabitatDef)SegmentDef, Owner);
        CopyBaseTo(clone);
        return clone;
    }
}

public static class SegmentEx {
    public static string SegmentReport(this IEnumerable<Segment> segments, Location location) {
        string result = "";

        // Group segments by kind
        var segmentsByKind = segments
            .GroupBy(s => s.SegmentKind)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in segmentsByKind) {

            switch (group.Key) {
                case SegmentKind.Power:
                    var powerTable = new Table(
                        ("Name", -24),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Length", 6),
                        ("Drain", 6),
                        ("Cost", 10),
                        ("Charge", 6),
                        ("Cap", 6),
                        ("Gen", 6)
                    );
                    foreach (var segment in group) {
                        string cap = "", gen = "", chg = "";
                        if (segment is ReactorSegment rs) {
                            chg = $"{rs.Charge:F1}";
                            cap = $"{rs.Capacity:F1}";
                            gen = $"{rs.Generation:F1}";
                        } else if (segment is ChargerSegment cs) {
                            gen = $"{cs.Generation:F1}";
                        }
                        powerTable.AddRow(
                            segment.NameSize,
                            segment.StatusLine(location),
                            $"{segment.Health}/{segment.MaxHealth}",
                            $"{segment.Weight:F1}",
                            $"{segment.Length:F1}",
                            $"{segment.Drain:F2}",
                            $"{segment.Cost:F1}",
                            chg,
                            cap,
                            gen
                        );
                    }
                    result += powerTable.ToString();
                    break;

                case SegmentKind.Traction:
                    var tractionTable = new Table(
                        ("Name", -24),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Length", 6),
                        ("Drain", 6),
                        ("Cost", 10),
                        ("Lift", 6),
                        ("Speed", 6),
                        ("Terrain", -10)
                    );
                    foreach (var segment in group.Cast<TractionSegment>()) {
                        tractionTable.AddRow(
                            segment.NameSize,
                            segment.StatusLine(location),
                            $"{segment.Health}/{segment.MaxHealth}",
                            $"{segment.Weight:F1}",
                            $"{segment.Length:F1}",
                            $"{segment.Drain:F2}",
                            $"{segment.Cost:F1}",
                            $"{segment.Lift:F1}",
                            $"{segment.Speed:F1}",
                            segment.TerrainLimit.ToString()
                        );
                    }
                    result += tractionTable.ToString();
                    break;

                case SegmentKind.Offense:
                    var offenseTable = new Table(
                        ("Name", -24),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Length", 6),
                        ("Drain", 6),
                        ("Cost", 10),
                        ("Dmg", 6),
                        ("Time s", 6),
                        ("Shots", 6),
                        ("Aim", 6),
                        ("DPM", 6),
                        ("DPM/C", 6)
                    );
                    foreach (var segment in group) {
                        string dmg = "", rate = "", shots = "", aim = "", dpm = "", dpsCost = "";
                        if (segment is WeaponSegment ws) {
                            dmg = $"{ws.Damage:F0}";
                            rate = $"{60/ws.Rate:F1}";
                            shots = $"{ws.Shots:F0}";
                            aim = $"{ws.Aim*100:F0}%";
                            float dpmValue = ws.Damage * ws.Shots * ws.Rate;
                            dpm = $"{dpmValue:F1}";
                            dpsCost = segment.Cost > 0 ? $"{dpmValue / segment.Cost:F3}" : "N/A";
                        }
                        offenseTable.AddRow(
                            segment.NameSize,
                            segment.StatusLine(location),
                            $"{segment.Health}/{segment.MaxHealth}",
                            $"{segment.Weight:F1}",
                            $"{segment.Length:F1}",
                            $"{segment.Drain:F2}",
                            $"{segment.Cost:F1}",
                            dmg,
                            rate,
                            shots,
                            aim,
                            dpm,
                            dpsCost
                        );
                    }
                    result += offenseTable.ToString();
                    break;

                case SegmentKind.Defense:
                    var defenseTable = new Table(
                        ("Name", -24),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Length", 6),
                        ("Drain", 6),
                        ("Cost", 10),
                        ("Reduction", 12),
                        ("Charge", 12)
                    );
                    foreach (var segment in group) {
                        string reduction = "";
                        string charge = "";
                        if (segment is ArmorSegment ars) {
                            reduction = $"{ars.Reduction:F1}";
                        } else if (segment is PlatingSegment ps) {
                            reduction = $"{ps.Mitigation*100:F0}%";
                        } else if (segment is ShieldSegment ss) {
                            reduction = $"{ss.ShieldLeft}";
                            charge = $"+{ss.Charge}/t";
                        }
                        defenseTable.AddRow(
                            segment.NameSize,
                            segment.StatusLine(location),
                            $"{segment.Health}/{segment.MaxHealth}",
                            $"{segment.Weight:F1}",
                            $"{segment.Length:F1}",
                            $"{segment.Drain:F2}",
                            $"{segment.Cost:F1}",
                            reduction,
                            charge
                        );
                    }
                    result += defenseTable.ToString();
                    break;

                case SegmentKind.Habitat:
                    var habitatTable = new Table(
                        ("Name", -24),
                        ("State", -12),
                        ("Health", 6),
                        ("Weight", 6),
                        ("Length", 6),
                        ("Drain", 6),
                        ("Cost", 10),
                        ("Crew Cap", 10),
                        ("Morale", 8),
                        ("Type", -10)
                    );
                    foreach (var segment in group.Cast<HabitatSegment>()) {
                        habitatTable.AddRow(
                            segment.NameSize,
                            segment.StatusLine(location),
                            $"{segment.Health}/{segment.MaxHealth}",
                            $"{segment.Weight:F1}",
                            $"{segment.Length:F1}",
                            $"{segment.Drain:F2}",
                            $"{segment.Cost:F1}",
                            $"{segment.CrewCapacity:F0}",
                            $"+{segment.MoraleBonus:F1}",
                            segment.HabitatType.ToString()
                        );
                    }
                    result += habitatTable.ToString();
                    break;
            }
        }

        return result;
    }

    // Becase leveling a segment by a level causes an increase in all its tiers,
    // Each individual tier can vary from the reference tier by one or two as long as the
    // average comes to [tier -0.5 (shitty items) to  tier + 1.5 (best in class items).
    public static List<WeaponDef> BaseWeaponDefs = [
        new GunDef('g', 1, "Guns", 1.2f, 1, 1, 1, 0f, 1.25f, (1, 1), 1.2f, 1),
        new LaserDef('l', 1, "Lasers", 0.8f, 1.2f, 1.2f, 1, 1, (1, 1), 1, 1.0f, 1),
        new MissileDef('m', 1, "Missiles", 1.0f, 0.8f, 1.1f, 1, 1.33f, 1.5f, 1, 0.8f, 1),
    ];

    public static List<WeaponDef> WeaponDefs = Variations(BaseWeaponDefs).ToList();

    public static List<OffenseDef> OffenseDefs = [
        .. WeaponDefs,
    ];

    // Power definitions (Amount = Capacity, Rate = Refill per round)
    public static List<PowerDef> PowerDefs = [
        new ReactorDef('r', 1, "Reactor", 1, 1, 2, 1, 2),
        new ReactorDef('b', 1, "Battery", 1, 1, 2, 2, 0.5f),
        new ChargerDef('c', 1, "Charger", 1, 1, 1.5f, 1.5f),
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
        new ArmorDef('a', 1, "Armor", 1.2f, 1, 1.25f, 1),
        new PlatingDef('p', 1, "Plating", 1.5f, 0.9f, 2.35f, 1),
        new ShieldDef('s', 1, "Shields", 0.5f, 2, 1.25f, 0.8f, 1, 1),
    ];

    public static IEnumerable<DefenseDef> CoreDefenseDefs => DefenseDefs;

    // Industry definitions (Throughput, Efficiency, ActivateCharge)
    public static List<IndustryDef> IndustryDefs = [
        new IndustryDef('R', 2, "Refinery", 2, 1.5f, 2, 1.5f, 1, 1, 1, Production.IndustryType.Refinery),
        new IndustryDef('F', 2, "Fabricator", 1.5f, 1.2f, 2.5f, 1.5f, 1, 1.2f, 1.5f, Production.IndustryType.Fabricator),
        new IndustryDef('A', 2, "Assembler", 1.2f, 1, 3, 1.5f, 1.2f, 1, 2, Production.IndustryType.Assembler),
        new IndustryDef('Y', 1.5f, "Recycler", 1.5f, 0.8f, 1.5f, 1, 0.8f, 0.7f, 0.5f, Production.IndustryType.Recycler),
    ];

    public static IEnumerable<IndustryDef> CoreIndustryDefs => IndustryDefs;

    // Storage definitions (Capacity, Type)
    public static List<StorageDef> StorageDefs = [
        new StorageDef('C', 1, "Cargo Hold", 1, 0, 1, 1, 1, StorageType.General),
        new StorageDef('K', 1.2f, "Cold Storage", 1, 0.5f, 1.5f, 1, 0.8f, StorageType.Refrigerated),
        new StorageDef('H', 1.5f, "Hazmat Bay", 1.5f, 0.2f, 2, 1.5f, 0.7f, StorageType.Hazardous),
        new StorageDef('B', 1.5f, "Bulk Tank", 0.8f, 0, 0.8f, 0.8f, 1.5f, StorageType.Bulk),
    ];

    public static IEnumerable<StorageDef> CoreStorageDefs => StorageDefs;

    // Harvest definitions (Yield, Type)
    public static List<HarvestDef> HarvestDefs = [
        new HarvestDef('M', 1, "Mining Drill", 1.5f, 1.5f, 1.5f, 1.2f, 1, HarvestType.Mining),
        new HarvestDef('B', 1, "Biomass Harvester", 1.2f, 1, 1.2f, 1, 1, HarvestType.Biomass),
        new HarvestDef('X', 1.5f, "Crystal Extractor", 1.3f, 1.2f, 1.8f, 1.5f, 0.8f, HarvestType.Crystal),
        new HarvestDef('I', 2, "Isotope Collector", 2, 2, 2.5f, 1.5f, 0.5f, HarvestType.Isotope),
    ];

    public static IEnumerable<HarvestDef> CoreHarvestDefs => HarvestDefs;

    // Habitat definitions (CrewCapacity, MoraleBonus, Type, FactorySize)
    // Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, CrewCapacityTier, MoraleBonusTier, Type, FactorySizeTier
    public static List<HabitatDef> HabitatDefs = [
        // Crawler-scale habitats (sizes 1-5) - FactorySize defaults to Size
        new HabitatDef('C', 1, "Crew Cabin", 1, 0.5f, 1, 1, 1, 0.8f, HabitatType.Cabin),
        new HabitatDef('Q', 1.5f, "Crew Quarters", 1.2f, 0.7f, 1.5f, 1, 1.2f, 1, HabitatType.Quarters),
        new HabitatDef('B', 1.2f, "Barracks", 1, 0.5f, 1, 1.2f, 1.5f, 0.5f, HabitatType.Barracks),
        new HabitatDef('S', 2, "VIP Suite", 1.5f, 1.5f, 2.5f, 1, 0.5f, 2, HabitatType.Suite),
        // Settlement domes (sizes 6-9) - FactorySize reduced so smaller industry can build them
        // Size 6 dome, FactorySize 4 -> needs size 6 industry
        new HabitatDef('D', 6, "Small Dome", 6, 3, 6, 5, 6, 5, HabitatType.Dome, FactorySizeTier: 4),
        // Size 7 dome, FactorySize 4.5 -> needs size 6-7 industry
        new HabitatDef('D', 7, "Medium Dome", 7, 3.5f, 7, 6, 7, 6, HabitatType.Dome, FactorySizeTier: 4.5f),
        // Size 8 dome, FactorySize 5 -> needs size 7 industry
        new HabitatDef('D', 8, "Large Dome", 8, 4, 8, 7, 8, 7, HabitatType.Dome, FactorySizeTier: 5),
        // Size 9 dome, FactorySize 5.5 -> needs size 7-8 industry
        new HabitatDef('D', 9, "Grand Dome", 9, 5, 9, 8, 9, 8, HabitatType.Dome, FactorySizeTier: 5.5f),
    ];

    public static IEnumerable<HabitatDef> CoreHabitatDefs => HabitatDefs;

    public static List<SegmentDef> AllDefs = [
        .. OffenseDefs,
        .. PowerDefs,
        .. TractionDefs,
        .. DefenseDefs,
        .. IndustryDefs,
        .. StorageDefs,
        .. HarvestDefs,
        .. HabitatDefs,
    ];

    // public static Dictionary<char, SegmentDef> Lookup = AllDefs.ToDictionary(s => s.Symbol);
    public static Dictionary<string, SegmentDef> BaseNameLookup = AllDefs.ToDictionary(s => s.Name);

    // Cache for dynamically resolved upgraded defs
    static readonly Dictionary<string, SegmentDef> _upgradeCache = new();

    /// <summary>
    /// Upgrade prefix definitions for dynamic name parsing.
    /// Order matters - longer/more specific prefixes should come first.
    /// </summary>
    static readonly (string prefix, Func<SegmentDef, int, SegmentDef?> apply)[] UpgradePrefixes = [
        // Weapon upgrades
        ("Accurate ", (def, lvl) => def is WeaponDef w ? w.Accurate(lvl) : null),
        ("Powerful ", (def, lvl) => def is WeaponDef w ? w.Powerful(lvl) : null),
        ("Sustained ", (def, lvl) => def is WeaponDef w ? w.Sustained(lvl) : null),
        ("Salvo ", (def, lvl) => def is WeaponDef w ? w.Salvo(lvl) : null),
        ("Thrifty ", (def, lvl) => def is WeaponDef w ? w.Thrifty(lvl) : null),
        // Reactor upgrades
        ("High-Capacity ", (def, lvl) => def is ReactorDef r ? r.HighCapacity(lvl) : null),
        ("Overcharged ", (def, lvl) => def is ReactorDef r ? r.Overcharged(lvl) : null),
        // Charger upgrades
        ("Boosted ", (def, lvl) => def is ChargerDef c ? c.Boosted(lvl) : null),
        // Traction upgrades
        ("Swift ", (def, lvl) => def is TractionDef t ? t.Swift(lvl) : null),
        ("Heavy-Duty ", (def, lvl) => def is TractionDef t ? t.HeavyDuty(lvl) : null),
        // Armor upgrades
        ("Reinforced ", (def, lvl) => def is ArmorDef a ? a.Reinforced(lvl) : null),
        // Plating upgrades
        ("Thick ", (def, lvl) => def is PlatingDef p ? p.Thick(lvl) : null),
        // Shield upgrades
        ("Capacious ", (def, lvl) => def is ShieldDef s ? s.Capacious(lvl) : null),
        ("Reactive ", (def, lvl) => def is ShieldDef s ? s.Reactive(lvl) : null),
        // Industry upgrades
        ("Streamlined ", (def, lvl) => def is IndustryDef i ? i.Streamlined(lvl) : null),
        ("Optimized ", (def, lvl) => def is IndustryDef i ? i.Optimized(lvl) : null),
        // Storage upgrades
        ("Spacious ", (def, lvl) => def is StorageDef st ? st.Spacious(lvl) : null),
        // Harvest upgrades
        ("Bountiful ", (def, lvl) => def is HarvestDef h ? h.Bountiful(lvl) : null),
        // Habitat upgrades
        ("Roomy ", (def, lvl) => def is HabitatDef hab ? hab.Roomy(lvl) : null),
        ("Comfortable ", (def, lvl) => def is HabitatDef hab ? hab.Comfortable(lvl) : null),
        // Universal upgrades (must try on base SegmentDef)
        ("Hardened ", (def, lvl) => def.Hardened(lvl)),
        ("Lightened ", (def, lvl) => def.Lightened(lvl)),
        ("Low-Power ", (def, lvl) => def.LowPower(lvl)),
    ];

    /// <summary>
    /// Looks up a segment definition by name, supporting dynamic upgrade parsing.
    /// For upgraded segments like "Very Accurate Powerful Gun", parses prefixes recursively.
    /// </summary>
    public static SegmentDef? LookupByName(string name) {
        // Try direct lookup first (base defs and pre-generated variants like Heavy/Rapid)
        if (BaseNameLookup.TryGetValue(name, out var def)) return def;

        // Check upgrade cache
        if (_upgradeCache.TryGetValue(name, out var cached)) return cached;

        // Parse upgrade prefixes
        var remaining = name;

        // Strip intensity prefixes and track level
        int level = 1;
        if (remaining.StartsWith("Super ")) { level = 4; remaining = remaining[6..]; }
        else if (remaining.StartsWith("Extra ")) { level = 3; remaining = remaining[6..]; }
        else if (remaining.StartsWith("Very ")) { level = 2; remaining = remaining[5..]; }

        // Try to find matching upgrade prefix
        foreach (var (prefix, apply) in UpgradePrefixes) {
            if (remaining.StartsWith(prefix)) {
                var baseName = remaining[prefix.Length..];
                var baseDef = LookupByName(baseName);  // Recursive for compound upgrades
                if (baseDef != null) {
                    var result = apply(baseDef, level);
                    if (result != null) {
                        _upgradeCache[name] = result;  // Cache for future lookups
                        return result;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Legacy accessor - now uses dynamic lookup.
    /// </summary>
    public static Dictionary<string, SegmentDef> NameLookup => BaseNameLookup;

    public static EArray<SegmentKind, SegmentDef[]> SegmentClassLookup = AllDefs
        .GroupBy(s => s.SegmentKind)
        .Select(g => (g.Key, g.ToArray()))
        .ToEArray();

    public static WeaponDef Heavy(this WeaponDef weapon, float qualityBias = 1.0f) => weapon with {
        Name = $"Heavy {weapon.Name}",
        WeightTier = weapon.WeightTier + (qualityBias / 3, 0),
        DamageTier = weapon.DamageTier + qualityBias * 2,
        RateTier = weapon.RateTier - qualityBias / 2,
        DrainTier = weapon.DrainTier + (qualityBias / 3, 0),
        CostTier = weapon.CostTier + qualityBias, // this should be == net quality
    };

    public static WeaponDef Rapid(this WeaponDef weapon, float qualityBias = 1.0f) => weapon with {
        Name = $"Rapid {weapon.Name}",
        WeightTier = weapon.WeightTier + (qualityBias / 3, 0),
        DamageTier = weapon.DamageTier - qualityBias / 2,
        RateTier = weapon.RateTier + qualityBias * 2,
        DrainTier = weapon.DrainTier + (qualityBias / 3, 0),
        CostTier = weapon.CostTier + qualityBias,
    };

    public static IEnumerable<WeaponDef> Variations(IEnumerable<WeaponDef> weapons) {
        foreach (var weapon in weapons) {
            yield return weapon;
            yield return weapon.Heavy();
            yield return weapon.Rapid();
            yield return weapon.Heavy().Rapid();
        }
    }

    // ============================================
    // UPGRADE EXTENSION METHODS
    // ============================================

    /// <summary>
    /// Returns intensity prefix for upgrade levels.
    /// Level 1 = no prefix, Level 2 = "Very ", Level 3 = "Extra ", Level 4+ = "Super ".
    /// </summary>
    static string IntensityPrefix(int level) => level switch {
        <= 1 => "",
        2 => "Very ",
        3 => "Extra ",
        _ => "Super "
    };

    // --- Weapon Upgrades ---
    // Individual stat upgrades are ~80% efficient compared to Heavy/Rapid combos

    /// <summary>Improves weapon accuracy. Less efficient than combo variants.</summary>
    public static WeaponDef Accurate(this WeaponDef weapon, int level = 1) => weapon with {
        Name = $"{IntensityPrefix(level)}Accurate {weapon.Name}",
        AimTier = weapon.AimTier + level * 0.8f,
        CostTier = weapon.CostTier + level * 0.2f,
    };

    /// <summary>Improves weapon damage. Less efficient than Heavy combo.</summary>
    public static WeaponDef Powerful(this WeaponDef weapon, int level = 1) => weapon with {
        Name = $"{IntensityPrefix(level)}Powerful {weapon.Name}",
        DamageTier = weapon.DamageTier + level * 1.5f,
        CostTier = weapon.CostTier + level * 0.3f,
    };

    /// <summary>Improves weapon fire rate. Less efficient than Rapid combo.</summary>
    public static WeaponDef Sustained(this WeaponDef weapon, int level = 1) => weapon with {
        Name = $"{IntensityPrefix(level)}Sustained {weapon.Name}",
        RateTier = weapon.RateTier + level * 1.5f,
        CostTier = weapon.CostTier + level * 0.3f,
    };

    /// <summary>Improves shots per cycle.</summary>
    public static WeaponDef Salvo(this WeaponDef weapon, int level = 1) => weapon with {
        Name = $"{IntensityPrefix(level)}Salvo {weapon.Name}",
        ShotsTier = weapon.ShotsTier + level * 0.8f,
        CostTier = weapon.CostTier + level * 0.25f,
    };

    /// <summary>Reduces ammunition consumption.</summary>
    public static WeaponDef Thrifty(this WeaponDef weapon, int level = 1) => weapon with {
        Name = $"{IntensityPrefix(level)}Thrifty {weapon.Name}",
        AmmoUseTier = weapon.AmmoUseTier - level * 0.5f,
        CostTier = weapon.CostTier + level * 0.2f,
    };

    // --- Reactor Upgrades ---

    /// <summary>Improves reactor energy storage capacity.</summary>
    public static ReactorDef HighCapacity(this ReactorDef reactor, int level = 1) => reactor with {
        Name = $"{IntensityPrefix(level)}High-Capacity {reactor.Name}",
        CapacityTier = reactor.CapacityTier + level * 0.8f,
        CostTier = reactor.CostTier + level * 0.25f,
    };

    /// <summary>Improves reactor power generation rate.</summary>
    public static ReactorDef Overcharged(this ReactorDef reactor, int level = 1) => reactor with {
        Name = $"{IntensityPrefix(level)}Overcharged {reactor.Name}",
        ChargerTier = reactor.ChargerTier + level * 0.8f,
        CostTier = reactor.CostTier + level * 0.25f,
    };

    // --- Charger Upgrades ---

    /// <summary>Improves charger power generation rate.</summary>
    public static ChargerDef Boosted(this ChargerDef charger, int level = 1) => charger with {
        Name = $"{IntensityPrefix(level)}Boosted {charger.Name}",
        ChargeTier = charger.ChargeTier + level * 0.8f,
        CostTier = charger.CostTier + level * 0.25f,
    };

    // --- Traction Upgrades ---

    /// <summary>Improves movement speed.</summary>
    public static TractionDef Swift(this TractionDef traction, int level = 1) => traction with {
        Name = $"{IntensityPrefix(level)}Swift {traction.Name}",
        SpeedTier = traction.SpeedTier + level * 0.8f,
        CostTier = traction.CostTier + level * 0.25f,
    };

    /// <summary>Improves cargo lift capacity.</summary>
    public static TractionDef HeavyDuty(this TractionDef traction, int level = 1) => traction with {
        Name = $"{IntensityPrefix(level)}Heavy-Duty {traction.Name}",
        LiftTier = traction.LiftTier + level * 0.8f,
        CostTier = traction.CostTier + level * 0.25f,
    };

    // --- Armor Upgrades ---

    /// <summary>Improves armor damage reduction.</summary>
    public static ArmorDef Reinforced(this ArmorDef armor, int level = 1) => armor with {
        Name = $"{IntensityPrefix(level)}Reinforced {armor.Name}",
        ReductionTier = armor.ReductionTier + level * 0.8f,
        CostTier = armor.CostTier + level * 0.25f,
    };

    // --- Plating Upgrades ---

    /// <summary>Improves plating mitigation percentage.</summary>
    public static PlatingDef Thick(this PlatingDef plating, int level = 1) => plating with {
        Name = $"{IntensityPrefix(level)}Thick {plating.Name}",
        MitigationTier = plating.MitigationTier + level * 0.8f,
        CostTier = plating.CostTier + level * 0.25f,
    };

    // --- Shield Upgrades ---

    /// <summary>Improves shield capacity.</summary>
    public static ShieldDef Capacious(this ShieldDef shield, int level = 1) => shield with {
        Name = $"{IntensityPrefix(level)}Capacious {shield.Name}",
        CapacityTier = shield.CapacityTier + level * 0.8f,
        CostTier = shield.CostTier + level * 0.25f,
    };

    /// <summary>Improves shield recharge rate.</summary>
    public static ShieldDef Reactive(this ShieldDef shield, int level = 1) => shield with {
        Name = $"{IntensityPrefix(level)}Reactive {shield.Name}",
        ChargeTier = shield.ChargeTier + level * 0.8f,
        CostTier = shield.CostTier + level * 0.25f,
    };

    // --- Industry Upgrades ---

    /// <summary>Improves production throughput/speed.</summary>
    public static IndustryDef Streamlined(this IndustryDef industry, int level = 1) => industry with {
        Name = $"{IntensityPrefix(level)}Streamlined {industry.Name}",
        ThroughputTier = industry.ThroughputTier + level * 0.8f,
        CostTier = industry.CostTier + level * 0.25f,
    };

    /// <summary>Improves production output efficiency.</summary>
    public static IndustryDef Optimized(this IndustryDef industry, int level = 1) => industry with {
        Name = $"{IntensityPrefix(level)}Optimized {industry.Name}",
        EfficiencyTier = industry.EfficiencyTier + level * 0.8f,
        CostTier = industry.CostTier + level * 0.25f,
    };

    // --- Storage Upgrades ---

    /// <summary>Improves storage capacity.</summary>
    public static StorageDef Spacious(this StorageDef storage, int level = 1) => storage with {
        Name = $"{IntensityPrefix(level)}Spacious {storage.Name}",
        CapacityTier = storage.CapacityTier + level * 0.8f,
        CostTier = storage.CostTier + level * 0.25f,
    };

    // --- Harvest Upgrades ---

    /// <summary>Improves extraction yield.</summary>
    public static HarvestDef Bountiful(this HarvestDef harvest, int level = 1) => harvest with {
        Name = $"{IntensityPrefix(level)}Bountiful {harvest.Name}",
        YieldTier = harvest.YieldTier + level * 0.8f,
        CostTier = harvest.CostTier + level * 0.25f,
    };

    // --- Habitat Upgrades ---

    /// <summary>Improves crew capacity.</summary>
    public static HabitatDef Roomy(this HabitatDef habitat, int level = 1) => habitat with {
        Name = $"{IntensityPrefix(level)}Roomy {habitat.Name}",
        CrewCapacityTier = habitat.CrewCapacityTier + level * 0.8f,
        CostTier = habitat.CostTier + level * 0.25f,
    };

    /// <summary>Improves morale bonus.</summary>
    public static HabitatDef Comfortable(this HabitatDef habitat, int level = 1) => habitat with {
        Name = $"{IntensityPrefix(level)}Comfortable {habitat.Name}",
        MoraleBonusTier = habitat.MoraleBonusTier + level * 0.8f,
        CostTier = habitat.CostTier + level * 0.25f,
    };

    // --- Universal Upgrades (work on any segment) ---

    /// <summary>Improves segment durability (max hits).</summary>
    public static SegmentDef Hardened(this SegmentDef def, int level = 1) => def with {
        Name = $"{IntensityPrefix(level)}Hardened {def.Name}",
        MaxHitsTier = def.MaxHitsTier + level * 0.8f,
        CostTier = def.CostTier + level * 0.2f,
    };

    /// <summary>Reduces segment weight.</summary>
    public static SegmentDef Lightened(this SegmentDef def, int level = 1) => def with {
        Name = $"{IntensityPrefix(level)}Lightened {def.Name}",
        WeightTier = def.WeightTier - (level * 0.5f, 0),
        CostTier = def.CostTier + level * 0.3f,
    };

    /// <summary>Reduces segment power drain.</summary>
    public static SegmentDef LowPower(this SegmentDef def, int level = 1) => def with {
        Name = $"{IntensityPrefix(level)}Low-Power {def.Name}",
        DrainTier = def.DrainTier - (level * 0.5f, 0),
        CostTier = def.CostTier + level * 0.3f,
    };
}
