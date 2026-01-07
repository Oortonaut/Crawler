namespace Crawler;

// Seconds since unix epoch
// Decimal time: 10 hours or 1000 minutes per day, 100 seconds per minute, 100k seconds/day
// Month: 35 days or 3.5 million seconds, a year is 10 months (350 days).
// Our starting time should be in the year 3126
// Visual representation:
// YYYY/Mon/DD H:MM:SS

public readonly struct TimePoint : IComparable<TimePoint>, IEquatable<TimePoint>, IComparable
{
    // Time constants
    public const long SecondsPerMinute = 100;
    public const long MinutesPerHour = 100;
    public const long HoursPerDay = 10;
    public const long DaysPerMonth = 35;
    public const long MonthsPerYear = 10;

    public const long SecondsPerHour = SecondsPerMinute * MinutesPerHour;        // 10,000
    public const long SecondsPerDay = SecondsPerHour * HoursPerDay;              // 100,000
    public const long SecondsPerMonth = SecondsPerDay * DaysPerMonth;              // 1,000,000
    public const long SecondsPerYear = SecondsPerMonth * MonthsPerYear;            // 32,000,000

    private readonly long _elapsed;

    public static readonly TimePoint None = new(long.MinValue);
    public static readonly TimePoint MinValue = new(long.MinValue + 1);
    public static readonly TimePoint Zero = new(0);

    // Primary constructor
    public TimePoint(long elapsed = long.MinValue)
    {
        _elapsed = elapsed;
    }

    // Constructor from components
    public TimePoint(int year, int month, int day, int hour, int minute, int second)
    {
        if (month < 0 || month >= MonthsPerYear) throw new ArgumentOutOfRangeException(nameof(month));
        if (day < 0 || day >= DaysPerMonth) throw new ArgumentOutOfRangeException(nameof(day));
        if (hour < 0 || hour >= HoursPerDay) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute < 0 || minute >= MinutesPerHour) throw new ArgumentOutOfRangeException(nameof(minute));
        if (second < 0 || second >= SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));

        _elapsed = year * SecondsPerYear +
                   month * SecondsPerMonth +
                   day * SecondsPerDay +
                   hour * SecondsPerHour +
                   minute * SecondsPerMinute +
                   second;
    }

    // Properties
    public long Elapsed => _elapsed;
    public bool IsNone => _elapsed == long.MinValue;
    public bool IsValid => _elapsed > long.MinValue;

    // Month names (10 months for 10 MonthsPerYear)
    public static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Sept", "Oct", "Nov", "Dec"];

    // Time components
    public int Year => (int)(_elapsed / SecondsPerYear);
    public int Month => (int)((_elapsed % SecondsPerYear) / SecondsPerMonth);
    public string MonthName => MonthNames[Month];
    public int Day => (int)((_elapsed % SecondsPerMonth) / SecondsPerDay);
    public int Hour => (int)((_elapsed % SecondsPerDay) / SecondsPerHour);
    public int Minute => (int)((_elapsed % SecondsPerHour) / SecondsPerMinute);
    public int Second => (int)(_elapsed % SecondsPerMinute);

    public int DayOfYear => (int)((_elapsed % SecondsPerYear) / SecondsPerDay);
    public long TotalSeconds => _elapsed;
    public double TotalMinutes => _elapsed / (double)SecondsPerMinute;
    public double TotalHours => _elapsed / (double)SecondsPerHour;
    public double TotalDays => _elapsed / (double)SecondsPerDay;
    public double TotalMonths => _elapsed / (double)SecondsPerMonth;
    public double TotalYears => _elapsed / (double)SecondsPerYear;

    // Arithmetic operators
    public static TimePoint operator +(TimePoint point, TimeDuration duration) =>
        new(point._elapsed + duration.Duration);

    public static TimePoint operator -(TimePoint point, TimeDuration duration) =>
        new(point._elapsed - duration.Duration);

    public static TimeDuration operator -(TimePoint left, TimePoint right) =>
        new(left._elapsed - right._elapsed);

    // Comparison operators
    public static bool operator ==(TimePoint left, TimePoint right) => left._elapsed == right._elapsed;
    public static bool operator !=(TimePoint left, TimePoint right) => left._elapsed != right._elapsed;
    public static bool operator <(TimePoint left, TimePoint right) => left._elapsed < right._elapsed;
    public static bool operator >(TimePoint left, TimePoint right) => left._elapsed > right._elapsed;
    public static bool operator <=(TimePoint left, TimePoint right) => left._elapsed <= right._elapsed;
    public static bool operator >=(TimePoint left, TimePoint right) => left._elapsed >= right._elapsed;

    // Interface implementations
    public int CompareTo(TimePoint other) => _elapsed.CompareTo(other._elapsed);
    public bool Equals(TimePoint other) => _elapsed == other._elapsed;

    public int CompareTo(object? obj)
    {
        if (obj == null) return 1;
        if (obj is not TimePoint other) throw new ArgumentException("Object must be of type TimePoint");
        return CompareTo(other);
    }

    public override bool Equals(object? obj) => obj is TimePoint other && Equals(other);
    public override int GetHashCode() => _elapsed.GetHashCode();

    // Helper methods
    public TimePoint AddSeconds(long seconds) => new(_elapsed + seconds);
    public TimePoint AddMinutes(long minutes) => new(_elapsed + minutes * SecondsPerMinute);
    public TimePoint AddHours(long hours) => new(_elapsed + hours * SecondsPerHour);
    public TimePoint AddDays(long days) => new(_elapsed + days * SecondsPerDay);
    public TimePoint AddMonths(long months) => new(_elapsed + months * SecondsPerMonth);
    public TimePoint AddYears(long years) => new(_elapsed + years * SecondsPerYear);

    public TimePoint Add(TimeDuration duration) => this + duration;
    public TimePoint Subtract(TimeDuration duration) => this - duration;

    // Formatting
    public override string ToString()
    {
        if (IsNone) return "None";
        return $"{Year:D4}/{MonthName}/{Day:D2} {Hour}:{Minute:D2}:{Second:D2}";
    }

    public string ToString(string format)
    {
        if (IsNone) return "None";

        return format switch
        {
            "F" => ToString(), // Full
            "D" => $"{Year:D4}/{MonthName}/{Day:D2}", // Date only
            "T" => $"{Hour}:{Minute:D2}:{Second:D2}", // Time only
            "S" => _elapsed.ToString(), // Seconds
            _ => ToString()
        };
    }

    // Parsing
    public static TimePoint Parse(string s)
    {
        if (s == "None") return None;

        // Format: YYYY/Mon/DD H:MM:SS
        if (ParseEx.TrySplitAny(ref s, ['/'], out var yearStr) == '\0')
            throw new FormatException("Invalid TimePoint format");
        if (ParseEx.TrySplitAny(ref s, ['/'], out var monthStr) == '\0')
            throw new FormatException("Invalid date format");
        if (ParseEx.TrySplitAny(ref s, [' '], out var dayStr) == '\0')
            throw new FormatException("Invalid date format");

        int year = int.Parse(yearStr);
        int month = Array.IndexOf(MonthNames, monthStr);
        if (month < 0) throw new FormatException($"Invalid month name: {monthStr}");
        int day = int.Parse(dayStr);

        long timeSeconds = ParseEx.ParseColonTime(ref s);
        int hour = (int)(timeSeconds / SecondsPerHour);
        int minute = (int)((timeSeconds % SecondsPerHour) / SecondsPerMinute);
        int second = (int)(timeSeconds % SecondsPerMinute);

        return new TimePoint(year, month, day, hour, minute, second);
    }

    public static bool TryParse(string s, out TimePoint result)
    {
        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            result = None;
            return false;
        }
    }

    public static TimePoint Now => new(GetCurrentElapsed());

    private static long GetCurrentElapsed()
    {
        // Convert real-world time to game time
        // This is a placeholder - adjust based on your game's time scaling
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return unixSeconds; // You may want to scale this differently
    }
}

public readonly struct TimeDuration : IComparable<TimeDuration>, IEquatable<TimeDuration>, IComparable
{
    private readonly long _duration;

    public static readonly TimeDuration None = new(long.MinValue);
    public static readonly TimeDuration Zero = new(0);

    // Primary constructor
    public TimeDuration(long duration = 0)
    {
        _duration = duration;
    }

    // Constructor from components
    public TimeDuration(int minutes, int seconds)
    {
        _duration = minutes * TimePoint.SecondsPerMinute +
                    seconds;
    }

    public TimeDuration(int hours, int minutes, int seconds)
    {
        _duration = hours * TimePoint.SecondsPerHour +
                    minutes * TimePoint.SecondsPerMinute +
                    seconds;
    }

    public TimeDuration(int days, int hours, int minutes, int seconds)
    {
        _duration = days * TimePoint.SecondsPerDay +
                    hours * TimePoint.SecondsPerHour +
                    minutes * TimePoint.SecondsPerMinute +
                    seconds;
    }

    // Properties
    public long Duration => _duration;
    public bool IsNone => _duration == long.MinValue;
    public bool IsValid => _duration > long.MinValue;
    public bool IsPositive => _duration > 0;
    public bool IsNegative => _duration < 0;

    // Time components
    public int Days => (int)(_duration / TimePoint.SecondsPerDay);
    public int Hours => (int)((_duration % TimePoint.SecondsPerDay) / TimePoint.SecondsPerHour);
    public int Minutes => (int)((_duration % TimePoint.SecondsPerHour) / TimePoint.SecondsPerMinute);
    public int Seconds => (int)(_duration % TimePoint.SecondsPerMinute);

    public long TotalSeconds => _duration;
    public double TotalMinutes => _duration / (double)TimePoint.SecondsPerMinute;
    public double TotalHours => _duration / (double)TimePoint.SecondsPerHour;
    public double TotalDays => _duration / (double)TimePoint.SecondsPerDay;
    public double TotalMonths => _duration / (double)TimePoint.SecondsPerMonth;
    public double TotalYears => _duration / (double)TimePoint.SecondsPerYear;

    // Factory methods
    public static TimeDuration FromSeconds(double seconds) => new((long)Math.Round(seconds));
    public static TimeDuration FromMinutes(double minutes) => new((long)Math.Round(minutes * TimePoint.SecondsPerMinute));
    public static TimeDuration FromHours(double hours) => new((long)Math.Round(hours * TimePoint.SecondsPerHour));
    public static TimeDuration FromDays(double days) => new((long)Math.Round(days * TimePoint.SecondsPerDay));
    public static TimeDuration FromMonths(double months) => new((long)Math.Round(months * TimePoint.SecondsPerMonth));
    public static TimeDuration FromYears(double years) => new((long)Math.Round(years * TimePoint.SecondsPerYear));
    // Arithmetic operators
    public static TimeDuration operator +(TimeDuration left, TimeDuration right) =>
        new(left._duration + right._duration);

    public static TimeDuration operator -(TimeDuration left, TimeDuration right) =>
        new(left._duration - right._duration);

    public static TimeDuration operator -(TimeDuration duration) =>
        new(-duration._duration);

    public static TimeDuration operator *(TimeDuration duration, int multiplier) =>
        new(duration._duration * multiplier);

    public static TimeDuration operator *(int multiplier, TimeDuration duration) =>
        new(duration._duration * multiplier);

    public static TimeDuration operator /(TimeDuration duration, int divisor) =>
        new(duration._duration / divisor);

    public static double operator /(TimeDuration left, TimeDuration right) =>
        (double)left._duration / right._duration;

    // Comparison operators
    public static bool operator ==(TimeDuration left, TimeDuration right) => left._duration == right._duration;
    public static bool operator !=(TimeDuration left, TimeDuration right) => left._duration != right._duration;
    public static bool operator <(TimeDuration left, TimeDuration right) => left._duration < right._duration;
    public static bool operator >(TimeDuration left, TimeDuration right) => left._duration > right._duration;
    public static bool operator <=(TimeDuration left, TimeDuration right) => left._duration <= right._duration;
    public static bool operator >=(TimeDuration left, TimeDuration right) => left._duration >= right._duration;

    // Interface implementations
    public int CompareTo(TimeDuration other) => _duration.CompareTo(other._duration);
    public bool Equals(TimeDuration other) => _duration == other._duration;

    public int CompareTo(object? obj)
    {
        if (obj == null) return 1;
        if (obj is not TimeDuration other) throw new ArgumentException("Object must be of type TimeDuration");
        return CompareTo(other);
    }

    public override bool Equals(object? obj) => obj is TimeDuration other && Equals(other);
    public override int GetHashCode() => _duration.GetHashCode();

    // Helper methods
    public TimeDuration Add(TimeDuration other) => this + other;
    public TimeDuration Subtract(TimeDuration other) => this - other;
    public TimeDuration Multiply(int multiplier) => this * multiplier;
    public TimeDuration Divide(int divisor) => this / divisor;
    public TimeDuration Negate() => -this;
    public TimeDuration Abs() => new(Math.Abs(_duration));

    // Formatting
    public override string ToString()
    {
        if (IsNone) return "None";
        if (_duration == 0) return "0s";

        var abs = Math.Abs(_duration);
        var sign = _duration < 0 ? "-" : "";

        var days = abs / TimePoint.SecondsPerDay;
        var hours = (abs % TimePoint.SecondsPerDay) / TimePoint.SecondsPerHour;
        var minutes = (abs % TimePoint.SecondsPerHour) / TimePoint.SecondsPerMinute;
        var seconds = abs % TimePoint.SecondsPerMinute;

        if (days > 0)
            return $"{sign}{days}d {hours}:{minutes:D2}:{seconds:D2}";
        else if (hours > 0)
            return $"{sign}{hours}:{minutes:D2}:{seconds:D2}";
        else if (minutes > 0)
            return $"{sign}{minutes}m {seconds}s";
        else
            return $"{sign}{seconds}s";
    }

    public string ToString(string format)
    {
        if (IsNone) return "None";

        return format switch
        {
            "F" => ToString(), // Full
            "S" => $"{_duration}s", // Total seconds
            "C" => $"{Days}d {Hours}:{Minutes:D2}:{Seconds:D2}", // Compact
            _ => ToString()
        };
    }


    // Parsing
    public static TimeDuration Parse(string s) {
        if (s == "None") return None;

        // Formats: "5d 3:45:20", "3:45:20", "45m 20s", "20s", "5h"
        s = s.Trim();
        long totalSeconds = 0;

        var sep = ParseEx.TrySplitAny(ref s, ['d', 'h', 'm', 's', ':'], out var part);

        while (sep != '\0') {
            switch (sep) {
                case 'd':
                    totalSeconds += long.Parse(part) * TimePoint.SecondsPerDay;
                    s = s.TrimStart();
                    break;
                case 'h':
                    totalSeconds += long.Parse(part) * TimePoint.SecondsPerHour;
                    s = s.TrimStart();
                    break;
                case 'm':
                    totalSeconds += long.Parse(part) * TimePoint.SecondsPerMinute;
                    s = s.TrimStart();
                    break;
                case 's':
                    totalSeconds += long.Parse(part);
                    s = s.TrimStart();
                    break;
                case ':':
                    // Colon format - reconstruct and use shared parser
                    s = part + ':' + s;
                    totalSeconds += ParseEx.ParseColonTime(ref s);
                    s = s.TrimStart();
                    break;
            }
            sep = ParseEx.TrySplitAny(ref s, ['d', 'h', 'm', 's', ':'], out part);
        }

        // Handle remaining content (raw number without suffix)
        if (!string.IsNullOrEmpty(s)) {
            totalSeconds += long.Parse(s);
        }

        return new TimeDuration(totalSeconds);
    }

    public static bool TryParse(string s, out TimeDuration result)
    {
        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            result = None;
            return false;
        }
    }
}
