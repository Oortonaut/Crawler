# Crawler Architecture

**Last Updated:** 2025-10-19

## Quick Navigation
- **Looking for system details?** → [SYSTEMS.md](SYSTEMS.md)
- **Looking for data structures?** → [DATA-MODEL.md](DATA-MODEL.md)
- **Want to add new content?** → [EXTENDING.md](EXTENDING.md)

## Recent Changes
- **2025-11-08**: Replaced `Random.Shared` with custom XorShift RNG for deterministic, seed-based randomness; all Crawlers and child objects now use seeded RNG; added `GaussianSampler` class for Box-Muller transform; updated extension methods (`ChooseRandom`, `StochasticInt`, `SamplePoisson`) to accept RNG parameter
- **2025-10-25**: Renamed `ActivitySources` to `LogCat` for OpenTelemetry logging categories
- **2025-10-25**: Renamed `InteractionMode` enum to `Immediacy` and `PerformMode()` method to `Immediacy()`
- **2025-10-24**: Added mutual hostility checks to InteractionCapable methods to prevent trading/repairing/taxing with hostile actors
- **2025-10-24**: Added comprehensive OpenTelemetry activity tracing to interaction system (InteractionsWith, TickInteractions, MenuItems)
- **2025-10-20**: Refactored demand system with ProposeDemand base class: Created abstract `ProposeDemand` base class for ultimatum-style demands ("comply or face consequence"). All taxes, extortions, and contraband seizures now inherit from this class. Added `AttackOffer` and `HostilityOffer` to encapsulate combat and diplomatic consequences as offers. Removed specialized interaction classes (`CooperateInteraction`, `RefuseDemandInteraction`, `ContrabandInteraction`) - all demands now use `ExchangeInteraction` with appropriate offers.
- **2025-10-20**: Refactored Meet/Part methods into role-based API: Meet(IEnumerable<IActor>) calls Greet(IActor) for each; Leave(IEnumerable<IActor>) calls Part(IActor) for each. Extracted shared threat-generation logic (bandit extortion, contraband scanning) into helper functions (SetupBanditExtortion, SetupContrabandAndTaxes, ExpireProposals).
- **2025-10-19**: Added EFlags enum to ActorToActor; replaced boolean fields with flag-based properties (Hostile, Surrendered, Spared, Betrayed) using SetFlag/HasFlag pattern
- **2025-10-19**: Replaced InteractionCapability enum with bool across proposals - simplified from three-state (Disabled, Possible, Mandatory) to two-state (enabled/disabled); moved urgency control to IInteraction.Immediacy()
- **2025-10-19**: Refactored interaction system into separate files: Interactions.cs (IInteraction types), Offers.cs (IOffer types), Proposals.cs (IProposal types), Trade.cs (trading proposals)
- **2025-01-19**: Mandatory interactions now use ultimatum timers
- **2025-01-19**: Documentation split into focused files for easier maintenance

---

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
│   │   │           └─2 Inventory: Supplies + Cargo (Commodities/Segments)
│   │   │               └─< Segments (Crawler Functionality)
│   │   │           └─< Relations (Actor-to-Actor state)
│   │   └── Faction Control (Regional powers)
│   └── Time System (Tick-based, hourly updates)
├── Player (Crawler instance)~~~~
├── Menu System (Console UI)
└── Save/Load (YAML serialization)
```

---

## Core Components

### Game (Game.cs)
**Singleton coordinator for the main game loop**

**Responsibilities:**
- Main game loop with AP (Action Points) system
- Turn processing and time advancement
- Menu orchestration and player input
- Win/loss condition checking
- Save/load coordination

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

### Map (Map.cs)
**Procedural world generation and spatial organization**

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

### Crawler (Crawler.cs)
**Primary actor representing vehicles (player and NPCs)**

**Component-Based Architecture:**
```
Crawlers are built from segments across 4 classes:
1. Power    - Reactors (charge storage + generation), Chargers (generation only)
2. Traction - Movement systems (wheels, tracks, legs, hover)
3. Offense  - Weapons and attack systems
4. Defense  - Shields and armor
```

**Critical States:**
- `IsDepowered` - No power segments or no fuel
- `IsImmobile` - Cannot move on current terrain
- `IsDisarmed` - No offense capability
- `IsDefenseless` - No defense segments
- `IsVulnerable` - Any of the above (triggers AI flee/surrender)

### Encounter (Encounter.cs)
**Event container and interaction coordinator**

**Types:**
- **Settlement** - Trading posts, controlled by faction
- **Crossroads** - Random travelers, mixed factions
- **Resource** - Harvestable materials
- **Hazard** - Dangerous locations

**Actor Management:**
- Permanent actors (settlements, resources)
- Dynamic actors (travelers with `ExitTime`)
- Tick processing for actor AI

### Inventory (Inventory.cs)
**Container for commodities and segments**

**Commodity Categories:**
- **Essential:** Scrap (currency), Fuel, Crew, Morale, Air, Water, Rations
- **Raw Materials:** Biomass, Ore, Silicates
- **Refined:** Metal, Chemicals, Glass, Ceramics, Polymers, Alloys
- **Consumer Goods:** Medicines, Textiles, Gems, Toys, Machines, Media
- **Vice:** Liquor, Stims, Downers, Trips (only sold by Bandits)
- **Dangerous:** Explosives, SmallArms, AiCores, Soldiers
- **Religious:** Idols, Texts, Relics

---

## Key Design Patterns

### 1. Proposal/Interaction System
**Three-level design for all actor interactions**

See [SYSTEMS.md#proposal-interaction-system](SYSTEMS.md#proposal-interaction-system) for details.

```
IProposal (capability check) → IInteraction (action) → IOffer (exchange)
```

**Key Innovation:**
- Boolean capability checks determine if proposals are available
- Mandatory interactions managed via ultimatum timers
- Ultimatum system for time-limited demands

**Benefits:**
- Zero coupling between actors and action implementations
- Easy to add new interactions without modifying actor classes
- Dynamic menu generation based on context
- Clear separation of capability checks vs execution

### 2. Component-Based Segments
**Crawlers built from modular parts**

See [DATA-MODEL.md#segments](DATA-MODEL.md#segments) for details.

**Segment States:**
```
Active → Deactivated → Disabled (damaged) → Destroyed → Packaged (for trade)
```

### 3. Singleton Pattern
**Global access to game state**

```csharp
Game.Instance  // Central coordinator
```

### 4. Factory Pattern
**Lazy instantiation for performance**

- Encounters created on-demand when player arrives
- Weak references prevent memory leaks
- Dynamic actor generation based on location/faction

---

## Core Systems Overview

For detailed information, see [SYSTEMS.md](SYSTEMS.md)

### Trading System
- Bid-ask spread model
- Dynamic pricing based on location, scarcity, faction policy
- Contraband scanning and enforcement

### Combat System
- Turn-based with power management
- Three-phase damage (shields → armor → segments → crew)
- Morale system affects gameplay

### Movement System
- Fuel/time calculation based on terrain
- Speed limited by power generation and weight
- Pinned mechanic prevents escape from faster enemies

### Mandatory Interaction System
- Time-limited ultimatums (5 minutes)
- Accept/Refuse choice system
- Auto-consequence on expiration

---

## Data Structures

For detailed information, see [DATA-MODEL.md](DATA-MODEL.md)

### IActor Interface
Common interface for all interactive entities (Crawlers, Static entities)

### ActorToActor
Relationship state tracking (hostile, surrendered, damage history)

### ActorLocation
Visit tracking and knowledge management (renamed from LocationActor in recent commits)

### Segments
Component hierarchy for crawler functionality

---

## Extension Points

For detailed information, see [EXTENDING.md](EXTENDING.md)

### Easy to Add:
- New proposals/interactions
- New segments
- New commodities
- New factions
- Tuning parameters

---

## Technical Foundation

### Memory Management
- Weak references for encounters
- Lazy instantiation patterns
- Hourly cleanup of expired references

### Console UI
- ANSI formatting for colors/positioning
- Dynamic menu construction
- Message queue system

### Persistence
- YAML serialization via YamlDotNet
- Save/load reconstruction of game state

### Random Generation (XorShift.cs)
- **XorShift RNG** - Custom 64-bit xorshift* generator for deterministic, reproducible randomness
  - Each `Crawler` instance has its own RNG state
  - Supports seeding for deterministic replay and testing
  - State can be saved/loaded for game persistence
  - Methods: `NextSingle()`, `NextDouble()`, `NextInt()`, `Seed()` (generates child seeds)
- **GaussianSampler** - Box-Muller transform for normal distribution
  - Each `Crawler` has its own Gaussian sampler instance
  - State includes primed flag, cached value, and RNG state
  - Used for trait variation (markup, spread, etc.)
- **Seed-based initialization** - All random objects accept seed parameters
  - Crawlers, Sectors, Locations, Segments, Inventories
  - Child objects receive seeds from parent RNG via `Seed()` method
  - Ensures deterministic behavior without direct parent RNG access
- **Distribution functions** - Pareto, Poisson, Exponential sampling
- **Procedural world generation** with fully seeded RNG chain

### Observability
- OpenTelemetry activity tracing via `LogCat` class
- Activity sources for Interaction, Encounter, Game, and Console subsystems
- Structured logging with tags for debugging and telemetry

---

## Architecture Principles

This architecture prioritizes:

1. **Modularity** - Component-based segments, proposal system for interactions
2. **Extensibility** - Easy to add commodities, segments, factions, proposals
3. **Separation of Concerns** - Game loop, world model, actor behavior, UI separated
4. **Data-Driven Design** - Tuning parameters, segment definitions, commodity data
5. **Performance** - Lazy instantiation, weak references, efficient indexing
6. **Persistence** - Clean save/load via YAML serialization

**The proposal/interaction system is the key architectural innovation**, enabling complex actor interactions without tight coupling between actor types and action implementations.

---

## File Guide

- **[SYSTEMS.md](SYSTEMS.md)** - Detailed system descriptions (trading, combat, movement, etc.)
- **[DATA-MODEL.md](DATA-MODEL.md)** - Class and interface references
- **[EXTENDING.md](EXTENDING.md)** - How to add new content to the game
- **[../Architecture.md](../Architecture.md)** - Original comprehensive doc (deprecated, kept for reference)
