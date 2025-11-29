using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public enum RepairMode {
    Off,
    RepairLowest,
    RepairHighest,
}

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

    // Ultimatum state for demands/threats with timeout
    public class UltimatumState {
        public long ExpirationTime { get; set; }
        public string Type { get; set; } = "";
        public object? Data { get; set; }
    }

    public UltimatumState? Ultimatum { get; set; }

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

    // Data structure for serialization
    public record class Data {
        public EFlags Flags { get; set; }
        public int DamageCreated { get; set; }
        public int DamageInflicted { get; set; }
        public int DamageTaken { get; set; }
        public UltimatumState? Ultimatum { get; set; }
    }

    public Data ToData() {
        return new Data {
            Flags = this.Flags,
            DamageCreated = this.DamageCreated,
            DamageInflicted = this.DamageInflicted,
            DamageTaken = this.DamageTaken,
            Ultimatum = this.Ultimatum
        };
    }

    public void FromData(Data data) {
        this.Flags = data.Flags;
        this.DamageCreated = data.DamageCreated;
        this.DamageInflicted = data.DamageInflicted;
        this.DamageTaken = data.DamageTaken;
        this.Ultimatum = data.Ultimatum;
    }
}

// For this faction, dealing in Controlled commodities requires a license.
// Early game commodities such as liquor or explosives require a category
// license for GameTier.EarlyGame (or higher), while late game commodities like trips and
// gems require a GameTier.LateGame license.
public class ActorFaction {
    public ActorFaction(IActor actor, Factions faction) {
        if (actor.Faction == faction) {
            // Bandits trust their own faction less
            if (actor is Crawler { Role: Roles.Bandit }) {
                ActorStanding = 25;
            } else {
                ActorStanding = 100;
            }
            FactionStanding = ActorStanding;
        } else {
            ActorStanding = FactionStanding = 10;
            // Bandits are hostile to everyone not in their faction
            if (actor is Crawler { Role: Roles.Bandit } || faction is Factions.Bandit) {
                ActorStanding = -100;
                FactionStanding = -100;
            }
        }
        Faction = faction;
    }
    public Factions Faction { get; }
    public GameTier GetLicense(CommodityCategory category) =>
        Faction.IsLegal(category) ? GameTier.Late :
        Faction.IsLicensed(category) ? licenses[category] :
        GameTier.None;
    public void GrantLicense(CommodityCategory category, GameTier tier) {
        if (tier > licenses[category]) {
            licenses[category] = tier;
        }
    }
    EArray<CommodityCategory, GameTier> licenses = new();
    public bool CanTrade(CommodityCategory cat, GameTier tier) => GetLicense(cat) >= tier;
    public bool CanTrade(Commodity c) => CanTrade(c.Category(), c.Tier());
    public bool CanTrade(SegmentDef segdef) => segdef.SegmentKind is not SegmentKind.Offense || CanTrade(CommodityCategory.Dangerous, weaponTier(segdef));
    public int ActorStanding { get; set; } // How the actor feels about the faction
    public int FactionStanding { get; set; } // How the faction feels about the actor
    GameTier weaponTier(SegmentDef segdef) => (GameTier)Math.Clamp((int)Math.Round(segdef.Size.Size * 0.667), 0, 3);
}
public partial class Crawler: ActorScheduled {
    public static Crawler NewRandom(ulong seed, Factions faction, Location here, int crew, float supplyDays, float goodsWealth, float segmentWealth, EArray<SegmentKind, float> segmentClassWeights) {
        var rng = new XorShift(seed);
        var crawlerSeed = rng.Seed();
        var invSeed = rng.Seed();
        var newInv = new Inventory();
        newInv.AddRandomInventory(invSeed, here, crew, supplyDays, goodsWealth, segmentWealth, true, segmentClassWeights, faction);

        // Extract working segments from inventory and add them to builder
        var workingSegments = newInv.Segments.Where(s => !s.IsPackaged).ToList();
        foreach (var segment in workingSegments) {
            newInv.Segments.Remove(segment);
        }

        var crawler = new Builder()
            .WithSeed(crawlerSeed)
            .WithFaction(faction)
            .WithLocation(here)
            .WithSupplies(newInv)
            .AddSegments(workingSegments)
            .Build();

        return crawler;
    }

    // Init-based constructor (primary constructor pattern)
    public Crawler(Init init) : base(init) {
        Flags |= ActorFlags.Mobile;
        Supplies.Overdraft = Cargo;
        Role = init.Role;

        // Initialize working segments from init
        _allSegments = init.WorkingSegments.OrderBy(s => (s.ClassCode, s.Cost)).ToList();

        // Initialize components based on role if requested
        if (init.InitializeComponents) {
            InitializeComponents(Rng.Seed());
        }
    }

    // Init + Data constructor (for loading from save)
    public Crawler(Init init, Data data) : this(init) {
        FromData(data);
    }

    // Builder-based constructor (convenience wrapper)
    private Crawler(Builder builder)
        : base(builder.GetSeed(), builder.GetName(), builder.GetBrief(), builder.GetFaction(), builder.GetSupplies(), builder.GetCargo(), builder.GetLocation()) {
        Flags |= ActorFlags.Mobile;
        Supplies.Overdraft = Cargo;
        Role = builder.GetRole();

        // Initialize working segments from builder
        _allSegments = builder.GetWorkingSegments().OrderBy(s => (s.ClassCode, s.Cost)).ToList();

        // Initialize components based on role if requested
        if (builder.GetInitializeComponents()) {
            InitializeComponents(Rng.Seed());
        }
    }
    public override string Brief(IActor viewer) {
        var C = CrawlerDisplay(viewer).Split('\n').ToList();

        var repairMode = Components.OfType<AutoRepairComponent>().FirstOrDefault()?.RepairMode ?? RepairMode.Off;
        C[1] += $" Power: {TotalCharge:F1} + {TotalGeneration:F1}/t  Drain: Off:{OffenseDrain:F1}  Def:{DefenseDrain:F1}  Move:{MovementDrain:F1}";
        C[2] += $" Weight: {Mass:F0} / {Lift:F0}T, {Speed:F0}km/h  Repair: {repairMode}";
        if (this == viewer) {
            float RationsPerDay = TotalPeople * Tuning.Crawler.RationsPerCrewDay;
            float WaterPerDay = WaterRecyclingLossPerHr * 24;
            float AirPerDay = AirLeakagePerHr * 24;
            C[3] += $" Cash: {ScrapInv:F1}¢¢  Fuel: {FuelInv:F1}, {FuelPerHr:F1}/h, {FuelPerKm * 100:F2}/100km";
            C[4] += $" Crew: {CrewInv:F0}  Morale: {MoraleInv}";
            C[5] += $" Rations: {RationsInv:F1} ({RationsPerDay:F1}/d)  Water: {WaterInv:F1} ({WaterPerDay:F1}/d)  Air: {AirInv:F1} ({AirPerDay:F1}/d)";
        } else {
            C = C.Take(3).ToList();
        }
        return string.Join("\n", C);
    }
    // Hourly fuel, wages, and rations are the primary timers.
    // Fuel use keeps the crawler size down
    // right now there's not much advantage to having any craw
    public float FuelPerHr => StandbyDrain / FuelEfficiency;
    public float WagesPerHr => CrewInv * Tuning.Crawler.WagesPerCrewDay / 24;
    public float RationsPerHr => TotalPeople * Tuning.Crawler.RationsPerCrewDay / 24;

    public float FuelPerKm => Tuning.Crawler.FuelPerKm * MovementDrain / FuelEfficiency;

    // Water recycling loss goes up as the number of people increases
    public float WaterRequirement => CrewInv * Tuning.Crawler.WaterPerCrew;
    public float WaterRecyclingLossPerHr => WaterRequirement * Tuning.Crawler.WaterRecyclingLossPerHour;
    // Air leakage increases with crawler damage
    public float AirLeakagePerHr {
        get {
            float hitSegments = UndestroyedSegments
                .Sum(s => s.Hits / (float)s.MaxHits);
            return TotalPeople * Tuning.Crawler.AirLeakagePerDamagedSegment * hitSegments;
        }
    }
    public float TotalPeople => CrewInv;
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

    public int EvilPoints { get; set; } = 0;

    public override void Begin() {
        base.Begin();
        UpdateSegmentCache();
    }
    /// <summary>
    /// Initialize components based on the crawler's role.
    /// Called after crawler creation to set up role-specific behaviors.
    /// </summary>
    public void InitializeComponents(ulong seed) {
        var rng = new XorShift(seed);

        AddComponent(new LifeSupportComponent());
        AddComponent(new AutoRepairComponent());
        if (Role is not Roles.Player) {
            AddComponent(new RelationPrunerComponent());
        }
        if (Role is not Roles.Settlement) {
            AddComponent(new RetreatComponent()); // High priority survival
            AddComponent(new SurrenderComponent(rng.Seed(), "S"));
        }
        if (Role is Roles.Bandit) {
            AddComponent(new CombatComponentAdvanced(rng.Seed())); // Smart combat targeting
        } else {
            AddComponent(new CombatComponentDefense(rng.Seed())); // Combat capable
        }

        switch (Role) {
        case Roles.Player:
            AddComponent(new AttackComponent("A"));
            AddComponent(new PlayerDemandComponent(rng.Seed(), 0.5f, "X"));
            AddComponent(new EncounterMessengerComponent());
            break;

        case Roles.Settlement:
            // Settlements: trade, repair, licensing, contraband enforcement
            AddComponent(new CustomsComponent());
            var settlementGaussian = new GaussianSampler(rng.Seed());
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.25f,
                Tuning.Trade.TradeMarkup(settlementGaussian),
                Tuning.Trade.TradeSpread(settlementGaussian)));
            AddComponent(new RepairComponent());
            AddComponent(new LicenseComponent());
            break;

        case Roles.Trader:
            // Mobile merchants: primarily trade-focused
            var traderGaussian = new GaussianSampler(rng.Seed());
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.35f,
                Tuning.Trade.TradeMarkup(traderGaussian),
                Tuning.Trade.TradeSpread(traderGaussian)));
            AddComponent(new CombatComponentDefense(rng.Seed())); // Can defend themselves
            break;

        case Roles.Customs:
            // Customs officers: contraband scanning and enforcement
            AddComponent(new CustomsComponent());
            break;

        case Roles.Bandit:
            // Bandits: extortion, robbery, combat
            AddComponent(new BanditComponent(rng.Seed(), 0.5f)); // Extortion/ultimatums
            // Bandits have higher markup/spread for goods they steal/trade
            var banditGaussian = new GaussianSampler(rng.Seed());
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.20f,
                Tuning.Trade.BanditMarkup(banditGaussian),
                Tuning.Trade.BanditSpread(banditGaussian)));
            break;

        case Roles.Traveler:
            // Travelers: quest givers, general interactions
            // TODO: Add quest-related components when quest system is implemented
            var travelerGaussian = new GaussianSampler(rng.Seed());
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.15f,
                Tuning.Trade.TradeMarkup(travelerGaussian),
                Tuning.Trade.TradeSpread(travelerGaussian))); // Limited trading
            break;

        case Roles.None:
        default:
            // No role-specific components
            break;
        }
    }

    public bool Pinned() {
        return Rng.NextSingle() > this.EscapeChance();
    }

    // Returns elapsed, >= 0
    public override void SimulateTo(long time) {
        base.SimulateTo(time);
        int elapsed = Elapsed;

        if (EndState != null || elapsed == 0) {
            return;
        }

        //using var activity = LogCat.Game.StartActivity(
        //        "Crawler.Tick", System.Diagnostics.ActivityKind.Internal)?
        //    .SetTag("crawler.name", Name)
        //    .SetTag("crawler.faction", Faction)
        //    .SetTag("elapsed_seconds", elapsed);
        Log.LogInformation($"Ticking {Name} at {Location.Name} time {time}, elapsed {elapsed}");

        Recharge(elapsed);
        Decay(elapsed);

        UpdateSegmentCache();
    }
    protected override void PostTick(long time) {
        TestEnded();
    }
    public override void Travel(Location loc) {
        var (fuel, time) = FuelTimeTo(loc);
        if (fuel < 0) {
            Message("Not enough fuel.");
            return;
        }

        Location.GetEncounter().RemoveActor(this);

        FuelInv -= fuel;
        int delay = (int)(time * 3600);
        var arrivalTime = Time + delay;

        // Schedule this crawler in the game's traveling crawlers queue
        Game.Instance!.ScheduleCrawler(this, arrivalTime);
        ConsumeTime(delay, () => {
            Location = loc;
            Location.GetEncounter().AddActor(this);
        });
    }

    public override void Think() {
        // using var activity = LogCat.Game.StartActivity($"{Name} Tick Against {Actors.Count()} others");
        if (Flags.HasFlag(ActorFlags.Player)) {
            return;
        }
        if (NextEvent != null) {
            Log.LogError($"Next event should be null, but it's {NextEvent} and elapsed {Elapsed}");
            return;
        }

        var actors = Location.GetEncounter().ActorsExcept(this);

        // Let components provide proactive behaviors in priority order
        // All NPCs should now have appropriate AI components:
        // - RetreatComponent (priority 1000): flee when vulnerable
        // - BanditComponent (priority 600): bandit-specific AI
        // - HostileAIComponent (priority 400): generic combat fallback
        CleanComponents(true);
        foreach (var component in Components) {
            component.GetNextEvent();
            if (NextEvent != null) {
                // Component scheduled an action, we're done
                break;
            }
        }

        // No fallback needed - all actors should have appropriate components
        // If we reach here, no component took action (idle/waiting)
    }

    public override void Message(string message) {
        // TODO: Message history for other actors
        if (this == Game.Instance?.Player) {
            CrawlerEx.Message(Game.TimeString(Time) + ": " + message);
        }
    }
    public int? WeaponDelay() {
        int minDelay = Tuning.MaxDelay;
        int N = 0;
        foreach (var segment in CyclingSegments) {
            minDelay = Math.Min(minDelay, segment.Cycle);
            ++N;
        }
        if (N > 0) {
            return minDelay;
        } else {
            return null;
        }
    }

    public void UpdateSegmentCache() {
        // _allSegments is already maintained as the working segments list
        // No need to re-sort unless segments were added/removed
        _allSegments = _allSegments.OrderBy(s => (s.ClassCode, s.Cost)).ToList();

        // Assert that all working segments are not packaged
        Debug.Assert(_allSegments.All(s => !s.IsPackaged), "Working segments should not be packaged");

        _activeSegments.Clear();
        _disabledSegments.Clear();
        _destroyedSegments.Clear();
        _undestroyedSegments.Clear();
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
                // This should not happen in working segments
                Debug.Fail("Packaged segment found in working segments list");
                break;
            }
        }
    }

    /// <summary>
    /// Install a segment from supplies or cargo into the working segments list.
    /// Unpacks the segment and adds it to _allSegments.
    /// </summary>
    public void InstallSegment(Segment segment) {
        Debug.Assert(segment.IsPackaged, "Can only install packaged segments");

        // Remove from supplies or cargo
        if (Supplies.Segments.Contains(segment)) {
            Supplies.Remove(segment);
        } else if (Cargo.Segments.Contains(segment)) {
            Cargo.Remove(segment);
        } else {
            throw new InvalidOperationException("Segment not found in supplies or cargo");
        }

        // Unpack and add to working segments
        segment.Packaged = false;
        _allSegments.Add(segment);
        UpdateSegmentCache();
    }

    /// <summary>
    /// Package a working segment and move it to supplies storage.
    /// </summary>
    public void PackageToSupplies(Segment segment) {
        Debug.Assert(!segment.IsPackaged, "Segment is already packaged");
        Debug.Assert(_allSegments.Contains(segment), "Segment not in working segments");

        // Remove from working segments
        _allSegments.Remove(segment);

        // Package and add to supplies
        segment.Packaged = true;
        Supplies.Add(segment);
        UpdateSegmentCache();
    }

    /// <summary>
    /// Package a working segment and move it to cargo storage.
    /// </summary>
    public void PackageToCargo(Segment segment) {
        Debug.Assert(!segment.IsPackaged, "Segment is already packaged");
        Debug.Assert(_allSegments.Contains(segment), "Segment not in working segments");

        // Remove from working segments
        _allSegments.Remove(segment);

        // Package and add to cargo
        segment.Packaged = true;
        Cargo.Add(segment);
        UpdateSegmentCache();
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
    public IEnumerable<Segment> ActiveCycleSegments => _activeSegments.Where(s => s.IsReadyToFire);
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
    public IEnumerable<ReactorSegment> ReactorSegments => PowerSegments.OfType<ReactorSegment>();
    public IEnumerable<ChargerSegment> ChargerSegments => PowerSegments.OfType<ChargerSegment>();
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
    public float TotalCharge => ReactorSegments.Aggregate(0.0f, (i, s) => i + s.Charge);
    public float TotalGeneration =>
        ReactorSegments.Aggregate(0.0f, (i, s) => i + s.Generation) +
        ChargerSegments.Aggregate(0.0f, (i, s) => i + s.Generation);

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
    public override string Report() {
        // Header with crawler name and location
        string result = Style.Name.Format($"[{Name}]");
        result += $" Evil: {EvilPoints}\n";

        result += "\nWorking Segments:\n" + _allSegments.SegmentReport(Location);
        result += "Packaged Segments (Supplies):\n" + Supplies.Segments.SegmentReport(Location);
        result += "Packaged Segments (Cargo):\n" + Cargo.Segments.SegmentReport(Location);
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
        if (IsImmobile && (Flags & ActorFlags.Mobile) != 0) {
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
        if (Flags.HasFlag(ActorFlags.Settlement)) {
            if (Flags.HasFlag(ActorFlags.Capital)) {
                nameString += " [Capital]";
            } else {
                nameString += $" [Settlement]";
            }
            nameString += $" ({Domes} Domes)";
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
    public float IsotopesInv {
        get => Supplies[Commodity.Isotopes];
        set => Supplies[Commodity.Isotopes] = value;
    }
    public float NanomaterialsInv {
        get => Supplies[Commodity.Nanomaterials];
        set => Supplies[Commodity.Nanomaterials] = value;
    }
    public bool IsDepowered => !PowerSegments.Any() || FuelInv <= 0;
    public bool IsImmobile => EvaluateMove(Location.Terrain).Speed == 0;
    public bool IsDisarmed => !OffenseSegments.Any() || IsDepowered;
    public bool IsDefenseless => !DefenseSegments.Any();

    public bool IsVulnerable => IsDefenseless || IsImmobile || IsDisarmed || IsDepowered;

    public string About => ToString();
    public override string ToString() => $"{base.ToString()} {StateString()}";

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
        float overflowPower = 0;
        float hours = elapsed / 3600f;
        overflowPower += ReactorSegments.Aggregate(0.0f, (i, s) => i + s.Generate(hours));
        overflowPower += ChargerSegments.Aggregate(0.0f, (i, s) => i + s.Generate(hours));
        float excessPower = FeedPower(overflowPower);

        // Use AutoRepairComponent if available, otherwise use old method
        // TODO: FeedPower on component
        var autoRepair = Components.OfType<AutoRepairComponent>().FirstOrDefault();
        if (autoRepair != null) {
            excessPower = autoRepair.PerformRepair(excessPower, elapsed);
        }

        // Use LifeSupportComponent if available, otherwise use old method
        var lifeSupport = Components.OfType<LifeSupportComponent>().FirstOrDefault();
        if (lifeSupport != null) {
            lifeSupport.ConsumeResources(this, elapsed);
        }
    }
    // Returns excess (wasted) power
    float FeedPower(float energy) {
        if (energy <= 0) {
            return energy;
        }

        var reactorSegments = ReactorSegments.ToArray();
        // Calculate the amount of charge to distribute
        var segmentChargeNeeded = reactorSegments.Select(s => s.Capacity - s.Charge).ToArray();
        if (!segmentChargeNeeded.Any()) {
            return energy;
        }
        var totalChargeNeeded = segmentChargeNeeded.Sum();
        if (totalChargeNeeded <= 0) {
            return energy;
        }
        float bonusRecharge = Math.Clamp(energy, 0, totalChargeNeeded);
        energy -= bonusRecharge;
        // Distributes the power to segments based on their charge available
        foreach (var (segment, chargeNeeded) in reactorSegments.Zip(segmentChargeNeeded)) {
            segment.Charge += chargeNeeded * (bonusRecharge / totalChargeNeeded);
        }
        return energy;
    }
    float DrawPower(float energy) {
        if (energy <= 0) {
            return energy;
        }

        var reactorSegments = ReactorSegments.ToArray();
        // Pulls power from segments based on their charge available
        var segmentChargeAvail = reactorSegments.Select(s => s.Charge).ToArray();
        if (!segmentChargeAvail.Any()) {
            return energy;
        }
        var totalChargeAvail = segmentChargeAvail.Sum();
        if (totalChargeAvail <= 0) {
            return energy;
        }
        float chargeDrawn = Math.Clamp(energy, 0, totalChargeAvail);
        energy -= chargeDrawn;

        // Distributes the power to segments based on their charge available
        // ( this is the same as the above, but with a different order of operations)
        reactorSegments.Zip(segmentChargeAvail, (segment, chargeAvail) => segment.Charge -= chargeDrawn * (chargeAvail / totalChargeAvail)).Do();
        return energy;
    }

    void Decay(int elapsed) {
        // Use LifeSupportComponent if available, otherwise use old method
        var lifeSupport = Components.OfType<LifeSupportComponent>().FirstOrDefault();
        if (lifeSupport != null) {
            lifeSupport.ProcessSurvival(this, elapsed);
        } else {
            // Check rations
            if (RationsInv <= 0) {
                Message("You are out of rations and your crew is starving.");
                float liveRate = 0.99f;
                liveRate = (float)Math.Pow(liveRate, elapsed / 3600);
                CrewInv *= liveRate;
                RationsInv = 0;
            }

            // Check water
            if (WaterInv <= 0) {
                Message("You are out of water. People are dying of dehydration.");
                float keepRate = 0.98f;
                keepRate = (float)Math.Pow(keepRate, elapsed / 3600);
                CrewInv *= keepRate;
                WaterInv = 0;
            }

            // Check air
            float maxPopulation = (int)(AirInv / Tuning.Crawler.AirPerPerson);
            if (maxPopulation < TotalPeople) {
                Message("You are out of air. People are suffocating.");
                var died = TotalPeople - maxPopulation;
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


    public override void ReceiveFire(IActor from, List<HitRecord> fire) {
        base.ReceiveFire(from, fire);

        if (!_allSegments.Any() || !fire.Any()) {
            return;
        }

        var rngRecvFire = new XorShift(Rng.Seed());

        bool wasDestroyed = IsDestroyed;
        int totalDamageDealt = 0;

        var relation = To(from);
        var wasAlreadyDamaged = relation.WasDamaged;

        SetHostileTo(from, true);

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

    // Serialization methods
    public override ActorBase.Data ToData() {
        var scheduledData = (ActorScheduled.Data)base.ToData();

        // Create Init from current state
        var init = new Init {
            Seed = scheduledData.Init.Seed,
            Name = scheduledData.Init.Name,
            Brief = scheduledData.Init.Brief,
            Faction = scheduledData.Init.Faction,
            Location = scheduledData.Init.Location,
            Supplies = scheduledData.Init.Supplies,
            Cargo = scheduledData.Init.Cargo,
            Role = this.Role,
            InitializeComponents = false, // Don't re-initialize on load
            WorkingSegments = new List<Segment>() // Will be restored from Data.WorkingSegments
        };

        return new Data {
            Init = init,
            Rng = scheduledData.Rng,
            Gaussian = scheduledData.Gaussian,
            Time = scheduledData.Time,
            LastTime = scheduledData.LastTime,
            EndState = scheduledData.EndState,
            EndMessage = scheduledData.EndMessage,
            ActorRelations = scheduledData.ActorRelations,
            LocationRelations = scheduledData.LocationRelations,
            NextEvent = scheduledData.NextEvent,
            Encounter_ScheduledTime = scheduledData.Encounter_ScheduledTime,
            WorkingSegments = _allSegments.Select(s => s.ToData()).ToList(),
            EvilPoints = this.EvilPoints
        };
    }

    public void FromData(Data data) {
        // Call base FromData
        base.FromData(data);

        // Restore working segments
        _allSegments.Clear();
        foreach (var segmentData in data.WorkingSegments) {
            if (SegmentEx.NameLookup.TryGetValue(segmentData.DefName, out var segmentDef)) {
                var segment = segmentDef.NewSegment(segmentData.Seed);
                segment.FromData(segmentData);
                _allSegments.Add(segment);
            }
        }

        // Restore other state
        this.EvilPoints = data.EvilPoints;

        // Update cached segment lists
        UpdateSegmentCache();
    }

    // Accessor methods for save/load (deprecated - use ToData/FromData instead)
    public XorShift GetRng() => Rng;
    public ulong GetRngState() => Rng.GetState();
    public void SetRngState(ulong state) => Rng.SetState(state);
    public ulong GetGaussianRngState() => Gaussian.GetRngState();
    public void SetGaussianRngState(ulong state) => Gaussian.SetRngState(state);
    public bool GetGaussianPrimed() => Gaussian.GetPrimed();
    public void SetGaussianPrimed(bool primed) => Gaussian.SetPrimed(primed);
    public double GetGaussianZSin() => Gaussian.GetZSin();
    public void SetGaussianZSin(double zSin) => Gaussian.SetZSin(zSin);
}
