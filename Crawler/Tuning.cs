namespace Crawler;

/// <summary>
/// Parameters for resource deposit extraction.
/// </summary>
public record ResourceParams(
    float BaseAmount,         // Base total (scaled by location wealth)
    float AmountVariance,     // 0.0-1.0, how much total varies at creation
    float BaseYield,          // Lambda for Poisson extraction (expected units/cycle)
    float EstimateVariance,   // Noise on estimate display (0.0-1.0)
    bool Refillable,          // Does it regenerate?
    float RefillRate          // Units/hour if refillable
);

public static partial class Tuning {
    public static class Game {
        public static float LootReturn = 0.5f;

        /// <summary>Default wait duration when player skips turn without specifying time.</summary>
        public static TimeDuration DefaultWaitDuration = TimeDuration.FromMinutes(5);

        // Hazard encounter risk/reward parameters
        public static float hazardWealthFraction = 0.5f;
        public static float hazardNegativePayoffChance = 0.3333f;
        public static float hazardNegativePayoffRatio = 1.5f; // negated, relative to wealth fraction

        // Resource encounter parameters
        public static float resourcePayoffFraction = 0.05f; // Fraction of location wealth
    }

    /// <summary>
    /// Parameters for resource extraction system.
    /// </summary>
    public static class Resource {
        /// <summary>Number of extraction cycles before showing noisy estimate.</summary>
        public static int EstimateCycles = 3;

        /// <summary>Time per extraction cycle.</summary>
        public static TimeDuration ExtractionTime = TimeDuration.FromHours(1);

        /// <summary>Per-resource extraction parameters.</summary>
        public static Dictionary<Commodity, ResourceParams> Params = new() {
            // BaseAmount, AmountVariance, BaseYield, EstimateVariance, Refillable, RefillRate
            [Commodity.Ore] = new(500, 0.3f, 50, 0.15f, false, 0),
            [Commodity.Biomass] = new(300, 0.5f, 40, 0.3f, true, 5),  // Regrows!
            [Commodity.Silicates] = new(400, 0.25f, 45, 0.2f, false, 0),
            [Commodity.Isotopes] = new(100, 0.4f, 10, 0.25f, false, 0),
            [Commodity.Gems] = new(50, 0.6f, 5, 0.5f, false, 0),  // High variance
        };
    }

    /// <summary>
    /// Combat risk tracking parameters.
    /// </summary>
    public static class Combat {
        /// <summary>Damage cap for severity normalization (100 damage = max severity).</summary>
        public static float DamageSeverityCap = 100f;

        /// <summary>Base severity for extortion risk events.</summary>
        public static float ExtortionRiskSeverity = 0.3f;
    }

    public static class Bandit {
        public static float demandChance = 0.6f;         // 60% chance to make demand on encounter entry
        public static float demandFraction = 0.33f;      // Demand 1/3 of cargo
        public static float minValueThreshold = 50f;     // Don't bother demanding if cargo worth less than this
    }

    public static class Civilian {
        public static float taxRate = 0.05f;             // 5% tax on cargo value at checkpoints
        // TODO: Vary these by TradePolicy
        public static float contrabandScanChance = 0.7f; // 70% chance to detect contraband
        public static float contrabandPenaltyMultiplier = 2.0f; // Fine is 2x value of contraband
    }

    public static class Encounter {
        public static float CrawlerDensity = 0.08f;
        public static EArray<EncounterType, float> HourlyArrivalsPerPop = [0, 0.375f, 1.2f, 0.24f, 0.12f]; // Tripled for decimal time (10 hrs/day)
        // Faction spawn weights by terrain type: Player, Bandit, Trade
        public static EArray<TerrainType, EArray<Factions, float>> crawlerSpawnWeight = [
            [0, 2, 12],    // Flat - more trade
            [0, 4, 8],    // Rough - balanced
            [0, 6, 6],    // Broken - equal mix
            [0, 8, 4],    // Shattered - more bandits
            [0, 10, 2],    // Ruined - mostly bandits
        ];
        public static float DynamicCrawlerLifetimeExpectation = 5000.0f * 7.5f; // 7.5 half-hours in decimal time
    }
    public static class Trade {
        // Legacy fields (kept for backward compatibility)
        public static float rate = 1.05f;
        public static float sd = 0.07f;
        // public static float LocalMarkup() => CrawlerEx.NextGaussian(1, sd);
        public static float LocalMarkup(GaussianSampler g) => g.NextSingle(1, sd);
        public static float TradeMarkup(GaussianSampler gaussian) => gaussian.NextSingle(rate, sd);
        public static float banditRate = 1.25f;          // Higher markup for bandits
        public static float banditSd = 0.1f;
        public static float BanditMarkup(GaussianSampler gaussian) => gaussian.NextSingle(banditRate, banditSd);
        public static float repairMarkup = 1.2f;
        public static float repairMarkupSd = 0.15f;
        public static float RepairMarkup(GaussianSampler gaussian) => gaussian.NextSingle(repairMarkup, repairMarkupSd);

        // New bid-ask spread model
        public static float baseBidAskSpread = 0.20f;   // 20% base spread (±10% around mid)
        public static float tradeSpreadMultiplier = 0.8f; // Trade: 16% spread
        public static float tradeSpreadSd = 0.05f;      // ±5% variance
        public static float banditSpreadMultiplier = 1.5f; // Bandit: 30% spread
        public static float banditSpreadSd = 0.10f;     // ±10% variance
        public static float TradeSpread(GaussianSampler gaussian) => gaussian.NextSingle(baseBidAskSpread * tradeSpreadMultiplier, tradeSpreadSd);
        public static float BanditSpread(GaussianSampler gaussian) => gaussian.NextSingle(baseBidAskSpread * banditSpreadMultiplier, banditSpreadSd);

        // Scarcity pricing
        public static float scarcityWeight = 1.0f;      // Multiplier for scarcity effect
        public static float scarcityEssential = 0.3f;   // Lower premium for essentials
        public static float scarcityLuxury = 0.8f;      // Was 1.5

        // Bandit markup settings
        public static float banditHostilityThreshold = 5.0f; // Evilness threshold for hostility check
        public static float banditHostilityChance = 0.3f;    // Base chance of turning hostile at threshold

        // Trade prohibition enforcement
        public static float restrictedScanChance = 0.7f;     // Chance to detect contraband

        // Policy-based pricing multipliers
        public static float subsidizedMultiplier = 0.7f;
        public static float legalMultiplier = 1.0f;
        public static float taxedMultiplier = 1.3f;
        public static float controlledMultiplier = 1.75f;
        public static float restrictedMultiplier = 2.75f;
        public static float prohibitedMultiplier = 5.0f;

        public static float PolicyMultiplier(TradePolicy policy) => policy switch {
            TradePolicy.Subsidized => subsidizedMultiplier,
            TradePolicy.Legal => legalMultiplier,
            TradePolicy.Taxed => taxedMultiplier,
            TradePolicy.Controlled => controlledMultiplier,
            TradePolicy.Restricted => restrictedMultiplier,
            TradePolicy.Prohibited => prohibitedMultiplier, // gray or black market only, based on reputation (tbi)
            _ => legalMultiplier
        };
    }

    // Faction trade policies - defines how each faction treats commodity categories and segment kinds
    public static class FactionPolicies {
        // Policies are stored per faction
        public static EArray<Factions, Policy> Policies = new();

        // Initialize default policies for core factions
        static FactionPolicies() {
            InitializeCoreFactionPolicies();
        }

        static void InitializeCoreFactionPolicies() {
            // Player - permissive for everything
            Policies[Factions.Player] = CreateDefaultPolicy(TradePolicy.Legal);

            // Bandit - sell everything (including contraband)
            Policies[Factions.Bandit] = CreateDefaultPolicy(TradePolicy.Legal);

            // Independent - legitimate merchants, restrict dangerous goods
            var tradeCommodityPolicy = CreateCommodityDefaultPolicy(TradePolicy.Legal);
            tradeCommodityPolicy[CommodityCategory.Vice] = TradePolicy.Prohibited;
            tradeCommodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Taxed;

            var tradeSegmentPolicy = CreateSegmentDefaultPolicy(TradePolicy.Legal);
            tradeSegmentPolicy[SegmentKind.Offense] = TradePolicy.Taxed;

            Policies[Factions.Independent] = new Policy(tradeCommodityPolicy, tradeSegmentPolicy, "Independent");
        }

        public static Policy CreateDefaultPolicy(TradePolicy defaultPolicy) {
            return new Policy(
                CreateCommodityDefaultPolicy(defaultPolicy),
                CreateSegmentDefaultPolicy(defaultPolicy),
                "Free Trade"
            );
        }

        public static EArray<CommodityCategory, TradePolicy> CreateCommodityDefaultPolicy(TradePolicy defaultPolicy) {
            var policy = new EArray<CommodityCategory, TradePolicy>();
            policy.Initialize(defaultPolicy);
            return policy;
        }
        
        public static EArray<SegmentKind, TradePolicy> CreateSegmentDefaultPolicy(TradePolicy defaultPolicy) {
            var policy = new EArray<SegmentKind, TradePolicy>();
            policy.Initialize(defaultPolicy);
            return policy;
        }
    }
    public static bool IsLegal(this Factions faction, CommodityCategory category) => faction.GetPolicy(category) is not (TradePolicy.Controlled or TradePolicy.Restricted or TradePolicy.Prohibited);
    public static bool IsLicensed(this Factions faction, CommodityCategory category) => faction.GetPolicy(category) is (TradePolicy.Controlled or TradePolicy.Restricted);
    public static bool IsIllegal(this Factions faction, CommodityCategory category) => faction.GetPolicy(category) is TradePolicy.Prohibited;
    public static TradePolicy GetPolicy(this Factions faction, Commodity commodity) => faction.GetPolicy(commodity.Category());
    public static TradePolicy GetPolicy(this Factions faction, CommodityCategory category) => FactionPolicies.Policies[faction].Commodities[category];
    public static TradePolicy GetPolicy(this Factions faction, SegmentKind kind) => FactionPolicies.Policies[faction].Segments[kind];
    public static Policy GetPolicy(this Factions faction) => FactionPolicies.Policies[faction];
    public static void SetPolicy(this Factions faction, CommodityCategory category, TradePolicy policy) => FactionPolicies.Policies[faction].Commodities[category] = policy;
    public static void SetPolicy(this Factions faction, SegmentKind kind, TradePolicy policy) => FactionPolicies.Policies[faction].Segments[kind] = policy;
    public static void SetPolicy(this Factions faction, Policy policy) => FactionPolicies.Policies[faction] = policy;
    public static class Crawler {
        public static float MoraleAdjCrewLoss = 0.5f;
        public static float StandbyFraction = 0.007f;
        public static float FuelPerKm = 0.002f;
        public static float RationsPerCrewDay = 0.01f;
        public static float WagesPerCrewDay = 0.1f;
        public static float MoraleTakeAttack = -1;
        public static float MoraleHostileDestroyed = 2;
        public static float MoraleFriendlyDestroyed = -2;
        public static float MoraleSurrendered = -1;
        public static float MoraleSurrenderedTo = 3;

        // Life support consumption
        public static float WaterPerCrew = 0.030f; // m^3 per crew
        public static float WaterRecyclingLossPerHour = 0.006f; // 0.6% per decimal hour (tripled)

        public static float AirPerPerson = 1.0f; // liters of liquid air per person
        public static float AirLeakagePerDamagedSegment = 0.015f; // 1.5% per decimal hour per damaged segment (tripled)

        // Self-repair system
        public static float RepairCrewPerHp = 6.0f;
        public static float RepairPowerPerHp = 10.0f;
        public static float RepairScrapPerHp = 50.0f;
        public static float RepairTime = 5000.0f; // Half decimal hour

        // Interaction durations
        public static TimeDuration HostilityTime = TimeDuration.FromMinutes(0.2);           // Declaring hostility
        public static TimeDuration TradeTime = TimeDuration.FromMinutes(5);              // Per trade transaction
        public static TimeDuration SurrenderTime = TimeDuration.FromMinutes(1);          // Processing surrender
        public static TimeDuration ExtortionTime = TimeDuration.FromMinutes(2.5);          // Handing over extorted goods
        public static TimeDuration RefuseTime = TimeDuration.FromMinutes(0.5);              // Refusing demands/searches
        public static TimeDuration ContrabandSearchClean = TimeDuration.FromHours(1.5);  // Clean contraband search
        public static TimeDuration ContrabandSearchFound = TimeDuration.FromHours(3.0); // Contraband found
        public static TimeDuration LicenseTime = TimeDuration.FromHours(1.25);            // License purchase paperwork
        public static TimeDuration HarvestTime = TimeDuration.FromHours(1);           // Harvesting resources
        public static TimeDuration HazardTime = TimeDuration.FromHours(3);             // Exploring hazards
        public static TimeDuration RepairDuration = TimeDuration.FromHours(3);        // Repair interaction duration
        public static TimeDuration AttackTime = TimeDuration.FromSeconds(10);              // Combat attack action
        public static TimeDuration UltimatumTimeout = TimeDuration.FromSeconds(300);       // Default ultimatum timeout
        public static TimeDuration FleeTime = TimeDuration.FromSeconds(25);                // Time to attempt fleeing
        public static EArray<Commodity, float> DefaultCommodityWeights => [
            1.0f, 1.0f, 1.0f, 1.0f, 0.4f, 0.6f, // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
            0.2f, 0.5f, 1.0f, // Air, Water, Rations
            0.3f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Biomass, Ore, Silicates, Metal, Chemicals, Glass
            0.4f, 0.4f, 0.6f, 0.7f, 0.4f, // Ceramics, Polymers, Alloys, Electronics, Explosives
            0.6f, 0.4f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
            0.2f, 0.1f, 0.1f, 0.05f, 0.3f, // Liquor, Stims, Downers, Trips, SmallArms
            0.2f, 0.2f, 0.1f, // Idols, Texts, Relics
            0.1f, 0.3f, 0.3f, 0.4f, // Slag, Lubricants, Coolant, SpareParts
            0.4f, 0.3f, 0.2f // Slugs, Cells, Rockets
        ];

        // Commodity weights by faction for crawler inventory generation
        public static EArray<Factions, EArray<Commodity, float>> CommodityWeights = [
            // Player - balanced loadout
            [
                1.0f, 1.0f, 1.0f, 1.0f, 0.4f, 0.6f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.2f, 0.5f, 1.0f,                     // Air, Water, Rations
                0.3f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.4f, 0.4f, 0.6f, 0.7f, 0.4f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.6f, 0.4f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.2f, 0.1f, 0.1f, 0.05f, 0.3f,       // Liquor, Stims, Downers, Trips, SmallArms
                0.2f, 0.2f, 0.1f,                     // Idols, Texts, Relics
                0.1f, 0.3f, 0.3f, 0.4f,              // Slag, Lubricants, Coolant, SpareParts
                0.4f, 0.3f, 0.2f                      // Slugs, Cells, Rockets
            ],
            // Bandit - combat focused, less crew support
            [
                1.5f, 0.8f, 0.8f, 0.6f, 0.3f, 0.4f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.1f, 0.3f, 0.7f,                     // Air, Water, Rations
                0.2f, 0.4f, 0.2f, 0.5f, 0.4f, 0.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.3f, 0.3f, 0.6f, 0.5f, 0.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.3f, 0.3f, 0.5f, 0.2f, 0.4f, 0.3f, 0.2f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.5f, 0.6f, 0.4f, 0.3f, 0.9f,        // Liquor, Stims, Downers, Trips, SmallArms - vice & weapons
                0.1f, 0.1f, 0.2f,                     // Idols, Texts, Relics
                0.05f, 0.2f, 0.2f, 0.3f,             // Slag, Lubricants, Coolant, SpareParts
                0.8f, 0.6f, 0.5f                      // Slugs, Cells, Rockets - high ammo
            ],
            // Trade - high on trade goods, low on combat
            [
                2.0f, 1.2f, 0.9f, 0.8f, 0.9f, 0.7f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.5f, 0.8f, 1.2f,                     // Air, Water, Rations - life support for trade
                0.8f, 0.9f, 0.7f, 1.0f, 1.0f, 0.8f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 0.9f, 1.2f, 0.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.2f, 1.0f, 0.8f, 0.9f, 1.1f, 1.2f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.7f, 0.3f, 0.2f, 0.1f, 0.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                0.6f, 0.7f, 0.4f,                     // Idols, Texts, Relics
                0.2f, 0.6f, 0.5f, 0.7f,              // Slag, Lubricants, Coolant, SpareParts - industrial goods
                0.3f, 0.2f, 0.1f                      // Slugs, Cells, Rockets - low ammo
            ],
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
            DefaultCommodityWeights,
        ];
    }

    public static class Segments {
        // PowerScaling Parameters for quick reference:
        // min: The base value at size 1, quality 0
        // (size, qual): the multiplier at size 5 and quality 3
        // ex: new PowerScaling(10.0f, (7, 5), "short", "long")
        // Exponential, ranges from
        //         Size
        // Quality   1   2   3   4   5
        //   0      10  16  26  42  70
        //   1      17
        //   2      29
        //   3      50             350
        // Approximate sizes: (in  m)  10 14 20 28 40/56 80 112 160 224
        public static PowerScaling LengthTiers = new PowerScaling(10.0f, (4, 1.2f), "short", "long");

        // Base segment parameters
        //                                                                30 .. 130 .. 600
        public static PowerScaling WeightTiers = new PowerScaling(30.0f, (20, 0.9f), "hefty", "lightened");
        public static PowerScaling CostTiers = new PowerScaling(300.0f, (50, 3.0f), "cheap", "expensive");
        public static PowerScaling MaxHitsTiers = new PowerScaling(10.0f, (12, 2.0f), "decrepit", "hardened");

        // Weapon parameters
        public static PowerScaling DamageTiers = new PowerScaling(6f, (15, 1.4f), "weak", "strong");
        public static PowerScaling RateTiers = new PowerScaling(3f, (0.65f, 1.4f), "slow", "fast");
        public static PowerScaling ShotsTiers = new PowerScaling(1.0f, (1.0f, 4f), "limited", "salvo");
        public static PowerScaling AimTiers = new PowerScaling(0.75f, (0.65f, 1.5f), "inaccurate", "accurate");
        public static PowerScaling AmmoUseTiers = new PowerScaling(1.4f, (9, 2.5f), "wasteful", "thrifty");

        // Power parameters
        public static PowerScaling DrainTiers = new PowerScaling(1.0f, (25.0f, 0.6f), "wasteful", "efficient");
        public static PowerScaling CapacityTiers = new PowerScaling(3.5f, (20, 2.0f), "small", "large");
        public static PowerScaling GenerationTiers = new PowerScaling(2.5f,(10, 2.0f), "weak", "strong");
        public static PowerScaling ChargerTiers = new PowerScaling(5, (15, 1.75f), "slow", "fast");
        
        // Traction parameters
        public static PowerScaling LiftTiers = new PowerScaling(150, (12, 1.5f), "weak", "strong");
        public static PowerScaling SpeedTiers = new PowerScaling(90, (0.15f, 1.2f), "slow", "fast");
        
        // Defense parameters
        public static PowerScaling ReductionTiers = new PowerScaling(5f, (7, 1.5f), "weak", "strong");
        public static PowerScaling MitigationTiers = new PowerScaling(0.75f, (0.6f, 0.75f), "thin", "thick");
        public static PowerScaling ShieldCapacityTiers = new PowerScaling(12.0f, (15, 1.3f), "weak", "strong");
        public static PowerScaling ShieldChargeTiers = new PowerScaling(5.0f, (8, 1.2f), "slow", "fast");

        // Industry parameters
        public static PowerScaling ThroughputTiers = new PowerScaling(1.0f, (1.5f, 1.5f), "slow", "fast");
        public static PowerScaling EfficiencyTiers = new PowerScaling(0.85f, (0.95f, 1.2f), "wasteful", "efficient");
        public static PowerScaling ActivateChargeTiers = new PowerScaling(10.0f, (30, 1.5f), "light", "heavy");

        // Storage parameters
        public static PowerScaling StorageCapacityTiers = new PowerScaling(10.0f, (50, 2.0f), "cramped", "spacious");

        // Harvest parameters
        public static PowerScaling HarvestYieldTiers = new PowerScaling(1, (4, 3), "inefficient", "efficient");

        // Habitat parameters
        public static PowerScaling CrewCapacityTiers = new PowerScaling(5.0f, (100, 2.0f), "cramped", "spacious");
        public static PowerScaling MoraleBonusTiers = new PowerScaling(0.1f, (3, 2.0f), "spartan", "luxurious");
    }

    public static class Economy {
        public static EArray<EncounterType, EArray<Commodity, float>> EncounterCommodityMarkup = [
            // None - all commodities at base price
            [
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.0f, 1.0f, 1.0f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f,                     // Idols, Texts, Relics
                1.0f, 1.0f, 1.0f, 1.0f,              // Slag, Lubricants, Coolant, SpareParts
                1.0f, 1.0f, 1.0f                      // Slugs, Cells, Rockets
            ],
            // Crossroads - cheap scrap, fuel premium
            [
                0.9f, 1.2f, 1.3f, 1.0f, 1.0f, 1.1f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.1f, 1.0f, 1.1f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.1f, 1.0f, 1.2f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.9f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 0.9f, 1.1f,                     // Idols, Texts, Relics
                0.8f, 1.0f, 1.0f, 1.1f,              // Slag, Lubricants, Coolant, SpareParts - cheap waste
                1.1f, 1.1f, 1.2f                      // Slugs, Cells, Rockets - slight premium
            ],
            // Settlement - cheap basics, morale services, manufactured goods
            [
                0.8f, 0.9f, 0.9f, 1.2f, 1.0f, 0.95f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.8f, 0.7f, 0.8f,                     // Air, Water, Rations - cheap life support
                0.9f, 1.0f, 1.0f, 1.0f, 0.9f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.9f, 0.9f, 1.0f, 0.9f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.8f, 0.8f, 0.9f, 0.8f, 0.9f, 0.9f, 0.8f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.3f, 1.3f, 1.3f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms - vice taxed
                0.9f, 0.8f, 1.0f,                     // Idols, Texts, Relics
                0.7f, 0.85f, 0.85f, 0.9f,            // Slag, Lubricants, Coolant, SpareParts - cheap industrial
                0.9f, 0.9f, 1.0f                      // Slugs, Cells, Rockets - standard
            ],
            // Resource - expensive scrap/crew, cheap raw materials
            [
                2.0f, 0.7f, 2.5f, 0.8f, 0.7f, 1.3f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.2f, 1.0f, 1.4f,                     // Air, Water, Rations
                0.6f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass - cheap raw
                0.9f, 0.9f, 0.8f, 1.1f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.4f, 1.3f, 1.2f, 1.3f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.1f, 1.2f, 1.2f, 1.2f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.3f, 1.4f,                     // Idols, Texts, Relics
                0.5f, 0.9f, 1.0f, 1.1f,              // Slag, Lubricants, Coolant, SpareParts - cheap slag
                1.2f, 1.3f, 1.4f                      // Slugs, Cells, Rockets - expensive
            ],
            // Hazard - everything expensive, low morale, desperate trades
            [
                1.5f, 1.8f, 3.0f, 0.6f, 1.4f, 1.5f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.5f, 1.4f, 2.0f,                     // Air, Water, Rations - life support premium
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.6f, 1.4f, 1.5f, 1.4f, 1.5f, 1.5f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.2f, 1.4f, 1.5f, 1.6f, 1.3f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.3f, 1.2f, 1.5f,                     // Idols, Texts, Relics
                0.6f, 1.3f, 1.4f, 1.5f,              // Slag, Lubricants, Coolant, SpareParts - expensive maint
                1.6f, 1.7f, 1.8f                      // Slugs, Cells, Rockets - high demand
            ],
        ];
        public static EArray<TerrainType, EArray<Commodity, float>> TerrainCommodityMarkup = [
            // Flat - baseline prices
            [
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.0f, 1.0f, 1.0f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f,                     // Idols, Texts, Relics
                1.0f, 1.0f, 1.0f, 1.0f,              // Slag, Lubricants, Coolant, SpareParts
                1.0f, 1.0f, 1.0f                      // Slugs, Cells, Rockets
            ],
            // Rough - fuel premium, morale harder
            [
                1.1f, 1.2f, 1.1f, 0.9f, 1.1f, 1.1f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.1f, 1.1f, 1.1f,                     // Air, Water, Rations
                1.1f, 1.0f, 1.0f, 1.1f, 1.1f, 1.1f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.1f, 1.1f, 1.1f, 1.1f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f,                     // Idols, Texts, Relics
                1.0f, 1.1f, 1.1f, 1.1f,              // Slag, Lubricants, Coolant, SpareParts
                1.1f, 1.1f, 1.1f                      // Slugs, Cells, Rockets
            ],
            // Broken - logistics costs
            [
                1.3f, 1.5f, 1.4f, 0.8f, 1.3f, 1.4f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.3f, 1.3f, 1.2f,                     // Air, Water, Rations
                1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.2f, 1.2f, 1.2f, 1.2f, 1.3f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.1f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.1f, 1.1f, 1.1f, 1.1f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.1f, 1.1f, 1.2f,                     // Idols, Texts, Relics
                1.0f, 1.2f, 1.2f, 1.3f,              // Slag, Lubricants, Coolant, SpareParts
                1.3f, 1.3f, 1.4f                      // Slugs, Cells, Rockets
            ],
            // Shattered - fuel very expensive, harsh conditions
            [
                1.5f, 2.0f, 1.8f, 0.7f, 1.6f, 1.8f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.6f, 1.5f, 1.4f,                     // Air, Water, Rations
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.5f, 1.4f, 1.2f, 1.3f, 1.4f, 1.4f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.2f, 1.3f, 1.3f, 1.3f, 1.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.2f, 1.3f,                     // Idols, Texts, Relics
                1.0f, 1.4f, 1.4f, 1.5f,              // Slag, Lubricants, Coolant, SpareParts
                1.5f, 1.5f, 1.6f                      // Slugs, Cells, Rockets
            ],
            // Ruined - extreme premiums on everything
            [
                2.0f, 2.5f, 2.2f, 0.5f, 2.0f, 2.3f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                2.0f, 1.8f, 1.6f,                     // Air, Water, Rations
                1.5f, 1.4f, 1.4f, 1.5f, 1.6f, 1.5f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.5f, 1.5f, 1.5f, 1.6f, 1.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.8f, 1.6f, 1.4f, 1.5f, 1.6f, 1.7f, 1.5f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.4f, 1.5f, 1.5f, 1.6f, 1.7f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.4f, 1.3f, 1.5f,                     // Idols, Texts, Relics
                1.0f, 1.6f, 1.7f, 1.8f,              // Slag, Lubricants, Coolant, SpareParts
                1.8f, 1.9f, 2.0f                      // Slugs, Cells, Rockets
            ],
        ];

        public static EArray<EncounterType, EArray<SegmentKind, float>> EncounterSegmentKindMarkup = [
            // Power, Traction, Offense, Defense, Industry, Storage, Harvest, Habitat
            [1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f], // None
            [1.1f, 1.2f, 1.0f, 1.1f, 1.0f, 1.0f, 1.2f, 0.9f], // Crossroads
            [0.9f, 1.0f, 0.8f, 0.9f, 0.85f, 0.9f, 0.7f, 0.7f], // Settlement - cheap habitat
            [1.3f, 1.5f, 1.4f, 1.2f, 0.8f, 1.1f, 0.6f, 1.5f], // Resource - expensive habitat
            [1.4f, 1.3f, 1.6f, 1.5f, 1.3f, 1.4f, 1.5f, 1.6f], // Hazard
        ];

        public static EArray<TerrainType, EArray<SegmentKind, float>> LocationSegmentKindMarkup = [
            // Power, Traction, Offense, Defense, Industry, Storage, Harvest, Habitat
            [1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f], // Flat
            [1.1f, 1.2f, 1.0f, 1.1f, 1.1f, 1.1f, 0.9f, 1.1f], // Rough
            [1.2f, 1.4f, 1.1f, 1.2f, 1.2f, 1.2f, 0.85f, 1.2f], // Broken
            [1.3f, 1.7f, 1.2f, 1.3f, 1.3f, 1.3f, 0.8f, 1.3f], // Shattered
            [1.5f, 2.0f, 1.4f, 1.5f, 1.5f, 1.5f, 0.75f, 1.5f], // Ruined
        ];

        public static float LocalMarkup(Commodity commodity, Location location) {
            return _LocalMarkup(commodity, location) * ScrapInflation(location);
        }
        public static float LocalMarkup(SegmentKind kind, Location location) {
            return _LocalMarkup(kind, location) * ScrapInflation(location);
        }
        public static float _LocalMarkup(Commodity commodity, Location location) {
            return EncounterCommodityMarkup[location.Type][commodity] * TerrainCommodityMarkup[location.Terrain][commodity];
        }
        public static float _LocalMarkup(SegmentKind kind, Location location) {
            return EncounterSegmentKindMarkup[location.Type][kind] * LocationSegmentKindMarkup[location.Terrain][kind];
        }
        public static float ScrapInflation(Location location) => 1 / _LocalMarkup(Commodity.Scrap, location);
    }

    public static float EvilLimit = 10.0f;
    public static int MaxDelay = 5000; // Half decimal hour

    public static float CostAt(this Commodity commodity, Location location) {
        return Commodity.Scrap.Round(commodity.BaseCost() * Economy.LocalMarkup(commodity, location));
    }
    public static float CostAt(this Segment segment, Location location) {
        return Commodity.Scrap.Round(segment.Cost * Economy.LocalMarkup(segment.SegmentKind, location));
    }

    public static TimePoint StartGameTime = new(3126, 0, 0, 0, 0, 0);

    /// <summary>
    /// Convoy system parameters.
    /// </summary>
    public static class Convoy {
        /// <summary>Base cost per km to hire a guard.</summary>
        public static float GuardBaseCostPerKm = 0.5f;

        /// <summary>Minimum wait time at waypoints before convoy departs.</summary>
        public static TimeDuration WaypointWaitTime = TimeDuration.FromMinutes(15);

        /// <summary>Maximum time to wait for straggling convoy members.</summary>
        public static TimeDuration MaxWaitForMembers = TimeDuration.FromHours(1);

        /// <summary>How long risk events remain relevant.</summary>
        public static TimeDuration RiskEventMaxAge = TimeDuration.FromDays(7);

        /// <summary>Route risk threshold at which NPCs consider forming convoys.</summary>
        public static float ConvoyFormationRiskThreshold = 0.3f;

        /// <summary>Minimum positive relation required to join another's convoy.</summary>
        public static float MinRelationToJoin = 0.0f;

        /// <summary>Cost multiplier for risk intel license.</summary>
        public static float RiskIntelLicenseCost = 500f;

        /// <summary>Duration of each transit step (5 minutes).</summary>
        public static TimeDuration TransitStepDuration = TimeDuration.FromMinutes(5);

        /// <summary>Time to process joining a convoy.</summary>
        public static TimeDuration JoinConvoyTime = TimeDuration.FromMinutes(1);

        /// <summary>Time to hire a guard.</summary>
        public static TimeDuration HireGuardTime = TimeDuration.FromMinutes(5);

        /// <summary>Time to purchase risk intel.</summary>
        public static TimeDuration BuyIntelTime = TimeDuration.FromMinutes(5);

        /// <summary>How often guards refresh at settlements.</summary>
        public static TimeDuration GuardRefreshInterval = TimeDuration.FromDays(1);

        /// <summary>Population required per potential guard.</summary>
        public static int PopulationPerGuard = 100;

        /// <summary>Maximum guards available at any settlement.</summary>
        public static int MaxGuardsPerSettlement = 3;

        /// <summary>Guard wealth multiplier relative to location.</summary>
        public static float GuardWealthMultiplier = 0.4f;

        /// <summary>Fraction of guard wealth allocated to segments.</summary>
        public static float GuardSegmentWealthFraction = 0.7f;

        /// <summary>Supply days for generated guards.</summary>
        public static float GuardSupplyDays = 10f;

        /// <summary>Risk contribution to guard hire cost.</summary>
        public static float RiskCostMultiplier = 0.5f;

        /// <summary>Guard quality contribution to hire cost.</summary>
        public static float QualityCostMultiplier = 0.01f;

        /// <summary>Threshold multiplier for high cargo when forming convoys.</summary>
        public static float HighCargoFormThresholdMultiplier = 0.8f;

        /// <summary>Base chance to leave convoy due to independence.</summary>
        public static float IndependenceLeaveChance = 0.02f;
    }

    /// <summary>
    /// Harvester NPC AI parameters.
    /// </summary>
    public static class Harvester {
        /// <summary>Minimum cargo fullness before seeking a settlement to sell.</summary>
        public static float SellThreshold = 0.7f;

        /// <summary>Maximum distance to search for resources.</summary>
        public static float MaxSearchRadius = 500f;

        /// <summary>How long to wait when no resources are found.</summary>
        public static TimeDuration IdleWaitDuration = TimeDuration.FromHours(1);
    }

    /// <summary>
    /// Trader NPC AI parameters.
    /// </summary>
    public static class Trader {
        /// <summary>Maximum age of price information to consider reliable.</summary>
        public static TimeDuration MaxPriceAge = TimeDuration.FromDays(3);

        /// <summary>Minimum profit margin to consider a trade worthwhile.</summary>
        public static float MinProfitMargin = 0.15f;

        /// <summary>Chance to explore when no good trades are found.</summary>
        public static float ExploreChance = 0.3f;

        /// <summary>Maximum quantity to trade in a single transaction.</summary>
        public static float MaxTradeQuantity = 100f;

        /// <summary>Cargo value threshold for high-value cargo decisions.</summary>
        public static float HighValueCargoThreshold = 1000f;

        /// <summary>How long to wait when no trade opportunities are found.</summary>
        public static TimeDuration IdleWaitDuration = TimeDuration.FromHours(1);

        /// <summary>How often trade offers/prices are recalculated.</summary>
        public static TimeDuration PriceRecalcInterval = TimeDuration.FromHours(2);
    }

    public static class Manufacturing {
        /// <summary>Manufacturing time per segment size tier (hours).</summary>
        public static float HoursPerSize = 4.0f;

        /// <summary>Upgrade base time (hours).</summary>
        public static float UpgradeBaseHours = 2.0f;

        /// <summary>Settlement markup for manufacturing services.</summary>
        public static float SettlementMarkup = 1.3f;

        /// <summary>Self-service markup (player with own fabricator).</summary>
        public static float SelfServiceMarkup = 1.0f;

        /// <summary>Settlement default industry size (can manufacture segments up to this - 2).</summary>
        public static int SettlementIndustrySize = 7;

        /// <summary>Capital settlement industry size (larger settlements).</summary>
        public static int CapitalIndustrySize = 9;
    }

    /// <summary>
    /// Production AI and consumption parameters.
    /// </summary>
    public static class Production {
        /// <summary>Value of reactor charge in scrap terms (for cost calculations).</summary>
        public static float ChargeValue = 0.5f;

        /// <summary>Weight multiplier for stock deficit bonus in recipe scoring.</summary>
        public static float StockDeficitWeight = 2.0f;

        /// <summary>Minimum profit margin (fraction) to consider a recipe viable.</summary>
        public static float MinProfitMargin = 0.1f;
    }

    /// <summary>
    /// Settlement population consumption parameters.
    /// </summary>
    public static class Settlement {
        /// <summary>Rations consumed per population per hour.</summary>
        public static float RationsPerPopPerHour = 0.001f;

        /// <summary>Water consumed per population per hour.</summary>
        public static float WaterPerPopPerHour = 0.0005f;

        /// <summary>Medicines consumed per population per hour.</summary>
        public static float MedicinesPerPopPerHour = 0.0001f;

        /// <summary>Consumer goods consumed per population per hour (distributed across categories).</summary>
        public static float GoodsPerPopPerHour = 0.00005f;
    }

    /// <summary>
    /// Priority scale: 100 (somewhat) -> 200 (desirable) -> 300 (very) -> 500 (critical) -> 1000 (life/death)
    /// </summary>
    public static class EventPriority {
        // Context calculation weights
        public static float DamageWeight = 0.4f;
        public static float VulnerabilityWeight = 0.4f;
        public static float CargoValueWeight = 0.2f;
        public static float ThreatBonus = 0.15f;

        // Normalization caps
        public static float CargoValueCap = 10000f;
        public static float TransactionValueCap = 2000f;

        // Survival/Flee priorities (300-1000)
        public static int FleeBase = 300;
        public static int FleeScale = 700;
        public static int SurvivalMax = 1000;

        // Combat priorities (200-600)
        public static int CombatBase = 200;
        public static int CombatScale = 400;
        public static int CombatMax = 600;

        // Extortion priorities (200-500)
        public static int ExtortionBase = 200;
        public static int ExtortionScale = 300;
        public static int ExtortionMax = 500;

        // Trade priorities (100-300)
        public static int TradeBase = 100;
        public static int TradeScale = 200;
        public static int TradeMax = 300;

        // Travel priorities (50-150)
        public static int TravelBase = 50;
        public static int TravelScale = 100;
        public static int TravelMax = 150;

        // Convoy priorities (200-500)
        public static int ConvoyBase = 200;
        public static int ConvoyScale = 300;
        public static int ConvoyMax = 500;

        // Guard priorities (300-700)
        public static int GuardBase = 300;
        public static int GuardScale = 400;
        public static int GuardMax = 700;
    }
}

// Power scaling for segments uses tiers or levels for each value which map onto the
// actual value. The ExpPowerScaling uses a baseline of tier 1
public record PowerScaling(float Min, Tier Tier, string badName, string goodName) {
    float numSizes = 5;
    float numUpgrades = 3;
    public const int NA = -1000;
    public float Value(float size, float quality) {
        double l = Math.Exp((size - 1) * Math.Log(Tier.Size) / (numSizes - 1));
        double u = Math.Exp(quality * Math.Log(Tier.Quality) / numUpgrades);
        double Y = Min * l * u;
        return ( float ) Y;
    }
    public float this[Tier tier] => tier.Size <= NA / 2.0f ? 0 : Value(tier.Size, tier.Quality);
}

public record struct Tier(float Size, float Quality) {
    public Tier(float size): this(size, 0) { }
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
