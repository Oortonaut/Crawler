using System.Diagnostics;

namespace Crawler;

public interface ISchedulerEvent<TElement, TTime>
    where TElement: IComparable<TElement>
    where TTime: struct, IComparable<TTime> {
    TElement Tag { get; }
    TTime Time { get; }
    int Priority { get; }
}
public class Scheduler<TContext, TEvent, TElement, TTime>(TContext Context)
    where TEvent: class, ISchedulerEvent<TElement, TTime>
    where TElement: IComparable<TElement>
    where TTime: struct, IComparable<TTime> {

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
    public bool Any() => eventQueue.Count > 0;
    public int Count => eventQueue.Count;
    public bool Peek(out TEvent? evtOut, out TTime? timeOut) {
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
    public TEvent? Dequeue() {
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
    public bool Any(TElement tag) => schedEventForTag.ContainsKey(tag);

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
}
