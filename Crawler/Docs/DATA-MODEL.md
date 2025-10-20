# Crawler Data Model

**Last Updated:** 2025-01-19
**Parent:** [ARCHITECTURE.md](ARCHITECTURE.md)

## Quick Navigation
- [IActor Interface](#iactor-interface)
- [ActorToActor](#actortoactor-relationship-state)
- [ActorLocation](#actorlocation-visit-tracking)
- [Segments](#segments)
- [Inventory](#inventory)
- [Commodities](#commodities)
- [Enums](#key-enums)

## Recent Changes
- **2025-10-19**: Added EFlags enum to ActorToActor; replaced boolean fields with flag-based properties (Hostile, Surrendered, Spared, Betrayed)
- **2025-01-19**: Added UltimatumTime to ActorToActor for mandatory interactions

---

## IActor Interface

**File:** `IActor.cs:46-76`
**Purpose:** Common interface for all interactive entities

```csharp
interface IActor {
    // ===== Identity =====
    string Name;                    // Display name (not unique)
    Faction Faction;                // Political allegiance
    Location Location;              // Current position
    EActorFlags Flags;              // Type flags (Mobile, Settlement, etc.)

    // ===== State =====
    Inventory Inv;                  // Commodities and segments
    string EndMessage;              // Game over message (if applicable)
    EEndState? EndState;            // End condition (Destroyed, Revolt, etc.)

    // ===== Knowledge & Relations =====
    bool Knows(IActor other);       // Has met this actor
    bool Knows(Location loc);       // Has visited this location
    ActorToActor To(IActor other);  // Relationship state
    ActorLocation To(Location loc); // Visit tracking

    // ===== Actions =====
    IEnumerable<IProposal> Proposals();                // potential interactions 
    void Tick();                                       // Update (solo)
    void Tick(IEnumerable<IActor> others);            // Update (with context)
    void ReceiveFire(IActor from, List<HitRecord> fire);  // Combat
    void End(EEndState state, string message);        // Game over

    // ===== Display =====
    string Brief(IActor viewer);    // One-line summary (context-aware)
    string Report();                // Detailed report
    void Message(string message);   // Send notification

    // ===== Misc =====
    int Domes;                      // Settlement size
}
```

### Implementations

**Crawler** (`Crawler.cs:24-793`)
- Mobile vehicles (player and NPCs)
- Has segments for functionality
- Resource management (fuel, crew, morale)
- Combat capabilities

**StaticActor** (`IActor.cs:78-115`)
- Immobile entities (settlements, resources, hazards)
- Simplified inventory
- No segment management
- Used for encounter content

---

## ActorToActor (Relationship State)

**File:** `Crawler.cs:8-43`
**Purpose:** Track relationship between two actors

```csharp
class ActorToActor {
    // ===== Flags Enum =====
    [Flags]
    enum EFlags {
        Hostile = 1 << 0,       // Currently hostile to each other
        Surrendered = 1 << 1,   // This actor surrendered to other
        Spared = 1 << 2,        // This actor was spared by other
        Betrayed = 1 << 3,      // Trust was betrayed
    }
    EFlags Flags;

    // ===== Flag-based Properties =====
    bool Hostile { get; set; }      // Uses HasFlag/SetFlag pattern
    bool Surrendered { get; set; }  // Uses HasFlag/SetFlag pattern
    bool Spared { get; set; }       // Uses HasFlag/SetFlag pattern
    bool Betrayed { get; set; }     // Uses HasFlag/SetFlag pattern

    // ===== Damage History =====
    bool WasHostile => DamageCreated > 0;   // Ever attacked them
    bool WasDamaged => DamageTaken > 0;     // Ever took damage from them

    int DamageCreated = 0;          // Total potential damage sent
    int DamageInflicted = 0;        // Total damage that hit
    int DamageTaken = 0;            // Total damage received

    // ===== Trust Calculation =====
    int StandingPositive = 0;       // Positive interaction points
    int StandingNegative = 0;       // Negative interaction points

    int Standing => StandingPositive - StandingNegative;

    float Trust => 1 - (Min(StandingPositive, StandingNegative) /
                       (Max(StandingPositive, StandingNegative) + 1e-12f));
}
```

### Usage

**Access:**
```csharp
var relation = crawler.To(otherActor);
if (relation.Hostile) {
    // Combat logic
}
```

**Dynamic Hostility:**
Bandits evaluate player evil points before turning hostile:
```csharp
// In Crawler.NewRelation() - Crawler.cs:737-761
if (Faction == Bandit && to.Faction == Player) {
    float evilness = to.EvilPoints;
    if (evilness >= threshold) {
        float hostilityChance = baseChance * (evilness / threshold);
        if (Random() < hostilityChance) {
            result.Hostile = true;
        }
    }
}
```

**Flag Usage:**
```csharp
// Set flags
relation.Hostile = true;
relation.Surrendered = true;
relation.Spared = true;

// Check flags
if (relation.Hostile) {
    // Combat logic
}
if (relation.Surrendered && relation.Betrayed) {
    // Trust penalty
}
```

---

## ActorLocation (Visit Tracking)

**File:** `Map.cs` (not shown in search, but referenced)
**Purpose:** Track actor's knowledge of locations

```csharp
class ActorLocation {
    bool Visited = false;           // Has been to this location
    long ForgetTime = 0;            // When knowledge expires (0 = never)
}
```

**Usage:**
```csharp
var locationKnowledge = crawler.To(location);
if (locationKnowledge.Visited) {
    // Show detailed info
}
```

---

## Segments

**Files:** `Segment.cs`, `Crawler.cs:287-340`
**Purpose:** Modular crawler components

### Hierarchy

```
Segment (base)
├── PowerSegment
│   ├── ReactorSegment (charge storage + generation)
│   └── ChargerSegment (generation only, e.g., solar)
├── TractionSegment (terrain-specific movement)
├── OffenseSegment
│   └── WeaponSegment (generates HitRecords)
└── DefenseSegment
    ├── ArmorSegment (damage reduction)
    └── ShieldSegment (regenerating protection)
```

### Segment States

```csharp
enum Working {
    Active,         // Functioning normally
    Deactivated,    // Manually turned off
    Disabled,       // Damaged (Hits > 0)
    Destroyed,      // Beyond repair (Hits >= MaxHits)
    Packaged,       // In inventory for trade
}
```

**State Transitions:**
```
Active ←→ Deactivated
  ↓
Disabled (Hits > 0)
  ↓
Destroyed (Hits >= MaxHits)

Any → Packaged (for trading)
```

### SegmentDef vs Segment

**SegmentDef** - Immutable template
- Defines stats (cost, size, drain, etc.)
- Tier-based scaling
- Shared across instances

**Segment** - Mutable instance
- Current state (Working enum)
- Damage (Hits counter)
- Ownership (IActor? Owner)
- Instance-specific modifications

### Segment Properties

```csharp
abstract class Segment {
    SegmentDef Def;                 // Template
    IActor? Owner;                  // Current owner
    int Hits = 0;                   // Damage taken
    Working State;                  // Current state
    bool Packaged;                  // In inventory vs installed

    // Derived properties
    float Cost => Def.Cost;
    float Drain => Def.Drain;
    int MaxHits => Def.MaxHits;
    SegmentKind SegmentKind => Def.SegmentKind;

    // Methods
    abstract void Tick();           // Per-hour update
    virtual (int, string) AddDmg(HitType, int damage);  // Combat damage
}
```

### Segment Organization

**Crawler maintains segment caches:**
```csharp
List<Segment> _allSegments;                         // All segments
List<Segment> _activeSegments;                      // State == Active
List<Segment> _undestroyedSegments;                 // State != Destroyed
EArray<SegmentKind, List<Segment>> _segmentsByClass;  // By kind (all)
EArray<SegmentKind, List<Segment>> _activeSegmentsByClass;  // By kind (active)
```

**Update trigger:**
```csharp
crawler.UpdateSegments();  // Rebuild caches after inventory changes
```

---

## Inventory

**File:** `Inventory.cs`
**Purpose:** Container for commodities and segments

```csharp
class Inventory {
    // ===== Commodities =====
    EArray<Commodity, float> Commodities;  // Indexed by enum

    // Accessor
    float this[Commodity c] {
        get => Commodities[c];
        set => Commodities[c] = value;
    }

    // ===== Segments =====
    List<Segment> Segments;

    // ===== Operations =====
    void Add(Commodity c, float amount);
    void Remove(Commodity c, float amount);
    void Add(Segment segment);
    void Remove(Segment segment);
    bool Contains(Inventory other);
    bool IsEmpty { get; }

    // ===== Value Calculations =====
    float ValueAt(Location location);  // Total scrap value
    float Mass { get; }                // Total weight
    float Volume { get; }              // Total volume
}
```

### Trade Inventory

**Crawler has separate TradeInv:**
```csharp
class Crawler {
    Inventory Inv;        // Main inventory (installed + stored)
    Inventory TradeInv;   // Trade-specific items (visible to NPCs)
}
```

**Purpose:**
- Segments in TradeInv are visible for sale
- Keeps packaged segments separate from installed
- NPCs only offer items from TradeInv

---

## Commodities

**File:** `Inventory.cs` (enum), `CommodityEx.cs` (data)
**Count:** 50+ types

### Categories

**Essential** (survival):
- Scrap (currency)
- Fuel
- Crew, Soldiers, Passengers
- Morale
- Air, Water, Rations

**Raw Materials:**
- Biomass (organic)
- Ore (metal ore)
- Silicates (minerals)

**Refined Materials:**
- Metal
- Chemicals
- Glass

**Parts & Components:**
- Ceramics
- Polymers
- Alloys
- Electronics

**Consumer Goods:**
- Medicines
- Textiles
- Gems
- Toys
- Machines
- Media

**Vice** (Bandit-only):
- Liquor
- Stims (stimulants)
- Downers (sedatives)
- Trips (psychedelics)

**Dangerous:**
- Explosives
- SmallArms
- AiCores

**Religious:**
- Idols
- Texts (sacred manuscripts)
- Relics

### Commodity Properties

```csharp
record CommodityData(
    float BaseValue,    // Price in scrap
    float Volume,       // Space taken
    float Mass,         // Weight
    CommodityFlag Flags,  // Special properties
    GameTier Tier       // When available (Early/Mid/Late)
);
```

**Flags:**
- `Perishable` - Degrades over time
- `Bulky` - Takes extra space
- `Integral` - Integer quantities only (Crew, Soldiers, etc.)

### Rounding

**Integral commodities:**
```csharp
Commodity.Crew.Round(5.7f) == 6f;  // Always whole numbers
```

**Non-integral:**
```csharp
Commodity.Fuel.Round(5.743f) == 5.7f;  // One decimal place
Commodity.Scrap.Round(123.456f) == 123.5f;  // Scrap rounds to 0.1
```

---

## Key Enums

### EActorFlags

**File:** `IActor.cs:36-44`
```csharp
[Flags]
enum EActorFlags {
    None = 0,
    Player = 1 << 0,        // Is the player
    Mobile = 1 << 1,        // Can move
    Settlement = 1 << 2,    // Is a settlement
    Creature = 1 << 3,      // Living being (unused currently)
    Looted = 1 << 16,       // Has been looted
}
```

**Usage:**
```csharp
if ((actor.Flags & EActorFlags.Settlement) != 0) {
    // Settlement-specific logic
}
```

### EEndState

**File:** `IActor.cs:26-33`
```csharp
enum EEndState {
    Destroyed,      // Crawler destroyed
    Revolt,         // Crew revolted (morale == 0)
    Killed,         // Crew killed
    Starved,        // Crew starved
    Won,            // Victory condition (future)
}
```

### SegmentKind

**File:** `Segment.cs`
```csharp
enum SegmentKind {
    Power,          // Reactors, chargers
    Traction,       // Movement systems
    Offense,        // Weapons
    Defense,        // Armor, shields
}
```

### Faction

**File:** `Faction.cs`
```csharp
enum Faction {
    Player,
    Independent,    // Neutral traders
    Bandit,

    // Civilian factions (procedurally generated)
    Civilian0, Civilian1, ..., Civilian19,  // 20 total

    Trade,          // Legacy (alias for Independent)
}
```

**Civilian Factions:**
- Generated during world creation
- Each controls territory
- Has faction capital (highest population settlement)
- Individual trade policies

### TerrainType

**File:** `Map.cs`
```csharp
enum TerrainType {
    Flat,           // Easiest (roads, plains)
    Rough,          // Moderate
    Broken,         // Difficult
    Shattered,      // Very difficult
    Ruined,         // Extreme (wasteland)
}
```

**Impact:**
- Movement speed
- Fuel consumption
- Segment effectiveness (especially traction)
- Faction spawn rates (more bandits in ruined)

### EncounterType

**File:** `Encounter.cs:3-9`
```csharp
enum EncounterType {
    None,           // Empty location
    Crossroads,     // Random travelers
    Settlement,     // Trading post
    Resource,       // Harvestable materials
    Hazard,         // Risk/reward location
}
```

### TradePolicy

**File:** `Tuning.cs` (implied from use)
```csharp
enum TradePolicy {
    Legal,          // No restrictions
    Controlled,     // Transaction fees apply
    Prohibited,     // Subject to seizure
}
```

**Usage:**
```csharp
var policy = Tuning.FactionPolicies.GetPolicy(faction, commodity);
if (policy == TradePolicy.Prohibited) {
    // Contraband!
}
```

### InteractionCapability

**File:** `Trade.cs:3-7`
```csharp
enum InteractionCapability {
    Disabled,       // Not available
    Possible,       // Optional interaction
    Mandatory,      // Time-limited demand
}
```

**See:** [SYSTEMS.md#interaction-capability-enum](SYSTEMS.md#interaction-capability-enum)

---

## Summary

The data model separates:
- **Identity** (IActor, Faction, EActorFlags)
- **Relations** (ActorToActor, ActorLocation)
- **Resources** (Inventory, Commodity, Segment)
- **State** (EEndState, Working, TerrainType)

This enables:
- ✅ Clean serialization (save/load)
- ✅ Type-safe enum indexing (EArray)
- ✅ Flexible relationship tracking
- ✅ Modular crawler construction

For system interactions, see [SYSTEMS.md](SYSTEMS.md)
For extension examples, see [EXTENDING.md](EXTENDING.md)
