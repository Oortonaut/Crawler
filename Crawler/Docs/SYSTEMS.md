# Crawler Systems

**Last Updated:** 2025-01-19
**Parent:** [ARCHITECTURE.md](ARCHITECTURE.md)

## Quick Navigation
- [Proposal/Interaction System](#proposalinteraction-system)
- [Mandatory Interaction System](#mandatory-interaction-system-ultimatums)
- [Trading System](#trading-system)
- [Combat System](#combat-system)
- [Movement System](#movement-system)
- [Power System](#power-system)
- [Tick System](#tick-system)

## Recent Changes
- **2025-10-19**: Refactored interaction system into separate files: Interactions.cs (IInteraction types), Offers.cs (IOffer types), Proposals.cs (IProposal types), Trade.cs (trading proposals)
- **2025-01-19**: Refactored InteractionCapability to use enum (Disabled/Possible/Mandatory)
- **2025-01-19**: ProposeDemand now yields separate Accept/Refuse interactions

---

## Proposal/Interaction System

**Files:** `Interaction/Interactions.cs`, `Interaction/Offers.cs`, `Interaction/Proposals.cs`, `Interaction/Trade.cs`
**Key Innovation:** Zero coupling between actors and interaction implementations

### Three-Level Design

```
IProposal (capability check)
  ├── AgentCapable(IActor) - Can agent make this proposal?
  ├── SubjectCapable(IActor) - Can subject receive it?
  └── InteractionCapable(Agent, Subject) → InteractionCapability
      ├── Disabled - Not available (conditions not met)
      ├── Possible - Available but optional
      └── Mandatory - Must be addressed immediately

      └── GetInteractions() → IInteraction[]

IInteraction (concrete action)
  ├── Enabled(string args) - Can perform now?
  ├── Perform(string args) - Execute action, return AP cost
  ├── Description - Display text
  └── OptionCode - Shortcut key

IOffer (exchange component)
  ├── EnabledFor(Agent, Subject) - Can this exchange happen?
  ├── PerformOn(Agent, Subject) - Execute the exchange
  ├── ValueFor(Agent) - Calculate value for agent
  └── Description - Display text
```

### InteractionCapability Enum

**Purpose:** Distinguish between optional and mandatory interactions

**Values:**
- **Disabled** - Not available (e.g., can't trade if hostile, can't loot if already looted)
- **Possible** - Available in normal context (e.g., voluntary trading, attacking)
- **Mandatory** - Time-limited demand requiring immediate response (e.g., bandit extortion, taxes)

**Usage:**
```csharp
public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) {
    if (!meetsBasicRequirements) return InteractionCapability.Disabled;

    // Check for active ultimatum
    if (Agent.To(Subject).UltimatumTime > 0 &&
        Game.SafeTime < Agent.To(Subject).UltimatumTime) {
        return InteractionCapability.Mandatory;
    }

    return InteractionCapability.Possible;
}
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
  ConsequenceFn: Func<IActor, IActor, IInteraction>,  // What happens if refused
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

**Trade Policies Per Commodity:**
- **Legal** - No restrictions
- **Controlled** - Transaction fees apply (Tuning.Trade.restrictedTransactionFee)
- **Prohibited** - Subject to seizure

**Scan Process:**
```csharp
ScanForContraband(target):
  if Random() > contrabandScanChance: return empty

  foreach commodity in target.Inv:
    policy = FactionPolicies.GetPolicy(faction, commodity)
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

**Process:** `TradeEx.MakeTradeProposals()`
1. Determine available commodities based on location/faction
2. Filter by faction trade policy
3. Calculate prices with spreads
4. Add transaction fees for controlled goods
5. Generate ProposeSellBuy and ProposeBuySell for each

**Vice Goods:**
Only sold by Bandits (Liquor, Stims, Downers, Trips)

**Segments:**
- Offered from trader's TradeInv
- Procedurally generated based on location wealth
- Local markup applied to base cost

---

## Combat System

**Files:** `Crawler.cs:535-717`, `Segment.cs` (various)
**Style:** Turn-based with power budget management

### Attack Flow

**1. Attacker.CreateFire() (Crawler.cs:535-563)**
```
1. Calculate available power (TotalCharge)
2. Group offense segments (weapons vs non-weapons)
3. Shuffle weapons for variety
4. Select segments within power budget:
   - Weapons prioritized
   - Drain subtracted from available power
5. DrawPower(used)
6. Each WeaponSegment.GenerateFire() → HitRecords
```

**HitRecord Structure:**
```csharp
record HitRecord(WeaponSegment Weapon, float Damage, float Aim)
  - Random t = NextSingle()
  - Hit type = t + Aim:
    < 0.5: Miss
    < 1.5: Hit
    ≥ 1.5: Pierce
```

**2. Defender.ReceiveFire(attacker, hitRecords) (Crawler.cs:624-717)**
```
foreach hit in hitRecords:
  damage = hit.Damage (converted to int)

  Phase 0: Shields (active shields with charge remaining)
    if shields available:
      (remaining, msg) = shield.AddDmg(hitType, damage)
      damage = remaining

  Phase 1: Armor (all defense segments except shields)
    if armor available:
      (remaining, msg) = armor.AddDmg(hitType, damage)
      damage = remaining

  Phase 2: Random Segment (all segments except defense)
    if segments available:
      segment = RandomChoice(non-defense segments)
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
Recharge(hours):
  for _ in 0..hours:
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

**Every Hour (TimeSeconds % 3600 == 0):**
```csharp
Recharge(1 hour)

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

- **Proposals** enable complex interactions without coupling
- **Ultimatums** create time pressure and consequences
- **Trading** provides economic depth with realistic pricing
- **Combat** requires resource management and tactical choices
- **Movement** balances speed, fuel, and terrain navigation
- **Power** creates optimization puzzles for crawler builds
- **Tick** drives the simulation forward consistently

For information on data structures, see [DATA-MODEL.md](DATA-MODEL.md)
For information on extending these systems, see [EXTENDING.md](EXTENDING.md)
