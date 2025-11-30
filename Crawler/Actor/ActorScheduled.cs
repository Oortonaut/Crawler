using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public record ScheduleEvent(string tag, int Priority, long StartTime, long EndTime, Action? Pre, Action? Post) {
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
        var result = $"{tag}+{Priority} @{Game.TimeString(EndTime)}";
        result += _invoked ? " Active" : " Pending";
        if (Post != null) {
            result += " Post";
        }
        return result;
    }
}
/// <summary>
/// Base class for actors with scheduling capability.
/// Provides NextEvent scheduling and ConsumeTime functionality.
/// </summary>
public class ActorScheduled : ActorBase {
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

    public void SetNextEvent(ScheduleEvent? nextEvent) {
        void doit() {
            (var last, NextEvent) = (NextEvent, nextEvent);
            // Ensure the encounter reschedules this actor for the new time
            if (last != NextEvent) {
                Location.GetEncounter().ActorScheduleChanged(this);
            }
        }

        if (nextEvent == null || NextEvent == null) {
            doit();
        } else {
            Debug.Assert(nextEvent.EndTime >= Time);
            Debug.Assert(nextEvent.StartTime <= Time);

            if (NextEvent.Priority != nextEvent.Priority) {
                if (nextEvent.Priority > NextEvent.Priority) {
                    doit();
                } else {
                    // dropped lower priority event
                    Log.LogWarning($"Dropped lower priority event {nextEvent.Priority} < {NextEvent.Priority}");
                }
            } else {
                // same priority
                if (nextEvent.EndTime < NextEvent.EndTime) {
                    doit();
                } else {
                    // dropped later event
                    Log.LogWarning($"Dropped later event {nextEvent.EndTime} > {NextEvent.EndTime}");
                }
            }
        }
    }
    /// <summary>
    /// Schedule an action to occur after a delay.
    /// Sets NextEvent and optionally associates an action to be invoked at that time.
    /// </summary>
    public void ConsumeTime(string tag, int priority, int delay, Action? pre = null, Action? post = null) {
        SetNextEvent(this.NewEvent(tag, priority, delay, pre, post));
    }
    public virtual void PassTime(string tag, long time) {
        SetNextEvent(this.NewEvent(tag, 0, time - Time));
    }

    public ScheduleEvent? NextEvent { get; private set; }
    public long NextScheduledTime => NextEvent?.EndTime ?? 0;
    public ScheduleEvent? Encounter_Scheduled = null;

    internal void TickTo(long encounterTime) {
        SimulateTo(encounterTime);
        if (encounterTime == NextScheduledTime) {
            var evt = NextEvent!;
            NextEvent = null;
            if (evt.Post == null) {
                Think();
            } else {
                evt.Post.Invoke();
            }
        } else if (NextEvent?.Priority == 0) {
            Think();
        }
        PostTick(encounterTime);
    }

    public override void SimulateTo(long time) {
        NextEvent?.PreInvoke();
        base.SimulateTo(time);
    }

    public override void Think() {
        if (NextEvent == null) {
            var idleEvent = this.NewEvent("Idle", 0, Tuning.MaxDelay);
            SetNextEvent(idleEvent);
        }
    }


    /// <summary>
    /// Post-tick hook for derived classes to perform cleanup or additional processing.
    /// </summary>
    protected virtual void PostTick(long time) { }
    public override string ToString() => $"{base.ToString()} [{Game.DateString(Time)} {Game.TimeString(Time)}]";

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
            NextEvent = this.NextEvent,
            //Encounter_ScheduledTime = this.Encounter_Scheduled
        };
    }

    public virtual void FromData(Data data) {
        base.FromData(data);
        SetNextEvent(data.NextEvent);
        //this.Encounter_Scheduled = data.Encounter_ScheduledTime;
    }
}
