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

## Documentation

* **Crawler/README.md** - Project overview and introduction
* **Crawler/Docs/DOCS.md** for complete documentation structure and guidance on when to update each file.
* **Crawler/Docs/ARCHITECTURE.md** - High-level overview
* **Crawler/Docs/SYSTEMS.md** - Detailed system descriptions
* **Crawler/Docs/DATA-MODEL.md** - Class and interface reference
* **Crawler/Docs/EXTENDING.md** - How to add new content
* **Crawler/Docs/TODO.md** - Plans and ideas for future development
* **Crawler/Docs/RULES.md** - This file. Guidelines for AI assistants 

## Processes

### Preparing stashes for a commit 
**IMPORTANT** Use this staging process only when the verb phrase "prepare stashes for commit" is used.
Otherwise, use these as general guidelines for git. For example, if the player asked to "prepare a commit message",
they are asking for that specifically. 
The changed files might have several different commits worth of changes. 
Only create multiple commits when there are distinct groups of changed functionality.
Working with hunks where necessary, compile and stash each commit.
Try to stash more basal changes first.
Update the documentation as needed for the changes in each commit, and include that in the stash.

My end goal is to be able to work through each stash
* copy the message and paste it into my commit. the stash message should be the commit message. focus on
  what the changes does, not why. use imperative mood.
* apply and drop the stash
* review the changed code
* commit

Do not do the commit yourself. All commits must be manually reviewed.
Do not presume the reason for a change unless you have context about my own reasoning or you are explicitly asked to.
Omit and do not add any claude branding. I don't like the noise or the waste of bits.

#### Process notes:
- Work backward from the current state: save modified files, revert to HEAD, then apply changes one commit at a time
- For files with multiple independent changes (like Game.cs), use Edit tool to apply each logical group separately
- Stage each group and create the stash immediately before moving to the next
- Include affected file list in your analysis but not in the stash message
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
