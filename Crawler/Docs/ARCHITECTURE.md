# Crawler Architecture

**Last Updated:** 2025-10-19

## Quick Navigation
- **Looking for system details?** → [SYSTEMS.md](SYSTEMS.md)
- **Looking for data structures?** → [DATA-MODEL.md](DATA-MODEL.md)
- **Want to add new content?** → [EXTENDING.md](EXTENDING.md)
- **Need the old comprehensive doc?** → [../Architecture.md](../Architecture.md) (deprecated, kept for reference)

## Recent Changes
- **2025-10-20**: Refactored Meet/Part methods into role-based API: Meet(IEnumerable<IActor>) calls Greet(IActor) for each; Leave(IEnumerable<IActor>) calls Part(IActor) for each. Extracted shared threat-generation logic (bandit extortion, contraband scanning) into helper functions (SetupBanditExtortion, SetupContrabandAndTaxes, ExpireProposals).
- **2025-10-19**: Added EFlags enum to ActorToActor; replaced boolean fields with flag-based properties (Hostile, Surrendered, Spared, Betrayed) using SetFlag/HasFlag pattern
- **2025-10-19**: Replaced InteractionCapability enum with bool across proposals - simplified from three-state (Disabled, Possible, Mandatory) to two-state (enabled/disabled)
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
│   │   │           └── Inventory (Commodities + Segments)
│   │   │               └─< Segments (Crawler Functionality)
│   │   │           └─< Relations (Actor-to-Actor state)
│   │   └── Faction Control (Regional powers)
│   └── Time System (Tick-based, hourly updates)
├── Player (Crawler instance)
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
Visit tracking and knowledge management

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

### Random Generation
- Gaussian distribution for variance
- Pareto distribution for populations
- Procedural world generation

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
