# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Crawler** is a console-based roguelike trading and exploration game set in a post-apocalyptic wasteland. Players pilot modular vehicles (crawlers) through a procedurally generated world, engaging in trading, tactical combat, and resource management.

**Language:** C# (.NET 9.0)
**Architecture:** Component-based with event-driven interactions

## Building and Running

### Build
```bash
dotnet build Crawler.sln
```

### Run
```bash
dotnet run --project Crawler/Crawler.csproj
```

### Build Configuration
- Debug builds include full debugging information
- Release builds also include full debugging information (see Crawler.csproj)
- Uses C# 13 features with implicit usings enabled
- Allows unsafe blocks for performance-critical sections

## Development Commands

### Testing
The project uses an arena test system for combat balance testing (referenced in README.md).

### Dependencies
- **YamlDotNet** (16.3.0) - Save/load serialization
- **CsvHelper** (33.1.0) - Data import/export
- **MathNet.Numerics** (5.0.0) - Mathematical operations
- **OpenTelemetry** (1.13.1) - Activity tracing and observability
- **Microsoft.Windows.CsWin32** (0.3.183) - Windows API interop
- **PInvoke.Kernel32** (0.7.124) - Kernel32 API bindings

## Architecture Overview

### Core Design Patterns

**1. Proposal/Interaction System**
- Three-level design: `IProposal` → `IInteraction` → `IOffer`
- Zero coupling between actors and action implementations
- Enables dynamic menu generation and complex interactions
- Key innovation: Boolean capability checks with mandatory/optional distinction via `Immediacy` enum

**2. Component-Based Actor Behaviors**
- Crawlers have pluggable `ICrawlerComponent` instances
- Components subscribe to encounter events and generate proposals on-demand
- Event-driven architecture eliminates stale cached state
- Components implement `ThinkAction()` for NPC AI behaviors

**3. Event-Driven Encounters**
- Encounters publish events: `ActorArrived`, `ActorLeaving`, `ActorLeft`, `EncounterTick`
- Components subscribe to specific events and respond accordingly
- Enables clean separation of concerns and easy extensibility

**4. Deterministic Random Number Generation**
- Custom XorShift RNG with seed-based initialization
- Each Crawler maintains its own RNG state for reproducibility
- Child objects receive seeds from parent RNG via `Seed()` method
- **CRITICAL RULE:** Child objects must NEVER access parent RNG directly

### Critical Architecture Rules

**RNG Pattern (MANDATORY):**
```csharp
// ❌ WRONG - Never access parent RNG directly
(Owner as Crawler)?.GetRng()?.NextSingle()

// ✅ CORRECT - Accept seed as parameter
public Segment NewSegment(ulong seed) {
    var rng = new XorShift(seed);
    // Use rng for initialization
}

// ✅ CORRECT - Derive RNG using path operator
var proposal1 = new ProposeAttackOrLoot(Rng/1, demandFraction);
var proposal2 = new ProposeAttackOrLoot(Rng/2, demandFraction);
```

**Known Violations:** Segment.cs lines 254, 267, 282 (GunSegment, LaserSegment, MissileSegment) - do not replicate this pattern.

### System Architecture

```
Game (Singleton)
├── Map (Procedural world generation)
│   ├── Sectors (Grid-based, wraps horizontally)
│   │   ├── Locations (Vector positions, terrain types)
│   │   │   └── Encounters (Event publishing + actor coordination)
│   │   │       ├── Event Handlers (Components subscribe to events)
│   │   │       └── Actors (Crawlers, Static entities)
│   │   │           ├── Components (Pluggable behaviors)
│   │   │           ├── Inventory (Supplies + Cargo)
│   │   │           │   └── Segments (Modular functionality)
│   │   │           └── Relations (Actor-to-Actor state)
│   └── Time System (Tick-based, per-second updates)
├── Player (Crawler instance)
├── Menu System (ANSI console UI)
└── Save/Load (YAML serialization)
```

### Key Files

**Core:**
- `Game.cs` - Main game loop, AP system, turn processing
- `Crawler.cs` - Primary actor class with component system
- `Encounter.cs` - Event container and interaction coordinator
- `Map.cs` - Procedural world generation
- `IActor.cs` - Common interface for all interactive entities

**Systems:**
- `Interaction/Interactions.cs` - IInteraction implementations
- `Interaction/Offers.cs` - IOffer implementations
- `Interaction/Proposals.cs` - IProposal implementations
- `Interaction/Trade.cs` - Trading proposals
- `Components.cs` - Component base classes
- `ActorComponents.cs` - Concrete component implementations
- `Segment.cs` - Modular crawler components (weapons, power, traction, defense)
- `Inventory.cs` - Commodity and segment containers
- `XorShift.cs` - Deterministic RNG system

**Data & Configuration:**
- `Tuning.cs` - Centralized game balance parameters
- `CommodityEx.cs` - Commodity data and categories
- `SegmentEx.cs` - Segment definitions and factories
- `Trade.cs` - Trading system and pricing logic

**Persistence:**
- Save/load uses Init/Cfg/Data pattern
- `ActorBase.Data`, `ActorScheduled.Data`, `Crawler.Data` form inheritance hierarchy
- Components are not serialized - reconstructed based on role and seed

## C# Style Guidelines

Use modern C# 13 patterns:
- File-scoped namespaces
- Primary constructors where appropriate
- Collection expressions `[]` instead of `new List<>()`
- Prefer `new()` over `new T()`
- Use `[Flags]` attribute with `HasFlag()` and `SetFlag()` instead of bitwise operators
- Prefer `var` for local variables
- LINQ pipeline style for data transformations

## Key Architectural Concepts

### Action Scheduling System
- `Crawler.ConsumeTime(long delay, Action<Crawler>? action)` commits actors to time periods
- Sets `NextEvent = LastEvent + delay` and stores optional callback
- Blocks `Think()` from running until NextEvent is reached
- Used for multi-actor interactions (repairs, trades) with automatic cleanup

### UltimatumState System
- Lives on `ActorToActor` relationships (directional: A → B)
- Used for time-limited demands AND multi-actor interaction state tracking
- Structure: `{ long ExpirationTime, string Type, object? Data }`
- Bidirectional ultimatums enable complex multi-turn interactions with automatic cleanup

### Mandatory Interactions (Ultimatums)
- Time-limited demands with 5-minute default timeout
- Examples: Bandit extortion, customs taxes, contraband seizure
- `ProposeDemand` pattern creates Accept/Refuse interaction pairs
- Auto-executes Refuse interaction on timeout

### Component Priorities
Components execute `ThinkAction()` in priority order (higher first):
- **RetreatComponent:** 1000 (survival first)
- **CombatComponentAdvanced:** 600 (Bandit AI)
- **BanditComponent:** 600 (extortion)
- **TradeOfferComponent:** 500 (default)
- **CombatComponentDefense:** 400 (generic combat)

### Segment States
```
Active ←→ Deactivated
  ↓
Disabled (Hits > 0)
  ↓
Destroyed (Hits >= MaxHits)

Any → Packaged (for trading)
```

### Trade Policy System
Policies are defined per **CommodityCategory** and **SegmentKind**, not individual items:
- **Subsidized** (0.7x), **Legal** (1.0x), **Taxed** (1.3x), **Controlled** (1.75x), **Prohibited**
- Faction policies generated by combining 3 random archetypes from 12 options
- Individual commodities/segments inherit policy from their category/kind

## Documentation Structure

**Primary documentation lives in `Crawler/Docs/`:**
- **DOCS.md** - Documentation structure and update guidance
- **ARCHITECTURE.md** - High-level overview and system map
- **SYSTEMS.md** - Detailed system descriptions (RNG, proposals, trading, combat, movement, power, ticking)
- **DATA-MODEL.md** - Class and interface references, data structures
- **EXTENDING.md** - How to add new content (components, proposals, segments, commodities, factions)
- **TODO.md** - Future development plans and ideas
- **RULES.md** - AI assistant guidelines and git workflow

**When making architectural changes, update the relevant documentation file(s).**

## Common Development Tasks

### Adding a New Component
1. Create class inheriting from `CrawlerComponentBase` in `ActorComponents.cs`
2. Implement `SubscribedEvents` property for event subscriptions
3. Override `HandleEvent()` to respond to encounter events
4. Override `GenerateProposals()` if component provides interactions
5. Override `ThinkAction()` if component needs NPC AI behavior
6. Set `Priority` if execution order matters
7. Update SYSTEMS.md and DATA-MODEL.md

### Adding a New Proposal/Interaction
1. Create record implementing `IProposal` in `Proposals.cs`
2. Implement capability checks: `AgentCapable()`, `SubjectCapable()`, `InteractionCapable()`
3. Create `IInteraction` record with `Immediacy()` and `Perform()` methods
4. Add to appropriate actor's `StoredProposals` or component's `GenerateProposals()`
5. Update SYSTEMS.md

### Adding a New Segment
1. Create `SegmentDef` in `SegmentEx.cs` with appropriate tiers
2. Create segment class inheriting from base type (PowerSegment, TractionSegment, OffenseSegment, DefenseSegment)
3. Add to `SegmentEx.AllDefs` for procedural generation
4. Update DATA-MODEL.md

### Adding a New Commodity
1. Add to `Commodity` enum in `Inventory.cs`
2. Add `CommodityData` in `CommodityEx.cs`
3. Set category in `CommodityEx.Category()` method
4. Configure availability if needed
5. Update DATA-MODEL.md

## Special Git Workflow

The author uses a specific git stashing workflow for preparing commits. Only use this when explicitly requested with the phrase "prepare stashes for commit":

1. Create backup stash before starting
2. Work backward from current state: save modified files, revert to HEAD
3. Apply changes one logical commit at a time using Edit tool
4. Stage and stash each group: `git stash push --staged -m "message"`
5. Include documentation updates in each stash
6. Use imperative mood for stash messages (will become commit messages)
7. Omit Claude branding from all output

## Important Notes

- **Never commit changes** unless explicitly asked - all commits require manual review
- Player is actively using AI (Claude Agent in Rider) for development
- Save/load system is AI-maintained - treat as black box unless debugging
- OpenTelemetry tracing uses `LogCat` class (renamed from `ActivitySources`)
- Console UI uses ANSI formatting for colors and positioning
- The game loop uses Action Points (AP) system for turn management
- Terrain types affect movement speed, fuel consumption, and segment effectiveness
- Combat uses three-phase damage: shields → armor → segments → crew
- Morale system can trigger game over (revolt at 0 morale)

## Logging Standards

Use severity levels appropriately:
- **Fatal/Critical:** Unsafe to continue (OOM, corruption)
- **Error:** Must shut down safely (assertions, critical asset missing)
- **Warning:** Recoverable errors (missing files, timeouts)
- **Display/Information:** Infrequent major events
- **Log/Trace:** Periodic state, frequent minor events
- **Verbose/Debug:** Frequent debug events
- **VeryVerbose/Debug:** Very frequent or marginally important debug

Prioritize searchability in log messages.
