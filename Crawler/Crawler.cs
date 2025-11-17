using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Crawler.Logging;

namespace Crawler;

public class ActorToActor {
    [Flags]
    public enum EFlags {
        Hostile = 1 << 0,
        Surrendered = 1 << 1,
        Spared = 1 << 2,
        Betrayed = 1 << 3,
        Betrayer = 1 << 4,
    }
    public EFlags Flags;

    public bool WasHostile => DamageCreated > 0;
    public bool WasDamaged => DamageTaken > 0; // Track if we've taken any damage from them

    // Returns true the first time that value is true for the given flag,
    // then sets the flag and returns false on subsequent calls.
    public bool Latch(EFlags flag, bool value = true) {
        if (!HasFlag(flag)) {
            SetFlag(flag, value);
            return value;
        }
        return false;
    }
    public bool HasFlag(EFlags flag) => Flags.HasFlag(flag);
    public bool SetFlag(EFlags flag, bool value = true) {
        if (HasFlag(flag) != value) {
            Flags.SetFlag(flag, value);
            DirtyInteractions = true;
        }
        return value;
    }
    public bool Hostile {
        get => HasFlag(EFlags.Hostile);
        set => SetFlag(EFlags.Hostile, value);
    }
    public bool Surrendered {
        get => HasFlag(EFlags.Surrendered);
        set => SetFlag(EFlags.Surrendered, value);
    }
    public bool Spared {
        get => HasFlag(EFlags.Spared);
        set => SetFlag(EFlags.Spared, value);
    }
    public int DamageCreated = 0;
    public int DamageInflicted = 0;
    public int DamageTaken = 0;

    public List<IProposal> StoredProposals { get; } = new();

    /// <summary>Add a proposal to the persistent list</summary>
    public void AddProposal(IProposal proposal) {
        StoredProposals.Add(proposal);
        DirtyInteractions = true;
    }

    /// <summary>Remove a specific proposal from the persistent list</summary>
    public void RemoveProposal(IProposal proposal) {
        StoredProposals.Remove(proposal);
        DirtyInteractions = true;
    }

    public bool DirtyInteractions { get; set; } = true;
    public override string ToString() {
        var Adjs = new List<string>();
        if (Hostile) {
            Adjs.Add("Hostile");
        }
        if (Surrendered) {
            Adjs.Add("Surrendered");
        }
        if (Spared) {
            Adjs.Add("Spared");
        }
        var result = "";
        if (Adjs.Any()) {
            result += $" (" + string.Join(", ", Adjs) + ")";
        }
        return result;
    }
}

// For this faction, dealing in Controlled commodities requires a license.
// Early game commodities such as liquor or explosives require a category
// license for GameTier.EarlyGame (or higher), while late game commodities like trips and
// gems require a GameTier.LateGame license.
public class ActorFaction {
    public ActorFaction(IActor actor, Faction faction) {
        if (actor.Faction == faction) {
            if (actor.Faction is Faction.Bandit) {
                ActorStanding = 25;
            } else {
                ActorStanding = 100;
            }
            FactionStanding = ActorStanding;
        } else {
            ActorStanding = FactionStanding = 10;
            if (actor.Faction is Faction.Bandit || faction is Faction.Bandit) {
                ActorStanding = -100;
                FactionStanding = -100;
            }
        }
    }

    public EArray<CommodityCategory, GameTier> Licenses = new();
    public bool CanTrade(Commodity c) => Licenses[c.Category()] >= c.Tier();
    public bool CanTrade(SegmentDef segdef) =>
        segdef.SegmentKind == SegmentKind.Offense ? Licenses[CommodityCategory.Dangerous] >= weaponTier(segdef) : true;
    public int ActorStanding { get; set; } // How the actor feels about the faction
    public int FactionStanding { get; set; } // How the faction feels about the actor
    GameTier weaponTier(SegmentDef segdef) => (GameTier)Math.Clamp((int)Math.Round(segdef.Size.Size * 0.667), 0, 3);
}
public class Crawler: IActor {
    XorShift Rng;
    GaussianSampler Gaussian;

    public static Crawler NewRandom(ulong seed, Faction faction, Location here, int crew, float supplyDays, float goodsWealth, float segmentWealth, EArray<SegmentKind, float> segmentClassWeights) {
        var rng = new XorShift(seed);
        var crawlerSeed = rng.Seed();
        var invSeed = rng.Seed();
        var newInv = new Inventory();
        newInv.AddRandomInventory(invSeed, here, crew, supplyDays, goodsWealth, segmentWealth, true, segmentClassWeights, faction);
        var crawler = new Crawler(crawlerSeed, faction, here, newInv);

        return crawler;
    }
    public Crawler(ulong seed, Faction faction, Location location, Inventory inventory) {
        Rng = new XorShift(seed);
        Gaussian = new GaussianSampler(Rng.Seed());
        Faction = faction;
        Supplies = inventory;
        Supplies.Overdraft = Cargo;
        Location = location;
        Name = Names.HumanName(Rng.Seed());
        if (faction == Faction.Bandit) {
            Markup = Tuning.Trade.BanditMarkup(Gaussian);
            Spread = Tuning.Trade.BanditSpread(Gaussian);
        } else {
            Markup = Tuning.Trade.TradeMarkup(Gaussian);
            Spread = Tuning.Trade.TradeSpread(Gaussian);
        }
        UpdateSegmentCache();
    }
    public string Name { get; set; }
    public string Brief(IActor viewer) {
        var C = CrawlerDisplay(viewer).Split('\n').ToList();

        C[1] += $" Power: {TotalCharge:F1} + {TotalGeneration:F1}/t  Drain: Off:{OffenseDrain:F1}  Def:{DefenseDrain:F1}  Move:{MovementDrain:F1}";
        C[2] += $" Weight: {Mass:F0} / {Lift:F0}T, {Speed:F0}km/h";
        if (this == viewer) {
            float RationsPerDay = TotalPeople * Tuning.Crawler.RationsPerCrewDay;
            float WaterPerDay = WaterRecyclingLossPerHr * 24;
            float AirPerDay = AirLeakagePerHr * 24;
            C[3] += $" Cash: {ScrapInv:F1}¢¢  Fuel: {FuelInv:F1}, -{FuelPerHr:F1}/h, -{FuelPerKm * 100:F2}/100km";
            C[4] += $" Crew: {CrewInv:F0}  Soldiers: {SoldiersInv:F0}  Passengers: {PassengersInv:F0}  Morale: {MoraleInv}";
            C[5] += $" Rations: {RationsInv:F1} ({RationsPerDay:F1}/d)  Water: {WaterInv:F1} ({WaterPerDay:F1}/d)  Air: {AirInv:F1} ({AirPerDay:F1}/d)";
        } else {
            C = C.Take(3).ToList();
        }
        return string.Join("\n", C);
    }
    public EActorFlags Flags { get; set; } = EActorFlags.Mobile;
    public Location Location { get; set; }

    static Crawler() {
    }
    // Hourly fuel, wages, and rations are the primary timers.
    //
    public float FuelPerHr => StandbyDrain / FuelEfficiency;
    public float WagesPerHr => CrewInv * Tuning.Crawler.WagesPerCrewDay / 24;
    public float RationsPerHr => TotalPeople * Tuning.Crawler.RationsPerCrewDay / 24;

    public float FuelPerKm => Tuning.Crawler.FuelPerKm * MovementDrain / FuelEfficiency;

    // Water recycling loss goes up as the number of people increases
    public float WaterRequirement => CrewInv * Tuning.Crawler.WaterPerCrew +
                                     SoldiersInv * Tuning.Crawler.WaterPerSoldier +
                                     PassengersInv * Tuning.Crawler.WaterPerPassenger;
    public float WaterRecyclingLossPerHr => WaterRequirement * Tuning.Crawler.WaterRecyclingLossPerHour;
    // Air leakage increases with crawler damage
    public float AirLeakagePerHr {
        get {
            float hitSegments = UndestroyedSegments
                .Sum(s => s.Hits / (float)s.MaxHits);
            return TotalPeople * Tuning.Crawler.AirLeakagePerDamagedSegment * hitSegments;
        }
    }
    public float TotalPeople => CrewInv + SoldiersInv + PassengersInv;
    // Returns negative values if not reachable
    public (float Fuel, float Time) FuelTimeTo(Location location) {
        float dist = Location.Distance(location);
        float startSpeed = Speed;
        float endSpeed = SpeedOn(location.Terrain);
        float terrainRate = Math.Min(startSpeed, endSpeed);
        if (terrainRate <= 0) {
            return (-1, -1);
        }
        float time = dist / terrainRate;
        float fuel = FuelPerKm * dist + FuelPerHr * time;
        return (fuel, time);
    }

    public Faction Faction { get; set; }
    public int EvilPoints { get; set; } = 0;
    public List<IProposal> StoredProposals { get; private set; } = new();
    public virtual IEnumerable<IProposal> Proposals() => StoredProposals;

    // Scan actor's inventory for contraband based on this faction's policies
    public bool HasContraband(IActor target) {
        if (target is not Crawler crawler) {
            return false;
        }
        return !ScanForContraband(crawler).IsEmpty;
    }
    public Inventory ScanForContraband(IActor target) {
        var contraband = new Inventory();

        // Random chance to detect
        //if (Rng.NextSingle() > Tuning.Civilian.contrabandScanChance) {
        //    return contraband; // Scan failed
        //}

        if (target is Crawler subject) {
            var targetToFaction = subject.To(this.Faction);
            // Check each commodity
            foreach (var commodityAmount in subject.Supplies.Pairs) {
                var (commodity, amount) = commodityAmount;
                amount += subject.Cargo[commodity];
                var policy = Tuning.FactionPolicies.GetPolicy(Faction, commodity);
                var licensed = targetToFaction.CanTrade(commodity);
                if (!licensed && amount > 0) {
                    contraband.Add(commodity, amount);
                }
            }
            foreach (var segment in subject.Cargo.Segments) {
                var policy = Tuning.FactionPolicies.GetPolicy(Faction, segment.SegmentKind);
                var licensed = targetToFaction.CanTrade(segment.SegmentDef);
                if (!licensed) {
                    contraband.Add(segment);
                }
            }
            foreach (var segment in subject.Supplies.Segments.Where(s => s.IsPackaged)) {
                var policy = Tuning.FactionPolicies.GetPolicy(Faction, segment.SegmentKind);
                var licensed = targetToFaction.CanTrade(segment.SegmentDef);
                if (!licensed) {
                    contraband.Add(segment);
                }
            }
        }
        return contraband;
    }

    public bool Pinned() {
        return Rng.NextSingle() > this.EscapeChance();
    }

    internal long LastEvent = 0;
    public void TickThink(long time) {
        int elapsed = (int)(time - LastEvent);
        Tick(time);
        Think(elapsed, Location.GetEncounter().ActorsExcept(this));
    }
    public void Tick(long time) {
        int elapsed = (int)(time - LastEvent);
        LastEvent = time;

        if (EndState != null || elapsed == 0) {
            return;
        }

        using var activity = LogCat.Game.StartActivity(
                "Crawler.Tick", System.Diagnostics.ActivityKind.Internal)?
            .SetTag("crawler.name", Name)
            .SetTag("crawler.faction", Faction)
            .SetTag("elapsed_seconds", elapsed);

        Recharge(elapsed);
        Decay(elapsed);

        UpdateSegmentCache();
        // post-update
        TestEnded();
    }
    public void Travel(Location loc) {
        var (fuel, time) = FuelTimeTo(loc);
        if (fuel < 0) {
            Message("Not enough fuel.");
            return;
        }
        long arrivalTime = LastEvent + (int)(time * 3600);

        Location.GetEncounter().RemoveActor(this);
        FuelInv -= fuel;
        Location = loc;
        loc.GetEncounter().AddActorAt(this, arrivalTime);
    }
    public void Think(int elapsed, IEnumerable<IActor> Actors) {
        // using var activity = LogCat.Game.StartActivity($"{Name} Tick Against {Actors.Count()} others");

        if (IsDepowered) {
            Message($"{Name} has no power.");
            return;
        }

        // Flee if vulnerable and not pinned
        if (IsVulnerable && !Pinned()) {
            Message($"{Name} flees the encounter.");
            Location.GetEncounter().RemoveActor(this);
            return;
        }

        var hostile = Rng.ChooseRandom(Actors.Where(a => To(a).Hostile));
        if (hostile != null) {
            int AP = this.Attack(hostile);
        }
    }

    public void Message(string message) {
        // TODO: Message history for other actors
        if (this == Game.Instance?.Player) {
            CrawlerEx.Message(Game.Instance!.TimeString() + ": " + message);
        }
    }
    public long NextEvent { get; set; } = 0;
    public int WeaponDelay() {
        int minDelay = Tuning.MaxDelay;
        int N = 0;
        foreach (var segment in CyclingSegments) {
            minDelay = Math.Min(minDelay, segment.Cycle);
            ++N;
        }
        return minDelay;
    }

    Inventory _inventory = new Inventory();
    public Inventory Supplies {
        get => _inventory;
        set {
            _inventory = value;
            UpdateSegmentCache();
        }
    }
    public Inventory Cargo { get; } = new Inventory();
    public void UpdateSegmentCache() {
        _allSegments = Supplies.Segments.OrderBy(s => (s.ClassCode, s.Cost)).ToList();

        _activeSegments.Clear();
        _disabledSegments.Clear();
        _destroyedSegments.Clear();
        _segmentsByClass.Initialize(() => new List<Segment>());
        _activeSegmentsByClass.Initialize(() => new List<Segment>());

        foreach (var segment in _allSegments) {
            _segmentsByClass[segment.SegmentKind].Add(segment);
            switch (segment.State) {
            case Segment.Working.Pristine:
            case Segment.Working.Running:
                _activeSegmentsByClass[segment.SegmentKind].Add(segment);
                _activeSegments.Add(segment);
                _undestroyedSegments.Add(segment);
                break;
            case Segment.Working.Damaged:
                _disabledSegments.Add(segment);
                _undestroyedSegments.Add(segment);
                break;
            case Segment.Working.Destroyed:
                _destroyedSegments.Add(segment);
                break;
            case Segment.Working.Deactivated:
                _undestroyedSegments.Add(segment);
                break;
            case Segment.Working.Packaged:
                _undestroyedSegments.Add(segment);
                break;
            }
        }
    }
    List<Segment> _allSegments = [];
    List<Segment> _activeSegments = [];
    List<Segment> _disabledSegments = [];
    List<Segment> _destroyedSegments = [];
    List<Segment> _undestroyedSegments = [];
    EArray<SegmentKind, List<Segment>> _segmentsByClass = new();
    EArray<SegmentKind, List<Segment>> _activeSegmentsByClass = new();
    public List<Segment> SegmentsFor(SegmentKind segmentKind) => _segmentsByClass[segmentKind];
    public List<Segment> ActiveSegmentsFor(SegmentKind segmentKind) => _activeSegmentsByClass[segmentKind];
    public List<Segment> Segments => _allSegments;
    public List<Segment> ActiveSegments => _activeSegments;
    public IEnumerable<Segment> ActiveCycleSegments => _activeSegments.Where(s => s.IsActiveCycle);
    public IEnumerable<Segment> CyclingSegments => _activeSegments.Where(s => s.IsCycling);

    public List<Segment> DisabledSegments => _disabledSegments;
    public List<Segment> DestroyedSegments => _destroyedSegments;
    public List<Segment> UndestroyedSegments => _undestroyedSegments;
    public IEnumerable<TractionSegment> TractionSegments => _activeSegmentsByClass[SegmentKind.Traction].Cast<TractionSegment>();
    public IEnumerable<OffenseSegment> OffenseSegments => _activeSegmentsByClass[SegmentKind.Offense].Cast<OffenseSegment>();
    public IEnumerable<OffenseSegment> WeaponSegments => _activeSegmentsByClass[SegmentKind.Offense].OfType<WeaponSegment>();
    public IEnumerable<DefenseSegment> DefenseSegments => _activeSegmentsByClass[SegmentKind.Defense].Cast<DefenseSegment>();
    public IEnumerable<DefenseSegment> CoreDefenseSegments => _activeSegmentsByClass[SegmentKind.Defense].Cast<DefenseSegment>();
    public IEnumerable<PowerSegment> PowerSegments => _activeSegmentsByClass[SegmentKind.Power].Cast<PowerSegment>();

    public float Speed => SpeedOn(Location.Terrain);
    public float Lift => LiftOn(Location.Terrain);
    public float MovementDrain => MovementDrainOn(Location.Terrain);
    public float OffenseDrain => OffenseSegments.Sum(s => s.Drain);
    public float DefenseDrain => DefenseSegments.Sum(s => s.Drain);
    public float TotalDrain => ActiveSegments.Sum(s => s.Drain);
    public float StandbyDrain => TotalDrain * Tuning.Crawler.StandbyFraction;

    public float SpeedOn(TerrainType t) {
        return EvaluateMove(t).Speed;
    }
    public float LiftOn(TerrainType t) => TractionSegments.Sum(ts => ts.LiftOn(t));
    public float MovementDrainOn(TerrainType t) {
        return EvaluateMove(t).Drain;
    }
    public record struct Move(float Speed, float Drain, string? Error);
    public Move EvaluateMove(TerrainType t) {
        float bestSpeed = 0;
        float drain = 0;
        string bestMsg = "";
        float generation = TotalGeneration;
        float weight = Mass;
        var segments = TractionSegments.ToArray();
        if (segments.Length == 0) {
            return new Move(0, 0, "No traction");
        }
        for (TerrainType i = TerrainType.Flat; i <= t; ++i) {
            float speed = segments.Sum(ts => ts.SpeedOn(i));
            float drainSum = segments.Sum(ts => ts.DrainOn(i));
            float generationLimit = Math.Min(1, generation / drainSum);
            float totalLift = segments.Sum(ts => ts.LiftOn(i));
            float liftFraction = weight / totalLift;
            List<string> notes = new();
            if (generationLimit < 1) {
                notes.Add("low gen");
            }
            speed *= generationLimit;
            if (totalLift > 0 && liftFraction > 1) {
                liftFraction = (float)Math.Pow(liftFraction, 0.6f);
                notes.Add("too heavy");
                drain *= liftFraction;
                speed /= liftFraction;
            }
            if (speed > bestSpeed) {
                bestSpeed = speed;
                drain = drainSum;
                bestMsg = notes.Any() ? "(" + string.Join(", ", notes) + ")" : "";
            }
        }
        return new Move(bestSpeed, drain, bestMsg == "" ? null : bestMsg);
    }

    public float Mass => Supplies.Mass + Cargo.Mass;
    public float TotalCharge => PowerSegments.Any()
        ? PowerSegments.Select(s => s switch {
                ReactorSegment rs => rs.Charge,
                _ => 0,
            })
            .Sum()
        : 0;
    public float TotalGeneration =>
        PowerSegments.OfType<ReactorSegment>().Sum(s => s.Generation) +
        PowerSegments.OfType<ChargerSegment>().Sum(s => s.Generation);

    // TODO: Replace with a power scaling
    public float FuelEfficiency => 0.4f;
    string InventoryReport(Inventory inv) {
        var commodityTable = new Table(
            ("Commodity", -16),
            ("Location", 8),
            ("Amount", 10),
            ("Mass", 8),
            ("Volume", 8),
            ("Local Value", 12)
        );

        foreach (var (commodity, amount) in inv.Commodities.Select((amt, idx) => ((Commodity)idx, amt)).Where(pair => pair.amt > 0)) {
            var value = amount * commodity.CostAt(Location);
            commodityTable.AddRow(
                commodity.ToString(),
                "Supplies",
                commodity.IsIntegral() ? $"{amount:F0}" : $"{amount:F1}",
                $"{commodity.Mass():F3}",
                $"{commodity.Volume():F3}",
                $"{value:F1}¢¢"
            );
        }
        return commodityTable.ToString();
    }
    public string Report() {
        // Header with crawler name and location
        string result = Style.Name.Format($"[{Name}]");
        result += $" Evil: {EvilPoints}\n";

        result += "\nCrawler Segments:\n" + Supplies.Segments.SegmentReport(Location);
        result += "Stored Segments:\n" + Cargo.Segments.SegmentReport(Location);
        result += "Inventory:\n" + InventoryReport(Supplies);
        result += "Cargo:\n" + InventoryReport(Cargo);

        return result;
    }
    public string StateString() {
        var Adjs = new List<string>();
        if (!string.IsNullOrWhiteSpace(EndMessage)) {
            Adjs.Add(Style.SegmentDestroyed.Format(EndMessage));
        }
        if (IsDepowered) {
            Adjs.Add("Depowered");
        }
        if (IsImmobile && (Flags & EActorFlags.Mobile) != 0) {
            Adjs.Add("Immobile");
        }
        if (IsDisarmed) {
            Adjs.Add("Disarmed");
        }
        if (IsDefenseless) {
            Adjs.Add("Defenseless");
        }
        if (Adjs.Any()) {
            return " (" + string.Join(", ", Adjs) + ")";
        } else {
            return "";
        }
    }
    public string StateString(IActor viewer) {
        var Adjs = new List<string>();
        var relation = To(viewer);
        if (relation.Hostile) {
            Adjs.Add("Hostile");
        }
        if (relation.Surrendered) {
            Adjs.Add("Surrendered");
        }
        if (relation.Spared) {
            Adjs.Add("Spared");
        }
        var result = StateString();
        if (Adjs.Any()) {
            result += $" (" + string.Join(", ", Adjs) + ")";
        }
        return result;
    }
    public string CrawlerDisplay(IActor viewer) {
        IEnumerable<StyledString> coloredSegments = [
            new StyledString(Style.SegmentNone, "|||||"),
            .. Segments.Select(s => s.Colored(Location)),
            new StyledString(Style.SegmentNone, "|||||"),
        ];
        string result = coloredSegments.TransposeJoinStyled();
        var Adjs = StateString(viewer);
        var nameString = Style.Name.Format(Name) + $": {Faction.Name()}{Adjs}";
        if (Flags.HasFlag(EActorFlags.Settlement)) {
            if (Flags.HasFlag(EActorFlags.Capital)) {
                nameString += " [Capital]";
            } else {
                nameString += $" [Settlement]";
            }
            nameString += $" ({Domes} Domes, {Supplies[Commodity.Passengers]} Civilians)";
        }
        return $"{nameString}\n{result}";
    }

    public float ScrapInv {
        get => Supplies[Commodity.Scrap];
        set => Supplies[Commodity.Scrap] = value;
    }
    public float FuelInv {
        get => Supplies[Commodity.Fuel];
        set => Supplies[Commodity.Fuel] = value;
    }
    public float RationsInv {
        get => Supplies[Commodity.Rations];
        set => Supplies[Commodity.Rations] = value;
    }
    public float CrewInv {
        get => Supplies[Commodity.Crew];
        set => Supplies[Commodity.Crew] = value;
    }
    public float MoraleInv {
        get => Supplies[Commodity.Morale];
        set => Supplies[Commodity.Morale] = value;
    }
    public float WaterInv {
        get => Supplies[Commodity.Water];
        set => Supplies[Commodity.Water] = value;
    }
    public float AirInv {
        get => Supplies[Commodity.Air];
        set => Supplies[Commodity.Air] = value;
    }
    public float PassengersInv {
        get => Supplies[Commodity.Passengers];
        set => Supplies[Commodity.Passengers] = value;
    }
    public float SoldiersInv {
        get => Supplies[Commodity.Soldiers];
        set => Supplies[Commodity.Soldiers] = value;
    }
    public void End(EEndState state, string message = "") {
        EndMessage = $"{state}: {message}";
        EndState = state;
        Message($"Game Over: {message} ({state})");
        Location.GetEncounter()[this].ExitAfter(36000);
    }
    public string EndMessage { get; set; } = string.Empty;
    public EEndState? EndState { get; set; }
    public bool IsDepowered => !PowerSegments.Any() || FuelInv <= 0;
    public bool IsImmobile => EvaluateMove(Location.Terrain).Speed == 0;
    public bool IsDisarmed => !OffenseSegments.Any() || IsDepowered;
    public bool IsDefenseless => !DefenseSegments.Any();

    public bool IsDestroyed => EndState is not null;
    public bool IsVulnerable => IsDefenseless || IsImmobile || IsDisarmed || IsDepowered;
    public bool IsSettlement => Flags.HasFlag(EActorFlags.Settlement);

    public string About => ToString() + StateString();
    public override string ToString() => $"{Name} ({Faction})";

    public List<HitRecord> CreateFire() {
        var rng = new XorShift(Rng.Seed());
        List<HitRecord> fire = new();
        float availablePower = TotalCharge;
        var offense = OffenseSegments.GroupBy(s => s is WeaponSegment).ToDictionary(s => s.Key, s => s.ToList());
        var weapons = offense.GetValueOrDefault(true) ?? new();
        var selectedWeapons = new List<OffenseSegment>();
        var nonWeapons = offense.GetValueOrDefault(false) ?? new();
        var selectedNonWeapons = new List<OffenseSegment>();
        rng.Shuffle(weapons);
        foreach (var segment in weapons) {
            if (availablePower >= segment.Drain) {
                selectedWeapons.Add(segment);
                availablePower -= segment.Drain;
            }
        }
        foreach (var segment in nonWeapons) {
            if (availablePower >= segment.Drain) {
                selectedNonWeapons.Add(segment);
                availablePower -= segment.Drain;
            }
        }
        var used = TotalCharge - availablePower;
        DrawPower(used);

        foreach (WeaponSegment segment in selectedWeapons.OfType<WeaponSegment>()) {
            fire.AddRange(segment.GenerateFire(rng.Seed(), 0));
        }
        return fire;
    }
    void Recharge(int elapsed) {
        Segments.Do(s => s.Tick(elapsed));
        float overflowPower = PowerSegments.OfType<ReactorSegment>().Sum(s => s.Generate(elapsed));
        overflowPower += PowerSegments.OfType<ChargerSegment>().Sum(s => s.Generate(elapsed));
        FeedPower(overflowPower);
        float hours = elapsed / 3600f;
        ScrapInv -= WagesPerHr * hours;
        RationsInv -= RationsPerHr * hours;
        WaterInv -= WaterRecyclingLossPerHr * hours;
        AirInv -= AirLeakagePerHr * hours;
        FuelInv -= FuelPerHr * hours;
    }
    // Returns excess (wasted) power
    float FeedPower(float delta) {
        if (delta <= 0) {
            return 0;
        }

        var reactorSegments = PowerSegments.OfType<ReactorSegment>();
        // Distributes the power to segments based on their charge available
        var segmentCapAvail = reactorSegments.Select(s => s.Capacity - s.Charge);
        var totalCapAvail = segmentCapAvail.Sum();
        delta = Math.Clamp(delta, 0, totalCapAvail);
        float result = 0;
        if (delta > totalCapAvail) {
            result = delta - totalCapAvail;
            delta = totalCapAvail;
        }
        if (delta <= 0) {
            return result;
        }
        // Distributes the power to segments based on their charge available
        // ( this is the same as the above, but with a different order of operations)
        reactorSegments.Zip(segmentCapAvail, (rs, capacity) => rs.Charge += delta * (capacity / totalCapAvail)).Do();
        return 0;
    }
    float DrawPower(float delta) {
        if (delta <= 0) {
            return 0;
        }

        var reactorSegments = PowerSegments.OfType<ReactorSegment>();
        // Distributes the power to segments based on their charge available
        var segmentCharge = reactorSegments.Select(s => s.Charge);
        var totalCharge = segmentCharge.Sum();
        delta = Math.Clamp(delta, 0, totalCharge);
        float result = 0;
        if (delta > totalCharge) {
            result = delta - totalCharge;
            delta = totalCharge;
        }
        if (delta <= 0) {
            return result;
        }
        // Distributes the power to segments based on their charge available
        // ( this is the same as the above, but with a different order of operations)
        reactorSegments.Zip(segmentCharge, (rs, charge) => rs.Charge -= delta * (charge / totalCharge)).Do();
        return 0;
    }

    void Decay(int elapsed) {
        // Check rations
        if (RationsInv <= 0) {
            Message("You are out of rations and your crew is starving.");
            float liveRate = 0.99f;
            liveRate = (float)Math.Pow(liveRate, elapsed / 3600);
            CrewInv *= liveRate;
            SoldiersInv *= liveRate;
            PassengersInv *= liveRate;
            RationsInv = 0;
        }

        // Check water
        if (WaterInv <= 0) {
            Message("You are out of water. People are dying of dehydration.");
            float keepRate = 0.98f;
            keepRate = (float)Math.Pow(keepRate, elapsed / 3600);
            CrewInv *= keepRate;
            SoldiersInv *= keepRate;
            PassengersInv *= keepRate;
            WaterInv = 0;
        }

        // Check air
        float maxPopulation = (int)(AirInv / Tuning.Crawler.AirPerPerson);
        if (maxPopulation < TotalPeople) {
            Message("You are out of air. People are suffocating.");
            var died = TotalPeople - maxPopulation;
            if (died >= PassengersInv) {
                died -= PassengersInv;
                PassengersInv = 0;
            } else {
                PassengersInv -= died;
                died = 0;
            }
            if (died >= SoldiersInv) {
                died -= SoldiersInv;
                SoldiersInv = 0;
            } else {
                SoldiersInv -= died;
                died = 0;
            }
            if (died >= CrewInv) {
                died -= CrewInv;
                CrewInv = 0;
            } else {
                CrewInv -= died;
                died = 0;
            }
        }

        if (IsDepowered) {
            Message("Your life support systems are offline.");
            MoraleInv -= 1.0f;
        }

    }

    void TestEnded() {
        if (CrewInv == 0) {
            // TODO: List killers
            if (RationsInv == 0) {
                End(EEndState.Starved, "All the crew have starved.");
            } else if (WaterInv == 0) {
                End(EEndState.Killed, "All the crew have died of dehydration.");
            } else if (AirInv == 0) {
                End(EEndState.Killed, "All the crew have suffocated.");
            } else {
                End(EEndState.Killed, "The crew have been killed.");
            }
        }
        if (MoraleInv == 0) {
            End(EEndState.Revolt, "The crew has revolted.");
        }
        if (!UndestroyedSegments.Any()) {
            End(EEndState.Destroyed, "Your crawler has been utterly destroyed.");
        }
    }

    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        if (!Supplies.Segments.Any() || !fire.Any()) {
            return;
        }
        var rngRecvFire = new XorShift(Rng.Seed());

        bool wasDestroyed = IsDestroyed;
        int totalDamageDealt = 0;

        var relation = To(from);
        var wasAlreadyDamaged = relation.WasDamaged;

        relation.Hostile = true;

        string msg = "";
        foreach (var hit in fire) {
            int damage = hit.Damage.StochasticInt(ref rngRecvFire);
            int originalDamage = damage;
            var hitType = hit.Hit;
            var phase0Segments = CoreDefenseSegments.OfType<ShieldSegment>().Where(s => s.ShieldLeft > 0).ToList();
            var phase1Segments = CoreDefenseSegments.Except(phase0Segments).ToList();
            var phase2Segments = Segments.Except(CoreDefenseSegments).ToList();
            if (hitType is HitType.Misses) {
                damage = 0;
            }
            msg += $"{hit.Weapon.Name} {hitType} ";
            if (damage > 0) {
                var shieldSegment = rngRecvFire.ChooseRandom(phase0Segments);
                if (shieldSegment != null) {
                    var (remaining, armorMsg) = shieldSegment.AddDmg(hitType, damage);
                    damage = remaining;
                    msg += armorMsg;
                }
            }
            if (damage > 0) {
                var armorSegment = rngRecvFire.ChooseRandom(phase1Segments);
                if (armorSegment != null) {
                    var (remaining, armorMsg) = armorSegment.AddDmg(hitType, damage);
                    damage = remaining;
                    msg += armorMsg;
                }
            }
            if (damage > 0) {
                var hitSegment = rngRecvFire.ChooseRandom(phase2Segments);
                if (hitSegment != null) {
                    var (rem, hitMsg) = hitSegment.AddDmg(hitType, damage);
                    msg += hitMsg;
                    damage = rem;
                }
            }
            if (damage > 0) {
                // TODO: Also maybe lose commodities here
                var (rem, crewMsg) = LoseCrew(damage / 3);
                msg += crewMsg;
                damage = rem;
            }

            // Track total damage actually dealt (original minus what's left over)
            int actualDamageDealt = originalDamage - damage;
            totalDamageDealt += actualDamageDealt;

            msg += ".\n";
        }

        // Update damage tracking in relation
        relation.DamageTaken += totalDamageDealt;

        // Apply first damage morale penalty only if this is the first time taking damage from this attacker
        if (!wasAlreadyDamaged && totalDamageDealt > 0) {
            float dMorale = Tuning.Crawler.MoraleTakeAttack;
            Supplies[Commodity.Morale] += dMorale;
            msg += $"{dMorale} morale for taking fire.\n";
        }

        // Check if this crawler was destroyed and give attacker morale bonus
        if (!wasDestroyed && IsDestroyed) {
            if (relation.Hostile) {
                float dMorale = Tuning.Crawler.MoraleHostileDestroyed;
                from.Supplies[Commodity.Morale] += dMorale;
                Message($"{dMorale} morale for destroying {Name}");
            } else {
                float dHostileMorale = Tuning.Crawler.MoraleHostileDestroyed;
                float dMorale = Tuning.Crawler.MoraleFriendlyDestroyed;
                from.Supplies[Commodity.Morale] += dMorale;
                if (from is Crawler fromCrawler) {
                    float evilage = fromCrawler.EvilPoints / Tuning.EvilLimit;
                    evilage = Math.Clamp(evilage, 0.0f, 1.0f);
                    dMorale = CrawlerEx.Lerp(dMorale, dHostileMorale, evilage);
                }
                Message($"{dMorale} morale for destroying friendly {Name}");
            }
        }
        from.Message(msg.TrimEnd());
        Message(msg.TrimEnd());
        if (totalDamageDealt > 0) {
            UpdateSegmentCache();
        }
    }

    (int, string) LoseCrew(int damage) {
        var start = (int)CrewInv;
        CrewInv -= damage;
        var taken = start - (int)CrewInv;
        var startMorale = MoraleInv;
        MoraleInv -= Tuning.Crawler.MoraleAdjCrewLoss * taken;
        var moraleLoss = startMorale - MoraleInv;
        var remaining = damage - taken;
        return (remaining, $" killing {CrewInv} crew and {moraleLoss} morale");
    }

    public EArray<Faction, ActorFaction> FactionRelations { get; } = new();
    public ActorFaction To(Faction faction) => FactionRelations.GetOrNullAddNew(faction, () => new ActorFaction(this, faction));

    Dictionary<IActor, ActorToActor> _relations = new();
    public bool Knows(IActor other) => _relations.ContainsKey(other);
    public ActorToActor To(IActor other) {
        return _relations.GetOrAddNew(other, () => NewRelation(other));
    }
    public ActorToActor NewRelation(IActor to) {
        var result = new ActorToActor();
        bool isTradeSettlement = Location.Type is EncounterType.Settlement && Location.GetEncounter().Faction is Faction.Independent;
        var actorFaction = To(to.Faction);
        if (actorFaction.ActorStanding < 0) {
            result.Hostile = true;
        }
        return result;
    }

    Dictionary<Location, LocationActor> _locations = new();
    public bool Knows(Location location) => _locations.ContainsKey(location);
    public LocationActor To(Location location) {
        return _locations.GetOrAddNew(location, () => NewRelation(location));
    }
    public LocationActor NewRelation(Location to) {
        var result = new LocationActor();
        return result;
    }

    /// <summary>
    /// Helper: Set up bandit extortion ultimatum if conditions are met
    /// </summary>
    void SetupBanditExtortion(IActor target) {
        return;

        if (Faction != Faction.Bandit || target.Faction != Faction.Player) return;

        float cargoValue = target.Supplies.ValueAt(Location);
        if (cargoValue >= Tuning.Bandit.minValueThreshold &&
            Rng.NextSingle() < Tuning.Bandit.demandChance &&
            !To(target).Hostile &&
            !To(target).Surrendered &&
            !IsDisarmed) {

            var extortion = new ProposeAttackOrLoot(Rng / 2, Tuning.Bandit.demandFraction);
            To(target).AddProposal(extortion);
        }
    }

    /// <summary>
    /// Helper: Scan for contraband and set up seizure/tax ultimatums if needed
    /// </summary>
    void SetupContrabandAndTaxes(IActor target) {
        var toFaction = To(target.Faction);
        if (!Flags.HasFlag(EActorFlags.Settlement))
            return;

        var seizure = new ProposeSearchSeizeHostile(this, target);
        To(target).AddProposal(seizure);

        // Taxes for settlements in own territory
        // if ((Flags & EActorFlags.Settlement) != 0 &&
        //     Faction.IsCivilian() &&
        //     Location.Sector.ControllingFaction == Faction) {
        //
        //     var taxes = new ProposeTaxes(Tuning.Civilian.taxRate);
        //     To(target).AddProposal(taxes);
        // }
    }

    /// <summary>
    /// Helper: Expire all persistent proposals when leaving encounter
    /// </summary>
    void ExpireProposals(IActor other) {
        To(other).StoredProposals.Clear();
    }

    /// <summary>
    /// Called when this actor enters an encounter with existing actors
    /// </summary>
    public void Meet(Encounter encounter, long time, IEnumerable<IActor> encounterActors) {
        foreach (var actor in encounterActors.OfType<Crawler>()) {
            actor.SetupBanditExtortion(this);
            actor.SetupContrabandAndTaxes(this);
        }
    }

    void PruneRelations() {
        Dictionary<IActor, ActorToActor> relations = new();
        foreach (var (actor, relation) in _relations) {
            if (actor is Crawler { IsSettlement: true, IsDestroyed: false } || relation.Hostile) {
                relations.Add(actor, relation);
            }
        }
        // TODO: Use a static and dynamic relations list
        _relations = relations;
    }

    /// <summary>
    /// Called when a new actor joins the encounter
    /// </summary>
    public void Greet(IActor newActor) {
        Message($"{newActor.Name} enters");
        SetupBanditExtortion(newActor);
        SetupContrabandAndTaxes(newActor);
    }

    /// <summary>
    /// Called when this actor leaves an encounter with remaining actors
    /// </summary>
    public void Leave(IEnumerable<IActor> encounterActors) {
        foreach (var actor in encounterActors.OfType<Crawler>()) {
            actor.ExpireProposals(this);
        }
        PruneRelations();
    }

    /// <summary>
    /// Called when another actor leaves the encounter
    /// </summary>
    public void Part(IActor leavingActor) {
        ExpireProposals(leavingActor);
        Message($"{leavingActor.Name} leaves");
    }

    // Accessor methods for save/load
    public Dictionary<IActor, ActorToActor> GetRelations() => _relations;
    public Dictionary<Location, LocationActor> GetVisitedLocations() => _locations;
    public float Markup { get; set; }
    public float Spread { get; set; }
    public void SetVisitedLocations(Dictionary<Location, LocationActor> locations) => _locations = locations;
    public void SetRelations(Dictionary<IActor, ActorToActor> relations) => _relations = relations;
    public XorShift GetRng() => Rng;
    public ulong GetRngState() => Rng.GetState();
    public void SetRngState(ulong state) => Rng.SetState(state);
    public ulong GetGaussianRngState() => Gaussian.GetRngState();
    public void SetGaussianRngState(ulong state) => Gaussian.SetRngState(state);
    public bool GetGaussianPrimed() => Gaussian.GetPrimed();
    public void SetGaussianPrimed(bool primed) => Gaussian.SetPrimed(primed);
    public double GetGaussianZSin() => Gaussian.GetZSin();
    public void SetGaussianZSin(double zSin) => Gaussian.SetZSin(zSin);
    public int Domes { get; set; } = 0;
}
