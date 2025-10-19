# Claude Code Guidelines for Crawler

## C# Style
- Use C#13 features when possible
- Prefer LINQ pipeline style
- Use file-scoped namespaces
- Use primary constructors where appropriate
- Use collection expressions `[]` instead of `new List<>()`

## Architecture Documentation

**IMPORTANT:** When you change code or notice a discrepancy, update the appropriate documentation file:

### Documentation Structure

**docs/ARCHITECTURE.md** - High-level overview
- Update when: Core game loop changes, system map changes, adding/removing major systems
- Contains: System map, component overview, design principles

**docs/SYSTEMS.md** - Detailed system descriptions
- Update when: Changing proposal system, trading calculations, combat flow, movement logic, tick behavior
- Contains: Proposal/interaction system, trading prices, combat phases, movement calculations, power system, tick hierarchy

**docs/DATA-MODEL.md** - Class and interface reference
- Update when: Changing IActor, ActorToActor, segments, inventory, commodities, enums
- Contains: Interface definitions, relationship tracking, segment hierarchy, commodity categories

**docs/EXTENDING.md** - How to add new content
- Update when: Adding new extension patterns, examples, best practices
- Contains: Step-by-step guides for adding proposals, segments, commodities, factions

### When to Update What

**Changed IProposal implementations?** → docs/SYSTEMS.md (Proposal/Interaction System section)

**Changed InteractionCapability behavior?** → docs/SYSTEMS.md + Trade.cs XML comments

**Changed ActorToActor fields?** → docs/DATA-MODEL.md (ActorToActor section)

**Added new commodity?** → docs/DATA-MODEL.md (Commodities section) + docs/EXTENDING.md (example)

**Changed combat damage flow?** → docs/SYSTEMS.md (Combat System section)

**Changed trading price formula?** → docs/SYSTEMS.md (Trading System section)

**Added new system?** → docs/ARCHITECTURE.md (add to system map) + docs/SYSTEMS.md (detailed description)

**Changed tuning parameters?** → Usually just code comments, not docs (unless pattern changes)

### Quick Orientation

New to the codebase? Read in this order:
1. docs/ARCHITECTURE.md (10 min) - Get the big picture
2. docs/SYSTEMS.md (20 min) - Understand key systems
3. Relevant section in docs/DATA-MODEL.md or docs/EXTENDING.md as needed

### Legacy Documentation

The old comprehensive Architecture.md still exists at `Crawler/Architecture.md` but is deprecated. It's kept for reference but should not be updated. Use the docs/ directory instead.

## Code Organization

See docs/ARCHITECTURE.md for the system map and component relationships.
