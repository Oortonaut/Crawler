using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public record ScheduleEvent(string tag, int Priority, long Start, long End, Action? Pre, Action? Post) : ISchedulerEvent<ActorScheduled, long> {
    public ActorScheduled? Actor { get; init; }

    bool _invoked;
    public void PreInvoke() {
        if (!_invoked) {
            _invoked = true;
            Pre?.Invoke();
        }
    }
    public void PostInvoke() {
        if (_invoked) {
            Post?.Invoke();
        }
    }
    public override string ToString() {
        var result = $"{tag}+{Priority} {Game.TimeString(Start)}-{Game.TimeString(End)}";
        result += _invoked ? " Active" : " Pending";
        if (Post != null) {
            result += " Post";
        }
        return result;
    }

    // ISchedulerEvent implementation
    ActorScheduled ISchedulerEvent<ActorScheduled, long>.Tag => Actor!;
    int ISchedulerEvent<ActorScheduled, long>.Priority => Priority;
    long ISchedulerEvent<ActorScheduled, long>.Time => End;
}
/// <summary>
/// Base class for actors with scheduling capability.
/// Provides NextEvent scheduling and ConsumeTime functionality.
/// </summary>
public class ActorScheduled : ActorBase, IComparable<ActorScheduled> {
    public new record class Init : ActorBase.Init {
        // Future: scheduling-specific initialization fields
    }

    public new record class Data : ActorBase.Data {
        public ScheduleEvent? NextEvent { get; set; }
        public long Encounter_ScheduledTime { get; set; }
    }

    public new class Builder : ActorBase.Builder {
        public Builder() : base() { }

        public new Init BuildInit() {
            var baseInit = base.BuildInit();
            return new Init {
                Seed = baseInit.Seed,
                Name = baseInit.Name,
                Brief = baseInit.Brief,
                Faction = baseInit.Faction,
                Location = baseInit.Location,
                Supplies = baseInit.Supplies,
                Cargo = baseInit.Cargo
            };
        }

        public static Builder Load(Init init) {
            return (Builder)new Builder()
                .WithSeed(init.Seed)
                .WithName(init.Name)
                .WithBrief(init.Brief)
                .WithFaction(init.Faction)
                .WithLocation(init.Location)
                .WithSupplies(init.Supplies)
                .WithCargo(init.Cargo);
        }
    }

    // Init-based constructor
    public ActorScheduled(Init init) : base(init) { }

    // Init + Data constructor (for loading from save)
    public ActorScheduled(Init init, Data data) : base(init, data) {
        FromData(data);
    }

    public ActorScheduled(ulong seed, string name, string brief, Factions faction, Inventory supplies, Inventory cargo, Location location)
        : base(seed, name, brief, faction, supplies, cargo, location) {
    }

    protected virtual void EventServiced(ScheduleEvent evt) {
        Debug.Assert(evt == _nextEvent);
        _nextEvent = null;
    }
    protected void SetNextEvent(ScheduleEvent nextEvent) {
        void doit() {
            (var last, _nextEvent) = (_nextEvent, nextEvent);
            // Note: Encounter scheduling has been removed
            // Scheduling is now handled at the Game level for crawlers only
        }

        if (_nextEvent == nextEvent) {
        } else if (_nextEvent == null) {
            doit();
        } else {
            Debug.Assert(nextEvent.End >= Time);
            Debug.Assert(nextEvent.Start <= Time);

            if (_nextEvent.Priority != nextEvent.Priority) {
                if (nextEvent.Priority > _nextEvent.Priority) {
                    doit();
                } else {
                    // dropped lower priority event
                    Log.LogWarning($"Dropped lower priority event {nextEvent.Priority} < {_nextEvent.Priority}");
                }
            } else {
                // same priority
                if (nextEvent.End < _nextEvent.End) {
                    doit();
                } else {
                    // dropped later event
                    Log.LogWarning($"Dropped later event {nextEvent.End} > {_nextEvent.End}");
                }
            }
        }
    }
    /// <summary>
    /// Schedule an action to occur after a delay.
    /// Sets NextEvent and optionally associates an action to be invoked at that time.
    /// </summary>
    public void ConsumeTime(string tag, int priority, long duration, Action? pre = null, Action? post = null) {
        SetNextEvent(this.NewEventFor(tag, priority, duration, pre, post));
    }
    public virtual void PassTimeUntil(string tag, long time) {
        SetNextEvent(this.NewEventFor(tag, 0, time - Time));
    }

    //public ScheduleEvent? NextEvent { get; private set; }
    public ScheduleEvent GetNextEvent() {
        if (_nextEvent == null) {
            SetNextEvent(this.NewIdleEvent());
        }
        return _nextEvent!;
    }
    protected ScheduleEvent? _nextEvent;
    public long NextScheduledTime => _nextEvent?.End ?? Time + Tuning.MaxDelay;
    //public ScheduleEvent? Encounter_Scheduled = null;

    public override void SimulateTo(long time) {
        _nextEvent?.PreInvoke();
        base.SimulateTo(time);
    }

    public override void Think() { }


    public override string ToString() => $"{base.ToString()}\n{_nextEvent?.tag} at {Game.TimeString(NextScheduledTime)}\nNow {Game.TimeString(Time)}";

    // Serialization methods
    public override ActorBase.Data ToData() {
        var baseData = base.ToData();
        return new Data {
            Init = baseData.Init,
            Rng = baseData.Rng,
            Gaussian = baseData.Gaussian,
            Time = baseData.Time,
            LastTime = baseData.LastTime,
            EndState = baseData.EndState,
            EndMessage = baseData.EndMessage,
            ActorRelations = baseData.ActorRelations,
            LocationRelations = baseData.LocationRelations,
            NextEvent = this._nextEvent,
            //Encounter_ScheduledTime = this.Encounter_Scheduled
        };
    }

    public virtual void FromData(Data data) {
        base.FromData(data);
        if (data.NextEvent != null) {
            SetNextEvent(data.NextEvent);
        }
        //this.Encounter_Scheduled = data.Encounter_ScheduledTime;
    }

    // IComparable implementation for use with Scheduler
    public int CompareTo(ActorScheduled? other) {
        if (other == null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        // Compare by identity using GetHashCode as a stable unique identifier
        // The Scheduler uses the Tag only for identity, not ordering
        return GetHashCode().CompareTo(other.GetHashCode());
    }
}
