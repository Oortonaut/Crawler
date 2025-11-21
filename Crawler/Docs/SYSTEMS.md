# Crawler Systems

**Last Updated:** 2025-01-19
**Parent:** [ARCHITECTURE.md](ARCHITECTURE.md)

## Quick Navigation
- [Random Number Generation System](#random-number-generation-system)
- [Proposal/Interaction System](#proposalinteraction-system)
- [Mandatory Interaction System](#mandatory-interaction-system-ultimatums)
- [Trading System](#trading-system)
- [Combat System](#combat-system)
- [Movement System](#movement-system)
- [Power System](#power-system)
- [Tick System](#tick-system)

## Recent Changes
- **2025-11-08**: Replaced `Random.Shared` with custom XorShift RNG; all random operations now use seeded RNG for determinism; combat uses local RNG instances spawned from Crawler RNG
- **2025-10-29**: Trade proposals now use actor's faction instead of passing it as parameter; improved faction policy generation with multiple archetype selection; settlements now display civilian population
- **2025-10-29**: Commodity availability calculation changed to use unavailability decay model; silicate prices rounded to 85¢¢
- **2025-10-29**: Improved settlement generation with log-based dome scaling and passenger-based population
- **2025-10-25**: Renamed `InteractionMode` enum to `Immediacy`; IInteraction.PerformMode() renamed to IInteraction.Immediacy()
- **2025-10-25**: Added comprehensive OpenTelemetry tracing via `LogCat` (renamed from `ActivitySources`)
- **2025-10-24**: Added mutual hostility checks to prevent trading/repairing/taxing with hostile actors

---

## Random Number Generation System

**File:** `XorShift.cs`
**Purpose:** Deterministic, reproducible randomness for game simulation

### Architecture

**Core Principle:** Every entity that needs randomness maintains its own seeded RNG state. Child objects receive seeds from parent RNG, never access parent RNG directly.

### XorShift RNG

**64-bit xorshift* generator with high-quality output:**
```csharp
struct XorShift {
    ulong state;

    // Core generation
    float NextSingle();      // [0, 1) uniform
    double NextDouble();     // [0, 1) uniform (double precision)
    int NextInt(int max);    // [0, max) uniform
    ulong Seed();            // Generate child seed

    // State management
    ulong GetState();
    void SetState(ulong state);
}
```

**Properties:**
- **Deterministic** - Same seed always produces same sequence
- **Fast** - Simple bit operations, no divisions
- **Non-cryptographic** - NOT suitable for security, perfect for games
- **Period** - 2^64 - 1 (never repeats within game timescale)

### GaussianSampler

**Box-Muller transform for normal distribution:**
```csharp
class GaussianSampler {
    XorShift rng;
    bool primed;       // Has cached value
    double zSin;       // Cached value from previous transform

    double Next();                          // N(0, 1)
    double Next(double mean, double stdDev); // N(mean, stdDev²)

    // State management for save/load
    ulong GetRngState();
    void SetRngState(ulong state);
    bool GetPrimed();
    void SetPrimed(bool value);
    double GetZSin();
    void SetZSin(double value);
}
```

**Used for:**
- Trader markup variance (mean=1.05, stdDev=0.07)
- Trader spread variance (mean=0.2, stdDev=0.05)
- Trait variation in generated entities

### Seeding Pattern

**Hierarchical seed propagation:**
```
Game RNG (master seed)
  ↓ seed
Map RNG
  ↓ seed for each sector
Sector[i,j] RNG
  ↓ seed for each location
Location[k] RNG
  ↓ seed for crawler
Crawler RNG (instance)
  ↓ seed for inventory
  ↓ seed for gaussian sampler
  ↓ seed for child RNG in methods
```

**Two idioms for derived RNG:**

1. **Sequence-based seeding** - Creates new child RNG with seed from parent:
```csharp
// Parent creates seeds for child
var rng = new XorShift(masterSeed);
var crawlerSeed = rng.Seed();
var invSeed = rng.Seed();

// Child objects use their seeds
var inv = new Inventory();
inv.AddRandomInventory(invSeed, ...);
var crawler = new Crawler(crawlerSeed, faction, location, inv);
```

2. **Path-based seeding** - Creates derived RNG using `/` operator with identifier:
```csharp
// Derive RNG with path identifier (number, string, or other type)
var proposal1 = new ProposeAttackOrLoot(Rng/1, demandFraction);
var proposal2 = new ProposeAttackOrLoot(Rng/2, demandFraction);
var namedRng = Rng/"weapon_fire";

// Operators available:
// XorShift / XorShift  - Combine two RNG states
// XorShift / string    - Hash string into derived state
// XorShift / int       - Mix integer into derived state
// XorShift / ulong     - Mix ulong into derived state
// XorShift / object    - Hash object into derived state
// (Also: long, uint, ushort, short, char, byte, sbyte)
```

**Example - Combat fire creation:**
```csharp
// Method creates local RNG from instance RNG
public List<HitRecord> CreateFire() {
    var rng = new XorShift(Rng.Seed());  // Local RNG
    // ... use rng for all randomness in this method
    foreach (WeaponSegment weapon in selectedWeapons) {
        fire.AddRange(weapon.GenerateFire(rng.Seed(), 0));
    }
    return fire;
}
```

**Why this pattern?**
- ✅ Deterministic replay - same seed = same game
- ✅ Saveable state - RNG state persists across saves
- ✅ No coupling - child objects don't access parent RNG
- ✅ Thread-safe potential - each entity has independent RNG
- ✅ Testable - can reproduce specific scenarios
- ✅ Path-based seeding - Stable seeds for named contexts without sequence dependence

### Extension Methods

**Updated signatures to accept RNG:**
```csharp
// Random selection
T? ChooseRandom<T>(ref this XorShift rng, IEnumerable<T> seq)
T? ChooseWeightedRandom<T>(IEnumerable<(T, float)> seq, ref XorShift rng)
IReadOnlyList<T> ChooseRandomK<T>(IEnumerable<T> seq, int k, ref XorShift rng)

// Stochastic conversion
int StochasticInt(this float value, ref XorShift rng)

// Distributions
int SamplePoisson(float lambda, ref XorShift rng)
float SampleExponential(float t)  // Uses quantile transform

// Enum sampling
ENUM ChooseRandom<ENUM>(ref this XorShift rng) where ENUM : struct, Enum
```

**Usage:**
```csharp
// Before (global state):
var item = items.ChooseRandom();  // Uses Random.Shared

// After (seeded):
var item = rng.ChooseRandom(items);  // Uses instance RNG
```

### Save/Load Support

**Crawler RNG state serialization:**
```csharp
// Getters for serialization
ulong GetRngState()
ulong GetGaussianRngState()
bool GetGaussianPrimed()
double GetGaussianZSin()

// Setters for deserialization
void SetRngState(ulong state)
void SetGaussianRngState(ulong state)
void SetGaussianPrimed(bool primed)
void SetGaussianZSin(double zSin)
```

**YAML will serialize these properties to restore exact RNG state.**

### Migration Notes

**Old pattern (AVOID):**
```csharp
// BAD - accessing parent RNG directly
(Owner as Crawler)?.GetRng()?.NextSingle()
```

**New pattern (CORRECT):**
```csharp
// GOOD - accept seed as parameter
public Segment NewSegment(ulong seed) {
    var rng = new XorShift(seed);
    // Use rng for initialization
}
```

**Rule:** If you need randomness, accept a seed parameter or create a local RNG from your instance RNG. Never access parent object's RNG.

---

## Proposal/Interaction System

**Files:** `Interaction/Interactions.cs`, `Interaction/Offers.cs`, `Interaction/Proposals.cs`, `Interaction/Trade.cs`
**Key Innovation:** Zero coupling between actors and interaction implementations

### Three-Level Design

```
IProposal (capability check)
  ├── AgentCapable(IActor) - Can agent make this proposal?
  ├── SubjectCapable(IActor) - Can subject receive it?
  ├── InteractionCapable(Agent, Subject) → bool
  │     Returns true if interaction is available
  └── GetInteractions() → Interaction[]

Interaction (concrete action)
  ├── Immediacy(string args) → Immediacy enum
  │   ├── Disabled - Not available (conditions not met)
  │   ├── Menu - Available in menu (user choice)
  │   └── Immediate - Auto-execute now
  ├── Perform(string args) - Execute action, return AP cost
  ├── MessageFor(IActor viewer) - Context-specific message for viewer
  ├── Description - Display text
  └── OptionCode - Shortcut key

IOffer (exchange component)
  ├── DisabledFor(Agent, Subject) - Returns null if enabled, or failure message if disabled
  ├── PerformOn(Agent, Subject) - Execute the exchange
  ├── ValueFor(Agent) - Calculate value for agent
  └── Description - Display text
```

### Immediacy Enum

**Purpose:** Control when and how interactions are presented and executed

**Values:**
- **Disabled** - Not available (e.g., can't trade if hostile, can't loot if already looted)
- **Menu** - Available in normal menu context (user must select)
- **Immediate** - Auto-execute immediately without menu interaction (e.g., ultimatum consequences)

**Usage:**
```csharp
public Immediacy Immediacy(string args = "") {
    if (!meetsBasicRequirements) return Crawler.Immediacy.Disabled;

    // Check if offers are enabled (null = enabled)
    string? aoe = AgentOffer.DisabledFor(Agent, Subject);
    string? soe = SubjectOffer.DisabledFor(Subject, Agent);

    if (aoe == null && soe == null) {
        return _mode;  // Usually Immediacy.Menu
    } else {
        return Crawler.Immediacy.Disabled;
    }
}
```

**IProposal.InteractionCapable** now returns a simple `bool`:
```csharp
public bool InteractionCapable(IActor Agent, IActor Subject) =>
    AgentCapable(Agent) && SubjectCapable(Subject);
```

### Common Proposals

**Trading:**
- `ProposeSellBuy` - Agent sells to subject (Possible)
- `ProposeBuySell` - Agent buys from subject (Possible)

**Resource Acquisition:**
- `ProposeLootFree` - Loot destroyed crawler (Possible)
- `ProposeHarvestFree` - Harvest resource site (Possible)
- `ProposeLootRisk` - Explore hazard with risk/reward (Possible)

**Combat & Surrender:**
- `ProposeAttackDefend` - Initiate combat (Possible)
- `ProposeAcceptSurrender` - Accept surrender from vulnerable enemy (Possible)

**Services:**
- `ProposeRepairBuy` - Purchase repairs at settlement (Possible)

**Demands:** *(Mandatory when active)*
- `ProposeDemand` - Generic ultimatum pattern
- `ProposeExtortion` - Bandit cargo demand or attack
- `ProposeTaxes` - Civilian checkpoint taxes or hostility
- `ProposeContrabandSeizure` - Surrender contraband or pay fine
- `ProposePlayerDemand` - Player threatens vulnerable NPCs (Possible, not mandatory)

### Why This Design?

**Benefits:**
- **Zero Coupling:** New interactions don't require actor class modifications
- **Composability:** Offers can be combined in different ways
- **Dynamic Menus:** UI built automatically from available proposals
- **Extensibility:** Easy to add new interaction types
- **Clear Separation:** Capability checking vs execution logic
- **Urgency Tracking:** Mandatory interactions clearly distinguished

**Example Flow:**
```
1. Bandit enters encounter with player
2. CheckAndSetUltimatums() adds ProposeExtortion to StoredProposals
3. Sets UltimatumTime = CurrentTime + 300 seconds
4. ProposeExtortion.InteractionCapable() returns Mandatory
5. Menu displays with "!!! URGENT DEMANDS !!!" and countdown
6. Player chooses Accept or Refuse interaction
7. UltimatumTime cleared on resolution
```

---

## Component and Event System

**Files:** `Components.cs`, `ActorComponents.cs`, `Encounter.cs`, `IActor.cs`, `Crawler.cs`
**Purpose:** Event-driven, pluggable behaviors for actors

### Overview

The component system enables actors to have modular, pluggable behaviors without modifying core actor classes. Components subscribe to encounter events and can generate proposals dynamically.

**Key Innovation:** Replaces fixed Meet/Greet/Leave/Part methods with flexible, event-driven components.

### Architecture

```
┌─────────────┐
│  Encounter  │ Publishes events
└──────┬──────┘
       │ ActorArrived, ActorLeaving, ActorLeft, EncounterTick
       ↓
┌──────────────────┐
│ Event Handlers   │ (Components that implement IEncounterEventHandler)
│ - Components     │ Subscribe to specific event types
└──────────────────┘
       ↓
┌──────────────────┐
│ Handle Event     │ Component responds to event
│ - Setup state    │
│ - Add ultimatums │
│ - Generate data  │
└──────────────────┘
```

### Event Types

**EncounterEventType enum:**
- `ActorArrived` - A new actor has joined the encounter
- `ActorLeaving` - An actor is about to leave (cleanup phase)
- `ActorLeft` - An actor has left the encounter
- `EncounterTick` - Time advancement (reserved for future use)

**EncounterEvent record:**
```csharp
public record EncounterEvent(
    EncounterEventType Type,
    IActor? Actor,        // The actor involved (null for EncounterTick)
    long Time,            // When event occurred
    Encounter Encounter   // The encounter where event occurred
);
```

### IActorComponent Interface

```csharp
public interface IActorComponent : IEncounterEventHandler {
    IActor Owner { get; }
    void Initialize(IActor owner);
    IEnumerable<IProposal> GenerateProposals(IActor owner);
    void OnComponentAdded();
    void OnComponentRemoved();
    IEnumerable<EncounterEventType> SubscribedEvents { get; }
}
```

**Key methods:**
- `SubscribedEvents` - Which events this component cares about
- `HandleEvent()` - Respond to subscribed events
- `GenerateProposals()` - Create proposals on-demand (replaces StoredProposals caching)
- Lifecycle hooks for component add/remove

### Built-in Components

**BanditExtortionComponent:**
- Subscribes to: ActorArrived, ActorLeft
- Behavior: Threatens valuable targets, creates extortion ultimatums
- Note: Currently disabled with early return (remove to enable)

**SettlementContrabandComponent:**
- Subscribes to: ActorArrived, ActorLeft
- Behavior: Scans for contraband, creates seizure ultimatums

**TradeOfferComponent:**
- Subscribes to: (none)
- Behavior: Generates trade proposals on-demand
- Replaces: Cached StoredProposals for trade offers

**EncounterMessengerComponent:**
- Subscribes to: ActorArrived, ActorLeft
- Behavior: Displays "{Name} enters" and "{Name} leaves" messages

**RelationPrunerComponent:**
- Subscribes to: ActorLeaving
- Behavior: Cleans up transient actor relationships when leaving
- Keeps: Settlements and hostile relationships

### Component Lifecycle

**1. Component Creation:**
```csharp
var trader = new Crawler(...);
trader.AddComponent(new TradeOfferComponent(seed, 0.25f));
```

**2. Automatic Subscription (when added to actor in encounter):**
```csharp
public void AddComponent(IActorComponent component) {
    component.Initialize(this);
    _components.Add(component);
    component.OnComponentAdded();

    // Auto-subscribe if actor is in encounter
    if (Location?.HasEncounter == true) {
        var encounter = Location.GetEncounter();
        foreach (var eventType in component.SubscribedEvents) {
            encounter.Subscribe(eventType, component);
        }
    }
}
```

**3. Event Handling:**
```csharp
// Encounter publishes events
PublishEvent(new EncounterEvent(EncounterEventType.ActorArrived, actor, time, this));

// Components receive and handle
foreach (var handler in _eventHandlers[eventType]) {
    handler.HandleEvent(evt);
}
```

**4. Proposal Generation:**
```csharp
// Crawler.Proposals() yields from all components
public virtual IEnumerable<IProposal> Proposals() {
    foreach (var component in _components) {
        foreach (var proposal in component.GenerateProposals(this)) {
            yield return proposal;
        }
    }
    // Also yield from stored proposals
    foreach (var proposal in StoredProposals) {
        yield return proposal;
    }
}
```

### Benefits

**Extensibility:**
- Add new behaviors without modifying IActor or Crawler
- Mix and match components freely
- Easy to enable/disable behaviors

**Performance:**
- Proposals generated fresh (no stale cache)
- Components only respond to events they care about
- Lazy evaluation via yield return

**Maintainability:**
- Clear separation of concerns
- Each behavior in its own class
- Easy to test components independently

**Liveness:**
- No cached proposals that become stale
- Dynamic proposal generation based on current state
- ActorToActor.StoredProposals used for time-sensitive ultimatums

---

## Mandatory Interaction System (Ultimatums)

**Files:** `Crawler.cs:116-162`, `Encounter.cs:183-224, 534-563`, `Proposals.cs:174-250`
**Purpose:** Time-limited demands with automatic consequences

### Architecture

**Setup Flow:**
```
1. Actor enters encounter
   ↓
2. Encounter.CheckForcedInteractions() called
   ↓
3. Crawler.CheckAndSetUltimatums(other) evaluates conditions
   ↓
4. If conditions met:
   - Add appropriate proposal to StoredProposals
   - Set ActorToActor.UltimatumTime = CurrentTime + 300 seconds
   ↓
5. Proposal.InteractionCapable() returns Mandatory when timer active
   ↓
6. Menu displays urgent interaction with countdown
   ↓
7. Player chooses Accept or Refuse
   ↓
8. UltimatumTime cleared (set to 0)
```

**Expiration Flow:**
```
Every 5 minutes: CheckPeriodicForcedInteractions() runs
   ↓
If UltimatumTime reached without response:
   ↓
1. Find proposal with Mandatory capability
2. Get Refuse interaction (option code contains "DR")
3. Auto-execute refuse interaction
4. Display "Time's up!" messages
5. Clear UltimatumTime
```

### ProposeDemand Pattern

**Generic ultimatum creator:**
```csharp
ProposeDemand(
  Demand: IOffer,                              // What they want
  ConsequenceFn: Func<IActor, IActor, Interaction>,  // What happens if refused
  Ultimatum: string,                           // Description
  Condition: Func<IActor, IActor, bool>?       // Optional condition check
)
```

**Returns TWO interactions:**
- `AcceptDemandInteraction` - Comply with demand (option code: "DA")
  - Performs the offer exchange
  - Clears UltimatumTime
  - Shows compliance messages

- `RefuseDemandInteraction` - Reject demand (option code: "DR")
  - Executes consequence interaction
  - Clears UltimatumTime
  - Shows refusal messages

### Use Cases

**Bandit Extortion** (`ProposeExtortion`)
- **Trigger:** On encounter entry, 60% chance if cargo value ≥ threshold
- **Demand:** 33% of player's cargo
- **Consequence:** Attack
- **Location:** `Crawler.cs:118-133`

**Civilian Taxes** (`ProposeTaxes`)
- **Trigger:** On entry to faction's controlled territory
- **Demand:** 5% of cargo value in scrap
- **Consequence:** Turn hostile
- **Location:** `Crawler.cs:149-159`

**Contraband Seizure** (`ProposeContrabandSeizure`)
- **Trigger:** 70% chance scan detects prohibited goods
- **Demand:** Choice of surrendering contraband OR paying 2x value fine
- **Consequence:** Turn hostile (if can't comply)
- **Location:** `Crawler.cs:139-147`

**Player Threats** (`ProposePlayerDemand`)
- **Trigger:** Player manually initiates on vulnerable NPCs
- **Demand:** Fraction of NPC's cargo
- **Consequence:** Player attacks
- **Note:** Returns Possible, not Mandatory (player-initiated, not NPC ultimatum)

### Menu Display

**Mandatory interactions shown prominently:**
```
!!! URGENT DEMANDS !!!
─────────────────────
>>> Bandit demands 150¢¢ worth of cargo or they will attack [Time: 287s]
  UA - Accept: ...
  UR - Refuse: ...
─────────────────────
```

**Styling:**
- Header uses `Style.SegmentDestroyed` (red highlighting)
- Each ultimatum shows countdown in seconds
- Shortcut keys: UA, UB, UC... (U = Urgent)
- Displayed at top of encounter menu

**Implementation:** `Encounter.cs:44-151`

### Contraband System

**Trade Policies (Category/Kind-Based):**
- **Subsidized** - 0.7x base price (government subsidy)
- **Legal** - 1.0x base price (no restrictions)
- **Taxed** - 1.3x markup (import tariffs)
- **Controlled** - 1.75x markup + transaction fee (heavily regulated)
- **Prohibited** - Cannot trade, subject to seizure (contraband)

**Policy System:**
- Policies defined per faction for CommodityCategory and SegmentKind
- Individual commodities/segments inherit from their category/kind

**Scan Process:**
```csharp
ScanForContraband(target):
  if Random() > contrabandScanChance: return empty

  foreach commodity in target.Supplies:
    policy = FactionPolicies.GetPolicy(faction, commodity.Category())
    if policy == Prohibited and amount > 0:
      contraband.Add(commodity, amount)

  return contraband
```

**Configuration:** `Tuning.FactionPolicies`, `Tuning.Civilian.contrabandScanChance`

---

## Trading System

**Files:** `Trade.cs:214-309`, `Tuning.cs:40-80`
**Model:** Bid-Ask Spread with dynamic pricing

### Price Calculation

**Mid-Price Formula:**
```
MidPrice = BaseValue × LocationMarkup × ScarcityPremium × PolicyMultiplier
```

**Where:**
- **BaseValue:** Commodity's base cost (CommodityEx.Data)
- **LocationMarkup:** `LocalMarkup(commodity, location)` based on terrain and tech level
- **ScarcityPremium:** `1 + (1 - Availability) × CategoryWeight`
  - Availability = `1 - Unavailability.Value(population/100, techLatitude*2 - commodityTech)`
  - Uses power scaling with 0.7 exponent and (0.15, 0.3) powers for primitive/tech axes
  - Essential goods: 0.3× weight (stable prices)
  - Luxury goods: 1.5× weight (volatile prices)
- **PolicyMultiplier:** From `Tuning.Trade.PolicyMultiplier(policy)`
  - Legal: 1.0×
  - Controlled: 1.2×

**Bid-Ask Spread:**
```
Spread = MidPrice × BaseBidAskSpread × FactionMultiplier × TraderMarkup

AskPrice (player buys from NPC) = MidPrice + Spread/2
BidPrice (player sells to NPC) = MidPrice - Spread/2
```

**Faction Multipliers:**
- Trade NPCs: 0.8 ± 0.05 → ~16% spread
- Bandits: 1.5 ± 0.1 → ~30% spread
- Base spread: 20%

**Trader Markup:**
Each crawler has personal markup (Gaussian: mean=1.05, sd=0.07)

### Example Calculation

```
Selling Fuel at Trade Settlement:
  BaseValue: 10¢¢
  LocationMarkup: 1.2× (rough terrain)
  ScarcityPremium: 1.1× (90% availability)
  PolicyMultiplier: 1.0× (legal)
  MidPrice = 10 × 1.2 × 1.1 × 1.0 = 13.2¢¢

  BaseBidAskSpread: 0.20 (20%)
  FactionMultiplier: 0.8 (trader)
  TraderMarkup: 1.05 (this specific NPC)
  Spread = 13.2 × 0.20 × 0.8 × 1.05 = 2.22¢¢

  AskPrice = 13.2 + 1.11 = 14.31¢¢ (player pays)
  BidPrice = 13.2 - 1.11 = 12.09¢¢ (player receives)
```

### Trade Proposal Generation

**Process:** `TradeEx.MakeTradeProposals(IActor Seller, float wealthFraction)`
1. Uses seller's faction for policy determination
2. Determine available commodities based on location/faction
3. Filter by faction trade policy (prohibited goods have 50% chance to appear)
4. Calculate prices with spreads
5. Add transaction fees for controlled goods
6. Generate ProposeSellBuy and ProposeBuySell for each

**Prohibited Goods:**
Goods with Prohibited policy have 50% chance to be skipped in trade offers

**Segments:**
- Offered from trader's Cargo
- Procedurally generated based on location wealth
- Local markup applied to base cost

---

## Combat System

**Files:** `Crawler.cs:535-717`, `Segment.cs` (various)
**Style:** Turn-based with power budget management

### Attack Flow

**1. Attacker.CreateFire() (Crawler.cs:535-563)**
```
1. Create local RNG from Crawler's RNG seed
2. Calculate available power (TotalCharge)
3. Group offense segments (weapons vs non-weapons)
4. Shuffle weapons for variety (using seeded RNG)
5. Select segments within power budget:
   - Weapons prioritized
   - Drain subtracted from available power
6. DrawPower(used)
7. Each WeaponSegment.GenerateFire(seed) → HitRecords
```

**HitRecord Structure:**
```csharp
record HitRecord(WeaponSegment Weapon, float Damage, float Aim)
  - Random t = rng.NextSingle()  // Using seeded XorShift RNG
  - Hit type = t + Aim:
    < 0.5: Miss
    < 1.5: Hit
    ≥ 1.5: Pierce
```

**2. Defender.ReceiveFire(attacker, hitRecords) (Crawler.cs:624-717)**
```
1. Create local RNG from Crawler's RNG seed
2. foreach hit in hitRecords:
     damage = hit.Damage.StochasticInt(rng)  // Seeded RNG for damage conversion

     Phase 0: Shields (active shields with charge remaining)
       if shields available:
         segment = rng.ChooseRandom(shields)  // Seeded random selection
         (remaining, msg) = segment.AddDmg(hitType, damage)
         damage = remaining

     Phase 1: Armor (all defense segments except shields)
       if armor available:
         segment = rng.ChooseRandom(armor)  // Seeded random selection
         (remaining, msg) = segment.AddDmg(hitType, damage)
         damage = remaining

     Phase 2: Random Segment (all segments except defense)
       if segments available:
         segment = rng.ChooseRandom(non-defense segments)  // Seeded random selection
         (remaining, msg) = segment.AddDmg(hitType, damage)
         damage = remaining

     Phase 3: Crew
       if damage > 0:
         CrewLoss(damage)
         MoraleLoss(based on crew lost)
```

**Segment Damage:**
```csharp
Segment.AddDmg(hitType, damage):
  if hitType == Pierce and segment has armor:
    reduce armor effectiveness by 50%

  Apply damage reduction
  Add hits to segment

  if Hits >= MaxHits:
    State = Destroyed
  else if Hits > 0:
    State = Disabled

  return (remaining damage, message)
```

### Morale System

**Penalties:**
- **First damage taken:** `Tuning.Crawler.MoraleTakeAttack` (applies once per attacker)
- **Crew losses:** `Tuning.Crawler.MoraleAdjCrewLoss × CrewLost`
- **Friendly fire:** Larger penalty, reduced by attacker's evil points

**Bonuses:**
- **Destroying hostile:** `Tuning.Crawler.MoraleHostileDestroyed`
- **Surrendering to:** `Tuning.Crawler.MoraleSurrendered` (penalty for surrenderer)
- **Accepted surrender:** `Tuning.Crawler.MoraleSurrenderedTo`

**Game Over:**
- Morale reaches 0 → Revolt (EEndState.Revolt)

### Relationship Tracking

**ActorToActor Updates:**
```csharp
On attack:
  relation.Hostile = true

On damage dealt:
  relation.DamageCreated += originalDamage
  relation.DamageInflicted += actualDamageDealt

On damage received:
  relation.DamageTaken += actualDamageTaken
  if !wasAlreadyDamaged:
    Apply first-damage morale penalty
```

**Implementation:** `Crawler.cs:624-717`

---

## Movement System

**Files:** `Crawler.cs:92-103, 341-387`
**Factors:** Terrain, power, weight

### Fuel/Time Calculation

```csharp
FuelTimeTo(destination):
  distance = Location.Distance(destination)
  startSpeed = SpeedOn(Location.Terrain)
  endSpeed = SpeedOn(destination.Terrain)
  terrainRate = min(startSpeed, endSpeed)  // Limited by worse terrain

  if terrainRate <= 0: return impossible

  time = distance / terrainRate
  fuel = FuelPerKm × distance + FuelPerHr × time

  return (fuel, time)
```

### Speed Calculation

**Multi-Tier Evaluation (Crawler.cs:357-387):**
```
For each terrain tier from Flat to CurrentTerrain:
  speed = Σ(traction segments' SpeedOn(tier))
  drain = Σ(traction segments' DrainOn(tier))

  // Power limitation
  generationLimit = generation / drain
  if generationLimit < 1:
    speed *= generationLimit
    notes.Add("low gen")

  // Weight penalty
  liftFraction = weight / Σ(traction segments' LiftOn(currentTerrain))
  if liftFraction > 1:
    liftFraction = liftFraction ^ 0.6  // Sublinear penalty
    speed /= liftFraction
    drain *= liftFraction
    notes.Add("too heavy")

  if speed > bestSpeed:
    bestSpeed = speed
    bestDrain = drain

return (bestSpeed, bestDrain, notes)
```

**Why multi-tier?**
- Tracks can move on broken terrain at lower tier speed
- Wheels move faster on flat but fail on rough
- System picks best achievable speed across all tiers

### Pinned Mechanic

**Cannot flee if faster hostile present:**
```csharp
Pinned():
  return Actors.Any(a =>
    a is Crawler hostile &&
    hostile.To(this).Hostile &&
    Speed < hostile.Speed
  )
```

**Used by:**
- Bandit AI (flees if vulnerable and not pinned)
- Movement validation (can't leave if pinned)

---

## Power System

**Files:** `Crawler.cs:390-623`, `Segment.cs:PowerSegment`
**Two segment types:** Reactors (storage + generation), Chargers (generation only)

### Generation Phase

```csharp
Recharge():
    Segments.Tick()  // Internal segment updates

    overflowPower = Σ(ReactorSegments.Generate())
    overflowPower += Σ(ChargerSegments.Generate())

    FeedPower(overflowPower)

    // Consume resources
    ScrapInv -= WagesPerHr
    RationsInv -= RationsPerHr
    WaterInv -= WaterPerHr
    AirInv -= AirPerHr
    FuelInv -= FuelPerHr
```

### Power Distribution

**FeedPower (Charging - Crawler.cs:578-600):**
```csharp
FeedPower(delta):
  reactors = PowerSegments.OfType<ReactorSegment>()

  // Calculate available capacity per reactor
  capacities = reactors.Select(r => r.Capacity - r.Charge)
  totalCapacity = capacities.Sum()

  // Distribute proportionally to available capacity
  delta = Clamp(delta, 0, totalCapacity)

  foreach (reactor, capacity) in Zip(reactors, capacities):
    reactor.Charge += delta × (capacity / totalCapacity)

  return excessPower
```

**DrawPower (Consumption - Crawler.cs:601-623):**
```csharp
DrawPower(delta):
  reactors = PowerSegments.OfType<ReactorSegment>()

  // Calculate current charge per reactor
  charges = reactors.Select(r => r.Charge)
  totalCharge = charges.Sum()

  // Draw proportionally to current charge
  delta = Clamp(delta, 0, totalCharge)

  foreach (reactor, charge) in Zip(reactors, charges):
    reactor.Charge -= delta × (charge / totalCharge)

  return deficit
```

**Why proportional?**
- Avoids draining single reactor completely
- Balances wear across multiple reactors
- Mimics parallel battery discharge

### Fuel Consumption

```
StandbyDrain = TotalDrain × StandbyFraction (0.1 = 10%)
FuelPerHr = StandbyDrain / FuelEfficiency

MovementFuelPerKm = FuelPerKm × MovementDrain / FuelEfficiency
```

**Standby:** Idling at location
**Movement:** Additional fuel for propulsion drain

---

## Tick System

**Files:** `Game.cs`, `Crawler.cs:180-239`, `Encounter.cs:488-532`
**Frequency:** Every game second

### Tick Hierarchy

```
Game.Tick() (every second)
├── TimeSeconds++
├── if Moving: Player.Tick()
└── CurrentEncounter.Tick()
    ├── UpdateDynamicCrawlers() (check exits/arrivals)
    ├── foreach actor: actor.Tick()
    └── foreach actor: actor.Tick(otherActors)
```

### Player Tick (Crawler.cs:180-239)

**Every Second:**
- UpdateSegments() - Refresh segment caches

**Every Hour (Game.Instance.IsHour()):**
```csharp
Recharge()

// Resource checks
if RationsInv <= 0:
  starve crew/soldiers/passengers (1% loss rate)
  Message("Out of rations")

if WaterInv <= 0:
  dehydrate crew/soldiers/passengers (2% loss rate)
  Message("Out of water")

if AirInv <= 0:
  suffocate crew/soldiers/passengers (3% loss rate)
  Message("Out of air")

if IsDepowered:
  MoraleInv -= 1.0
  Message("Life support offline")

// Game over checks
if CrewInv == 0:
  End(appropriate state based on cause)

if MoraleInv <= 0:
  End(EEndState.Revolt)

if !UndestroyedSegments.Any():
  End(EEndState.Destroyed)
```

### Encounter Tick (Encounter.cs:588-637)

**Every Second:**
```csharp
UpdateDynamicCrawlers()  // Handle arrivals/exits

foreach actor in actors:
  if !actor.IsDestroyed:
    actor.Tick()

foreach actor in actors:
  actor.Tick(otherActors)  // AI behavior
```

**Dynamic Crawler Spawning:**
- Settlements spawn traders (Independent or civilian faction) ~75%, bandits ~25%
- Crossroads spawn based on terrain weights (see Tuning.cs)
- Dynamic spawns never overwrite encounter name (only set for permanent actors)

**Every 5 Minutes (TimeSeconds % 300 == 0):**
```csharp
CheckPeriodicForcedInteractions()  // Ultimatum expiration
```

### AI Tick (Crawler.cs:246-268)

**Bandit Behavior:**
```csharp
TickBandit(actors):
  if IsDepowered:
    Message("radio silent")
    return

  if IsVulnerable and !Pinned():
    Message("flees")
    RemoveFromEncounter()
    return

  foreach actor in actors:
    if actor.Faction == Player and To(actor).Hostile:
      Attack(actor)
      break
```

**Other factions:** Currently passive (no Tick behavior)

---

## Summary

These systems work together to create emergent gameplay:

- **RNG** provides deterministic, reproducible randomness for testing and replay
- **Proposals** enable complex interactions without coupling
- **Ultimatums** create time pressure and consequences
- **Trading** provides economic depth with realistic pricing
- **Combat** requires resource management and tactical choices
- **Movement** balances speed, fuel, and terrain navigation
- **Power** creates optimization puzzles for crawler builds
- **Tick** drives the simulation forward consistently

For information on data structures, see [DATA-MODEL.md](DATA-MODEL.md)
For information on extending these systems, see [EXTENDING.md](EXTENDING.md)
