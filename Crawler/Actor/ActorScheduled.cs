using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public record ScheduleEvent(long StartTime, bool Busy, long EndTime, Action? Action) {
}
/// <summary>
/// Base class for actors with scheduling capability.
/// Provides NextEvent scheduling and ConsumeTime functionality.
/// </summary>
public class ActorScheduled : ActorBase {
    public ActorScheduled(string name, string brief, Faction faction, Inventory supplies, Inventory cargo, Location location)
        : base(name, brief, faction, supplies, cargo, location) {
    }

    /// <summary>
    /// Schedule an action to occur after a delay.
    /// Sets NextEvent and optionally associates an action to be invoked at that time.
    /// </summary>
    public void ConsumeTime(long delay, Action? action = null) {
        if (delay < 0) throw new ArgumentOutOfRangeException(nameof(delay));

        if (NextEvent == null) {
            NextEvent = new ScheduleEvent(Time, true, delay, action);
        } else {
            Log.LogWarning($"Double scheduled {NextEvent} vs {Time + delay}");
        }

        // Ensure the encounter reschedules this actor for the new time
        Location.GetEncounter().Schedule(this);
    }
    public virtual void PassTime(long time) {
        if (time < LastTime) {
            throw new InvalidOperationException($"Time travel.");
        } else if (time == LastTime) {
            Log.LogInformation($"Passing no time");
        } else {
            if (NextEvent == null) {
                NextEvent = new ScheduleEvent(Time, false, time, null);
            } else {
                Log.LogWarning($"Double scheduled {NextScheduledTime} vs {time}");
            }
        }
    }

    public ScheduleEvent? NextEvent;
    public long NextScheduledTime => NextEvent?.EndTime ?? 0;
    public long Encounter_ScheduledTime = 0;
    /// <summary>
    /// Tick this actor to the specified time, invoking scheduled actions when appropriate.
    /// </summary>
    public void TickTo(long encounterTime) {
        if (NextEvent == null) {
            _TickTo(encounterTime);
            return;
        }
        while (encounterTime <= NextScheduledTime) {
            _TickTo(NextScheduledTime);
        }
        if (Time < encounterTime) {
            // There may be a remaining NextEvent and NextEvent.Action might not
            // be null but it won't get called
            _TickTo(encounterTime);
        }
    }

    void _TickTo(long encounterTime) {
        SimulateTo(encounterTime);
        Debug.Assert(NextScheduledTime <= encounterTime);
        if (encounterTime == NextScheduledTime && NextEvent!.Action != null) {
            NextEvent.Action?.Invoke();
            NextEvent = null;
        } else {
            Think();
        }
        PostTick(encounterTime);
    }

    /// <summary>
    /// Post-tick hook for derived classes to perform cleanup or additional processing.
    /// </summary>
    protected virtual void PostTick(long time) { }
    public override string ToString() => $"{base.ToString()} [{Game.DateString(Time)} {Game.TimeString(Time)}]";

    public ScheduleEvent ScheduleEvent(int Duration, bool Busy, Action? Action) => new(Time, Busy, Time + Duration, Action);
}
