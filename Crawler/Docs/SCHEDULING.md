# Scheduling System Documentation

## Overview

The Crawler game uses a hierarchical event-driven scheduling system with three levels:
1. **Game Level** - Coordinates all encounters and traveling crawlers
2. **Encounter Level** - Schedules actor events within each encounter
3. **Actor Level** - Individual actors schedule their own actions

## Architecture

### Core Components

#### 1. Generic Scheduler (`Scheduler<TContext, TEvent, TElement, TTime>`)

Located in: `Scheduler.cs`

A generic priority queue-based scheduler that:
- Maintains one event per "tag" (element)
- Uses **lazy deletion** for efficiency when events are rescheduled
- Supports **priority levels** - higher priority events preempt lower priority ones
- For same priority, earlier events preempt later ones

**Key Design Principles:**
- Each tag (element) can only have ONE scheduled event at a time
- When a new event is scheduled for an existing tag:
  - If higher priority OR (same priority AND earlier time): replaces current event
  - Otherwise: dropped (current event kept)
- Old events remain in the priority queue but are skipped during dequeue ("lazy deletion")

**Data Structures:**
```csharp
PriorityQueue<TEvent, TTime> eventQueue;          // All events ordered by time
Dictionary<TElement, TEvent> schedEventForTag;    // Current event per tag
```

**Key Methods:**
- `Schedule(TEvent evt)` - Schedule/reschedule event for a tag
- `Peek(out TEvent?, out TTime?)` - View next event (skips lazy-deleted events)
- `Dequeue()` - Remove and return next event (skips lazy-deleted events)

#### 2. ScheduleEvent

Located in: `ActorScheduled.cs`

Represents an actor's scheduled action:
```csharp
public record ScheduleEvent(
    string tag,         // Description of the event (e.g., "Idle", "Traveling")
    int Priority,       // Higher priority preempts lower
    long Start,         // When the action begins
    long End,           // When the action completes
    Action? Pre,        // Invoked once when event starts
    Action? Post        // Invoked once when event completes
) : ISchedulerEvent<ActorScheduled, long>
```

**Important Properties:**
- `Actor` - The actor this event belongs to
- `_invoked` - Tracks if Pre action has been called
- Implements `ISchedulerEvent<ActorScheduled, long>` for use with generic Scheduler

**ISchedulerEvent Interface:**
- `Tag` → Actor (the element/tag for this event)
- `Time` → End (when the event completes)
- `Priority` → Priority

## Three-Level Hierarchy

### Level 1: Game Scheduler

**Purpose:** Coordinate all encounters and traveling crawlers globally

**Components:**
```csharp
Scheduler<Game, EncounterSchedulerEvent, Encounter, long> encounterScheduler;
Scheduler<Game, CrawlerTravelEvent, Crawler, long> crawlerTravelScheduler;
```

**GameTime:**
- `public long GameTime { get; private set; } = 100_000_000_000;`
- Updated as events are processed: `GameTime = evt.Time;`
- Accessible via `Game.SafeTime` static property
- Represents current global simulation time

**Main Loop (`Game.Run()`):**
1. Peek at next encounter event time
2. Peek at next crawler travel event time
3. Process whichever is earlier:
   - `RunEncounters()` - Tick an encounter forward
   - `CrawlerArrival()` - Handle crawler arriving at location
4. Update `GameTime` to the event time
5. Repeat until game ends

**Encounter Scheduling (`Game.Schedule(Encounter)`):**
```csharp
public void Schedule(Encounter encounter) {
    var nextEvt = encounter.NextEncounterEvent;
    var schedulerEvent = new EncounterSchedulerEvent(encounter, nextEvt);
    encounterScheduler.Schedule(schedulerEvent);
    encounter.Game_Scheduled = nextEvt;
}
```

**Key Points:**
- Each encounter has at most one scheduled event in the game scheduler
- When encounter needs to be rescheduled (actor events change), call `Game.Schedule()`
- Encounters track their scheduled event via `Game_Scheduled` field

**RunEncounters() Flow:**
```csharp
void RunEncounters() {
    var evt = encounterScheduler.Dequeue();
    if (evt != null) {
        var encounter = evt.Encounter;
        var turn = evt.Time;
        GameTime = turn;                    // Update global time
        encounter.Game_Scheduled = null;    // Clear cached scheduled event
        encounter.Tick(turn);               // Process encounter up to this time
        Schedule(encounter);                // Reschedule for next event
    }
}
```

### Level 2: Encounter Scheduler

**Purpose:** Schedule actor events within a single encounter

**Components:**
```csharp
Scheduler<Encounter, ScheduleEvent, ActorScheduled, long> actorScheduler;
public long EncounterTime { get; set; }  // Current time within this encounter
```

**EncounterTime:**
- Represents the "current time" within this encounter
- Initialized in constructor (typically in the past for retroactive simulation):
  ```csharp
  long offset = (Rng / "StartTime").NextInt64(3600);
  EncounterTime = Game.SafeTime - offset -
                  CrawlerEx.PoissonQuantileAt(..., 0.95f);
  ```
- Advanced as actor events are processed (in `Tick()`)
- **Invariant:** All scheduled actors must have `nextEvent.End >= EncounterTime`

**Actor Scheduling (`ActorScheduleChanged()`):**
```csharp
public void ActorScheduleChanged(ActorScheduled actor) {
    var nextEvent = actor.GetNextEvent();
    long nextTurn = nextEvent.End;

    Debug.Assert(nextTurn >= EncounterTime);  // Actor must be scheduled in future

    actorScheduler.Schedule(nextEvent);  // Add to encounter scheduler
    UpdateGlobalSchedule();              // May need to reschedule encounter at game level
}
```

**UpdateGlobalSchedule():**
- Only calls `Game.Schedule(this)` if NOT currently ticking
- Optimization: batch multiple actor changes during tick, update game scheduler once at end

**NextEncounterEvent Property:**
Returns the next actor event (or an idle event if no actors scheduled):
```csharp
public ScheduleEvent NextEncounterEvent {
    get {
        if (actorScheduler.Peek(out var evt, out var time)) {
            return evt!;
        } else {
            // No actors scheduled - return far-future idle event
            return new ScheduleEvent("Encounter Idle", 0,
                EncounterTime, EncounterTime + Tuning.MaxDelay, null, null);
        }
    }
}
```

**Encounter.Tick(long time) Flow:**

```csharp
public void Tick(long time) {
    Debug.Assert(EncounterTime <= time);

    isTicking = true;  // Block UpdateGlobalSchedule during batch processing

    try {
        while (TryDequeueEvent(time, out var actor, out var evt)) {
            long eventTime = evt.End;

            // 1. Spawn dynamic crawlers for elapsed time
            SpawnDynamicCrawlers(EncounterTime, eventTime);

            // 2. Advance encounter time
            EncounterTime = eventTime;

            // 3. Process actor event
            Tick(actor, evt);  // Calls actor.TickTo() and actor.Think()

            // 4. Actor reschedules itself
            ActorScheduleChanged(actor);

            // 5. Trigger menu if player
            if (actor.Flags.HasFlag(ActorFlags.Player)) {
                Game.Instance!.GameMenu();
            }
        }
    } finally {
        isTicking = false;
        UpdateGlobalSchedule();  // Update game scheduler once after all events
    }
}
```

**TryDequeueEvent():**
- Peeks at next actor event
- Only dequeues if `eventTime <= time` (event happens by the requested time)
- Returns false if no events or all events are in the future

**Key Points:**
- Processes ALL actor events up to requested `time` in chronological order
- `EncounterTime` advances incrementally with each actor event
- Dynamic crawlers spawned for each time step
- Re-entrancy guard prevents nested global schedule updates

### Level 3: Actor Scheduling

**Purpose:** Actors schedule their own actions

**Components:**
```csharp
protected ScheduleEvent? _nextEvent;  // Actor's current scheduled event
public long NextScheduledTime => _nextEvent?.End ?? Time + Tuning.MaxDelay;
```

**Actor Time:**
- `public long Time { get; protected set; }` - Actor's current simulation time
- Advanced via `SimulateTo(long time)` method
- Updated by encounter when actor's event is processed

**SetNextEvent(ScheduleEvent nextEvent):**

Handles scheduling/rescheduling with priority logic:

```csharp
public void SetNextEvent(ScheduleEvent nextEvent) {
    if (_nextEvent == null) {
        // No current event - accept new one
        _nextEvent = nextEvent;
        Encounter.ActorScheduleChanged(this);
    } else {
        // Already have an event - check priority
        if (nextEvent.Priority > _nextEvent.Priority) {
            // Higher priority - preempt current event
            _nextEvent = nextEvent;
            Encounter.ActorScheduleChanged(this);
        } else if (nextEvent.Priority == _nextEvent.Priority &&
                   nextEvent.End < _nextEvent.End) {
            // Same priority but earlier - replace current event
            _nextEvent = nextEvent;
            Encounter.ActorScheduleChanged(this);
        } else {
            // Lower priority or later time - drop new event
            Log.LogWarning($"Dropped event {nextEvent}");
        }
    }
}
```

**Scheduling Methods:**

```csharp
// Schedule event after a duration from current time
void ConsumeTime(string tag, int priority, long duration,
                 Action? pre = null, Action? post = null)

// Schedule event at specific absolute time
void PassTimeUntil(string tag, long time)

// Get current event (creates idle event if none)
ScheduleEvent GetNextEvent()
```

**TickTo(long encounterTime, ScheduleEvent? evt):**

Called by encounter when processing actor's event:

```csharp
internal void TickTo(long encounterTime, ScheduleEvent? evt) {
    Debug.Assert(evt == null || evt.End == encounterTime);

    // 1. Invoke event's Pre action (if any) and simulate to time
    SimulateTo(encounterTime);

    // 2. Invoke event's Post action (if any)
    if (evt?.Post != null) {
        evt.Post.Invoke();
    }

    // 3. Actor AI decides next action
    Think();

    // 4. Any cleanup
    PostTick(encounterTime);
}
```

**EventServiced(ScheduleEvent evt):**
```csharp
public void EventServiced(ScheduleEvent evt) {
    Debug.Assert(evt == _nextEvent);
    _nextEvent = null;  // Clear current event (actor will reschedule in Think())
}
```

## Timing Invariants

The scheduling system maintains two fundamental invariants:

### Core Invariants

1. **New crawlers can't act before completed actions**
   - When dynamic crawlers are spawned during tick processing, they must not be scheduled for times before the current event being processed
   - Enforced by: `arrivalTime >= EncounterTime` check in `SpawnDynamicCrawlers`
   - Prevents retroactive scheduling during simulation

2. **Encounter time never moves backward**
   - `EncounterTime` is monotonically increasing
   - Each processed event advances time forward
   - Enforced by: `Debug.Assert(EncounterTime <= eventTime)` before advancing

### Critical Assertions

1. **Actor scheduling:**
   ```csharp
   Debug.Assert(nextTurn >= EncounterTime);
   ```
   - Actors must be scheduled for future times (relative to encounter's current time)
   - Called in `ActorScheduleChanged` whenever an actor is scheduled/rescheduled

2. **Event time bounds:**
   ```csharp
   Debug.Assert(nextEvent.End >= Time);    // Event end is in actor's future
   Debug.Assert(nextEvent.Start <= Time);  // Event start is not in future
   ```
   - Ensures actor events make temporal sense relative to actor's current time

3. **Encounter tick:**
   ```csharp
   Debug.Assert(EncounterTime <= time);         // Encounter moves forward
   Debug.Assert(EncounterTime <= eventTime);    // Process events in order
   Debug.Assert(eventTime <= time);             // Don't overshoot requested time
   ```
   - Ensures encounter processes events in chronological order
   - Prevents time from moving backward during processing

4. **All actors scheduled in future:**
   ```csharp
   scheduledActors.Assert(a => a.NextScheduledTime >= EncounterTime);
   ```
   - Before processing each actor event, verifies all other actors have valid schedules
   - Catches any actor accidentally scheduled in the past

### Time Relationships

```
Game.SafeTime (GameTime)
  ↓ (global simulation time)
  |
  ├─ Encounter1.EncounterTime
  │    ↓ (encounter's current time)
  │    |
  │    ├─ Actor1.Time ← must equal EncounterTime after processing
  │    │    ↓
  │    │    └─ Actor1._nextEvent.End ← must be >= EncounterTime
  │    │
  │    └─ Actor2.Time
  │         └─ Actor2._nextEvent.End
  │
  └─ Encounter2.EncounterTime
       └─ ...
```

## Dynamic Crawler Spawning

Located in: `Encounter.SpawnDynamicCrawlers(long previousTime, long currentTime)`

**Purpose:** Retroactively generate "background" crawlers for time periods

**When Called:**
1. During `Encounter.Create()` - spawn from `EncounterTime` to `Game.SafeTime`
2. During `Encounter.Tick()` - spawn for each time step as encounter advances

**Algorithm:**
```csharp
void SpawnDynamicCrawlers(long previousTime, long currentTime) {
    long elapsed = currentTime - previousTime;
    if (elapsed <= 0) return;

    // 1. Calculate expected number of arrivals (Poisson distribution)
    float expectation = hourlyArrivals * elapsed / 3600;
    int arrivalCount = CrawlerEx.PoissonQuantile(expectation, ref Rng);

    if (arrivalCount > 0) {
        var arrivals = new List<(long arrivalTime, int lifetime)>();

        // 2. Generate random arrival times and lifetimes
        for (int i = 0; i < arrivalCount; i++) {
            int lifetime = CrawlerEx.PoissonQuantile(
                Tuning.Encounter.DynamicCrawlerLifetimeExpectation, ref Rng);
            long arrivalTime = previousTime + Rng.NextInt64(elapsed);

            // 3. Only add crawlers that:
            //    a) Are still present at currentTime (arrivalTime + lifetime > currentTime)
            //    b) Arrive at or after current encounter time (arrivalTime >= EncounterTime)
            //       This prevents adding crawlers scheduled in the past during tick processing
            if (arrivalTime + lifetime > currentTime && arrivalTime >= EncounterTime) {
                arrivals.Add((arrivalTime, lifetime));
            }
        }

        // 4. Sort by arrival time and add in order
        arrivals.Sort((a, b) => a.arrivalTime.CompareTo(b.arrivalTime));
        foreach (var (arrivalTime, lifetime) in arrivals) {
            AddDynamicCrawler(Rng.Seed(), arrivalTime, lifetime);
        }
    }
}
```

**Key Points:**
- Crawlers are added with `arrivalTime` in `[previousTime, currentTime)`
- Each crawler has a `lifetime` - they'll leave after `arrivalTime + lifetime`
- Only crawlers still present at `currentTime` are added (optimization)
- **Critical:** Only crawlers with `arrivalTime >= EncounterTime` are added to prevent scheduling actors in the past
- Arrivals are processed in chronological order

**Timing Invariant Enforcement:**

During tick processing, `SpawnDynamicCrawlers(EncounterTime, eventTime)` is called **before** `EncounterTime` advances to `eventTime`. The check `arrivalTime >= EncounterTime` ensures:

1. No crawler is scheduled for a time before the current encounter time
2. New crawlers can only act at or after completed actions
3. Encounter time never moves backward (monotonically increasing)

## Initialization Flow

### Game Initialization

```
Game Constructor
  ↓
Game.Construct(crawlerName, H)
  ↓
Map Constructor (creates sectors and locations)
  ↓
Map.InitializeFactionSettlements()
  ↓
For each faction:
  ├─ new Encounter(seed, location, faction)
  │    ├─ Calculate EncounterTime = SafeTime - offset - PoissonQuantile
  │    ├─ Create actorScheduler
  │    └─ Game.RegisterEncounter(this)
  │
  ├─ encounter.CreateCapital(seed)  [creates but doesn't add]
  │
  ├─ encounter.AddActorAt(capital, encounter.EncounterTime)
  │    ├─ capital.PassTimeUntil("Arriving", EncounterTime)
  │    │    └─ Calls ActorScheduleChanged()
  │    │         └─ Updates actorScheduler and Game scheduler
  │    └─ Fires ActorArrived event
  │
  └─ encounter.SpawnDynamicCrawlers(EncounterTime, Game.SafeTime)
       └─ Adds retroactive crawlers with various arrival times
```

### Lazy Encounter Creation

When player moves to a new location:

```
Location.GetEncounter()
  ↓ (if _encounter == null)
NewEncounter(this)
  ↓
new Encounter(seed, location).Create()
  ↓
Encounter Constructor
  ├─ EncounterTime = Game.SafeTime - offset - PoissonQuantile
  │    ↑ (Game.SafeTime is CURRENT time, updated during gameplay)
  └─ Register with Game
  ↓
Encounter.Create()
  ├─ CreateSettlement/Resource/Hazard/Crossroads actor
  ├─ AddActorAt(actor, EncounterTime)
  └─ SpawnDynamicCrawlers(EncounterTime, Game.SafeTime)
```

**Why EncounterTime is in the past:**
- Makes newly discovered locations feel "lived in"
- Dynamic crawlers spawn retroactively
- Gives encounter history/state when player arrives

## Common Patterns

### Adding an Actor to an Encounter

```csharp
// Option 1: Add at current encounter time
encounter.AddActor(actor, lifetime);  // Uses EncounterTime

// Option 2: Add at specific time (past or present)
encounter.AddActorAt(actor, arrivalTime, lifetime);
```

**What happens in AddActorAt:**
1. Actor begins if loading: `actor.Begin()`
2. If lifetime specified, add `LeaveEncounterComponent(arrivalTime + lifetime)`
3. Add to encounter's actor dictionary
4. Call `actor.Arrived(this)` to notify actor
5. Fire `ActorArrived` event
6. If ActorScheduled: `actor.PassTimeUntil("Arriving", arrivalTime)`
   - This schedules the actor
   - Triggers `ActorScheduleChanged()`
   - May update game scheduler

### Actor Scheduling Its Next Action

In actor's `Think()` method:

```csharp
public override void Think() {
    // Option 1: Wait for a duration
    ConsumeTime("Idle", 0, 3600);  // Idle for 1 hour

    // Option 2: Schedule at specific time
    PassTimeUntil("Travel", targetTime);

    // Option 3: Conditional logic
    if (needsToAct) {
        ConsumeTime("Acting", 1, actionDuration,
            pre: () => StartAction(),
            post: () => FinishAction());
    } else {
        ConsumeTime("Idle", 0, 60);
    }
}
```

### Component Scheduling

Components can schedule actor actions:

```csharp
public class MyComponent : ActorComponent {
    public override void Tick(long time) {
        // Check if we need to do something
        if (ShouldAct()) {
            // Schedule higher priority event to interrupt current action
            Owner.ConsumeTime("MyAction", 10, duration,
                post: () => CompleteAction());
        }
    }
}
```

**Priority Guidelines:**
- 0: Default/idle actions
- 1-9: Normal priority actions
- 10+: High priority/urgent actions that should interrupt

## Edge Cases & Gotchas

### 1. Stale EncounterTime References

**Problem:** If `Game.SafeTime` doesn't advance, new encounters use stale time

**Solution:** Update `GameTime` in `RunEncounters()` and `CrawlerArrival()`

### 2. Actor Scheduled Before EncounterTime

**Problem:** Assertion `nextTurn >= EncounterTime` fails

**Common Causes:**
- Adding actor at fixed past time, but EncounterTime is random/later
- EncounterTime advanced but actor scheduled at old time

**Solution:** Always add actors at times >= current `EncounterTime`

### 3. Re-entrancy During Tick

**Problem:** Actor scheduling change during tick could cause cascading updates

**Solution:** `isTicking` guard prevents `UpdateGlobalSchedule()` during batch processing

### 4. Lazy Deletion Buildup

**Problem:** Priority queue contains many stale events

**Impact:** Minimal - Peek/Dequeue skip stale events efficiently

**Why it works:** Only one "real" event per tag, rest are ignored

### 5. Event Pre/Post Invocation

**Subtlety:** `Pre` called once when event starts (in `SimulateTo`)
**Subtlety:** `Post` called once when event completes (in `TickTo`)

**Tracking:** `_invoked` flag ensures `Pre` only called once even if `SimulateTo` called multiple times

## Performance Considerations

### Batch Processing

Encounter tick processes multiple actor events before updating game scheduler:
- Reduces overhead of scheduler updates
- Maintains correct event ordering within encounter

### Lazy Deletion

Instead of removing old events from priority queue:
- Just update `schedEventForTag` dictionary
- Skip stale events during peek/dequeue
- Amortized O(1) for most operations

### Idle Events

When no actors scheduled, encounter returns idle event far in future:
- Prevents unnecessary rescheduling
- Game can process other encounters/crawlers
- Re-scheduled when actor actually added/changed

## Debugging Tips

### 1. Time Assertions Failing

Check:
- Is `GameTime` being updated?
- Is `EncounterTime` initialized correctly?
- Are actors being added at valid times?

### 2. Events Not Processing

Check:
- Is actor properly scheduled? (Call `GetNextEvent()`)
- Is encounter scheduled at game level? (Check `Game_Scheduled`)
- Is event time in the past or too far future?

### 3. Unexpected Event Order

Check:
- Event priorities (higher preempts lower)
- Event times (earlier preempts later for same priority)
- Lazy deleted events cluttering queue (usually not an issue)

### 4. Trace Logging

Enable logging to see scheduling flow:
```csharp
Log.LogTrace($"{Name}: {actor.Name} scheduled for {nextTurn}");
Log.LogInformation($"{actor.Name} running {evt}");
```

## Summary

The scheduling system provides:
- **Hierarchical time management** across game, encounters, and actors
- **Event-driven simulation** with priority-based scheduling
- **Efficient rescheduling** via lazy deletion
- **Retroactive simulation** for creating "lived-in" world state
- **Component-based** actor behavior with flexible scheduling

Key insight: Each level maintains its own scheduler and time, coordinating through scheduled events that bubble up/down the hierarchy.
