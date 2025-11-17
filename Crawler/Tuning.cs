namespace Crawler;

public static partial class Tuning {
    public static class Game {
        public static float LootReturn = 0.5f;

        // Hazard encounter risk/reward parameters
        public static float hazardWealthFraction = 0.5f;
        public static float hazardNegativePayoffChance = 0.3333f;
        public static float hazardNegativePayoffRatio = 1.5f; // negated, relative to wealth fraction

        // Resource encounter parameters
        public static float resourcePayoffFraction = 0.05f; // Fraction of location wealth
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
        public static EArray<EncounterType, float> HourlyArrivals = [0, 0.125f, 0.4f, 0.08f, 0.04f];
        // Faction spawn weights by terrain type: Player, Bandit, Trade
        public static EArray<TerrainType, EArray<Faction, float>> crawlerSpawnWeight = [
            [0, 2, 12],    // Flat - more trade
            [0, 4, 8],    // Rough - balanced
            [0, 6, 6],    // Broken - equal mix
            [0, 8, 4],    // Shattered - more bandits
            [0, 10, 2],    // Ruined - mostly bandits
        ];
        public static float DynamicCrawlerLifetimeExpectation = 3600.0f * 7.5f;
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
        public static EArray<Faction, Policy> Policies = new();

        // Initialize default policies for core factions
        static FactionPolicies() {
            InitializeCoreFactionPolicies();
        }

        static void InitializeCoreFactionPolicies() {
            // Player - permissive for everything
            Policies[Faction.Player] = CreateDefaultPolicy(TradePolicy.Legal);

            // Bandit - sell everything (including contraband)
            Policies[Faction.Bandit] = CreateDefaultPolicy(TradePolicy.Legal);

            // Independent - legitimate merchants, restrict dangerous goods
            var tradeCommodityPolicy = CreateCommodityDefaultPolicy(TradePolicy.Legal);
            tradeCommodityPolicy[CommodityCategory.Vice] = TradePolicy.Prohibited;
            tradeCommodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Taxed;

            var tradeSegmentPolicy = CreateSegmentDefaultPolicy(TradePolicy.Legal);
            tradeSegmentPolicy[SegmentKind.Offense] = TradePolicy.Taxed;

            Policies[Faction.Independent] = new Policy(tradeCommodityPolicy, tradeSegmentPolicy, "Independent");
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

        public static TradePolicy GetPolicy(Faction faction, Commodity commodity) {
            return GetPolicy(faction, commodity.Category());
        }

        public static TradePolicy GetPolicy(Faction faction, CommodityCategory category) => Policies[faction].Commodities[category];
        public static TradePolicy GetPolicy(Faction faction, SegmentKind kind) => Policies[faction].Segments[kind];
    }

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
        public static float WaterRecyclingLossPerHour = 0.002f; // 0.2% per hour

        public static float AirPerPerson = 1.0f; // liters of liquid air per person
        public static float AirLeakagePerDamagedSegment = 0.005f; // 0.5% per hour per damaged segment

        public static EArray<Commodity, float> DefaultCommodityWeights => [
            1.0f, 1.0f, 1.0f, 1.0f, 0.4f, 0.6f, // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
            0.2f, 0.5f, 1.0f, // Air, Water, Rations
            0.3f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Biomass, Ore, Silicates, Metal, Chemicals, Glass
            0.4f, 0.4f, 0.6f, 0.7f, 0.4f, // Ceramics, Polymers, Alloys, Electronics, Explosives
            0.6f, 0.4f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
            0.2f, 0.1f, 0.1f, 0.05f, 0.3f, // Liquor, Stims, Downers, Trips, SmallArms
            0.2f, 0.2f, 0.1f // Idols, Texts, Relics
        ];

        // Commodity weights by faction for crawler inventory generation
        public static EArray<Faction, EArray<Commodity, float>> CommodityWeights = [
            // Player - balanced loadout
            [
                1.0f, 1.0f, 1.0f, 1.0f, 0.4f, 0.6f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.2f, 0.5f, 1.0f,                     // Air, Water, Rations
                0.3f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.4f, 0.4f, 0.6f, 0.7f, 0.4f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.6f, 0.4f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.2f, 0.1f, 0.1f, 0.05f, 0.3f,       // Liquor, Stims, Downers, Trips, SmallArms
                0.2f, 0.2f, 0.1f                      // Idols, Texts, Relics
            ],
            // Bandit - combat focused, less crew support
            [
                1.5f, 0.8f, 0.8f, 0.6f, 0.3f, 0.4f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.1f, 0.3f, 0.7f,                     // Air, Water, Rations
                0.2f, 0.4f, 0.2f, 0.5f, 0.4f, 0.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.3f, 0.3f, 0.6f, 0.5f, 0.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.3f, 0.3f, 0.5f, 0.2f, 0.4f, 0.3f, 0.2f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.5f, 0.6f, 0.4f, 0.3f, 0.9f,        // Liquor, Stims, Downers, Trips, SmallArms - vice & weapons
                0.1f, 0.1f, 0.2f                      // Idols, Texts, Relics
            ],
            // Trade - high on trade goods, low on combat
            [
                2.0f, 1.2f, 0.9f, 0.8f, 0.9f, 0.7f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.5f, 0.8f, 1.2f,                     // Air, Water, Rations - life support for trade
                0.8f, 0.9f, 0.7f, 1.0f, 1.0f, 0.8f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 0.9f, 1.2f, 0.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.2f, 1.0f, 0.8f, 0.9f, 1.1f, 1.2f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.7f, 0.3f, 0.2f, 0.1f, 0.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                0.6f, 0.7f, 0.4f                      // Idols, Texts, Relics
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
        // Approximate sizes: (in  m)  10 14 20 28 40
        public static PowerScaling LengthTiers = new PowerScaling(10.0f, (4, 1.2f), "short", "long");

        // Base segment parameters
        //                                                                30 .. 130 .. 600
        public static PowerScaling WeightTiers = new PowerScaling(30.0f, (20, 0.9f), "hefty", "lightened");
        public static PowerScaling CostTiers = new PowerScaling(300.0f, (50, 3.0f), "cheap", "expensive");
        public static PowerScaling MaxHitsTiers = new PowerScaling(10.0f, (12, 2.0f), "decrepit", "hardened");

        // Weapon parameters
        public static PowerScaling DamageTiers = new PowerScaling(10f, (15, 1.4f), "weak", "strong");
        public static PowerScaling RateTiers = new PowerScaling(3f, (0.65f, 1.4f), "slow", "fast");
        public static PowerScaling ShotsTiers = new PowerScaling(1.0f, (1.0f, 4f), "limited", "repeating");
        public static PowerScaling AimTiers = new PowerScaling(0.75f, (0.65f, 1.5f), "inaccurate", "accurate");

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
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Crossroads - cheap scrap, fuel premium
            [
                0.9f, 1.2f, 1.3f, 1.0f, 1.0f, 1.1f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.1f, 1.0f, 1.1f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.1f, 1.0f, 1.2f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                0.9f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 0.9f, 1.1f                      // Idols, Texts, Relics
            ],
            // Settlement - cheap basics, morale services, manufactured goods
            [
                0.8f, 0.9f, 0.9f, 1.2f, 1.0f, 0.95f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                0.8f, 0.7f, 0.8f,                     // Air, Water, Rations - cheap life support
                0.9f, 1.0f, 1.0f, 1.0f, 0.9f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.9f, 0.9f, 1.0f, 0.9f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.8f, 0.8f, 0.9f, 0.8f, 0.9f, 0.9f, 0.8f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.3f, 1.3f, 1.3f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms - vice taxed
                0.9f, 0.8f, 1.0f                      // Idols, Texts, Relics
            ],
            // Resource - expensive scrap/crew, cheap raw materials
            [
                2.0f, 0.7f, 2.5f, 0.8f, 0.7f, 1.3f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.2f, 1.0f, 1.4f,                     // Air, Water, Rations
                0.6f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass - cheap raw
                0.9f, 0.9f, 0.8f, 1.1f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.4f, 1.3f, 1.2f, 1.3f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.1f, 1.2f, 1.2f, 1.2f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.3f, 1.4f                      // Idols, Texts, Relics
            ],
            // Hazard - everything expensive, low morale, desperate trades
            [
                1.5f, 1.8f, 3.0f, 0.6f, 1.4f, 1.5f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.5f, 1.4f, 2.0f,                     // Air, Water, Rations - life support premium
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.6f, 1.4f, 1.5f, 1.4f, 1.5f, 1.5f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.2f, 1.4f, 1.5f, 1.6f, 1.3f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.3f, 1.2f, 1.5f                      // Idols, Texts, Relics
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
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Rough - fuel premium, morale harder
            [
                1.1f, 1.2f, 1.1f, 0.9f, 1.1f, 1.1f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.1f, 1.1f, 1.1f,                     // Air, Water, Rations
                1.1f, 1.0f, 1.0f, 1.1f, 1.1f, 1.1f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.1f, 1.1f, 1.1f, 1.1f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Broken - logistics costs
            [
                1.3f, 1.5f, 1.4f, 0.8f, 1.3f, 1.4f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.3f, 1.3f, 1.2f,                     // Air, Water, Rations
                1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.2f, 1.2f, 1.2f, 1.2f, 1.3f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.1f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.1f, 1.1f, 1.1f, 1.1f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.1f, 1.1f, 1.2f                      // Idols, Texts, Relics
            ],
            // Shattered - fuel very expensive, harsh conditions
            [
                1.5f, 2.0f, 1.8f, 0.7f, 1.6f, 1.8f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                1.6f, 1.5f, 1.4f,                     // Air, Water, Rations
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.5f, 1.4f, 1.2f, 1.3f, 1.4f, 1.4f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.2f, 1.3f, 1.3f, 1.3f, 1.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.2f, 1.3f                      // Idols, Texts, Relics
            ],
            // Ruined - extreme premiums on everything
            [
                2.0f, 2.5f, 2.2f, 0.5f, 2.0f, 2.3f,  // Scrap, Fuel, Crew, Morale, Isotopes, Nanomaterials
                2.0f, 1.8f, 1.6f,                     // Air, Water, Rations
                1.5f, 1.4f, 1.4f, 1.5f, 1.6f, 1.5f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.5f, 1.5f, 1.5f, 1.6f, 1.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.8f, 1.6f, 1.4f, 1.5f, 1.6f, 1.7f, 1.5f, // Medicines, Textiles, Gems, Toys, Machines, AI, Media
                1.4f, 1.5f, 1.5f, 1.6f, 1.7f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.4f, 1.3f, 1.5f                      // Idols, Texts, Relics
            ],
        ];

        public static EArray<EncounterType, EArray<SegmentKind, float>> EncounterSegmentKindMarkup = [
            // Power, Traction, Offense, Defense
            [1.0f, 1.0f, 1.0f, 1.0f], // None
            [1.1f, 1.2f, 1.0f, 1.1f], // Crossroads - fuel/power premium
            [0.9f, 1.0f, 0.8f, 0.9f], // Settlement - civilian discount on weapons
            [1.3f, 1.5f, 1.4f, 1.2f], // Resource - industrial equipment premium
            [1.4f, 1.3f, 1.6f, 1.5f], // Hazard - combat equipment premium
        ];

        public static EArray<TerrainType, EArray<SegmentKind, float>> LocationSegmentKindMarkup = [
            // Power, Traction, Offense, Defense
            [1.0f, 1.0f, 1.0f, 1.0f], // Flat
            [1.1f, 1.2f, 1.0f, 1.1f], // Rough - traction premium
            [1.2f, 1.4f, 1.1f, 1.2f], // Broken - mobility challenges
            [1.3f, 1.7f, 1.2f, 1.3f], // Shattered - severe terrain penalties
            [1.5f, 2.0f, 1.4f, 1.5f], // Ruined - extreme conditions
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
    public static int MaxDelay = 3600;

    public static float CostAt(this Commodity commodity, Location location) {
        return Commodity.Scrap.Round(commodity.BaseCost() * Economy.LocalMarkup(commodity, location));
    }
    public static float CostAt(this Segment segment, Location location) {
        return Commodity.Scrap.Round(segment.Cost * Economy.LocalMarkup(segment.SegmentKind, location));
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
