# Crawler Data Model

**Last Updated:** 2025-01-19
**Parent:** [ARCHITECTURE.md](ARCHITECTURE.md)

## Quick Navigation
- [IActor Interface](#iactor-interface)
- [Save/Load Data Structures](#saveload-data-structures)
- [ActorToActor](#actortoactor-relationship-state)
- [ActorLocation](#actorlocation-visit-tracking)
- [Segments](#segments)
- [Inventory](#inventory)
- [Commodities](#commodities)
- [Enums](#key-enums)

## Recent Changes
- **2025-01-28**: Implemented Init/Cfg/Data pattern for save/load system; added ActorBase.Data, ActorScheduled.Data, and Crawler.Data structures forming inheritance hierarchy; all Data structures contain Init as first field with ToData()/FromData() methods
- **2025-11-08**: Added XorShift RNG and GaussianSampler to Crawler class for deterministic randomness; all crawlers now maintain their own RNG state with seed-based initialization
- **2025-10-29**: Policy record now includes Description field for faction policy descriptions
- **2025-10-24**: Added `Overdraft` property and `ContainsResult` enum to Inventory; withdrawal methods now support overdraft from linked inventory
- **2025-10-19**: Added `Betrayer` flag to `EFlags` enum; removed Standing/Trust fields from `ActorToActor`; added `Latch()`, `HasFlag()`, `SetFlag()` helper methods; removed `Betrayed` property (now uses `Betrayer` flag)
- **2025-10-19**: Added EFlags enum to ActorToActor; replaced boolean fields with flag-based properties (Hostile, Surrendered, Spared, Betrayed)

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
    Inventory Supplies;             // Commodities and segments
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
- Each crawler has its own XorShift RNG and GaussianSampler for deterministic behavior

**StaticActor** (`IActor.cs:78-115`)
- Immobile entities (settlements, resources, hazards)
- Simplified inventory
- No segment management
- Used for encounter content

---

## Save/Load Data Structures

**Pattern:** Init/Cfg/Data
**Purpose:** Serialization and state restoration

### Design Principles

The save/load system follows a consistent Init/Cfg/Data pattern across all actor types:

1. **Init** - Construction-time configuration
   - Contains static configuration and initial state
   - Used for object construction
   - Seed-based for deterministic generation

2. **Data** - Serializable runtime state
   - Contains Init as first field (not duplicating fields)
   - Captures current runtime state for save/load
   - Forms inheritance hierarchy matching class hierarchy

3. **Constructor Pattern** - Two constructors for each class:
   ```csharp
   new Foo(Foo.Init init)              // Normal construction
   new Foo(Foo.Init init, Foo.Data data)  // Load from save
   ```

### Data Hierarchy

```
ActorBase.Data
├── ActorScheduled.Data
    └── Crawler.Data
```

### ActorBase.Data

**File:** `IActor.cs:179-189`

```csharp
public record class Data {
    public Init Init { get; set; }
    public XorShift.Data Rng { get; set; }
    public GaussianSampler.Data Gaussian { get; set; }
    public long Time { get; set; }
    public long LastTime { get; set; }
    public EEndState? EndState { get; set; }
    public string EndMessage { get; set; }
    public Dictionary<string, ActorToActor.Data> ActorRelations { get; set; }
    public Dictionary<string, LocationActor.Data> LocationRelations { get; set; }
}
```

**Contains:**
- Init structure for reconstruction
- RNG state (XorShift and GaussianSampler)
- Time tracking
- End state (destroyed, etc.)
- Actor relationships keyed by actor name
- Location relationships keyed by position string

### ActorScheduled.Data

**File:** `ActorScheduled.cs:18-21`

```csharp
public new record class Data : ActorBase.Data {
    public ScheduleEvent? NextEvent { get; set; }
    public long Encounter_ScheduledTime { get; set; }
}
```

**Adds:**
- Scheduled event state
- Encounter scheduling time

### Crawler.Data

**File:** `Crawler.Builder.cs:11-20`

```csharp
public new record class Data : ActorScheduled.Data {
    public new Init Init { get; set; }  // Override to use Crawler.Init
    public List<Segment.Data> WorkingSegments { get; set; }
    public int EvilPoints { get; set; }
}
```

**Adds:**
- Working segments state (unpacked segments)
- Evil points counter

### Serialization Methods

Each actor class implements:

```csharp
// ActorBase
public virtual Data ToData() {
    return new Data {
        Init = CreateInitFromCurrentState(),
        Rng = this.Rng.ToData(),
        Gaussian = this.Gaussian.ToData(),
        // ... other fields
    };
}

public virtual void FromData(Data data) {
    this.Rng.FromData(data.Rng);
    this.Gaussian.FromData(data.Gaussian);
    // ... restore other state
}
```

**Key points:**
- `ToData()` creates a snapshot of current state
- `FromData()` restores runtime state from Data
- Each level calls base implementation
- Init is reconstructed from current state

### Relationship Restoration

Actor relationships require two-pass restoration:

**First pass:** Create all actors
```csharp
var init = CreateInitFromSaveData(savedData);
var data = CreateDataFromSaveData(savedData);
var actor = new Crawler(init, data);
```

**Second pass:** Restore relations after all actors exist
```csharp
actor.RestoreActorRelations(data.ActorRelations, actorLookup);
actor.RestoreLocationRelations(data.LocationRelations, map);
```

**Why two passes?**
- ActorRelations use IActor references, not names
- Must resolve names → actor references after all actors loaded
- Helper methods on ActorBase handle this restoration

### Component State

Components are **not** serialized in Data structures. They are reconstructed based on Role:

```csharp
var init = new Crawler.Init {
    // ... other fields
    Role = Roles.Bandit,
    InitializeComponents = true  // Reconstruct components based on role
};
```

**Rationale:**
- Components are deterministic based on role and seed
- Avoids complex component state serialization
- Simpler save file format
- Components re-subscribe to events on Begin()

### Usage Example

**Saving:**
```csharp
var crawlerData = (Crawler.Data)crawler.ToData();
// Serialize crawlerData to YAML/JSON
```

**Loading:**
```csharp
// Deserialize from YAML/JSON to get savedCrawler data

// Create Init structure
var init = new Crawler.Init {
    Seed = savedCrawler.RngState,
    Name = savedCrawler.Name,
    Faction = savedCrawler.Faction,
    Location = location,
    Supplies = supplies,
    Cargo = cargo,
    Role = Roles.None,
    InitializeComponents = false,
    WorkingSegments = new List<Segment>()
};

// Create Data structure
var data = new Crawler.Data {
    Init = init,
    Rng = new XorShift.Data { State = savedCrawler.RngState },
    Gaussian = new GaussianSampler.Data { /* ... */ },
    WorkingSegments = /* ... */,
    EvilPoints = savedCrawler.EvilPoints,
    // ... other fields
};

// Construct crawler from Init + Data
var crawler = new Crawler(init, data);
crawler.Begin();  // Initialize components
```

### Related Data Structures

**Segment.Data:**
```csharp
public record class Data {
    public ulong Seed { get; set; }
    public string DefName { get; set; }
    public int Hits { get; set; }
    public bool Packaged { get; set; }
    public bool Activated { get; set; }
}
```

**ActorToActor.Data:**
```csharp
public record class Data {
    public EFlags Flags { get; set; }
    public int DamageCreated { get; set; }
    public int DamageInflicted { get; set; }
    public int DamageTaken { get; set; }
    public UltimatumState? Ultimatum { get; set; }
}
```

**LocationActor.Data:**
```csharp
public record class Data {
    public bool Visited { get; set; }
}
```

---

## Actor Component System

**Files:** `Components.cs`, `ActorComponents.cs`
**Purpose:** Pluggable, event-driven behaviors for Crawlers

### ICrawlerComponent Interface

```csharp
public interface ICrawlerComponent {
    Crawler Owner { get; }
    int Priority { get; }

    void Attach(Crawler owner);
    void Detach();
    void OnComponentsDirty();

    IEnumerable<Interaction> EnumerateInteractions(IActor subject);
    int ThinkAction();

    void SubscribeToEncounter(Encounter encounter);
    void UnsubscribeFromEncounter(Encounter encounter);
}
```

**Properties:**
- `Owner` - The Crawler this component is attached to
- `Priority` - Higher values execute ThinkAction first (default 500)

**Methods:**
- `Attach(owner)` - Called when component is attached to crawler
- `Detach()` - Called when component is detached from crawler
- `OnComponentsDirty()` - Called after component list changes (for late initialization)
- `EnumerateInteractions(subject)` - Generate interactions on-demand between owner and subject
- `ThinkAction()` - NPC AI behavior, returns AP cost if action taken (0 = no action)
- `SubscribeToEncounter()/UnsubscribeFromEncounter()` - Register/unregister event handlers

### CrawlerComponentBase

```csharp
public abstract class CrawlerComponentBase : ICrawlerComponent {
    public Crawler Owner { get; private set; }
    public virtual int Priority => 500;

    public virtual void Attach(Crawler owner);
    public virtual void Detach();
    public virtual void OnComponentsDirty();
    public virtual IEnumerable<Interaction> EnumerateInteractions(IActor subject) => [];
    public virtual int ThinkAction() => 0;
    public virtual void SubscribeToEncounter(Encounter encounter);
    public virtual void UnsubscribeFromEncounter(Encounter encounter);
}
```

Base class providing common component functionality with default implementations.

### Encounter Event Handlers

Components can subscribe to encounter events via delegates:

```csharp
// In SubscribeToEncounter():
encounter.ActorArrived += OnActorArrived;
encounter.ActorLeaving += OnActorLeaving;
encounter.ActorLeft += OnActorLeft;
encounter.EncounterTick += OnEncounterTick;

// Event handler signatures:
void OnActorArrived(IActor actor, long time);
void OnActorLeaving(IActor actor, long time);
void OnActorLeft(IActor actor, long time);
void OnEncounterTick(long time);
```

### Crawler Event Handlers

Components can also subscribe to crawler-specific events:

```csharp
// In Attach() or OnComponentsDirty():
Owner.HostilityChanged += OnHostilityChanged;
Owner.ReceivingFire += OnReceivingFire;

// Event handler signatures:
void OnHostilityChanged(IActor other, bool hostile);
void OnReceivingFire(IActor from, List<HitRecord> fire);
```

### Built-in Component Types

See [SYSTEMS.md#built-in-components](SYSTEMS.md#built-in-components) for full descriptions.

**Core Components:**
- **CustomsComponent** - Contraband scanning and seizure ultimatums
- **TradeOfferComponent** - On-demand trade interaction generation
- **LifeSupportComponent** - Resource consumption and crew survival
- **AutoRepairComponent** - Automatic segment repair

**Player Components:**
- **AttackComponent** - Player attack interactions
- **PlayerDemandComponent** - Player extortion of vulnerable NPCs
- **EncounterMessengerComponent** - Display messages for encounters and combat

**NPC AI Components:**
- **RetreatComponent** (Priority 1000) - Flee when vulnerable
- **CombatComponentAdvanced** (Priority 600) - Intelligent combat AI (Bandits)
- **CombatComponentDefense** (Priority 400) - Generic hostile target attack
- **BanditComponent** (Priority 600) - Extortion and ultimatum creation

**Service Components:**
- **RepairComponent** - Repair services at settlements
- **LicenseComponent** - License purchases at settlements
- **SurrenderComponent** - Accept surrender from vulnerable enemies

**Utility Components:**
- **RelationPrunerComponent** - Cleanup transient relationships on departure
- **HarvestComponent** - Resource gathering interactions
- **HazardComponent** - Risk/reward exploration interactions

### Usage Example

```csharp
// Creating a crawler with components
var trader = Crawler.NewRandom(seed, faction, location, ...);
trader.AddComponent(new TradeOfferComponent(seed, 0.25f));
trader.AddComponent(new RelationPrunerComponent());

// Components automatically subscribe when crawler joins encounter
encounter.AddActor(trader);

// Interactions enumerated dynamically
var interactions = trader.EnumerateInteractions(player); // Yields from all components

// NPC AI runs through components by priority
trader.ThinkFor(elapsed); // Calls ThinkAction() on each component until one acts
```

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
        Betrayed = 1 << 3,      // Trust was betrayed (victim)
        Betrayer = 1 << 4,      // Trust was betrayed (attacker)
    }
    EFlags Flags;

    // ===== Helper Methods =====
    bool Latch(EFlags flag, bool value = true);  // Set flag once, return true first time
    bool HasFlag(EFlags flag);                    // Check if flag is set
    EFlags SetFlag(EFlags flag, bool value = true);  // Set/clear flag

    // ===== Flag-based Properties =====
    bool Hostile { get; set; }      // Uses HasFlag/SetFlag pattern
    bool Surrendered { get; set; }  // Uses HasFlag/SetFlag pattern
    bool Spared { get; set; }       // Uses HasFlag/SetFlag pattern

    // ===== Damage History =====
    bool WasHostile => DamageCreated > 0;   // Ever attacked them
    bool WasDamaged => DamageTaken > 0;     // Ever took damage from them

    int DamageCreated = 0;          // Total potential damage sent
    int DamageInflicted = 0;        // Total damage that hit
    int DamageTaken = 0;            // Total damage received
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

// Latch pattern - returns true only first time flag is set
if (relation.Latch(ActorToActor.EFlags.Betrayed, true)) {
    // Handle betrayal consequences (only once)
    ++attacker.EvilPoints;
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
[Damaged (Hits > 0)] (affects packagability only, not functionality, so not exposed in   
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
        set => Commodities[c] = value;  // Supports overdraft on withdrawal
    }

    // ===== Segments =====
    List<Segment> Segments;

    // ===== Overdraft =====
    Inventory? Overdraft { get; set; }  // Linked inventory for withdrawals

    // ===== Operations =====
    void Add(Commodity c, float amount);
    void Remove(Commodity c, float amount);  // Pulls from Overdraft if needed
    void Add(Segment segment);
    void Remove(Segment segment);
    ContainsResult Contains(Inventory other);  // Returns True/False/Overdraft
    bool IsEmpty { get; }

    // ===== Value Calculations =====
    float ValueAt(Location location);  // Total scrap value
    float Mass { get; }                // Total weight
    float Volume { get; }              // Total volume
}
```

### ContainsResult Enum

**File:** `Inventory.cs:176-180`
**Purpose:** Indicates whether an inventory can fulfill a request

```csharp
enum ContainsResult {
    False,      // Cannot fulfill (even with overdraft)
    True,       // Can fulfill from current inventory alone
    Overdraft   // Can fulfill, but requires pulling from overdraft
}
```

**Usage:**
```csharp
var result = supplies.Contains(requestedItems);
switch (result) {
    case ContainsResult.True:
        // Have everything locally
        break;
    case ContainsResult.Overdraft:
        // Need to pull from cargo
        break;
    case ContainsResult.False:
        // Can't fulfill request
        break;
}
```

### Trade Inventory

**Crawler has separate Cargo:**
```csharp
class Crawler {
    Inventory Supplies;   // Main inventory (installed + stored)
    Inventory Cargo;      // Trade-specific items (visible to NPCs)
}
```

**Purpose:**
- Segments in Cargo are visible for sale
- Keeps packaged segments separate from installed
- NPCs only offer items from Cargo

### Overdraft System

**Setup:**
```csharp
// In Crawler.cs:90 and IActor.cs:164
Supplies.Overdraft = Cargo;
```

**Behavior:**
- Withdrawals from `Supplies` automatically pull from `Cargo` if needed
- **One-way flow**: Supplies → Cargo only (not Cargo → Supplies)
- Indexer setter, `Remove(Commodity)`, and `Remove(Inventory)` all support overdraft
- `Contains()` checks both current inventory and overdraft availability

**Example:**
```csharp
// Player has 10 Fuel in Supplies, 50 Fuel in Cargo
Supplies[Commodity.Fuel] = 5;  // Reduces Supplies to 5
Supplies[Commodity.Fuel] = 0;  // Drains Supplies to 0

Supplies.Remove(Commodity.Fuel, 30);
// Drains all 0 from Supplies, then removes 30 from Cargo
// Result: Supplies.Fuel = 0, Cargo.Fuel = 20
```

---

## Commodities

**File:** `Inventory.cs` (enum), `CommodityEx.cs` (data)
**Count:** 50+ types

### Categories

**Essential** (survival):
- Scrap (currency) - Early Game
- Fuel - Early Game
- Crew - Early Game
- Morale - Early Game
- Air - Early Game
- Water - Early Game
- Rations - Early Game

**Raw:**
- Biomass (organic) - Early Game
- Ore (metal ore) - Early Game
- Silicates (minerals) - Early Game

**Refined:**
- Metal - Early Game
- Chemicals - Mid Game
- Glass - Early Game

**Parts:**
- Ceramics - Mid Game
- Polymers - Mid Game
- Alloys - Mid Game
- Electronics - Late Game

**Consumer:**
- Medicines - Mid Game
- Passengers - Early Game
- Toys - Early Game
- Machines - Late Game

**Luxury:**
- Textiles - Early Game
- Gems - Late Game
- Media - Mid Game

**Vice** (Bandit-only):
- Liquor - Early Game
- Stims (stimulants) - Mid Game
- Downers (sedatives) - Mid Game
- Trips (psychedelics) - Late Game

**Dangerous:**
- Soldiers - Mid Game
- Explosives - Mid Game
- SmallArms - Mid Game
- AiCores - Late Game

**Religious:**
- Idols - Early Game
- Texts (sacred manuscripts) - Mid Game
- Relics - Late Game

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

### ActorFlags

**File:** `IActor.cs:38-48`
```csharp
[Flags]
enum ActorFlags : ulong {
    None = 0,
    Player = 1 << 0,        // Is the player
    Mobile = 1 << 1,        // Can move
    Settlement = 1 << 2,    // Is a settlement
    Creature = 1 << 3,      // Living being (unused currently)
    Capital = 1 << 4,       // Is a faction capital
    Loading = 1ul << 63,    // Temporary flag during initialization
}
```

**Usage:**
```csharp
if (actor.Flags.HasFlag(ActorFlags.Settlement)) {
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

**File:** `Trade.cs:7-14`
```csharp
enum TradePolicy {
    Subsidized,     // 0.7x base price - government subsidy
    Legal,          // 1.0x base price - normal trade
    Taxed,          // 1.3x markup - import tariffs
    Controlled,     // 1.75x markup - can carry but can't transact without license
    Restricted,     // Higher markup, license fees, and tariffs - illegal to carry without license
    Prohibited      // Cannot trade - illegal in this faction's territory, no carry or transactions
}
```

**Policy Record:**
```csharp
record Policy(
    EArray<CommodityCategory, TradePolicy> Commodities,
    EArray<SegmentKind, TradePolicy> Segments,
    string Description
);
```

**Policy System:**
- Policies are defined per faction for **CommodityCategory** and **SegmentKind**
- Individual commodities/segments inherit policy from their category/kind
- Each faction has a Description field summarizing their policy archetype (e.g., "Authoritarian, Pious, Industrial")

**Policy Archetypes:**
Factions are generated by combining 3 randomly selected archetypes from the following 12 options:
- **Authoritarian**: Prohibits dangerous goods and vice, controls religious items and offense segments
- **Libertarian**: Subsidizes vice and luxury, allows dangerous goods freely
- **Pious**: Subsidizes religious items, prohibits vice, controls dangerous goods, taxes luxury
- **Debauched**: Subsidizes vice and luxury, taxes religious items
- **Industrial**: Subsidizes raw materials and refined goods, taxes dangerous goods and offense segments
- **Mercantile**: Subsidizes luxury and refined goods, taxes raw materials
- **Militaristic**: Subsidizes offense segments and dangerous goods, controls vice, taxes luxury
- **Isolationist**: Controls dangerous goods, vice, and offense segments, taxes luxury
- **Protectionist**: Restricts raw materials and offense segments, prohibits luxury, taxes refined goods
- **Technocratic**: Subsidizes refined goods, restricts dangerous goods and religious items, taxes vice
- **Xenophobic**: Restricts dangerous goods, vice, luxury, and offense segments
- **Corporatist**: Subsidizes luxury and refined goods, restricts religious items, taxes raw materials

**Usage:**
```csharp
// Commodity policy (by category)
var policy = Tuning.FactionPolicies.GetPolicy(faction, commodity);
// or directly: GetPolicy(faction, commodityCategory)
if (policy == TradePolicy.Prohibited) {
    // Contraband!
}

// Segment policy (by kind)
var segmentPolicy = Tuning.FactionPolicies.GetPolicy(faction, segmentKind);
if (segmentPolicy == TradePolicy.Controlled) {
    // Transaction fee applies
}
```

### Immediacy

**File:** `Interactions.cs:5-9`
```csharp
enum Immediacy {
    Disabled,       // Not available
    Menu,           // Available in menu (user choice)
    Immediate,      // Perform now (auto-execute)
}
```

**Purpose:** Controls when and how interactions are presented/executed.
- **Disabled**: Interaction cannot be performed (conditions not met)
- **Menu**: Normal menu option (user must select)
- **Immediate**: Auto-executes without menu (e.g., time-sensitive ultimatum consequences)

**See:** [SYSTEMS.md#immediacy-enum](SYSTEMS.md#immediacy-enum)

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
