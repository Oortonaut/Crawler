# Extending Crawler

**Last Updated:** 2025-11-21
**Parent:** [ARCHITECTURE.md](ARCHITECTURE.md)

## Quick Navigation
- [Creating Actor Components](#creating-actor-components) ⭐ NEW
- [Adding Proposals/Interactions](#adding-proposalsinteractions)
- [Adding Segments](#adding-segments)
- [Adding Commodities](#adding-commodities)
- [Adding Factions](#adding-factions)
- [Tuning Parameters](#tuning-parameters)

---

## Creating Actor Components

**Difficulty:** Easy-Medium
**Files:** Add new component class to `ActorComponents.cs`
**Documentation:** Update [SYSTEMS.md](SYSTEMS.md) and [DATA-MODEL.md](DATA-MODEL.md)

**When to use components:**
- Adding behavior that responds to encounter events
- Creating reusable behaviors that can be mixed and matched
- Generating proposals dynamically instead of caching them
- Avoiding modification of core IActor/Crawler classes

### Pattern

```csharp
/// <summary>
/// Example component that does something when actors arrive
/// </summary>
public class MyCustomComponent : ActorComponentBase {
    // Component state (if needed)
    ulong _seed;

    public MyCustomComponent(ulong seed) {
        _seed = seed;
    }

    // Which events to subscribe to
    public override IEnumerable<EncounterEventType> SubscribedEvents => new[] {
        EncounterEventType.ActorArrived,
        EncounterEventType.ActorLeft
    };

    // Handle events
    public override void HandleEvent(EncounterEvent evt) {
        if (Owner is not Crawler crawler) return;

        switch (evt.Type) {
            case EncounterEventType.ActorArrived:
                // React to new actor arriving
                if (evt.Actor != null && evt.Actor != Owner) {
                    DoSomethingWithNewActor(crawler, evt.Actor);
                }
                break;

            case EncounterEventType.ActorLeft:
                // React to actor leaving
                if (evt.Actor != null && evt.Actor != Owner) {
                    CleanupAfterActor(crawler, evt.Actor);
                }
                break;
        }
    }

    // Generate proposals (if this component provides any)
    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        // Option 1: No proposals (just event handling)
        yield break;

        // Option 2: Generate proposals dynamically
        // yield return new ProposeMyAction(owner);

        // Option 3: Use existing proposal generators
        // return owner.MakeTradeProposals(_seed, 0.5f);
    }

    void DoSomethingWithNewActor(Crawler owner, IActor newActor) {
        // Your logic here
        owner.Message($"Detected {newActor.Name}!");
    }

    void CleanupAfterActor(Crawler owner, IActor leavingActor) {
        // Your cleanup logic here
    }
}
```

### Adding Component to Actor

```csharp
// In Encounter generation method (e.g., GenerateSettlement, GenerateBanditActor)
var actor = Crawler.NewRandom(seed, faction, location, ...);
actor.AddComponent(new MyCustomComponent(rng.Seed()));

// Components automatically subscribe when actor joins encounter
encounter.AddActor(actor);
```

### Component Lifecycle Hooks

```csharp
public class MyComponent : ActorComponentBase {
    public override void OnComponentAdded() {
        // Called when component is first added to actor
        // Good place for initialization
        base.OnComponentAdded();
    }

    public override void OnComponentRemoved() {
        // Called when component is removed from actor
        // Good place for cleanup
        base.OnComponentRemoved();
    }
}
```

### Examples from Codebase

**TradeOfferComponent** - Generates proposals on-demand:
```csharp
public class TradeOfferComponent : ActorComponentBase {
    float _wealthFraction;
    ulong _seed;

    public TradeOfferComponent(ulong seed, float wealthFraction = 0.25f) {
        _seed = seed;
        _wealthFraction = wealthFraction;
    }

    // No event subscriptions (proposals only)
    public override IEnumerable<EncounterEventType> SubscribedEvents =>
        Array.Empty<EncounterEventType>();

    public override void HandleEvent(EncounterEvent evt) { }

    // Generate trade proposals fresh each time
    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        return owner.MakeTradeProposals(_seed, _wealthFraction);
    }
}
```

**EncounterMessengerComponent** - Displays messages:
```csharp
public class EncounterMessengerComponent : ActorComponentBase {
    public override IEnumerable<EncounterEventType> SubscribedEvents => new[] {
        EncounterEventType.ActorArrived,
        EncounterEventType.ActorLeft
    };

    public override void HandleEvent(EncounterEvent evt) {
        if (evt.Actor == null || evt.Actor == Owner) return;

        switch (evt.Type) {
            case EncounterEventType.ActorArrived:
                Owner.Message($"{evt.Actor.Name} enters");
                break;
            case EncounterEventType.ActorLeft:
                Owner.Message($"{evt.Actor.Name} leaves");
                break;
        }
    }

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        yield break; // No proposals
    }
}
```

### Component Best Practices

1. **Single Responsibility**: Each component should handle one specific behavior
2. **Event Selection**: Only subscribe to events you actually need
3. **Owner Type Checking**: Check `Owner is Crawler` before casting if needed
4. **Null Checks**: Always check `evt.Actor != null` and `evt.Actor != Owner`
5. **Error Handling**: Components are called with try-catch, but avoid exceptions
6. **State Management**: Store component-specific state in fields, not in Owner
7. **Performance**: Keep HandleEvent() fast; proposals are lazily evaluated
8. **Seed Management**: Pass seeds to components for deterministic RNG

### When to Use Components vs Proposals

**Use Components when:**
- You need to react to encounter events
- You want to generate proposals dynamically
- You want reusable, pluggable behaviors
- You're creating actor-specific logic

**Use Proposals when:**
- You have a standalone interaction type
- No event handling needed
- Logic is shared across all actor types
- Simple capability checks suffice

---

## Adding Proposals/Interactions

**Difficulty:** Easy
**Files:** Create new record in `Proposals.cs` or `Trade.cs`
**Documentation:** Update [SYSTEMS.md](SYSTEMS.md)

### Pattern

```csharp
// Define the proposal
public record ProposeMyAction(string OptionCode = "M"): IProposal {
    // 1. Can the agent make this proposal?
    public bool AgentCapable(IActor Agent) =>
        Agent is Crawler && /* agent requirements */;

    // 2. Can the subject receive this proposal?
    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction != Faction.Player || /* subject requirements */;

    // 3. Is this interaction available?
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        AgentCapable(Agent) && SubjectCapable(Subject) && /* other conditions */;

    // 4. Create the actual interactions
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new MyInteraction(Agent, Subject);
    }

    // 5. Define the interaction
    public record MyInteraction(IActor Agent, IActor Subject): IInteraction {
        public Immediacy Immediacy(string args = "") {
            // Check if can execute now
            if (/* conditions not met */)
                return Crawler.Immediacy.Disabled;

            // Return Menu for normal menu option, or Immediate for auto-execute
            return Crawler.Immediacy.Menu;
        }

        public int Perform(string args = "") {
            // Execute the action
            Agent.Message($"You did something to {Subject.Name}");
            Subject.Message($"{Agent.Name} did something to you");

            // Return AP cost (0 for instant)
            return 0;
        }

        public string? MessageFor(IActor viewer) => null;  // Optional context message
        public string Description => "Do something";
        public string OptionCode => "M";
    }

    public string Description => "My Action";
}
```

### Adding to Actors

**Global Proposals** (available to all actors):
```csharp
// In Game.cs or initialization
Game.Instance.StoredProposals.Add(new ProposeMyAction());
```

**Specific Actors**:
```csharp
// In Crawler constructor or encounter generation
crawler.StoredProposals.Add(new ProposeMyAction());
```

**Conditional Proposals** (set dynamically):
```csharp
// In Crawler.CheckAndSetUltimatums() or similar
if (/* condition met */) {
    StoredProposals.Add(new ProposeMyAction());
    To(other).UltimatumTime = Game.SafeTime + 300; // 5 min
}
```

### Examples

**Simple Exchange:**
```csharp
public record ProposeGiveItem(Commodity Item, float Amount): IProposal {
    public bool AgentCapable(IActor Agent) => Agent.Supplies[Item] >= Amount;
    public bool SubjectCapable(IActor Subject) => true;

    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        AgentCapable(Agent) && SubjectCapable(Subject);

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var offer = new CommodityOffer(Item, Amount);
        yield return new ExchangeInteraction(
            Agent, offer,
            Subject, new EmptyOffer(),
            "G", $"Give {Amount} {Item}",
            Immediacy.Menu  // Normal menu option
        );
    }

    public string Description => $"Give {Amount} {Item}";
}
```

**Using ProposeDemand Pattern:**
```csharp
public record ProposeMyDemand(IOffer Demand): IProposal {
    public bool AgentCapable(IActor Agent) => /* requirements */;
    public bool SubjectCapable(IActor Subject) => /* requirements */;

    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        AgentCapable(Agent) && SubjectCapable(Subject);

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var demandProposal = new ProposeDemand(
            Demand,
            (agent, subject) => new ProposeAttackDefend.AttackInteraction(agent, subject),
            $"{Agent.Name} demands {Demand.Description}",
            (agent, subject) => true
        );

        return demandProposal.GetInteractions(Agent, Subject);
    }

    public string Description => $"Demand {Demand.Description}";
}
```

---

## Adding Segments

**Difficulty:** Moderate
**Files:** Add to `Segment.cs`
**Documentation:** Update [DATA-MODEL.md](DATA-MODEL.md)

### 1. Create Segment Definition

```csharp
// In SegmentEx or similar
var mySegmentDef = new MySegmentDef(
    Symbol: 'X',                    // Display character
    Size: new Tier(2),              // Size tier (affects cost scaling)
    Name: "MySegment",
    SegmentKind: SegmentKind.Offense,  // Which class
    WeightTier: new Tier(2),        // Mass
    DrainTier: new Tier(1),         // Power drain
    CostTier: new Tier(3),          // Base cost
    MaxHitsTier: new Tier(2),       // Durability
    // Custom properties...
);
```

### 2. Create Segment Class (if custom behavior needed)

```csharp
public class MySegment(SegmentDef def, IActor? owner): OffenseSegment(def, owner) {
    // Custom state
    private int _customValue = 0;

    // Override if needed
    public override void Tick() {
        base.Tick();
        _customValue++;
    }

    // Custom methods
    public override List<HitRecord> GenerateFire(int aimBonus) {
        // Custom attack logic
        var records = new List<HitRecord>();
        float damage = CustomDamageCalculation();
        float aim = 0.5f + aimBonus * 0.1f;

        records.Add(new HitRecord(this, damage, aim));
        return records;
    }

    private float CustomDamageCalculation() {
        return Def.Size.Value * 10f + _customValue;
    }
}
```

### 3. Add to Segment Factory

```csharp
// In SegmentEx.AllDefs or similar
public static List<SegmentDef> AllDefs = [
    // ... existing defs
    new MySegmentDef(...),
];
```

### 4. Register Definition (for procedural generation)

If you want it to appear in random generation:
```csharp
// SegmentEx already includes AllDefs in generation
// Just adding to AllDefs is sufficient
```

### Examples

**Simple Offense Segment:**
```csharp
var laserDef = new OffenseSegmentDef(
    Symbol: 'L',
    Size: new Tier(3),
    Name: "Laser Cannon",
    SegmentKind: SegmentKind.Offense,
    WeightTier: new Tier(2),
    DrainTier: new Tier(4),     // High power
    CostTier: new Tier(4),
    MaxHitsTier: new Tier(1),   // Fragile
    DamageTier: new Tier(3),
    AimTier: new Tier(2)
);
```

**Custom Traction Segment:**
```csharp
public class HoverSegment(SegmentDef def, IActor? owner): TractionSegment(def, owner) {
    public override float SpeedOn(TerrainType terrain) {
        // Hover ignores terrain completely
        return BaseSpe ed * 1.5f;
    }

    public override float LiftOn(TerrainType terrain) {
        // Consistent lift
        return BaseLift;
    }

    public override float DrainOn(TerrainType terrain) {
        // Higher drain on rough terrain (fighting wind)
        return BaseDrain * (1f + (int)terrain * 0.1f);
    }
}
```

---

## Adding Commodities

**Difficulty:** Easy
**Files:** `Inventory.cs` (enum), `CommodityEx.cs` (data)
**Documentation:** Update [DATA-MODEL.md](DATA-MODEL.md)

### 1. Add to Commodity Enum

```csharp
// In Inventory.cs
public enum Commodity {
    // ... existing commodities
    MyNewCommodity,
}
```

### 2. Add Commodity Data

```csharp
// In CommodityEx.cs - CommodityEx.Data initialization
new CommodityData(
    BaseValue: 50f,                 // Base price in scrap
    Volume: 0.1f,                   // Space per unit
    Mass: 0.05f,                    // Weight per unit
    Flag: CommodityFlag.None,       // Or Perishable, Bulky, Integral
    Tier: GameTier.Mid              // When available (Early/Mid/Late)
)
```

### 3. Set Category

```csharp
// In CommodityEx.Category() method
Commodity.MyNewCommodity => CommodityCategory.Consumer,
```

### 4. Configure Availability (optional)

```csharp
// In Tuning.Economy or similar
public static float AvailabilityAt(this Commodity commodity, Location location) {
    return commodity switch {
        Commodity.MyNewCommodity => location.TechLatitude * 0.5f,  // Higher tech = more available
        // ... existing cases
    };
}
```

### 5. Set Trade Policy (if restricted)

**Note:** Policies are now defined per **CommodityCategory**, not individual commodities.

```csharp
// In Tuning.FactionPolicies.InitializeCoreFactionPolicies()
// Set policy for the category containing your commodity

// Example: Restrict a Dangerous category commodity
var independentPolicy = CreateCommodityDefaultPolicy(TradePolicy.Legal);
independentPolicy[CommodityCategory.Dangerous] = TradePolicy.Controlled;
CommodityPolicies[Faction.Independent] = independentPolicy;

// Or customize GetPolicy for specific factions
public static TradePolicy GetPolicy(Faction faction, CommodityCategory category) {
    if (faction.IsCivilian() && category == CommodityCategory.Vice) {
        return TradePolicy.Prohibited;
    }
    // ... default lookup
}
```

### Example

```csharp
// 1. Add enum
public enum Commodity {
    // ...
    Nanomachines,  // High-tech healing
}

// 2. Add data
new CommodityData(
    BaseValue: 200f,        // Expensive
    Volume: 0.01f,          // Very compact
    Mass: 0.001f,           // Nearly weightless
    Flag: CommodityFlag.None,
    Tier: GameTier.Late     // Late-game only
)

// 3. Category
Commodity.Nanomachines => CommodityCategory.Dangerous,

// 4. Availability
Commodity.Nanomachines => Math.Max(0, location.TechLatitude - 1.0f),  // Only high tech

// 5. Policy
if (commodity == Commodity.Nanomachines &&
    faction != Faction.Player) {
    return TradePolicy.Controlled;  // Regulated everywhere
}
```

---

## Adding Factions

**Difficulty:** Moderate (civilian), Hard (new faction type)
**Files:** `Faction.cs`, `Tuning.cs`, `Crawler.cs:737-761`
**Documentation:** Update [DATA-MODEL.md](DATA-MODEL.md), [SYSTEMS.md](SYSTEMS.md)

### Civilian Factions (0-19)

**Already exist** - Generated procedurally during world creation
- Each controls territory
- Has faction capital
- Individual trade policies

**To customize civilian faction:**
```csharp
// In Map.cs GenerateFactionPolicy() or in Tuning.FactionPolicies
public static TradePolicy GetPolicy(Faction faction, CommodityCategory category) {
    // Make Civilian5 more restrictive
    if (faction == Faction.Civilian5) {
        if (category == CommodityCategory.Vice) {
            return TradePolicy.Prohibited;
        }
        if (category == CommodityCategory.Dangerous) {
            return TradePolicy.Controlled;
        }
    }

    // Default lookup
    if (CommodityPolicies.TryGetValue(faction, out var policy)) {
        return policy[category];
    }
    return TradePolicy.Legal;
}

// Similarly for segments:
public static TradePolicy GetPolicy(Faction faction, SegmentKind kind) {
    if (faction == Faction.Civilian5 && kind == SegmentKind.Offense) {
        return TradePolicy.Prohibited;
    }
    // ... default lookup
}
```

### New Faction Type

**1. Add to Faction Enum:**
```csharp
// In Faction.cs
public enum Faction {
    Player,
    Independent,
    Bandit,
    Civilian0, ..., Civilian19,
    MyNewFaction,  // Add here
}
```

**2. Define Spawn Weights:**
```csharp
// In Tuning.Encounter
public static EArray<TerrainType, EArray<Faction, float>> crawlerSpawnWeight = [
    // Flat terrain
    [0, 1, 6, 0, 0, ..., 2],  // Add weight for MyNewFaction

    // ... other terrains
];
```

**3. Define Initial Relationship:**
```csharp
// In Crawler.NewRelation() - Crawler.cs:737-761
ActorToActor NewRelation(IActor to) {
    var result = new ActorToActor();

    if (Faction == Faction.MyNewFaction && to.Faction == Faction.Player) {
        // Define how MyNewFaction treats player
        result.Hostile = SomeCondition();
    }

    // ... existing logic
    return result;
}
```

**4. Define Behavior:**
```csharp
// In Crawler.Tick(IEnumerable<IActor> Actors)
public void Tick(IEnumerable<IActor> Actors) {
    if (Faction == Faction.MyNewFaction) {
        TickMyNewFaction(Actors);
    }
    // ... existing logic
}

private void TickMyNewFaction(IEnumerable<IActor> Actors) {
    // AI behavior for your faction
    foreach (var actor in Actors) {
        if (ShouldInteractWith(actor)) {
            // Custom interaction logic
        }
    }
}
```

**5. Generate Faction Actors:**
```csharp
// In Encounter.GenerateFactionActor()
result = faction switch {
    Faction.Bandit => GenerateBanditActor(),
    Faction.Player => GeneratePlayerActor(),
    Faction.Independent => GenerateTradeActor(),
    Faction.MyNewFaction => GenerateMyNewFactionActor(),
    _ when faction.IsCivilian() => GenerateCivilianActor(faction),
    _ => throw new ArgumentException($"Unexpected faction: {faction}")
};
```

---

## Tuning Parameters

**Difficulty:** Easy
**File:** `Tuning.cs`
**Documentation:** Usually comment changes in code

### Structure

```csharp
public static partial class Tuning {
    public static class Game {
        public static float LootReturn = 0.5f;
        // ...
    }

    public static class Bandit {
        public static float demandChance = 0.6f;
        public static float demandFraction = 0.33f;
        // ...
    }

    // ... more categories
}
```

### Common Tuning Categories

**Game** - Core mechanics
- Loot return rates
- Hazard/resource payoffs

**Bandit** - Bandit behavior
- Demand chances
- Demand fractions
- Value thresholds

**Civilian** - Civilian faction behavior
- Tax rates
- Contraband scan chances
- Penalty multipliers

**Encounter** - Encounter generation
- Arrival rates
- Faction spawn weights
- Dynamic crawler lifetimes

**Trade** - Trading system
- Bid-ask spreads
- Faction markups
- Policy multipliers

**Crawler** - Crawler resources
- Consumption rates
- Morale adjustments
- Standby power fraction

**Economy** - Price calculations
- Local markups
- Scarcity premiums
- Availability curves

**Segments** - Segment scaling
- Tier value tables
- Size/cost/drain curves

**FactionPolicies** - Trade restrictions
- Policy per faction/category/kind (CommodityCategory and SegmentKind)

### Adding New Tuning Section

```csharp
public static partial class Tuning {
    public static class MyNewSystem {
        public static float myParameter = 1.0f;
        public static int myThreshold = 100;
        public static float[] myTable = [0.5f, 1.0f, 1.5f];

        // Can have methods too
        public static float CalculateSomething(float input) {
            return input * myParameter;
        }
    }
}
```

**Usage:**
```csharp
float value = Tuning.MyNewSystem.myParameter;
float result = Tuning.MyNewSystem.CalculateSomething(value);
```

---

## Best Practices

### When Adding Proposals
1. ✅ Make InteractionCapable() return true only when the interaction should be available
2. ✅ Use Immediacy enum correctly in IInteraction.Immediacy() (Disabled/Menu/Immediate)
3. ✅ Provide clear descriptions (shown in menus)
4. ✅ Test both Immediacy() and Perform() paths
5. ✅ Return appropriate AP costs from Perform()
6. ✅ Send messages to both actors via Message()
7. ✅ Implement MessageFor() if you need context-specific display messages
8. ✅ Update [SYSTEMS.md](SYSTEMS.md)

### When Adding Segments
1. ✅ Choose appropriate SegmentKind
2. ✅ Balance tiers (size/weight/drain/cost)
3. ✅ Test with various terrain types
4. ✅ Implement Tick() if stateful
5. ✅ Update [DATA-MODEL.md](DATA-MODEL.md)

### When Adding Commodities
1. ✅ Set appropriate category
2. ✅ Configure availability curves
3. ✅ Set trade policies if restricted
4. ✅ Balance value/weight/volume
5. ✅ Update [DATA-MODEL.md](DATA-MODEL.md)

### When Tuning
1. ✅ Test changes in gameplay
2. ✅ Document rationale in comments
3. ✅ Keep related values in same category
4. ✅ Use meaningful parameter names

---

## Testing Your Changes

### Proposals
```csharp
// Add to player for testing
Game.Instance.Player.StoredProposals.Add(new ProposeMyAction());

// Check capability
var proposal = new ProposeMyAction();
var capability = proposal.InteractionCapable(player, npc);
Console.WriteLine($"Capability: {capability}");
```

### Segments
```csharp
// Add to player crawler
var segment = mySegmentDef.NewSegment();
Game.Instance.Player.Supplies.Add(segment);
Game.Instance.Player.UpdateSegments();
```

### Commodities
```csharp
// Give to player
Game.Instance.Player.Supplies[Commodity.MyNewCommodity] = 100f;

// Check price
float price = Commodity.MyNewCommodity.CostAt(player.Location);
Console.WriteLine($"Price: {price}¢¢");
```

---

## Summary

The extension system is designed for easy additions:
- ✅ Proposals require no actor modifications
- ✅ Segments use inheritance for custom behavior
- ✅ Commodities are data-driven
- ✅ Tuning is centralized and type-safe

For system details, see [SYSTEMS.md](SYSTEMS.md)
For data structures, see [DATA-MODEL.md](DATA-MODEL.md)
