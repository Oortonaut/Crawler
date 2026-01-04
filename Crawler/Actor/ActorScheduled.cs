using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

/// <summary>
/// Base class for actors with scheduling capability.
/// Provides NextEvent scheduling and ConsumeTime functionality.
/// </summary>
public class ActorScheduled : ActorBase, IComparable<ActorScheduled> {
    public new record class Init : ActorBase.Init {
        // Future: scheduling-specific initialization fields
    }

    public new record Data : ActorBase.Data {
        public ActorEvent? NextEvent { get; set; }
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

    /// <summary>
    /// Schedule an action to occur after a delay.
    /// Sets NextEvent and optionally associates an action to be invoked at that time.
    /// </summary>
    public override void ConsumeTime(string tag, int priority, TimeDuration duration, Action? pre = null, Action? post = null) {
        var time = Time + duration;
        var evt = this.NewEventFor(tag, priority, duration, pre, post);
        LogCat.Log.LogInformation("ConsumeTime: {ActorName} scheduling {Tag} at {Time} (current={Current}, duration={Duration})",
            Name, tag, Game.TimeString(time), Game.TimeString(Time), duration);
        Game.Instance!.Schedule(evt);
    }
    public override void IdleUntil(string tag, TimePoint time) {
        var duration = time - Time;
        if (duration.IsPositive) {
            Game.Instance!.ScheduleEncounter(this, time, tag, null, null, 0);
        }
    }

    protected ActorEvent? _nextEvent;
    public TimePoint NextScheduledTime => _nextEvent?.Time ?? Time + TimeDuration.FromSeconds(Tuning.MaxDelay);
    //public ScheduleEvent? Encounter_Scheduled = null;

    public override string ToString() => $"{base.ToString()}\n{_nextEvent} at {Game.TimeString(NextScheduledTime)}\nNow {Game.TimeString(Time)}";

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
            //SetNextEvent(data.NextEvent);
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
