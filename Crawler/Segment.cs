namespace Crawler;

public enum SegmentKind {
    Power,
    Traction,
    Offense,
    Defense,
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
    Tier MaxHitsTier) {
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
        return this with {
            Size = this.Size + delta,
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
    public int MaxHits => SegmentDef.MaxHits;
    public char ClassCode => SegmentDef.ClassCode;
    public int Cycle { get; set; } = 0;
    public XorShift Rng = new(seed);
    public virtual string Report => $"{Name} {GetStyle()}";

    long lastTick = Game.SafeTime;
    public void TickUntil(long time) => Tick(( int ) (time - lastTick));
    public virtual void Tick(int elapsed) {
        lastTick += elapsed;
        Cycle = Math.Max(0, Cycle - elapsed);
    }

    int _hits = 0;
    public int Hits {
        get => _hits;
        set => _hits = Math.Clamp(value, 0, SegmentDef.MaxHits);
    }

    public bool Packaged { get; set; } = false;
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
        Hits >= MaxHits ? Working.Destroyed :
        Packaged ? Working.Packaged :
        !Activated ? Working.Deactivated :
        Hits > MaxHits / 2 ? Working.Damaged :
        Hits > 0 ? Working.Running :
        Working.Pristine;
    // Active segments are usable until they take half damage, rounded down
    public bool IsActive => State is Working.Pristine or Working.Running;
    public bool IsActiveCycle => IsActive && Cycle == 0;
    public bool IsCycling => IsActive && Cycle > 0;
    public void CycleStart() => Cycle = CycleLength;
    public virtual int CycleLength => 0;
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

    public int Health => MaxHits - Hits;

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
public record WeaponDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : OffenseDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier) {
    public override WeaponSegment NewSegment(ulong seed) => new(seed, this, null);

    public float Damage => (float)Math.Round(Tuning.Segments.DamageTiers[DamageTier]);
    public float Rate => Tuning.Segments.RateTiers[RateTier];
    public float Shots => (float)Math.Round(Tuning.Segments.ShotsTiers[ShotsTier]);
    public float Aim => Tuning.Segments.AimTiers[AimTier];
    public int CycleLength => (int)(60 / Rate);

    public override SegmentDef Resize(int Size) {
        Tier delta = new(Size - (( SegmentDef ) this).Size.Size);
        return (( WeaponDef ) base.Resize(Size)) with {
            DamageTier = DamageTier + delta,
            RateTier = RateTier + delta,
            ShotsTier = ShotsTier + delta,
            AimTier = AimTier + delta,
        };
    }
}
public class WeaponSegment(ulong seed, WeaponDef weaponDef, IActor? Owner): OffenseSegment(seed, weaponDef, Owner) {
    public float Damage => weaponDef.Damage;
    public float Rate => weaponDef.Rate;
    public float Shots => weaponDef.Shots;
    public float Aim => weaponDef.Aim;
    public override int CycleLength => weaponDef.CycleLength;
    public virtual IEnumerable<HitRecord> GenerateFire(ulong seed, float aim) {
        if (IsActiveCycle) {
            var rng = new XorShift(seed);
            for (int i = 0; i < Shots; i++) {
                yield return new(rng.Seed(), this, Damage, Aim + aim);
            }
            CycleStart();
        }
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
public record GunDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
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
public record LaserDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
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
public record MissileDef(char Symbol, Tier Size, string Name, Tier WeightTier, Tier DrainTier, Tier CostTier, Tier MaxHitsTier, Tier DamageTier, Tier RateTier, Tier ShotsTier, Tier AimTier)
    : WeaponDef(Symbol, Size, Name, WeightTier, DrainTier, CostTier, MaxHitsTier, DamageTier, RateTier, ShotsTier, AimTier) {
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

    public override void Tick(int elapsed) {
        base.Tick(elapsed);
        if (IsActive) {
            ShieldLeft += Charge * elapsed / 3600;
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
                            $"{segment.Health}/{segment.MaxHits}",
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
                            $"{segment.Health}/{segment.MaxHits}",
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
                            $"{segment.Health}/{segment.MaxHits}",
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
                            $"{segment.Health}/{segment.MaxHits}",
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
            }
        }

        return result;
    }

    // Becase leveling a segment by a level causes an increase in all its tiers,
    // Each individual tier can vary from the reference tier by one or two as long as the
    // average comes to [tier -0.5 (shitty items) to  tier + 1.5 (best in class items).
    public static List<WeaponDef> BaseWeaponDefs = [
        new GunDef('g', 1, "Guns", 1.2f, 1, 1, 1, 0f, 1.25f, (1, 1), 1.2f),
        new LaserDef('l', 1, "Lasers", 0.8f, 1.2f, 1.2f, 1, 1, (1, 1), 1, 1.0f),
        new MissileDef('m', 1, "Missiles", 1.0f, 0.8f, 1.1f, 1, 1.33f, 1.5f, 1, 0.8f),
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
}
