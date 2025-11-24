using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

/// <summary>
/// Base class for actors with scheduling capability.
/// Provides NextEvent scheduling and ConsumeTime functionality.
/// </summary>
public class ActorScheduled : ActorBase {
    public ActorScheduled(string name, string brief, Faction faction, Inventory supplies, Inventory cargo, Location location)
        : base(name, brief, faction, supplies, cargo, location) {
    }

    public long NextEvent { get; private set; } = 0;

    Action<ActorScheduled>? _nextEventAction;

    /// <summary>
    /// Schedule an action to occur after a delay.
    /// Sets NextEvent and optionally associates an action to be invoked at that time.
    /// </summary>
    public void ConsumeTime(long delay, Action<ActorScheduled>? action = null) {
        if (delay < 0) throw new ArgumentOutOfRangeException(nameof(delay));

        if (NextEvent == 0) {
            NextEvent = SimulationTime + delay;
            _nextEventAction = action;
        } else {
            Log.LogWarning($"Double scheduled {NextEvent} vs {SimulationTime + delay}");
        }

        // Ensure the encounter reschedules this actor for the new time
        Location?.GetEncounter()?.Schedule(this);
    }

    /// <summary>
    /// Tick this actor to the specified time, invoking scheduled actions when appropriate.
    /// </summary>
    public void TickTo(long time) {
        if (NextEvent == 0) {
            _nextEventAction = null; // to be sure, should be already
            _TickTo(time);
            return;
        }
        while (time <= NextEvent) {
            _TickTo(NextEvent);
        }
        if (SimulationTime < time) {
            // _nextEventAction might not be null but it won't get called
            _TickTo(time);
        }
    }

    void _TickTo(long time) {
        int elapsed = SimulateTo(time);
        if (time == NextEvent) {
            NextEvent = 0;
            if (_nextEventAction != null) {
                var action = _nextEventAction;
                _nextEventAction = null;
                action.Invoke(this);
            } else {
                if (elapsed == 0) {
                    ThinkFor(elapsed);
                } else {
                    ThinkFor(elapsed);
                }
            }
        } else if (elapsed > 0) {
            ThinkFor(elapsed);
        } else {
            // throw new InvalidOperationException($"Elapsed time should only be zero for scheduled events.");
            ThinkFor(elapsed);
        }
        PostTick(time);
    }

    /// <summary>
    /// Post-tick hook for derived classes to perform cleanup or additional processing.
    /// </summary>
    protected virtual void PostTick(long time) { }
}
