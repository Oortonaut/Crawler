namespace Crawler;

public static class ParseEx {
    public static bool TrySplit(ref string left, char split, out string right) {
        var idx = left.IndexOf(split);
        if (idx >= 0) {
            right = left[..idx];
            left = left[(idx + 1)..];
            return true;
        } else {
            right = "";
            return false;
        }
    }
    // Returns \0 on failure
    public static char TrySplitAny(ref string left, char[] split, out string right) {
        var idx = left.IndexOfAny(split);
        if (idx >= 0) {
            right = left[..idx];
            var result = left[idx];
            left = left[(idx + 1)..];
            return result;
        } else {
            right = "";
            return '\0';
        }
    }

    // Shared time parsing: parses H:MM:SS or MM:SS format, returns total seconds
    public static long ParseColonTime(ref string s) {
        var sep = TrySplitAny(ref s, [':'], out var first);
        if (sep == '\0') {
            // No colon, just seconds
            return long.Parse(s);
        }

        sep = TrySplitAny(ref s, [':'], out var second);
        if (sep == '\0') {
            // MM:SS format
            return long.Parse(first) * TimePoint.SecondsPerMinute + long.Parse(s);
        }

        // H:MM:SS format
        return long.Parse(first) * TimePoint.SecondsPerHour +
               long.Parse(second) * TimePoint.SecondsPerMinute +
               long.Parse(s);
    }
}
