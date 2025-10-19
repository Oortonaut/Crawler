# Crawler Architecture (DEPRECATED)

**⚠️ This file is deprecated as of 2025-01-19**

**Please use the new documentation structure:**
- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** - High-level overview
- **[docs/SYSTEMS.md](docs/SYSTEMS.md)** - Detailed system descriptions
- **[docs/DATA-MODEL.md](docs/DATA-MODEL.md)** - Class/interface reference
- **[docs/EXTENDING.md](docs/EXTENDING.md)** - How to add new content

This file is kept for reference only and should not be updated.

---

# Original Content (Deprecated)

## Overview

**Crawler** is a post-apocalyptic trading and exploration game where players pilot modular vehicles (crawlers) through a procedurally generated wasteland. The game combines roguelike elements with tactical combat, resource management, and economic trading.

**Core Loop:**
```
Navigate World → Encounter NPCs/Locations → Trade/Fight/Harvest → Manage Resources → Progress
```

**Genre:** Console-based roguelike trading simulator with turn-based tactical combat

---

## System Map

```
Game (Singleton)
├── Map (World Generation)
│   ├── Sectors (Grid-based, wrap horizontally)
│   │   ├─* Locations (Vector positions, terrain types)
│   │   │   └── Encounters (Events/interactions)
│   │   │       └─< Actors (Crawlers, Static entities)
│   │   │           v └── Inventory (Commodities + Segments)
│   │   │           │     └─< Segments (Crawler Functionality)
│   │   │           └─< Relations (Actor-to-Actor state)
│   │   └── Faction Control (Regional powers)
│   └── Time System (Tick-based, hourly updates)
├── Player (Crawler instance)
├── Menu System (Console UI)
└── Save/Load (YAML serialization)
```

---

## Core Systems

### 1. Game (Game.cs)

**Role:** Central game loop coordinator and singleton instance manager

**Responsibilities:**
- Main game loop with AP (Action Points) system
- Turn processing and time advancement
- Menu orchestration and player input
- Win/loss condition checking
- Save/load coordination

**Key Features:**
- Singleton pattern via `Game.Instance`
- Turn-based with AP costs for actions
- Manages pending location changes (movement)
- Tracks all encounters via weak references (memory management)
- Time measured in seconds (`TimeSeconds`)

**Game Loop:**
```
while (!quit) {
    if (Moving) { Player.Location = PendingLocation }
    GameMenu()  // Present options, get player choice
    while (AP <= 0) {
        Tick()  // Advance simulation
        AP += TurnAP
    }
    CheckWinLoss()
}
```

### 2. Map (Map.cs)

**Role:** Procedural world generation and spatial organization

**Structure:**
```
Map (H×W sectors, wraps horizontally)
└── Sector (grid cell with terrain type)
    ├── Locations (2-5 per sector, Vector2 positions)
    │   ├── Type: Settlement, Crossroads, Resource, Hazard
    │   ├── Wealth (affects trading prices)
    │   ├── Population (Pareto distribution, 0-500)
    │   └── TechLatitude (latitude affects tech level)
    └── ControllingFaction (Civilian0-19, Trade, or Bandit)
```

**Terrain Types:** Flat → Rough → Broken → Shattered → Ruined
- Affects movement speed and fuel consumption
- Determines segment effectiveness (traction)

**Faction Capitals:**
- Generated during world creation
- Highest population settlements become faction capitals
- Each capital is represented by a Crawler (settlement actor), not just a Location
- Up to 20 civilian factions control regions

### 3. Crawler (Crawler.cs)

**Role:** Primary actor representing vehicles (player and NPCs)

**Component-Based Architecture:**
Crawlers are built from segments across 4 classes:
1. **Power** - Reactors (charge storage + generation), Chargers (generation only)
2. **Traction** - Movement systems (wheels, tracks, legs, hover)
3. **Offense** - Weapons and attack systems
4. **Defense** - Shields and armor

**State Machine:**
```
Segments can be: Active → Deactivated → Disabled (damaged) → Destroyed → Packaged (for trade)
```

**Critical States:**
- `IsDepowered` - No power segments or no fuel
- `IsImmobile` - Cannot move on current terrain
- `IsDisarmed` - No offense capability
- `IsDefenseless` - No defense segments
- `IsVulnerable` - Any of the above (triggers AI flee/surrender)

**Resource Consumption (per hour):**
- Fuel: `StandbyDrain / FuelEfficiency` (initial fuel calculated as `FuelPerHr * supplyDays * 24`)
- Wages: `CrewCount * WagesPerCrewDay / 24`
- Rations: `TotalPeople * RationsPerCrewDay / 24`
- Water: Crew needs, recycling loss
- Air: Crew needs, recycling loss (increases with damaged segments)

**Relationship Tracking:**
```
Dictionary<IActor, ActorToActor>
    - Hostile flag
    - Surrendered flag
    - Damage tracking (created, inflicted, taken)
    - First damage trigger (morale penalties)
```

### 4. Encounter (Encounter.cs)

**Role:** Event container and interaction coordinator

**Types:**
- **Settlement** - Trading posts, controlled by faction
- **Crossroads** - Random travelers, mixed factions
- **Resource** - Harvestable materials
- **Hazard** - Dangerous locations

**Lazy Instantiation:**
Encounters are created on-demand when player arrives at a location. Weak references prevent memory leaks.

**Actor Management:**
- Permanent actors (settlements, resources)
- Dynamic actors (travelers with `ExitTime`)
- Tick processing for actor AI

**Menu Generation:**
Dynamically builds interaction menus based on:
- Proposals from all actors
- Relationship state (hostile, neutral, friendly)
- Actor capabilities (vulnerable, destroyed, etc.)

### 5. Inventory (Inventory.cs)

**Role:** Container for commodities and segments

**Commodities (50+ types):**
- **Essential:** Scrap (currency), Fuel, Crew, Morale, Air, Water, Rations
- **Raw Materials:** Biomass, Ore, Silicates
- **Refined:** Metal, Chemicals, Glass
- **Parts:** Ceramics, Polymers, Alloys, Electronics
- **Consumer Goods:** Medicines, Textiles, Gems, Toys, Machines, Media, Passengers
- **Vice:** Liquor, Stims, Downers, Trips (only sold by Bandits)
- **Dangerous:** Explosives, SmallArms, AiCores, Soldiers
- **Religious:** Idols, Texts, Relics

**Commodity Properties:**
- Base value, volume, mass
- Category: Essential, Raw, Refined, Parts, Consumer, Vice, Dangerous, Religious
- Flags: Perishable, Bulky, Integral
- Game tier: Early, Mid, Late

**Segments:**
Stored separately from commodities. Can be:
- Installed (active/deactivated)
- Damaged (disabled/destroyed)
- Packaged (in inventory or trade inventory)

---

## Design Patterns

### 1. Singleton Pattern

**Game.Instance** - Global access to game state
```csharp
static Game? _instance = null;
public static Game Instance => _instance!;
```

### 2. Factory Pattern

**Encounter Generation:**
```csharp
Func<Location, Encounter> NewEncounter
// Stored in Location, invoked lazily
```

**Crawler Creation:**
```csharp
Crawler.NewRandom(location, crew, supplyDays, goodsWealth, segmentWealth, segmentClassWeights, faction)
// Initial fuel calculated based on crawler's FuelPerHr and supplyDays after segments are installed
```

### 3. Proposal/Interaction System

**Three-Level Design for Actor Interactions:**

```
IProposal (capability check)
  ├── AgentCapable(IActor) - Can agent make this proposal?
  ├── SubjectCapable(IActor) - Can subject receive it?
  └── InteractionCapable(Agent, Subject) → InteractionCapability
      ├── Disabled - Not available
      ├── Possible - Available but optional
      └── Mandatory - Must be addressed immediately
      └── GetInteractions() → IInteraction[]

IInteraction (concrete action)
  ├── Enabled() - Can perform now?
  ├── Perform() - Execute action
  └── Description, OptionCode (menu display)

IOffer (exchange component)
  ├── EnabledFor(Agent, Subject)
  ├── PerformOn(Agent, Subject)
  └── ValueFor(Agent)
```

**InteractionCapability System:**
- **Disabled**: Interaction is not available (conditions not met)
- **Possible**: Optional interaction available in normal menus
- **Mandatory**: Urgent interaction displayed prominently with countdown timer
  - Tracked via `ActorToActor.UltimatumTime`
  - Automatically triggers consequence if time expires
  - Displayed at top of menus with highlighting

**Example Proposals:**
- `ProposeLootFree` - Loot destroyed crawler (Possible)
- `ProposeAttackDefend` - Initiate combat (Possible)
- `ProposeAcceptSurrender` - Accept surrender from vulnerable enemy (Possible)
- `ProposeSellBuy` / `ProposeBuySell` - Trading (Possible)
- `ProposeRepairBuy` - Repair services (Possible)
- `ProposeDemand` - Generic "give me A or I'll do B" demand (Mandatory when active)
  - Yields both AcceptDemandInteraction and RefuseDemandInteraction
  - Each has separate option codes (DA for accept, DR for refuse)
- `ProposeExtortion` - Bandit demands cargo or attacks (Mandatory when ultimatum set)
- `ProposeTaxes` - Civilian faction checkpoint taxes (Mandatory when ultimatum set)
- `ProposeContrabandSeizure` - Confiscate prohibited goods or fine (Mandatory when ultimatum set)
- `ProposePlayerDemand` - Player threatens vulnerable NPCs (Possible)

**Why This Design?**
- **Separation of concerns:** Capability checking vs execution
- **Composability:** Offers can be combined in different ways
- **Extensibility:** New proposals don't require changes to Actor class
- **Dynamic menu generation:** Menus built from available proposals
- **Urgency tracking:** Mandatory interactions are clearly distinguished and time-limited

### 4. Mandatory Interaction System (Ultimatums)

**Purpose:** Enable "offers you can't refuse" - time-limited demands triggered on encounter entry.

**Architecture:**
```
Crawler.CheckAndSetUltimatums(other)
  Called by:
  - Encounter.AddActor() → CheckForcedInteractions() - when actor enters

Flow:
1. Actor enters encounter
2. CheckAndSetUltimatums() evaluates conditions and adds proposals to StoredProposals
3. Sets ActorToActor.UltimatumTime = CurrentTime + 300 seconds (5 minutes)
4. Proposal.InteractionCapable() returns Mandatory when UltimatumTime is active
5. Encounter menu displays mandatory interactions at top with countdown
6. Player selects Accept or Refuse interaction
7. UltimatumTime cleared on resolution
```

**Ultimatum Expiration:**
- Every 5 minutes, `Encounter.CheckPeriodicForcedInteractions()` runs
- If `UltimatumTime` reached without response:
  - Auto-execute Refuse interaction (triggers consequence)
  - Display "Time's up!" message to both parties

**Demand Pattern (ProposeDemand):**
```
ProposeDemand(
  Demand: IOffer,              // What they want
  Consequence: IInteraction,   // What happens if refused (created via ConsequenceFn)
  Ultimatum: string,           // Description
  Condition: Func<bool>?       // When to apply (checked in InteractionCapable)
)

Returns two interactions:
  - AcceptDemandInteraction (option code: DA)
  - RefuseDemandInteraction (option code: DR)
```

**Use Cases:**
- **Bandit Extortion:** 60% chance to demand cargo on entry, attacks if refused
  - Triggered via `CheckAndSetUltimatums()` in Crawler.cs
  - Adds `ProposeExtortion` to StoredProposals with 5-min timer
- **Civilian Taxes:** Settlements in own territory demand 5% cargo value
  - Triggered on entry to controlled territory
  - Adds `ProposeTaxes` to StoredProposals with 5-min timer
- **Contraband Seizure:** 70% chance to detect prohibited goods, fine or confiscate
  - Triggered via `ScanForContraband()` check
  - Adds `ProposeContrabandSeizure` to StoredProposals with 5-min timer
- **Player Threats:** Player can manually threaten vulnerable NPCs (not time-limited)

**Contraband System:**
- Each faction has `TradePolicy` per commodity (Legal/Controlled/Prohibited)
- `ScanForContraband()` detects prohibited goods with configurable success rate
- Penalties: Surrender contraband OR pay 2x value fine OR turn hostile

**Menu Display:**
- Mandatory interactions shown at top with "!!! URGENT DEMANDS !!!" header
- Each displays: `>>> {Description} [Time: {Seconds}s]`
- Uses Style.SegmentDestroyed for red highlighting
- Shortcut keys: UA, UB, UC... (U = Urgent)

### 5. Component-Based Segments

Segments use inheritance hierarchy:
```
Segment (base)
├── PowerSegment
│   ├── ReactorSegment (charge + generation)
│   └── ChargerSegment (generation only)
├── TractionSegment (terrain-specific speed/lift)
├── OffenseSegment
│   └── WeaponSegment (generates HitRecords)
└── DefenseSegment
    ├── ArmorSegment (damage reduction)
    └── ShieldSegment (regenerating protection)
```

**Segment Definition vs Instance:**
- `SegmentDef` - Immutable definition with stats
- `Segment` - Mutable instance with damage/state
- Tier system for scaling (size, quality)

### 5. EArray<TEnum, TValue>

Custom generic array indexed by enum:
```csharp
EArray<Commodity, float> commodities;
float fuel = commodities[Commodity.Fuel];
```

Used for:
- Inventory commodities
- Segment class grouping
- Tuning parameters by faction/terrain

---

## Data Model

### IActor Interface

Common interface for all interactive entities:
```csharp
interface IActor {
    // Identity
    string Name;
    Faction Faction;
    Location Location;
    EActorFlags Flags;

    // State
    Inventory Inv;
    string EndMessage;
    EEndState? EndState;

    // Knowledge
    bool Knows(IActor other);
    bool Knows(Location loc);
    ActorToActor To(IActor other);  // Relationship
    ActorLocation To(Location loc); // Visit tracking

    // Actions
    IEnumerable<IProposal> Proposals();
    IEnumerable<IInteraction> ForcedInteractions(IActor other);
    void Tick();
    void Tick(IEnumerable<IActor> others);
    void ReceiveFire(IActor from, List<HitRecord> fire);
    void End(EEndState state, string message);

    // Display
    string Brief(IActor viewer);
    string Report();
    void Message(string message);
}
```

**Implementations:**
- `Crawler` - Mobile vehicles with segments
- `StaticActor` - Settlements, resources, hazards

### ActorToActor (Relationship State)

```csharp
class ActorToActor {
    bool Hostile;           // Currently hostile
    bool Surrendered;       // Has surrendered to this actor
    bool WasHostile;        // Ever created damage
    bool WasDamaged;        // Ever took damage from them
    int DamageCreated;      // Total potential damage sent
    int DamageInflicted;    // Total damage actually dealt
    int DamageTaken;        // Total damage received
}
```

**Dynamic Hostility:**
Bandits check player "evil points" before turning hostile:
```csharp
if (evilness >= threshold) {
    hostilityChance = baseChance * (evilness / threshold);
    result.Hostile = Random() < hostilityChance;
}
```

### ActorLocation (Visit Tracking)

```csharp
class ActorLocation {
    bool Visited;
    long ForgetTime;  // When knowledge expires
}
```

---

## Key Interactions

### Trading System

**Bid-Ask Spread Model:**
```
MidPrice = BaseValue × LocationMarkup × ScarcityPremium × RestrictedMarkup
Spread = MidPrice × BaseBidAskSpread × FactionMultiplier
AskPrice (player buys) = MidPrice + Spread/2
BidPrice (player sells) = MidPrice - Spread/2
```

**Factors:**
- **LocationMarkup:** EncounterMarkup × TerrainMarkup × ScrapInflation
- **ScarcityPremium:** `1 + (1 - Availability) × Weight`
  - Essential goods: 0.3× weight
  - Luxury goods: 1.5× weight
- **BaseBidAskSpread:** 20% base
- **FactionMultiplier:**
  - Trade: 0.8 ± 0.05 (16% spread)
  - Bandit: 1.5 ± 0.1 (30% spread)

**Trader Markup:**
Each crawler has a personal markup (Gaussian distribution) affecting their prices.

### Combat System

**Attack Flow:**
```
1. Attacker.CreateFire()
   - Power budget allocation
   - Weapon selection (weapons prioritized)
   - Generate HitRecords (damage, aim)

2. Defender.ReceiveFire(attacker, hitRecords)
   - Hit type calculation (Miss, Hit, Pierce)
   - Damage phases:
     Phase 0: Shields (absorb and take damage)
     Phase 1: Armor (reduce and take damage)
     Phase 2: Random segment (take damage)
     Phase 3: Crew losses
   - Track damage for relationship
   - Morale penalties

3. Segment.AddDmg(hitType, damage)
   - Apply damage to segment
   - State transitions (Active → Disabled → Destroyed)
   - Return remaining damage
```

**Morale System:**
- First damage taken: penalty
- Friendly fire: larger penalty (reduced by attacker's evil points)
- Destroying hostile: bonus
- Low morale → revolt (game over)

### Movement System

**Fuel/Time Calculation:**
```csharp
float distance = Location.Distance(destination);
float terrainSpeed = min(currentTerrainSpeed, destTerrainSpeed);
float time = distance / terrainSpeed;
float fuel = MovementFuelPerKm × distance + FuelPerHr × time;
```

**Speed Calculation:**
```
For each terrain tier (Flat → current terrain):
    speed = Σ(traction segments' speed on tier)
    drain = Σ(traction segments' drain on tier)

    // Power limitation
    if (drain > generation):
        speed *= generation / drain

    // Weight penalty
    if (weight > lift):
        liftFraction = weight / lift
        speed /= liftFraction ^ 0.6
        drain *= liftFraction

    Use best speed found across terrain tiers
```

**Pinned Mechanic:**
Cannot flee if any hostile crawler is faster than you.

---

## Game Loop

### Turn Structure

```
Main Loop:
    if Moving:
        Player.Location = PendingLocation
        Map.AddActor(Player)

    GameMenu()  // AP cost returned

    while AP <= 0:
        Tick()  // Advance time by 1 second
        AP += TurnAP

    Check win/loss conditions
```

### Tick System

**Every Second:**
- Time advances (`TimeSeconds++`)
- Moving crawler ticks (1 hour of resources consumed per hour of travel)
- Current encounter ticks (actors take AI actions)

**Hourly (TimeSeconds % 3600 == 0):**
- Player recharge (1 hour of generation)
- Resource consumption checks (rations, water, air)
- Crew loss from starvation/dehydration/suffocation
- Morale loss from depowered life support
- Clean weak encounter references

**Recharge:**
```
Tick all segments
Generate power (reactors + chargers)
Feed power to reactors (charge storage)
Consume: wages, rations, water, air, fuel
```

### AI Behavior (Bandit Tick)

```
if IsDepowered:
    Message "radio silent"
    return

if IsVulnerable and not Pinned:
    Message "flees"
    RemoveFromEncounter()
    return

foreach hostile player:
    Attack(player)
```

---

## Subsystems

### Save/Load System

**Serialization:** YAML via YamlDotNet

**SaveGameData Structure:**
```
Hour, AP, TurnAP, Quit
Map → MapSaveData (sectors, locations, terrain)
Player → CrawlerSaveData (inventory, segments, relations, visits)
CurrentLocationPos (for reconnection)
```

**Reconstruction:**
1. Rebuild map from save data
2. Find current location by position
3. Reconstruct player crawler
4. Add player to map

### Power System

**Two Power Segment Types:**
- **ReactorSegment:** Battery (charge storage) + generation
- **ChargerSegment:** Generation only (solar panels, etc.)

**Power Flow:**
```
Generation Phase:
    overflowPower = Σ(all power segments' generation)
    FeedPower(overflowPower)  // Distribute to reactors by capacity

Consumption Phase:
    DrawPower(amount)  // Pull from reactors by current charge
```

**Distribution:**
- Feed: Proportional to available capacity
- Draw: Proportional to current charge

### Packaging System

Segments can be packaged for trade:
- Must have 0 hits (undamaged)
- Must not be destroyed
- Packaged segments don't contribute to crawler stats
- Can be moved to trade inventory for NPC visibility

### Menu System

**Dynamic Menu Construction:**
```
GameMenu:
    LocalMenuItems (travel within sector)
    WorldMapMenuItems (travel between sectors)
    EncounterMenuItems (interactions with actors)
    PlayerMenuItems (manage crawler)
    GameMenuItems (meta actions: save, quit, etc.)
```

**Menu Item Types:**
- `MenuItem` - Display only
- `ActionMenuItem` - Executes function, returns AP cost
- `EnableArg` / `ShowArg` - Control visibility

**Shortcut System:**
- Single character shortcuts (M, W, R, K, Q, etc.)
- Nested shortcuts (CA, CA1, CAT1 for Contact Actor 1, Trade option 1)
- Hidden shortcuts for direct access (e.g., "PP1" to toggle power on segment 1)

---

## Extension Points

### Adding New Proposals

```csharp
record ProposeMyAction(string OptionCode): IProposal {
    public bool AgentCapable(IActor Agent) => /* check agent */;
    public bool SubjectCapable(IActor Subject) => /* check subject */;
    public bool InteractionCapable(IActor Agent, IActor Subject) => /* check context */;

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new MyInteraction(Agent, Subject);
    }

    record MyInteraction(IActor Agent, IActor Subject): IInteraction {
        public bool Enabled(string args = "") => true;
        public int Perform(string args = "") { /* execute */ return apCost; }
        public string Description => "My Action";
        public string OptionCode => "MA";
    }
}

// Add to Game.StoredProposals or Crawler.StoredProposals
```

### Adding New Segments

```csharp
// 1. Define segment def
var mySegmentDef = new MySegmentDef(
    Symbol: 'X',
    Size: new Tier(2),  // Size 2
    Name: "MySegment",
    SegmentKind: SegmentKind.Offense,
    WeightTier: new Tier(2),
    DrainTier: new Tier(1),
    CostTier: new Tier(3),
    MaxHitsTier: new Tier(2)
);

// 2. Create segment class (if custom behavior needed)
class MySegment(SegmentDef def, IActor? owner): OffenseSegment(def, owner) {
    public override List<HitRecord> GenerateFire(int aimBonus) {
        // Custom attack logic
    }
}

// 3. Add to SegmentEx.AllDefs for procedural generation
```

### Adding New Commodities

```csharp
// 1. Add to Commodity enum
public enum Commodity {
    // ... existing
    MyNewCommodity,
}

// 2. Add data to CommodityEx.Data
new CommodityData(
    BaseValue: 50f,
    Volume: 0.1f,
    Mass: 0.05f,
    Flag: CommodityFlag.None,
    Tier: GameTier.Mid
)

// 3. Update Tuning.Economy availability if needed
```

### Adding New Factions

Civilian factions (Civilian0-19) are dynamically generated during world creation. To add new non-civilian factions:

```csharp
// 1. Add to Faction enum
public enum Faction {
    // ... existing
    MyFaction,
}

// 2. Update Tuning.Encounter.crawlerSpawnWeight
// 3. Define faction behavior in Crawler.NewRelation()
// 4. Add faction-specific proposals if needed
```

### Tuning Parameters

All game balance parameters live in `Tuning.cs`:
- `Tuning.Crawler` - Resource consumption rates, morale adjustments
- `Tuning.Economy` - Price markups, availability curves
- `Tuning.Trade` - Bid-ask spreads, faction markups, contraband detection
- `Tuning.Segments` - Tier value tables
- `Tuning.Encounter` - Spawn weights, generation parameters
- `Tuning.Game` - Core mechanics (loot return, etc.)
- `Tuning.Bandit` - Extortion chances, demand fractions, value thresholds
- `Tuning.Civilian` - Tax rates, contraband scanning, penalty multipliers
- `Tuning.FactionPolicies` - Trade policies per faction (Legal/Taxed/Restricted/Prohibited)

---

## Technical Notes

### Memory Management

**Weak References for Encounters:**
```csharp
List<WeakReference<Encounter>> allEncounters
```
Cleaned every hour to prevent memory leaks from dynamically created encounters.

### Console UI

**ANSI Formatting:**
- `CrawlerEx.CursorPosition(x, y)` - Position cursor
- `CrawlerEx.ClearScreen` - Clear display
- `Style` enum for colored/formatted text

**Message Queue:**
- Messages accumulated during turn
- Displayed after menu rendering
- Cleared after display

### Random Generation

**Gaussian Distribution:**
```csharp
CrawlerEx.NextGaussian(mean, stddev)
```
Used for:
- Trader markup variance
- Price fluctuations
- Stat generation

**Pareto Distribution:**
```csharp
Population = Clamp(MaxPop × 0.005^zipf, 0, MaxPop)
```
Used for location populations (power law distribution).

### Coordinate System

**Sector Grid:**
- Integer coordinates (x, y)
- Horizontal wrapping (toroidal topology on X-axis)

**Location Positions:**
- Vector2 within sector (fractional coordinates)
- Offset and Distance calculation accounts for wrap-around

**Tech Latitude:**
```csharp
TechLatitude = 2 × (1 - ((Y + 0.5) / MapHeight))
```
North = higher tech, South = lower tech

---

## Summary

This architecture prioritizes:

1. **Modularity** - Component-based segments, proposal system for interactions
2. **Extensibility** - Easy to add commodities, segments, factions, proposals
3. **Separation of Concerns** - Game loop, world model, actor behavior, UI separated
4. **Data-Driven Design** - Tuning parameters, segment definitions, commodity data
5. **Performance** - Lazy instantiation (encounters), weak references, EArray indexing
6. **Persistence** - Clean save/load via YAML serialization

The proposal/interaction system is the key architectural innovation, enabling complex actor interactions without tight coupling between actor types and action implementations.
