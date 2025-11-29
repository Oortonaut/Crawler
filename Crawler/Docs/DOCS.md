## Crawler Documentation

**IMPORTANT:** When you change code or notice a discrepancy, update the appropriate documentation file:

### Documentation Structure

Feel free to also modify this file as needed.

**Crawler/README.md** - Project overview and introduction

- Update when: Project description changes, future plans change, major features added
- Contains: Project description, author notes, future plans

**Crawler/Docs/ARCHITECTURE.md** - High-level overview

- Update when: Core game loop changes, system map changes, adding/removing major systems
- Contains: System map, component overview, design principles

**Crawler/Docs/SYSTEMS.md** - Detailed system descriptions

- Update when: Changing proposal system, trading calculations, combat flow, movement logic, tick behavior
- Contains: Proposal/interaction system, trading prices, combat phases, movement calculations, power system, tick
  hierarchy

**Crawler/Docs/DATA-MODEL.md** - Class and interface reference

- Update when: Changing IActor, ActorToActor, segments, inventory, commodities, enums
- Contains: Interface definitions, relationship tracking, segment hierarchy, commodity categories

**Crawler/Docs/EXTENDING.md** - How to add new content

- Update when: Adding new extension patterns, examples, best practices
- Contains: Step-by-step guides for adding proposals, segments, commodities, factions

### When to Update What

**Changed IProposal implementations?** → Crawler/Docs/SYSTEMS.md (Proposal/Interaction System section)

**Changed IInteraction.Immediacy() behavior?** → Crawler/Docs/SYSTEMS.md + Interactions.cs XML comments

**Changed ActorToActor fields?** → Crawler/Docs/DATA-MODEL.md (ActorToActor section)

**Added new commodity?** → Crawler/Docs/DATA-MODEL.md (Commodities section) + Crawler/Docs/EXTENDING.md (example)

**Changed combat damage flow?** → Crawler/Docs/SYSTEMS.md (Combat System section)

**Changed trading price formula?** → Crawler/Docs/SYSTEMS.md (Trading System section)

**Added new system?** → Crawler/Docs/ARCHITECTURE.md (add to system map) + Crawler/Docs/SYSTEMS.md (detailed description)

**Changed tuning parameters?** → Usually just code comments, not docs (unless pattern changes)

### Quick Orientation

New to the codebase? Read in this order:

1. Crawler/Docs/ARCHITECTURE.md (10 min) - Get the big picture
2. Crawler/Docs/SYSTEMS.md (20 min) - Understand key systems
3. Relevant section in Crawler/Docs/DATA-MODEL.md or Crawler/Docs/EXTENDING.md as needed

## Code Organization

See Crawler/Docs/ARCHITECTURE.md for the system map and component relationships.

## Processes

See Crawler/Docs/RULES.md for process guidelines including commit preparation and documentation grooming.
                  
## Style and Naming Guidelines
- a FooCfg (or Foo.Cfg) contains static, constant information shared by multiple Foo objects. Configs are typically held and referenced to avoid duplication of const data. Typically a long-lifetime or read-serialized object.
- a FooInit (or Foo.Init) contains information about the initial dynamic state of a Foo object. Typically a short-lived structure containing a config and constructor information that will be moved or copied into a Foo. Used once only.
- a FooData (or Foo.Data, FooFactionData, etc.) contains privileged information about the current internal state of a Foo object. Used for networking, save load, cloning, etc.
- a Foo contains the persistent internal state, indexes and helper data, transient internal state, and logic for Foo objects.
- A FooBuilder creates a Foo from a (possibly default) Init or Cfg when the object setup process requires multiple steps or finalization. It calls the Init or Cfg constructor, performs any setup or deserialization of the internal state, and finalizes initialization if necessary.

### Constructor Patterns

Objects follow these constructor patterns:

```csharp
new Foo(Foo.Def)                      // From definition
new Bar(Bar.Cfg)                      // From config
new Baz(Baz.Init)                     // From init (normal construction)
new Baz(Baz.Init, Baz.Data)           // From init + data (load from save)
new Wut()                             // Default constructor
```

### Init/Cfg/Data Pattern (Actors)

The actor hierarchy uses a consistent Init/Cfg/Data pattern:

**Init Structure:**
- Contains construction-time configuration
- Used for object initialization
- Short-lived, used once

**Data Structure:**
- Contains Init as first field (not duplicating fields)
- Captures current runtime state for save/load
- Forms inheritance hierarchy matching class hierarchy
- Example: `ActorBase.Data` → `ActorScheduled.Data` → `Crawler.Data`

**Constructor Pattern:**
```csharp
public Crawler(Init init) : base(init) {
    // Normal construction from Init
}

public Crawler(Init init, Data data) : this(init) {
    FromData(data);  // Restore state from Data
}
```

**Serialization Methods:**
```csharp
public virtual Data ToData() {
    // Create snapshot of current state
}

public virtual void FromData(Data data) {
    // Restore runtime state from Data
}
```

**See:** [DATA-MODEL.md#saveload-data-structures](DATA-MODEL.md#saveload-data-structures) for full details

These constructor patterns ensure consistent initialization across the codebase. Objects with final initialization (actors) use the Init/Data pattern, while simpler classes use regular constructors.
