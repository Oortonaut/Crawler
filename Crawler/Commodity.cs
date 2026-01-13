namespace Crawler;

public enum Commodity {
    Scrap, // Cash
    Fuel,
    Crew,
    Morale,
    Isotopes, // mid-game raw material
    Nanomaterials, // late-game refined material

    // Life support
    Air,
    Water,
    Rations,

    // Raw materials
    Biomass,
    Ore,
    Silicates,

    // refined
    Metal,
    Chemicals,
    Glass,

    // Parts
    Ceramics,
    Polymers,
    Alloys,
    Electronics,
    Explosives,

    // Consumer Goods
    Medicines,
    Textiles,
    Gems,
    Toys,
    Machines,
    AiCores,
    Media,

    // Vice & Contraband
    Liquor,
    Stims,
    Downers,
    Trips,
    SmallArms,

    // Religious items
    Idols,
    Texts,
    Relics,

    // Production intermediates
    Slag,        // Waste from refining
    Lubricants,  // Maintenance consumable
    Coolant,     // Industry operation
    SpareParts,  // Generic maintenance

    // Ammunition (manufactured from Parts)
    Slugs,       // Gun ammo
    Cells,       // Laser ammo
    Rockets,     // Missile ammo
}

public enum GameTier {
    None,
    Early,
    Mid,
    Late
}

[Flags]
public enum CommodityFlag {
    None = 0,
    Perishable = 1 << 0, // Requires power/refrigeration
    Bulky = 1 << 1, // High volume relative to value
    Integral = 1 << 2, // Display as integer (no decimal places)
    Ammunition = 1 << 3, // Consumed by weapons
    Industrial = 1 << 4, // Required for production processes
    Waste = 1 << 5, // Production byproduct
}

public enum CommodityCategory {
    Essential,
    Raw,
    Refined,
    Parts,
    Consumer,
    Luxury,
    Vice,
    Dangerous,
    Religious,
    Industrial,  // Production consumables (lubricants, coolant, spare parts)
    Ammunition,  // Weapon consumables (slugs, cells, rockets)
    Waste,       // Production byproducts (slag)
}

public record struct CommodityData(float BaseValue, float Volume, float Mass, CommodityFlag Flag, GameTier Tier, CommodityCategory Cat);
public static class CommodityEx {
    // Volume in cubic meters per unit, Weight in tons per unit, Flags, Tier
    public static EArray<Commodity, CommodityData> Data = [
        new CommodityData(1, 0.001f, 0.005f, CommodityFlag.None, GameTier.Early, CommodityCategory.Essential), // Scrap
        new CommodityData(7.5f, 0.0002f, 0.001f, CommodityFlag.None, GameTier.Early, CommodityCategory.Essential), // Fuel
        new CommodityData(60.0f, 1.0f, 0.08f, CommodityFlag.Integral, GameTier.Early, CommodityCategory.Essential), // Crew
        new CommodityData(1.5f, 0.0f, 0.0f, CommodityFlag.Integral, GameTier.Early, CommodityCategory.Essential), // Morale
        new CommodityData(50f, 1.0f, 1.5f, CommodityFlag.Bulky, GameTier.Mid, CommodityCategory.Raw), // Isotopes
        new CommodityData(200f, 0.05f, 0.03f, CommodityFlag.None, GameTier.Late, CommodityCategory.Refined), // Nanomaterials

        // Life support
        new CommodityData(11.5f, 0.1f, 0.087f, CommodityFlag.None, GameTier.Early, CommodityCategory.Essential), // Air
        new CommodityData(33f, 1.0f, 1.0f, CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Essential), // Water
        new CommodityData(150f, 0.5f, 0.1f, CommodityFlag.Perishable, GameTier.Early, CommodityCategory.Essential), // Rations

        // Raw materials
        new CommodityData(10f, 1.0f, 0.5f, CommodityFlag.Perishable | CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Raw), // Biomass
        new CommodityData(15f, 1.0f, 3.0f, CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Raw), // Ore
        new CommodityData(12f, 1.0f, 2.0f, CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Raw), // Silicates

        // refined
        new CommodityData(50f, 0.25f, 0.25f, CommodityFlag.None, GameTier.Early, CommodityCategory.Refined), // Metal
        new CommodityData(40f, 0.25f, 0.075f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Refined), // Chemicals
        new CommodityData(35f, 0.25f, 0.065f, CommodityFlag.None, GameTier.Early, CommodityCategory.Refined), // Glass

        // Parts (masses based on recipe inputs)
        new CommodityData(150f, 0.1f, 0.2f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Parts), // Ceramics
        new CommodityData(150f, 0.1f, 0.15f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Parts), // Polymers
        new CommodityData(250f, 0.1f, 0.5f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Parts), // Alloys
        new CommodityData(300f, 0.1f, 0.3f, CommodityFlag.None, GameTier.Late, CommodityCategory.Parts), // Electronics
        new CommodityData(200f, 0.1f, 0.25f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Dangerous), // Explosives

        // Consumer Goods
        new CommodityData(25f, 0.001f, 0.0005f, CommodityFlag.Perishable, GameTier.Mid, CommodityCategory.Consumer), // Medicines
        new CommodityData(200f, 1.0f, 0.2f, CommodityFlag.None, GameTier.Early, CommodityCategory.Luxury), // Textiles
        new CommodityData(800f, 0.001f, 0.002f, CommodityFlag.None, GameTier.Late, CommodityCategory.Luxury), // Gems
        new CommodityData(180f, 1.0f, 0.4f, CommodityFlag.None, GameTier.Early, CommodityCategory.Consumer), // Toys
        new CommodityData(2000f, 1.0f, 0.75f, CommodityFlag.None, GameTier.Late, CommodityCategory.Consumer), // Machines
        new CommodityData(5000f, 0.025f, 0.04f, CommodityFlag.Integral, GameTier.Late, CommodityCategory.Dangerous), // AiCores
        new CommodityData(200f, 0.001f, 0.00035f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Luxury), // Media

        // Vice & Contraband
        new CommodityData(3.75f, 0.01f, 0.01f, CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Vice), // Liquor
        new CommodityData(80f, 0.001f, 0.0005f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Vice), // Stims
        new CommodityData(70f, 0.001f, 0.0005f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Vice), // Downers
        new CommodityData(1200f, 0.001f, 0.0006f, CommodityFlag.None, GameTier.Late, CommodityCategory.Vice), // Trips
        new CommodityData(35f, 0.01f, 0.0075f, CommodityFlag.None, GameTier.Mid, CommodityCategory.Dangerous), // SmallArms

        // Religious items
        new CommodityData(8f, 0.003f, 0.004f, CommodityFlag.Integral, GameTier.Early, CommodityCategory.Religious), // Idols
        new CommodityData(25f, 0.005f, 0.002f, CommodityFlag.Integral, GameTier.Mid, CommodityCategory.Religious), // Texts
        new CommodityData(500f, 0.002f, 0.003f, CommodityFlag.Integral, GameTier.Late, CommodityCategory.Religious), // Relics

        // Production intermediates
        new CommodityData(5f, 1.0f, 2.0f, CommodityFlag.Waste | CommodityFlag.Bulky, GameTier.Early, CommodityCategory.Waste), // Slag
        new CommodityData(50f, 0.05f, 0.05f, CommodityFlag.Industrial, GameTier.Early, CommodityCategory.Industrial), // Lubricants
        new CommodityData(75f, 0.1f, 0.12f, CommodityFlag.Industrial, GameTier.Mid, CommodityCategory.Industrial), // Coolant
        new CommodityData(100f, 0.5f, 0.3f, CommodityFlag.Industrial, GameTier.Mid, CommodityCategory.Industrial), // SpareParts

        // Ammunition
        new CommodityData(15f, 0.01f, 0.02f, CommodityFlag.Ammunition, GameTier.Early, CommodityCategory.Ammunition), // Slugs
        new CommodityData(80f, 0.02f, 0.015f, CommodityFlag.Ammunition, GameTier.Mid, CommodityCategory.Ammunition), // Cells
        new CommodityData(250f, 0.1f, 0.08f, CommodityFlag.Ammunition, GameTier.Mid, CommodityCategory.Ammunition), // Rockets
    ];

    public static CommodityFlag Flags(this Commodity commodity) => Data[commodity].Flag;
    public static CommodityCategory Category(this Commodity commodity) => Data[commodity].Cat;
    public static bool IsPerishable(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Perishable);
    public static bool IsBulky(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Bulky);
    public static bool IsEssential(this Commodity commodity) => commodity.Category() == CommodityCategory.Essential;
    public static bool IsIntegral(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Integral);
    public static bool IsAmmunition(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Ammunition);
    public static bool IsIndustrial(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Industrial);
    public static bool IsWaste(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Waste);
    public static float BaseCost(this Commodity commodity) => Data[commodity].BaseValue;
    public static GameTier Tier(this Commodity commodity) => Data[commodity].Tier;
    public static float Mass(this Commodity commodity) => Data[commodity].Mass;
    public static float Volume(this Commodity commodity) => Data[commodity].Volume;
    public static float AvailabilityAt(this Commodity commodity, Location location) {
        float commodityTech =  (float)commodity.Tier() - 1;

        return 1 - Unavailability.Value(location.Population / 100, location.TechLatitude * 2 - commodityTech);
    }
    public static PowerScaling Unavailability = new(0.7f, new(0.15f, 0.3f), "primitive", "tech");
    public static float Round(this Commodity commodity, float value) {
        if (commodity.IsIntegral()) {
            return ( float ) Math.Round(value);
        } else {
            return ( float ) Math.Round(value * 512) / 512;
        }
    }
}
