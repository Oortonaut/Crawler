namespace Crawler;

// Seconds since unix epoch
// Decimal time: 10 hours or 1000 minutes per day, 100 seconds per minute, 100k seconds/day
// Week: A week is 10 days or a million seconds, a year is 32 weeks.
// Our starting time should be in the year 3126
// Visual representation:
// YYYY/WW/D H:MM:SS

public readonly struct TimePoint : IComparable<TimePoint>, IEquatable<TimePoint>, IComparable
{
    // Time constants
    public const int SecondsPerMinute = 100;
    public const int MinutesPerHour = 10;
    public const int HoursPerDay = 10;
    public const int DaysPerWeek = 10;
    public const int WeeksPerYear = 32;

    public const int SecondsPerHour = SecondsPerMinute * MinutesPerHour;        // 1,000
    public const int SecondsPerDay = SecondsPerHour * HoursPerDay;              // 100,000
    public const int SecondsPerWeek = SecondsPerDay * DaysPerWeek;              // 1,000,000
    public const int SecondsPerYear = SecondsPerWeek * WeeksPerYear;            // 32,000,000

    // Epoch: 3126/00/0 0:00:00 in decimal time
    private const long EpochYear = 3126;
    private const long EpochOffset = EpochYear * SecondsPerYear;

    private readonly long _elapsed;

    public static readonly TimePoint None = new(long.MinValue);
    public static readonly TimePoint MinValue = new(long.MinValue + 1);
    public static readonly TimePoint MaxValue = new(long.MaxValue);
    public static readonly TimePoint Zero = new(0);

    // Primary constructor
    public TimePoint(long elapsed = long.MinValue)
    {
        _elapsed = elapsed;
    }

    // Constructor from components
    public TimePoint(int year, int week, int day, int hour, int minute, int second)
    {
        if (week < 0 || week >= WeeksPerYear) throw new ArgumentOutOfRangeException(nameof(week));
        if (day < 0 || day >= DaysPerWeek) throw new ArgumentOutOfRangeException(nameof(day));
        if (hour < 0 || hour >= HoursPerDay) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute < 0 || minute >= MinutesPerHour) throw new ArgumentOutOfRangeException(nameof(minute));
        if (second < 0 || second >= SecondsPerMinute) throw new ArgumentOutOfRangeException(nameof(second));

        _elapsed = (long)year * SecondsPerYear +
                   week * SecondsPerWeek +
                   day * SecondsPerDay +
                   hour * SecondsPerHour +
                   minute * SecondsPerMinute +
                   second;
    }

    // Properties
    public long Elapsed => _elapsed;
    public bool IsNone => _elapsed == long.MinValue;
    public bool IsValid => _elapsed > long.MinValue;

    // Time components
    public int Year => (int)(_elapsed / SecondsPerYear);
    public int Week => (int)((_elapsed % SecondsPerYear) / SecondsPerWeek);
    public int Day => (int)((_elapsed % SecondsPerWeek) / SecondsPerDay);
    public int Hour => (int)((_elapsed % SecondsPerDay) / SecondsPerHour);
    public int Minute => (int)((_elapsed % SecondsPerHour) / SecondsPerMinute);
    public int Second => (int)(_elapsed % SecondsPerMinute);

    public int DayOfYear => (int)((_elapsed % SecondsPerYear) / SecondsPerDay);
    public long TotalSeconds => _elapsed;
    public double TotalMinutes => _elapsed / (double)SecondsPerMinute;
    public double TotalHours => _elapsed / (double)SecondsPerHour;
    public double TotalDays => _elapsed / (double)SecondsPerDay;
    public double TotalWeeks => _elapsed / (double)SecondsPerWeek;
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
    public TimePoint AddWeeks(long weeks) => new(_elapsed + weeks * SecondsPerWeek);
    public TimePoint AddYears(long years) => new(_elapsed + years * SecondsPerYear);

    public TimePoint Add(TimeDuration duration) => this + duration;
    public TimePoint Subtract(TimeDuration duration) => this - duration;

    // Formatting
    public override string ToString()
    {
        if (IsNone) return "None";
        return $"{Year:D4}/{Week:D2}/{Day} {Hour}:{Minute:D2}:{Second:D2}";
    }

    public string ToString(string format)
    {
        if (IsNone) return "None";

        return format switch
        {
            "F" => ToString(), // Full
            "D" => $"{Year:D4}/{Week:D2}/{Day}", // Date only
            "T" => $"{Hour}:{Minute:D2}:{Second:D2}", // Time only
            "S" => _elapsed.ToString(), // Seconds
            _ => ToString()
        };
    }

    // Parsing
    public static TimePoint Parse(string s)
    {
        if (s == "None") return None;

        // Format: YYYY/WW/D H:MM:SS
        var parts = s.Split(' ');
        if (parts.Length != 2) throw new FormatException("Invalid TimePoint format");

        var dateParts = parts[0].Split('/');
        if (dateParts.Length != 3) throw new FormatException("Invalid date format");

        var timeParts = parts[1].Split(':');
        if (timeParts.Length != 3) throw new FormatException("Invalid time format");

        int year = int.Parse(dateParts[0]);
        int week = int.Parse(dateParts[1]);
        int day = int.Parse(dateParts[2]);
        int hour = int.Parse(timeParts[0]);
        int minute = int.Parse(timeParts[1]);
        int second = int.Parse(timeParts[2]);

        return new TimePoint(year, week, day, hour, minute, second);
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
    public static readonly TimeDuration MinValue = new(long.MinValue + 1);
    public static readonly TimeDuration MaxValue = new(long.MaxValue);

    // Primary constructor
    public TimeDuration(long duration = 0)
    {
        _duration = duration;
    }

    // Constructor from components
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
    public double TotalWeeks => _duration / (double)TimePoint.SecondsPerWeek;
    public double TotalYears => _duration / (double)TimePoint.SecondsPerYear;

    // Factory methods
    public static TimeDuration FromSeconds(long seconds) => new(seconds);
    public static TimeDuration FromMinutes(long minutes) => new(minutes * TimePoint.SecondsPerMinute);
    public static TimeDuration FromHours(long hours) => new(hours * TimePoint.SecondsPerHour);
    public static TimeDuration FromDays(long days) => new(days * TimePoint.SecondsPerDay);
    public static TimeDuration FromWeeks(long weeks) => new(weeks * TimePoint.SecondsPerWeek);
    public static TimeDuration FromYears(long years) => new(years * TimePoint.SecondsPerYear);

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
    public static TimeDuration Parse(string s)
    {
        if (s == "None") return None;

        // Simple parsing for formats like "5d 3:45:20" or "3:45:20" or "45m 20s" or "20s"
        s = s.Trim();
        long totalSeconds = 0;

        if (s.Contains('d'))
        {
            var parts = s.Split('d', StringSplitOptions.TrimEntries);
            totalSeconds += long.Parse(parts[0]) * TimePoint.SecondsPerDay;
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                totalSeconds += ParseTime(parts[1]);
            }
        }
        else if (s.Contains(':'))
        {
            totalSeconds = ParseTime(s);
        }
        else if (s.Contains('m'))
        {
            var parts = s.Split('m', StringSplitOptions.TrimEntries);
            totalSeconds += long.Parse(parts[0]) * TimePoint.SecondsPerMinute;
            if (parts.Length > 1 && parts[1].EndsWith('s'))
            {
                totalSeconds += long.Parse(parts[1].TrimEnd('s'));
            }
        }
        else if (s.EndsWith('s'))
        {
            totalSeconds = long.Parse(s.TrimEnd('s'));
        }
        else
        {
            totalSeconds = long.Parse(s);
        }

        return new TimeDuration(totalSeconds);
    }

    private static long ParseTime(string time)
    {
        var parts = time.Split(':');
        if (parts.Length == 3)
        {
            return long.Parse(parts[0]) * TimePoint.SecondsPerHour +
                   long.Parse(parts[1]) * TimePoint.SecondsPerMinute +
                   long.Parse(parts[2]);
        }
        else if (parts.Length == 2)
        {
            return long.Parse(parts[0]) * TimePoint.SecondsPerMinute +
                   long.Parse(parts[1]);
        }
        throw new FormatException("Invalid time format");
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
