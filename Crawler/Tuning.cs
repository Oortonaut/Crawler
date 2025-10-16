namespace Crawler;

public static partial class Tuning {
    public static class Game {
        public static float LootReturn = 0.5f;

        // Hazard encounter risk/reward parameters
        public static float hazardPayoffFraction = 0.01f;
        public static float hazardNegativePayoffChance = 0.3333f;
        public static float hazardNegativePayoffRatio = 1.5f; // negated, relative to payoff fraction

        // Resource encounter parameters
        public static float resourcePayoffFraction = 0.05f; // Fraction of location wealth
    }

    public static class Encounter {
        public static EArray<EncounterType, float> HourlyArrivals = [0, 0.125f, 0.4f, 0.08f, 0.04f];
        // Faction spawn weights by terrain type: Player, Bandit, Trade
        public static EArray<TerrainType, EArray<Faction, float>> crawlerSpawnWeight = [
            [0, 1, 6],    // Flat - more trade
            [0, 2, 4],    // Rough - balanced
            [0, 3, 3],    // Broken - equal mix
            [0, 4, 2],    // Shattered - more bandits
            [0, 5, 1],    // Ruined - mostly bandits
        ];
        public static float DynamicCrawlerLifetimeExpectation = 3600.0f * 7.5f;
    }
    public static class Trade {
        public static float rate = 1.2f;
        public static float sd = 0.1f;
        public static float hiked = rate * 1.2f;
        public static float sale = rate * 0.9f;
        public static float repairMarkup = 2.0f;
        public static float repairMarkupSd = 0.3f;
        public static float commodityBundleCost = 40.0f;

        // Bandit markup settings
        public static float banditRate = 2.0f;          // Higher markup for bandits
        public static float banditSd = 0.5f;            // More variance
        public static float banditHostilityThreshold = 5.0f; // Evilness threshold for hostility check
        public static float banditHostilityChance = 0.3f;    // Base chance of turning hostile at threshold
    }

    public static class Crawler {
        public static float MoraleAdjCrewLoss = 0.5f;
        public static float StandbyFraction = 0.007f;
        public static float FuelPerKm = 0.002f;
        public static float RationsPerCrewDay = 1.0f;
        public static float WagesPerCrewDay = 0.1f;
        public static float MoraleTakeAttack = -1;
        public static float MoraleHostileDestroyed = 2;
        public static float MoraleFriendlyDestroyed = -2;
        public static float MoraleSurrendered = -1;
        public static float MoraleSurrenderedTo = 3;

        // Commodity weights by faction for crawler inventory generation
        public static EArray<Faction, EArray<Commodity, float>> CommodityWeights = [
            // Player - balanced loadout
            [
                1.0f, 1.0f, 1.0f, 1.0f, 0.5f, 0.3f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                0.2f, 0.5f, 1.0f,                     // Air, Water, Rations
                0.3f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.4f, 0.4f, 0.6f, 0.7f, 0.4f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.6f, 0.4f, 0.3f, 0.3f, 0.5f, 0.5f, 0.3f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                0.2f, 0.1f, 0.1f, 0.05f, 0.3f,       // Liquor, Stims, Downers, Trips, SmallArms
                0.2f, 0.2f, 0.1f                      // Idols, Texts, Relics
            ],
            // Bandit - combat focused, less crew support
            [
                1.5f, 0.8f, 0.8f, 0.6f, 0.2f, 0.8f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                0.1f, 0.3f, 0.7f,                     // Air, Water, Rations
                0.2f, 0.4f, 0.2f, 0.5f, 0.4f, 0.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.3f, 0.3f, 0.6f, 0.5f, 0.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.3f, 0.3f, 0.5f, 0.2f, 0.4f, 0.3f, 0.2f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                0.5f, 0.6f, 0.4f, 0.3f, 0.9f,        // Liquor, Stims, Downers, Trips, SmallArms - vice & weapons
                0.1f, 0.1f, 0.2f                      // Idols, Texts, Relics
            ],
            // Trade - high on trade goods, low on combat
            [
                2.0f, 1.2f, 0.9f, 0.8f, 0.8f, 0.3f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                0.5f, 0.8f, 1.2f,                     // Air, Water, Rations - life support for trade
                0.8f, 0.9f, 0.7f, 1.0f, 1.0f, 0.8f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 0.9f, 1.2f, 0.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.2f, 1.0f, 0.8f, 0.9f, 1.1f, 1.2f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                0.7f, 0.3f, 0.2f, 0.1f, 0.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                0.6f, 0.7f, 0.4f                      // Idols, Texts, Relics
            ],
        ];
    }

    public static class Segments {
        // Approximate sizes: (in  m)  10 14 20 28 40
        public static PowerScaling LengthTiers = new PowerScaling(10.0f, new(4, 1.2f), "short", "long");

        // Base segment parameters
        public static PowerScaling WeightTiers = new PowerScaling(30.0f, new(20, 0.9f), "hefty", "lightened");
        public static PowerScaling CostTiers = new PowerScaling(300.0f, new(100, 2.5f), "cheat", "expensive");
        public static PowerScaling MaxHitsTiers = new PowerScaling(2.0f, new(12, 2.0f), "decrepit", "hardened");

        // Weapon parameters
        public static PowerScaling DamageTiers = new PowerScaling(3.0f, new(9, 2.0f), "weak", "strong");
        public static PowerScaling RateTiers = new PowerScaling(1.5f, new(0.4f, 1.5f), "slow", "fast");
        public static PowerScaling VolleyTiers = new PowerScaling(1.0f, new(3.0f, 2.0f), "limited", "repeating");
        public static PowerScaling AimTiers = new PowerScaling(0.5f, new Tier(0.75f, 1.5f), "inaccurate", "accurate");

        // Power parameters
        public static PowerScaling DrainTiers = new PowerScaling(0.5f, new(6.0f, 0.75f), "wasteful", "efficient");
        public static PowerScaling CapacityTiers = new PowerScaling(2, new(6.0f, 2.0f), "small", "large");
        public static PowerScaling GenerationTiers = new PowerScaling(1.5f, new(6.0f, 2.0f), "weak", "strong");
        public static PowerScaling ChargerTiers = new PowerScaling(3.0f, new(10, 2.0f), "slow", "fast");
        
        // Traction parameters
        public static PowerScaling LiftTiers = new PowerScaling(150, new (12, 1.5f), "weak", "strong");
        public static PowerScaling SpeedTiers = new PowerScaling(30, new (8, 1.2f), "slow", "fast");
        
        // Defense parameters
        public static PowerScaling ReductionTiers = new PowerScaling(1.5f, new(7, 2.5f), "weak", "strong");
        public static PowerScaling MitigationTiers = new PowerScaling(0.75f, new(0.4f, 0.7f), "weak", "strong");
        public static PowerScaling ShieldCapacityTiers = new PowerScaling(2.0f, new(15, 1.3f), "weak", "strong");
        public static PowerScaling ShieldChargeTiers = new PowerScaling(1.0f, new(4, 1.2f), "slow", "fast");
    }

    public static class Economy {
        public static EArray<Commodity, float> BaseCommodityValues = [
            1,      // Scrap
            1.25f,  // Fuel
            4.0f,   // Crew
            15f,    // Morale
            3.5f,   // Passengers
            5.0f,   // Soldiers

            // Life support
            0.5f,   // Air
            0.3f,   // Water
            1.5f,   // Rations

            // Raw materials
            0.8f,   // Biomass
            0.6f,   // Ore
            0.5f,   // Silicates
            // refined
            1.2f,   // Metal
            1.5f,   // Chemicals
            1.0f,   // Glass
            // Parts
            1.8f,   // Ceramics
            2.0f,   // Polymers
            2.5f,   // Alloys
            4.0f,   // Electronics
            3.0f,   // Explosives

            // Consumer Goods
            8.0f,   // Medicines
            2.5f,   // Textiles
            20.0f,  // Gems - high value
            1.5f,   // Toys
            5.0f,   // Machines
            6.0f,   // Computers
            2.0f,   // Media

            // Vice & Contraband
            3.0f,   // Liquor
            12.0f,  // Stims - high value
            10.0f,  // Downers - high value
            18.0f,  // Trips - very high value
            4.5f,   // SmallArms

            // Religious items
            6.0f,   // Idols
            4.0f,   // Texts
            15.0f,  // Relics - high value
        ];

        public static EArray<EncounterType, EArray<Commodity, float>> EncounterCommodityMarkup = [
            // None - all commodities at base price
            [
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.0f, 1.0f, 1.0f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Crossroads - cheap scrap, fuel premium
            [
                0.9f, 1.2f, 1.3f, 1.0f, 1.2f, 1.4f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.1f, 1.0f, 1.1f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.1f, 1.0f, 1.2f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.9f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                0.9f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 0.9f, 1.1f                      // Idols, Texts, Relics
            ],
            // Settlement - cheap basics, morale services, manufactured goods
            [
                0.8f, 0.9f, 0.9f, 1.2f, 0.9f, 1.0f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                0.8f, 0.7f, 0.8f,                     // Air, Water, Rations - cheap life support
                0.9f, 1.0f, 1.0f, 1.0f, 0.9f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                0.9f, 0.9f, 1.0f, 0.9f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                0.8f, 0.8f, 0.9f, 0.8f, 0.9f, 0.9f, 0.8f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.0f, 1.3f, 1.3f, 1.3f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms - vice taxed
                0.9f, 0.8f, 1.0f                      // Idols, Texts, Relics
            ],
            // Resource - expensive scrap/crew, cheap raw materials
            [
                2.0f, 0.7f, 2.5f, 0.8f, 2.0f, 2.2f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.2f, 1.0f, 1.4f,                     // Air, Water, Rations
                0.6f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass - cheap raw
                0.9f, 0.9f, 0.8f, 1.1f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.4f, 1.3f, 1.2f, 1.3f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.1f, 1.2f, 1.2f, 1.2f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.3f, 1.4f                      // Idols, Texts, Relics
            ],
            // Hazard - everything expensive, low morale, desperate trades
            [
                1.5f, 1.8f, 3.0f, 0.6f, 2.5f, 2.8f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.5f, 1.4f, 2.0f,                     // Air, Water, Rations - life support premium
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.6f, 1.4f, 1.5f, 1.4f, 1.5f, 1.5f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.2f, 1.4f, 1.5f, 1.6f, 1.3f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.3f, 1.2f, 1.5f                      // Idols, Texts, Relics
            ],
        ];
        public static EArray<TerrainType, EArray<Commodity, float>> TerrainCommodityMarkup = [
            // Flat - baseline prices
            [
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.0f, 1.0f, 1.0f,                     // Air, Water, Rations
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.0f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Rough - fuel premium, morale harder
            [
                1.1f, 1.2f, 1.1f, 0.9f, 1.1f, 1.1f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.1f, 1.1f, 1.1f,                     // Air, Water, Rations
                1.1f, 1.0f, 1.0f, 1.1f, 1.1f, 1.1f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.1f, 1.1f, 1.1f, 1.1f, 1.1f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.1f, 1.0f, 1.0f, 1.0f, 1.1f, 1.1f, 1.0f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.0f, 1.0f, 1.0f, 1.0f, 1.1f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.0f, 1.0f, 1.0f                      // Idols, Texts, Relics
            ],
            // Broken - logistics costs
            [
                1.3f, 1.5f, 1.4f, 0.8f, 1.3f, 1.4f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.3f, 1.3f, 1.2f,                     // Air, Water, Rations
                1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.2f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.2f, 1.2f, 1.2f, 1.2f, 1.3f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.3f, 1.2f, 1.1f, 1.1f, 1.2f, 1.2f, 1.1f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.1f, 1.1f, 1.1f, 1.1f, 1.2f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.1f, 1.1f, 1.2f                      // Idols, Texts, Relics
            ],
            // Shattered - fuel very expensive, harsh conditions
            [
                1.5f, 2.0f, 1.8f, 0.7f, 1.6f, 1.8f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                1.6f, 1.5f, 1.4f,                     // Air, Water, Rations
                1.3f, 1.2f, 1.2f, 1.3f, 1.4f, 1.3f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.3f, 1.3f, 1.3f, 1.4f, 1.5f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.5f, 1.4f, 1.2f, 1.3f, 1.4f, 1.4f, 1.3f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
                1.2f, 1.3f, 1.3f, 1.3f, 1.4f,        // Liquor, Stims, Downers, Trips, SmallArms
                1.2f, 1.2f, 1.3f                      // Idols, Texts, Relics
            ],
            // Ruined - extreme premiums on everything
            [
                2.0f, 2.5f, 2.2f, 0.5f, 2.0f, 2.3f,  // Scrap, Fuel, Crew, Morale, Passengers, Soldiers
                2.0f, 1.8f, 1.6f,                     // Air, Water, Rations
                1.5f, 1.4f, 1.4f, 1.5f, 1.6f, 1.5f,  // Biomass, Ore, Silicates, Metal, Chemicals, Glass
                1.5f, 1.5f, 1.5f, 1.6f, 1.8f,        // Ceramics, Polymers, Alloys, Electronics, Explosives
                1.8f, 1.6f, 1.4f, 1.5f, 1.6f, 1.7f, 1.5f, // Medicines, Textiles, Gems, Toys, Machines, Computers, Media
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

    public static float Value(this Commodity commodity, Location location) {
        return Economy.BaseCommodityValues[commodity] * Economy.LocalMarkup(commodity, location);
    }
}
