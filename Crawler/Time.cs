namespace Crawler;

public record struct TimePoint(long Seconds) {
    // 1 million seconds per orbit (a little over the 11.2 day week)
    // 10 days per week
    // 10 hour days
    // 100 minute hours
    // 100 second minutes
    public override string ToString() => WeekString + " " + TimeString;
    const long day = 100000;
    public string WeekString {
        get {
            long days = Seconds / day;
            var s = $"{days:D4}";
            s = s.Insert(s.Length - 1, "/");
            s = s.Insert(s.Length - 3, "/");
            return s;
        }
    }
    public string TimeString {
        get {
            long daySeconds = Seconds % day;
            var s = $"{daySeconds:D5}";
            s = s.Insert(4, ":");
            s = s.Insert(2, ":");
            s = s.Insert(1, ":");
            return s;
        }
    }
}

public record struct TimeDuration(long Elapsed) {

}

// Scheduler event wrappers for use with generic Scheduler<TContext, TEvent, TElement, TTime>

/// <summary>
/// Wraps an Encounter and its ScheduleEvent for Game-level scheduling
/// </summary>
public class EncounterSchedulerEvent : ISchedulerEvent<Encounter, long> {
    public EncounterSchedulerEvent(Encounter encounter, ScheduleEvent evt) {
        Encounter = encounter;
        Event = evt;
    }

    public Encounter Encounter { get; }
    public ScheduleEvent Event { get; }

    // ISchedulerEvent implementation
    public Encounter Tag => Encounter;
    public int Priority => Event.Priority;
    public long Time => Event.End;

    public override string ToString() => $"EncounterSchedulerEvent({Encounter.Name}, {Event})";
}

/// <summary>
/// Wraps a Crawler and its travel arrival time for Game-level traveling crawler scheduling
/// </summary>
public class CrawlerTravelEvent : ISchedulerEvent<Crawler, long> {
    public CrawlerTravelEvent(Crawler crawler, long arrivalTime) {
        Crawler = crawler;
        ArrivalTime = arrivalTime;
    }

    public Crawler Crawler { get; }
    public long ArrivalTime { get; }

    // ISchedulerEvent implementation
    public Crawler Tag => Crawler;
    public int Priority => 0; // All travel events have same priority
    public long Time => ArrivalTime;

    public override string ToString() => $"CrawlerTravelEvent({Crawler.Name}, arrival={Game.TimeString(ArrivalTime)})";
}
