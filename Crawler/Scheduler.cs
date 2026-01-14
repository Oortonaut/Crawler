using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public interface ISchedulerEvent<TElement, TTime>
    where TElement: IComparable<TElement>
    where TTime: struct, IComparable<TTime> {
    TElement Tag { get; }
    TTime Time { get; }
    int Priority { get; }
}
/// <summary>
/// Thread-safe event scheduler for parallel simulation processing.
/// Uses a priority queue with lazy deletion for efficient event management.
/// </summary>
public class Scheduler<TContext, TEvent, TElement, TTime>(TContext Context)
    where TEvent: class, ISchedulerEvent<TElement, TTime>
    where TElement: IComparable<TElement>
    where TTime: struct, IComparable<TTime> {

    // Lock for thread-safe access during parallel processing
    readonly object _lock = new();

    // The highest priority, earliest event for each tag is scheduled
    // Since we keep track of the currently scheduled event for each tag
    // for lazy deletion, we can easily find the currently scheduled priority.
    // The design does not allow multiple events per tag, whether they have different times, etc.
    // Priority is only relevant per tag; two tags with the same time but different priorities
    // may dequeue in any order.
    //
    // The priority is meant to allow multiple components on each tag
    // to schedule their own events against each other.
    public bool Schedule(TEvent evt) {
        lock (_lock) {
            var tag = evt.Tag;
            if (schedEventForTag.TryGetValue(tag, out var schedEvent)) {
                if (evt.Priority > schedEvent.Priority ||
                    evt.Priority == schedEvent.Priority && evt.Time.CompareTo(schedEvent.Time) < 0) {
                    schedEventForTag[tag] = evt;
                    eventQueue.Enqueue(evt, evt.Time);
                    MaybeMinChanged(evt);
                    return true;
                }
            } else {
                schedEventForTag.Add(tag, evt);
                eventQueue.Enqueue(evt, evt.Time);
                MaybeMinChanged(evt);
                return true;
            }
            return false;
        }
    }
    public bool Any() {
        lock (_lock) {
            return eventQueue.Count > 0;
        }
    }
    public int Count {
        get {
            lock (_lock) {
                return eventQueue.Count;
            }
        }
    }
    public bool Peek(out TEvent? evtOut, out TTime? timeOut) {
        lock (_lock) {
            evtOut = null;
            timeOut = null;
            if (eventQueue.Count == 0) {
                Debug.Assert(schedEventForTag.Count == 0);
                return false;
            }
            while (eventQueue.Count > 0) {
                eventQueue.TryPeek(out var evt, out var time);

                if (evt != null && schedEventForTag.TryGetValue(evt.Tag, out var scheduledEvent) && scheduledEvent == evt) {
                    Debug.Assert(schedEventForTag.Count > 0);
                    evtOut = evt;
                    timeOut = time;
                    return true;
                }
                // Lazy deletion
                eventQueue.Dequeue();
            }
            Debug.Assert(schedEventForTag.Count == 0);
            return false;
        }
    }
    public TEvent? Dequeue() {
        lock (_lock) {
            if (eventQueue.Count == 0) {
                return null;
            }
            while (eventQueue.Count > 0) {
                var evt = eventQueue.Dequeue();
                if (schedEventForTag.TryGetValue(evt.Tag, out var lastScheduled) && lastScheduled == evt) {
                    MaybeMinChanged(evt);
                    schedEventForTag.Remove(evt.Tag);
                    return evt;
                }
                // just drop the event if it's getting lazy deleted
            }
            return null;
        }
    }
    public bool Any(TElement tag) {
        lock (_lock) {
            return schedEventForTag.ContainsKey(tag);
        }
    }

    /// <summary>
    /// Collects and dequeues all events at the specified time for batch processing.
    /// Returns events in dequeue order (not deterministic - caller must sort).
    /// </summary>
    public List<TEvent> CollectEventsAtTime(TTime targetTime) {
        lock (_lock) {
            var batch = new List<TEvent>();
            while (PeekInternal(out var evt, out var time) && time!.Value.CompareTo(targetTime) == 0) {
                var dequeued = DequeueInternal();
                if (dequeued != null) {
                    batch.Add(dequeued);
                }
            }
            return batch;
        }
    }

    // Internal versions without locking for use within locked sections
    bool PeekInternal(out TEvent? evtOut, out TTime? timeOut) {
        evtOut = null;
        timeOut = null;
        if (eventQueue.Count == 0) {
            return false;
        }
        while (eventQueue.Count > 0) {
            eventQueue.TryPeek(out var evt, out var time);
            if (evt != null && schedEventForTag.TryGetValue(evt.Tag, out var scheduledEvent) && scheduledEvent == evt) {
                evtOut = evt;
                timeOut = time;
                return true;
            }
            eventQueue.Dequeue();
        }
        return false;
    }

    TEvent? DequeueInternal() {
        if (eventQueue.Count == 0) {
            return null;
        }
        while (eventQueue.Count > 0) {
            var evt = eventQueue.Dequeue();
            if (schedEventForTag.TryGetValue(evt.Tag, out var lastScheduled) && lastScheduled == evt) {
                MaybeMinChanged(evt);
                schedEventForTag.Remove(evt.Tag);
                return evt;
            }
        }
        return null;
    }

    public delegate void HeadChangedHandler(TContext ctx, TEvent evt);
    public event HeadChangedHandler? MinChanged;

    PriorityQueue<TEvent, TTime> eventQueue = new();
    Dictionary<TElement, TEvent> schedEventForTag = new();
    TTime? lastMin = default;

    protected virtual void MaybeMinChanged(TEvent evt) {
        if (!lastMin.HasValue ||
            lastMin.Value.CompareTo(evt.Time) != 0) {
            lastMin = evt.Time;
            MinChanged?.Invoke(Context, evt);
        }
    }

    public bool Unschedule(TElement tag) {
        lock (_lock) {
            LogCat.Log.LogInformation("Unschedule {Tag}", tag);
            return schedEventForTag.Remove(tag);
        }
    }
    public bool Preempt(TElement tag, int priority) {
        lock (_lock) {
            if (schedEventForTag.TryGetValue(tag, out var evt)) {
                if (evt.Priority > priority) {
                    return false;
                } else {
                    LogCat.Log.LogInformation("Unschedule {Tag}", tag);
                    schedEventForTag.Remove(tag);
                    return true;
                }
            } else {
                return true;
            }
        }
    }
}
