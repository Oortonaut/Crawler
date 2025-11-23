using System;
using System.Collections;
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
}

// For this faction, dealing in Controlled commodities requires a license.
// Early game commodities such as liquor or explosives require a category
// license for GameTier.EarlyGame (or higher), while late game commodities like trips and
// gems require a GameTier.LateGame license.
public class ActorFaction {
    public ActorFaction(IActor actor, Faction faction) {
        if (actor.Faction == faction) {
            // Bandits trust their own faction less
            if (actor is Crawler { Role: CrawlerRole.Bandit }) {
                ActorStanding = 25;
            } else {
                ActorStanding = 100;
            }
            FactionStanding = ActorStanding;
        } else {
            ActorStanding = FactionStanding = 10;
            // Bandits are hostile to everyone not in their faction
            if (actor is Crawler { Role: CrawlerRole.Bandit } || faction is Faction.Bandit) {
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
        // Default markup/spread - will be updated based on Role if needed
        Markup = Tuning.Trade.TradeMarkup(Gaussian);
        Spread = Tuning.Trade.TradeSpread(Gaussian);
        UpdateSegmentCache();
    }
    public string Name { get; set; }
    public string Brief(IActor viewer) {
        var C = CrawlerDisplay(viewer).Split('\n').ToList();
        var hitRepairTime =

        C[1] += $" Power: {TotalCharge:F1} + {TotalGeneration:F1}/t  Drain: Off:{OffenseDrain:F1}  Def:{DefenseDrain:F1}  Move:{MovementDrain:F1}";
        C[2] += $" Weight: {Mass:F0} / {Lift:F0}T, {Speed:F0}km/h  Repair: {RepairMode}";
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
    public EActorFlags Flags { get; set; } = EActorFlags.Mobile;
    public Location Location { get; set; }

    static Crawler() {
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

    public Faction Faction { get; set; }
    public CrawlerRole Role { get; set; } = CrawlerRole.None;
    public int EvilPoints { get; set; } = 0;

    // Component system
    List<IActorComponent> _components = new();
    bool _componentsDirty = false;

    public IEnumerable<IActorComponent> Components => _components;

    /// <summary>
    /// Get components sorted by priority (highest first), using lazy sorting.
    /// </summary>
    List<IActorComponent> ComponentsByPriority() {
        if (_componentsDirty) {
            _components.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _componentsDirty = false;
        }
        return _components;
    }

    public void AddComponent(IActorComponent component) {
        component.Initialize(this);
        _components.Add(component);
        _componentsDirty = true; // Mark for re-sort
        component.OnComponentAdded();

        // Subscribe component to encounter events if we're in an encounter
        if (Location?.HasEncounter == true) {
            var encounter = Location.GetEncounter();
            component.SubscribeToEncounter(encounter);
        }
    }

    /// <summary>
    /// Initialize components based on the crawler's role.
    /// Called after crawler creation to set up role-specific behaviors.
    /// </summary>
    public void InitializeRoleComponents(ulong seed) {
        var rng = new XorShift(seed);

        switch (Role) {
        case CrawlerRole.Settlement:
            // Settlements: trade, repair, licensing, contraband enforcement
            AddComponent(new SettlementContrabandComponent());
            AddComponent(new ContrabandScannerComponent());
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.25f));
            AddComponent(new RepairComponent());
            AddComponent(new LicenseComponent());
            break;

        case CrawlerRole.Trader:
            // Mobile merchants: primarily trade-focused
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.35f));
            AddComponent(new HostileAIComponent(rng.Seed())); // Can defend themselves
            AddComponent(new RetreatComponent()); // Flee when vulnerable
            break;

        case CrawlerRole.Customs:
            // Customs officers: contraband scanning and enforcement
            AddComponent(new ContrabandScannerComponent());
            AddComponent(new SettlementContrabandComponent());
            AddComponent(new HostileAIComponent(rng.Seed())); // Combat capable
            AddComponent(new RetreatComponent());
            break;

        case CrawlerRole.Bandit:
            // Bandits: extortion, robbery, combat
            AddComponent(new BanditComponent(rng.Seed(), 0.5f));
            AddComponent(new RetreatComponent());
            // Bandits have higher markup/spread for goods they steal/trade
            var gaussian = new GaussianSampler(rng.Seed());
            Markup = Tuning.Trade.BanditMarkup(gaussian);
            Spread = Tuning.Trade.BanditSpread(gaussian);
            break;

        case CrawlerRole.Traveler:
            // Travelers: quest givers, general interactions
            // TODO: Add quest-related components when quest system is implemented
            AddComponent(new TradeOfferComponent(rng.Seed(), 0.15f)); // Limited trading
            AddComponent(new RetreatComponent());
            break;

        case CrawlerRole.None:
        default:
            // No role-specific components
            break;
        }

        // All NPCs get basic survival components
        if (!Flags.HasFlag(EActorFlags.Player)) {
            // Settlement actors already have components, don't duplicate
            if (Role != CrawlerRole.Settlement) {
                if (!_components.Any(c => c is RetreatComponent)) {
                    AddComponent(new RetreatComponent());
                }
                if (!_components.Any(c => c is HostileAIComponent)) {
                    AddComponent(new HostileAIComponent(rng.Seed()));
                }
            }
        }
    }

    public void RemoveComponent(IActorComponent component) {
        if (_components.Remove(component)) {
            _componentsDirty = true; // Mark for re-sort (though not strictly necessary)

            // Unsubscribe component from encounter events
            if (Location?.HasEncounter == true) {
                var encounter = Location.GetEncounter();
                component.UnsubscribeFromEncounter(encounter);
            }
            component.OnComponentRemoved();
        }
    }

    // Scan actor's inventory for contraband based on this faction's policies
    public bool HasContraband(IActor target) {
        if (target is not Crawler crawler) {
            return false;
        }
        return !ScanForContraband(crawler).IsEmpty;
    }
    public Inventory ScanForContraband(IActor target) {
        // Use ContrabandScannerComponent if available, otherwise use old method
        var scanner = _components.OfType<ContrabandScannerComponent>().FirstOrDefault();
        if (scanner != null && target is Crawler targetCrawler) {
            return scanner.ScanForContraband(targetCrawler);
        }

        // Fallback: original implementation
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

    internal long SimulationTime = 0;
    // Simulate
    // run action or think
    public void TickTo(long time) {
        if (NextEvent == 0) {
            _nextEventAction = null; // to be sure, should be already
            _TickTo(time);
            return;
        }
        while (time <= NextEvent) {
            _TickTo(NextEvent);
        }
        if (SimulationTime < time) {
            // _nextEventAction might not be null but it won't get called
            _TickTo(time);
        }
    }
    void _TickTo(long time) {
        int elapsed = SimulateTo(time);
        if (time == NextEvent) {
            NextEvent = 0;
            if (_nextEventAction != null) {
                var action = _nextEventAction;
                _nextEventAction = null;
                action.Invoke(this);
            } else {
                if (elapsed == 0) {
                    ThinkFor(elapsed);
                } else {
                    ThinkFor(elapsed);
                }
            }
        } else if (elapsed > 0) {
            ThinkFor(elapsed);
        } else  {
            // throw new InvalidOperationException($"Elapsed time should only be zero for scheduled events.");
            ThinkFor(elapsed);
        }
        PostTick(time);
    }
    public ILogger Log => LogCat.Log;
    // Returns elapsed, >= 0
    public int SimulateTo(long time) {
        int elapsed = (int)(time - SimulationTime);
        SimulationTime = time;
        if (elapsed < 0) {
            throw new InvalidOperationException("TODO: Time Travel");
        }

        if (EndState != null || elapsed == 0) {
            return elapsed;
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
        return elapsed;
    }
    void PostTick(long time) {
        TestEnded();
    }
    public void Travel(Location loc) {
        var (fuel, time) = FuelTimeTo(loc);
        if (fuel < 0) {
            Message("Not enough fuel.");
            return;
        }

        Location.GetEncounter().RemoveActor(this);

        FuelInv -= fuel;
        int delay = (int)(time * 3600);
        var arrivalTime = SimulationTime + delay;

        // Schedule this crawler in the game's traveling crawlers queue
        Game.Instance!.ScheduleCrawler(this, arrivalTime);
        ConsumeTime(delay, _ => {
            Location = loc;
            Location.GetEncounter().AddActor(this);
        });
    }
    public void ThinkFor(int elapsed) {
        // using var activity = LogCat.Game.StartActivity($"{Name} Tick Against {Actors.Count()} others");
        if (Flags.HasFlag(EActorFlags.Player)) {
            return;
        }

        var actors = Location.GetEncounter().ActorsExcept(this);

        // Let components provide proactive behaviors in priority order
        // All NPCs should now have appropriate AI components:
        // - RetreatComponent (priority 1000): flee when vulnerable
        // - BanditComponent (priority 600): bandit-specific AI
        // - HostileAIComponent (priority 400): generic combat fallback
        foreach (var component in ComponentsByPriority()) {
            int ap = component.ThinkAction();

            if (NextEvent != 0) {
                // Component scheduled an action, we're done
                break;
            }
        }

        // No fallback needed - all actors should have appropriate components
        // If we reach here, no component took action (idle/waiting)
    }

    public void Message(string message) {
        // TODO: Message history for other actors
        if (this == Game.Instance?.Player) {
            CrawlerEx.Message(Game.TimeString(SimulationTime) + ": " + message);
        }
    }
    public long NextEvent { get; private set; } = 0;

    Action<Crawler>? _nextEventAction;

    public void ConsumeTime(long delay, Action<Crawler>? action = null) {
        if (delay < 0) throw new ArgumentOutOfRangeException(nameof(delay));

        if (NextEvent == 0) {
            NextEvent = SimulationTime + delay;
            _nextEventAction = action;
        } else {
            throw new InvalidOperationException($"Double scheduled.");
        }

        // Ensure the encounter reschedules this crawler for the new time
        Location?.GetEncounter()?.Schedule(this);
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
        float overflowPower = 0;
        float hours = elapsed / 3600f;
        overflowPower += ReactorSegments.Aggregate(0.0f, (i, s) => i + s.Generate(hours));
        overflowPower += ChargerSegments.Aggregate(0.0f, (i, s) => i + s.Generate(hours));
        float excessPower = FeedPower(overflowPower);

        // Use AutoRepairComponent if available, otherwise use old method
        var autoRepair = _components.OfType<AutoRepairComponent>().FirstOrDefault();
        if (autoRepair != null) {
            excessPower = autoRepair.PerformRepair(this, excessPower, elapsed);
        } else {
            excessPower = Repair(excessPower, elapsed);
        }

        // Use LifeSupportComponent if available, otherwise use old method
        var lifeSupport = _components.OfType<LifeSupportComponent>().FirstOrDefault();
        if (lifeSupport != null) {
            lifeSupport.ConsumeResources(this, elapsed);
        } else {
            ScrapInv -= WagesPerHr * hours;
            RationsInv -= RationsPerHr * hours;
            WaterInv -= WaterRecyclingLossPerHr * hours;
            AirInv -= AirLeakagePerHr * hours;
            FuelInv -= FuelPerHr * hours;
        }
    }
    float repairProgress = 0;
    // Self-repair system: use excess power to repair damaged segments
    float Repair(float power, int elapsed) {
        // TODO: We should consume the energy if we have undestroyed segments and are adding repair progress
        // At the moment the energy is only consumed when we do the repair
        // Not a problem because nothing follows this to use it.

        float maxRepairsCrew = CrewInv / Tuning.Crawler.RepairCrewPerHp;
        float maxRepairsPower = power / Tuning.Crawler.RepairPowerPerHp;
        float maxRepairsScrap =  ScrapInv / Tuning.Crawler.RepairScrapPerHp;
        float maxRepairs = Math.Min(Math.Min(maxRepairsCrew, maxRepairsPower), maxRepairsScrap);
        float maxRepairsHr = maxRepairs * elapsed / Tuning.Crawler.RepairTime;
        float repairHitsFloat = maxRepairsHr + repairProgress;
        int repairHits = (int)repairHitsFloat;
        repairProgress = repairHitsFloat - repairHits;
        var candidates = UndestroyedSegments.Where(s => s.Hits > 0);
        if (RepairMode is RepairMode.Off || !candidates.Any()) {
            repairProgress = 0;
            return power;
        } else if (RepairMode is RepairMode.RepairHighest) {
            var damagedSegments = candidates.OrderByDescending(s => s.Hits).ToStack();
            while (repairHits > 0 && damagedSegments.Count > 0) {
                var segment = damagedSegments.Pop();
                --repairHits;
                --segment.Hits;
            }
        } else if (RepairMode is RepairMode.RepairLowest) {
            var damagedSegments = candidates.OrderBy(s => s.Health).ToList();
            while (repairHits > 0 && damagedSegments.Count > 0) {
                int health = damagedSegments[0].Health;
                foreach (var segment in damagedSegments.TakeWhile(s => s.Health <= health).Where(s => s.Hits > 0)) {
                    if (repairHits <= 0) {
                        break;
                    }
                    --repairHits;
                    --segment.Hits;
                }
            }
        }
        int repaired = (int)repairHitsFloat - repairHits;

        float repairScrap = Tuning.Crawler.RepairScrapPerHp * repaired;
        float repairPower = Tuning.Crawler.RepairPowerPerHp * repaired;
        Message($"Repaired {repaired} Hits for {repairScrap:F1}¢¢.");

        power -= repairPower;
        ScrapInv -= repairScrap;


        return power;
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
        var lifeSupport = _components.OfType<LifeSupportComponent>().FirstOrDefault();
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

    /// <summary>
    /// Event fired when this crawler receives fire from another actor.
    /// Invoked before damage is processed, allowing components to react to incoming attacks.
    /// </summary>
    public event Action<IActor, List<HitRecord>>? OnReceiveFire;

    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        if (!Supplies.Segments.Any() || !fire.Any()) {
            return;
        }

        // Notify components that we're receiving fire
        OnReceiveFire?.Invoke(from, fire);

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

    public EArraySparse<Faction, ActorFaction> FactionRelations { get; } = new();
    public ActorFaction To(Faction faction) => FactionRelations.GetOrAddNew(faction, () => new ActorFaction(this, faction));

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

    RepairMode _repairMode = RepairMode.Off;
    public RepairMode RepairMode {
        get {
            // If AutoRepairComponent exists, use its mode
            var autoRepair = _components.OfType<AutoRepairComponent>().FirstOrDefault();
            if (autoRepair != null) {
                return autoRepair.RepairMode;
            }
            return _repairMode;
        }
        set {
            _repairMode = value;
            // If AutoRepairComponent exists, update its mode
            var autoRepair = _components.OfType<AutoRepairComponent>().FirstOrDefault();
            if (autoRepair != null) {
                autoRepair.RepairMode = value;
            }
        }
    }
}
