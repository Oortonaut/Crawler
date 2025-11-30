namespace Crawler;

public record struct TimePoint(long Seconds) {
    // 1 million seconds per orbit (a little over the 11.2 day week)
    // 10 days per week
    // 10 hour days
    // 100 minute hours
    // 100 second minutes
    public override string ToString() => WeekString + " " + TimeString;
    const long day = 10_00_00;
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
