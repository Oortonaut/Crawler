namespace Crawler.Production;

/// <summary>
/// Factory methods and static data for segment manufacturing and upgrade recipes.
/// </summary>
public static class SegmentRecipeEx {
    // Helper to create immutable dictionaries more concisely
    static IReadOnlyDictionary<Commodity, float> D(params (Commodity c, float amt)[] items) =>
        items.ToDictionary(x => x.c, x => x.amt);

    static IReadOnlyDictionary<Commodity, float> Empty => D();

    /// <summary>
    /// Creates a manufacturing recipe for the given segment definition.
    /// Material costs scale with segment cost tier.
    /// </summary>
    public static SegmentRecipe CreateRecipe(SegmentDef def) {
        float baseCost = def.Cost;
        int size = (int)def.Size.Size;

        return new SegmentRecipe(
            Name: $"Manufacture {def.NameSize}",
            OutputDef: def,
            Inputs: MaterialsForKind(def.SegmentKind, baseCost),
            Consumables: D(
                (Commodity.Fuel, baseCost * 0.001f),
                (Commodity.Lubricants, baseCost * 0.0002f)
            ),
            Maintenance: D((Commodity.SpareParts, baseCost * 0.0005f)),
            CrewRequired: 2 + size,
            ActivateCharge: 20 * size,
            CycleTime: TimeDuration.FromHours(Tuning.Manufacturing.HoursPerSize * size),
            TechTier: size <= 2 ? GameTier.Early : size <= 4 ? GameTier.Mid : GameTier.Late
        );
    }

    /// <summary>
    /// Generates all segment recipes for base definitions at various sizes.
    /// </summary>
    public static IEnumerable<SegmentRecipe> GenerateAllRecipes() {
        foreach (var baseDef in SegmentEx.AllDefs) {
            // Generate recipes for sizes 1-5
            for (int size = 1; size <= 5; size++) {
                var resized = baseDef.Resize(size);
                yield return CreateRecipe(resized);
            }
        }
    }

    /// <summary>
    /// Gets the material requirements for manufacturing a segment of the given kind.
    /// </summary>
    static IReadOnlyDictionary<Commodity, float> MaterialsForKind(SegmentKind kind, float cost) {
        return kind switch {
            SegmentKind.Offense => D(
                (Commodity.Alloys, cost * 0.003f),
                (Commodity.Electronics, cost * 0.002f),
                (Commodity.Explosives, cost * 0.001f)
            ),
            SegmentKind.Defense => D(
                (Commodity.Alloys, cost * 0.004f),
                (Commodity.Ceramics, cost * 0.002f)
            ),
            SegmentKind.Power => D(
                (Commodity.Alloys, cost * 0.002f),
                (Commodity.Electronics, cost * 0.003f),
                (Commodity.Nanomaterials, cost * 0.001f)
            ),
            SegmentKind.Traction => D(
                (Commodity.Alloys, cost * 0.004f),
                (Commodity.Polymers, cost * 0.002f)
            ),
            SegmentKind.Industry => D(
                (Commodity.Alloys, cost * 0.003f),
                (Commodity.Electronics, cost * 0.002f),
                (Commodity.Polymers, cost * 0.001f)
            ),
            SegmentKind.Storage => D(
                (Commodity.Alloys, cost * 0.003f),
                (Commodity.Polymers, cost * 0.002f)
            ),
            SegmentKind.Harvest => D(
                (Commodity.Alloys, cost * 0.003f),
                (Commodity.Electronics, cost * 0.001f),
                (Commodity.Ceramics, cost * 0.001f)
            ),
            SegmentKind.Habitat => D(
                (Commodity.Alloys, cost * 0.003f),
                (Commodity.Ceramics, cost * 0.002f),
                (Commodity.Polymers, cost * 0.001f)
            ),
            _ => D((Commodity.Alloys, cost * 0.005f)),
        };
    }

    /// <summary>
    /// Creates a manufacturing recipe for a dome with higher material costs and longer build times.
    /// Domes require more materials and crew but have reduced FactorySize requirements.
    /// </summary>
    public static SegmentRecipe CreateDomeRecipe(HabitatDef def) {
        float baseCost = def.Cost;
        int size = (int)def.Size.Size;

        return new SegmentRecipe(
            Name: $"Construct {def.NameSize}",
            OutputDef: def,
            Inputs: D(
                (Commodity.Alloys, baseCost * 0.005f),      // More metal for structure
                (Commodity.Ceramics, baseCost * 0.003f),   // Dome shell
                (Commodity.Glass, baseCost * 0.004f),      // Windows/panels
                (Commodity.Polymers, baseCost * 0.002f),   // Seals and insulation
                (Commodity.Electronics, baseCost * 0.001f) // Environmental controls
            ),
            Consumables: D(
                (Commodity.Fuel, baseCost * 0.002f),
                (Commodity.Lubricants, baseCost * 0.0005f)
            ),
            Maintenance: D((Commodity.SpareParts, baseCost * 0.001f)),
            CrewRequired: 5 + size * 2,  // Domes need more crew
            ActivateCharge: 50 * size,   // Higher power needs
            CycleTime: TimeDuration.FromHours(Tuning.Manufacturing.HoursPerSize * size * 2), // 2x normal time
            TechTier: GameTier.Mid
        );
    }

    /// <summary>
    /// Gets all dome construction recipes.
    /// </summary>
    public static IEnumerable<SegmentRecipe> GetDomeRecipes() {
        foreach (var def in SegmentEx.HabitatDefs.Where(h => h.Type == HabitatType.Dome)) {
            yield return CreateDomeRecipe(def);
        }
    }

    // ============================================
    // UPGRADE RECIPES
    // ============================================

    /// <summary>
    /// All available upgrade recipes.
    /// </summary>
    public static IReadOnlyList<UpgradeRecipe> AllUpgrades { get; } = [
        // --- Weapon Upgrades ---
        new UpgradeRecipe(
            Name: "Accuracy Enhancement",
            UpgradePrefix: "Accurate",
            ApplicableKinds: [SegmentKind.Offense],
            ApplyUpgrade: def => def is WeaponDef w ? w.Accurate() : def,
            CostMultiplier: 0.2f,
            Materials: D((Commodity.Electronics, 0.002f), (Commodity.Glass, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Early
        ),

        new UpgradeRecipe(
            Name: "Damage Enhancement",
            UpgradePrefix: "Powerful",
            ApplicableKinds: [SegmentKind.Offense],
            ApplyUpgrade: def => def is WeaponDef w ? w.Powerful() : def,
            CostMultiplier: 0.3f,
            Materials: D((Commodity.Alloys, 0.002f), (Commodity.Explosives, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Rate Enhancement",
            UpgradePrefix: "Sustained",
            ApplicableKinds: [SegmentKind.Offense],
            ApplyUpgrade: def => def is WeaponDef w ? w.Sustained() : def,
            CostMultiplier: 0.3f,
            Materials: D((Commodity.Alloys, 0.001f), (Commodity.Electronics, 0.002f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Salvo Enhancement",
            UpgradePrefix: "Salvo",
            ApplicableKinds: [SegmentKind.Offense],
            ApplyUpgrade: def => def is WeaponDef w ? w.Salvo() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.002f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Ammo Efficiency",
            UpgradePrefix: "Thrifty",
            ApplicableKinds: [SegmentKind.Offense],
            ApplyUpgrade: def => def is WeaponDef w ? w.Thrifty() : def,
            CostMultiplier: 0.2f,
            Materials: D((Commodity.Electronics, 0.001f), (Commodity.Polymers, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Early
        ),

        // --- Reactor Upgrades ---
        new UpgradeRecipe(
            Name: "Capacity Enhancement",
            UpgradePrefix: "High-Capacity",
            ApplicableKinds: [SegmentKind.Power],
            ApplyUpgrade: def => def is ReactorDef r ? r.HighCapacity() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.002f), (Commodity.Electronics, 0.001f)),
            CrewRequired: 3,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Generation Enhancement",
            UpgradePrefix: "Overcharged",
            ApplicableKinds: [SegmentKind.Power],
            ApplyUpgrade: def => def is ReactorDef r ? r.Overcharged() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Electronics, 0.002f), (Commodity.Nanomaterials, 0.001f)),
            CrewRequired: 3,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        // --- Traction Upgrades ---
        new UpgradeRecipe(
            Name: "Speed Enhancement",
            UpgradePrefix: "Swift",
            ApplicableKinds: [SegmentKind.Traction],
            ApplyUpgrade: def => def is TractionDef t ? t.Swift() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.001f), (Commodity.Polymers, 0.002f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        new UpgradeRecipe(
            Name: "Lift Enhancement",
            UpgradePrefix: "Heavy-Duty",
            ApplicableKinds: [SegmentKind.Traction],
            ApplyUpgrade: def => def is TractionDef t ? t.HeavyDuty() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.003f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        // --- Defense Upgrades ---
        new UpgradeRecipe(
            Name: "Armor Reinforcement",
            UpgradePrefix: "Reinforced",
            ApplicableKinds: [SegmentKind.Defense],
            ApplyUpgrade: def => def is ArmorDef a ? a.Reinforced() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.003f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        new UpgradeRecipe(
            Name: "Plating Thickening",
            UpgradePrefix: "Thick",
            ApplicableKinds: [SegmentKind.Defense],
            ApplyUpgrade: def => def is PlatingDef p ? p.Thick() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Ceramics, 0.002f), (Commodity.Alloys, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        new UpgradeRecipe(
            Name: "Shield Capacity Enhancement",
            UpgradePrefix: "Capacious",
            ApplicableKinds: [SegmentKind.Defense],
            ApplyUpgrade: def => def is ShieldDef s ? s.Capacious() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Electronics, 0.002f), (Commodity.Nanomaterials, 0.001f)),
            CrewRequired: 3,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Shield Recharge Enhancement",
            UpgradePrefix: "Reactive",
            ApplicableKinds: [SegmentKind.Defense],
            ApplyUpgrade: def => def is ShieldDef s ? s.Reactive() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Electronics, 0.002f)),
            CrewRequired: 3,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        // --- Industry Upgrades ---
        new UpgradeRecipe(
            Name: "Throughput Enhancement",
            UpgradePrefix: "Streamlined",
            ApplicableKinds: [SegmentKind.Industry],
            ApplyUpgrade: def => def is IndustryDef i ? i.Streamlined() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.001f), (Commodity.Electronics, 0.002f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Efficiency Enhancement",
            UpgradePrefix: "Optimized",
            ApplicableKinds: [SegmentKind.Industry],
            ApplyUpgrade: def => def is IndustryDef i ? i.Optimized() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Electronics, 0.002f), (Commodity.Nanomaterials, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Mid
        ),

        // --- Storage Upgrades ---
        new UpgradeRecipe(
            Name: "Capacity Enhancement",
            UpgradePrefix: "Spacious",
            ApplicableKinds: [SegmentKind.Storage],
            ApplyUpgrade: def => def is StorageDef s ? s.Spacious() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.002f), (Commodity.Polymers, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        // --- Harvest Upgrades ---
        new UpgradeRecipe(
            Name: "Yield Enhancement",
            UpgradePrefix: "Bountiful",
            ApplicableKinds: [SegmentKind.Harvest],
            ApplyUpgrade: def => def is HarvestDef h ? h.Bountiful() : def,
            CostMultiplier: 0.25f,
            Materials: D((Commodity.Alloys, 0.001f), (Commodity.Electronics, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Early
        ),

        // --- Universal Upgrades ---
        new UpgradeRecipe(
            Name: "Durability Enhancement",
            UpgradePrefix: "Hardened",
            ApplicableKinds: [SegmentKind.Offense, SegmentKind.Defense, SegmentKind.Power, SegmentKind.Traction, SegmentKind.Industry, SegmentKind.Storage, SegmentKind.Harvest],
            ApplyUpgrade: def => def.Hardened(),
            CostMultiplier: 0.2f,
            Materials: D((Commodity.Alloys, 0.002f), (Commodity.Ceramics, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Early
        ),

        new UpgradeRecipe(
            Name: "Weight Reduction",
            UpgradePrefix: "Lightened",
            ApplicableKinds: [SegmentKind.Offense, SegmentKind.Defense, SegmentKind.Power, SegmentKind.Traction, SegmentKind.Industry, SegmentKind.Storage, SegmentKind.Harvest],
            ApplyUpgrade: def => def.Lightened(),
            CostMultiplier: 0.3f,
            Materials: D((Commodity.Polymers, 0.002f), (Commodity.Nanomaterials, 0.001f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Mid
        ),

        new UpgradeRecipe(
            Name: "Power Efficiency",
            UpgradePrefix: "Low-Power",
            ApplicableKinds: [SegmentKind.Offense, SegmentKind.Defense, SegmentKind.Traction, SegmentKind.Industry, SegmentKind.Storage, SegmentKind.Harvest],
            ApplyUpgrade: def => def.LowPower(),
            CostMultiplier: 0.3f,
            Materials: D((Commodity.Electronics, 0.002f)),
            CrewRequired: 2,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Mid
        ),
    ];

    /// <summary>
    /// Find upgrade recipes applicable to the given segment.
    /// </summary>
    public static IEnumerable<UpgradeRecipe> GetUpgradesFor(Segment segment) =>
        AllUpgrades.Where(u => u.CanApplyTo(segment));
}
