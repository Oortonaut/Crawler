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
