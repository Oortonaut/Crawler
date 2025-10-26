---
apply: always
---

# Claude Code Guidelines for Crawler

## C# Style

- Use C#13 features when possible
- Prefer LINQ pipeline style
- Use file-scoped namespaces
- Use primary constructors where appropriate
- Use collection expressions `[]` instead of `new List<>()`
- Prefer `new ()` over `new T()`
- Use the [Flags] attribute and HasFlag() and SetFlag() instead of bitwise operators
- Prefer `var`

## Architecture Documentation

**IMPORTANT:** When you change code or notice a discrepancy, update the appropriate documentation file:

### Documentation Structure

Feel free to also modify this file as needed.

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

### Preparing for a commit

The changed files will probably have several different commits worth of changes.
To prepare for a commit, you need to go through and partition the changed hunks into groups based on function.
Try to work out the dependencies between the different groups, if any, but don't bust your ass too hard.
Then linearize that graph and create a separate stash for each group with the staged changes and a strong description.
My end goal is to be able to work through each stash in my source control tool, apply and drop the stash, and then
do my own review and commit without having to stage or enter the message.
Omit and do not add any claude branding. I don't like the noise or the waste of bits.
Do not do the commit yourself. All commits must be manually reviewed.
Do not presume the reason for a change unless you have context about my own reasoning or you are explicitly asked to.

#### Process notes:
- Work backward from the current state: save modified files, revert to HEAD, then apply changes one commit at a time
- For files with multiple independent changes (like Game.cs), use Edit tool to apply each logical group separately
- Stage each group and create the stash immediately before moving to the next
- Git stashes are LIFO - the final order will be reversed, with most recent (last created) on top
- Include affected file list in your analysis but not in the stash message
- Stash messages should be imperative mood, focused on what the change does, not why
- Verify working tree is clean after all stashes are created
- Create a backup stash before you begin work, to help fix things if they go wrong. Clean these up after a week. 
  
#### Staging techniques
- **Whole files (simple):** `git add <files>` when all changes in those files belong to one commit
- **Partial files (complex):** For files needing split:
    1. `cp <file> /tmp/<file>_full` (backup the full changes)
    2. `git checkout HEAD -- <file>` (revert to clean state)
    3. Use Edit tool to apply only the changes for this commit
    4. `git add <file> && git stash push --staged -m "description"`
    5. Repeat steps 2-4 for each subsequent commit affecting that file
- **Avoid:** `git add -p` (interactive mode doesn't work well in this environment)
- **Avoid:** Trying to selectively unstage hunks - too fragile, easy to make mistakes

#### Creating stashes:
- Always use: `git stash push --staged -m "message"`
- Never use: `git stash` alone (stashes unstaged changes too)
- The `--staged` flag ensures only explicitly staged changes are stashed

### Grooming the documentation
The code is the ultimate source of truth; the documentation is just that.
The documentation should be correct and complete. Check for the missing and extra. Check names and relationships.

~~~~
