namespace Crawler;
using System.Drawing;

public enum Style {
    None, // Gray on black
    Em, // Bright white on black
    UL, // Normal + underline
    Name, // For names of crawlers, cities, and people etc.
    // Status states messages
    SegmentNone,
    SegmentActive, // white on dark green
    SegmentDisabled, // white on brown
    SegmentDestroyed, // white on dark red
    // Menu
    MenuNormal,
    MenuTitle,
    MenuDisabled,
    MenuOption,
    MenuInput,
}

public record StyledChar(Style Style, char Char) {
    public string Styled => Style.Format(Char.ToString());
    public override string ToString() => Styled;
}

public record StyledString(Style Style, string Text) {
    public string Styled => Style.Format(Text);
    public override string ToString() => Styled;
}

public static partial class CrawlerEx {
    public static int GetColorIndex(this ConsoleColor c) {
        int[] mapping = [
            30, 94, 92, 96, 91, 95, 93, 37,
            90, 34, 32, 36, 31, 35, 33, 97,
        ];
        return mapping[(int)c];
    }
    public static string On(this ConsoleColor fg, ConsoleColor bg) {
        int fgB = GetColorIndex(fg);
        int bgB = GetColorIndex(bg) + 10;
        return $"\e[{fgB};{bgB}m";
    }
    static int GetColorIndex(this Color color) {
        int gamma(byte x) {
            double exp = 1.6;
            double pow = Math.Pow(x / 255.0, exp) * 255.0;
            return (int)pow;
        }
        int r = Math.Clamp((gamma(color.R) - 8) / 40, 0, 5);
        int g = Math.Clamp((gamma(color.G) - 8) / 40, 0, 5);
        int b = Math.Clamp((gamma(color.B) - 8) / 40, 0, 5);
        if (r == g && g == b) {
            int gray = Math.Clamp((( int ) color.R - 8) / 10, 0, 23);
            return gray + 216 + 16;
        }
        return 16 + r * 36 + g * 6 + b;
    }
    public static string On(this Color fg, Color bg) {
        return $"\e[38;5;{fg.GetColorIndex()}m\e[48;5;{bg.GetColorIndex()}m";
    }
    public static Dictionary<Style, string> Styles = new();
    public const string ClearScreenToEnd = "\e[0J";
    public const string ClearScreenToStart = "\e[1J";
    public const string ClearScreen = "\e[2J";
    public const string ClearConsole = "\e[3J";
    public const string ClearLine = "\e[2K";
    public const string ClearLineToEnd = "\e[K";
    public const string ClearLineToStart = "\e[1K";
    public static string CursorPosition(int x, int y) => $"\e[{y};{x}H";
    public const string CursorUp = "\e[1A";
    public const string CursorDown = "\e[1B";
    public const string CursorFwd = "\e[1C";
    public const string CursorBwd = "\e[1D";
    public const string CursorSave = "\e[s";
    public const string CursorRestore = "\e[u";
    public const string CursorHide = "\e[?25l";
    public const string CursorShow = "\e[?25h";

    public const string ClearTabHere = "\e[0g";
    public const string ClearTabs = "\e[3g";
    public const string SetTab = "\eH";

    public static string Scroll(int lines) => lines > 0 ? $"\e[{lines}S" : $"\e[{-lines}T";

    public const string StyleDefault = "\e[m";
    public const string StyleBold = "\e[1m";
    public const string StyleFaint = "\e[2m";
    public const string StyleItalic = "\e[3m";
    public const string StyleUnderline = "\e[4m";
    public const string StyleBlink = "\e[5m";
    // Works: Bold: 1, Dim: 2, Italic: 3, Underline: 4
    // windows terminal: inverse: 7, hide: 8, strikethrough: 9, double underline: 21
    public const string Inverse = "\e[7m";
    public const string Hide = "\e[8m";
    public const string Strikethrough = "\e[9m";
    public const string DoubleUnderline = "\e[21m";
    public const string StyleNormalIntensity = "\e[22m";
    public const string StyleNotBold = "\e[22m";
    public const string StyleNotFaint = "\e[22m";
    public const string StyleNotItalic = "\e[23m";
    public const string StyleNotUnderline = "\e[24m";
    public const string StyleNotBlink = "\e[25m";
    public const string StyleNotInverse = "\e[27m";
    public const string StyleNotHide = "\e[28m";
    public const string StyleNotStrikethrough = "\e[29m";
    public const string StyleNotDoubleUnderline = "\e[22m";

    public static string StyleNone = "\e[m";
    static CrawlerEx() {
        Color bg = Color.DarkSlateGray;
        Color fg = Color.LightGray;

        StyleNone = fg.On(bg);
        Styles[Style.Name] = Color.Yellow.On(bg);

        Color menuBg = bg;
        Color menuFg = fg;

        Styles[Style.MenuNormal] = menuFg.On(menuBg);
        Styles[Style.MenuDisabled] = Color.OrangeRed.On(menuBg);
        Styles[Style.MenuTitle] = Color.LightGray.On(Color.OrangeRed);
        Styles[Style.MenuOption] = Color.Yellow.On(menuBg);
        Styles[Style.MenuInput] = Color.LightGray.On(menuBg);


        Color segmentBg = Color.LightGray;
        Color segmentFg = Color.Black;
        Styles[Style.SegmentNone] = segmentFg.On(segmentBg);
        Styles[Style.SegmentActive] = Color.DarkGreen.On(segmentBg);
        Styles[Style.SegmentDisabled] = Color.Orange.Dark().On(segmentBg);
        Styles[Style.SegmentDestroyed] = Color.Red.On(segmentBg);
    }
    public static string Format(this Style style, string text = "") {
        if (Styles.TryGetValue(style, out var result)) {
            return result + text + StyleNone;
        } else {
            return StyleNone + text;
        }
    }
    public static string StyleString(this Style style) {
        if (Styles.TryGetValue(style, out var result)) {
            return result;
        } else {
            return "";
        }
    }
    public static StyledChar On(this Style style, char c) => new(style, c);
    public static StyledString On(this Style style, string text) => new(style, text);

    const uint Prime1 = 2654435761U;
    const uint Prime2 = 2246822519U;
    const uint Prime3 = 3266489917U;

    public static ulong rrxmrrxmsx(ulong v) {
        v ^= Rotate(v, 25) ^ Rotate(v, 50);
        v *= 0xA24BAED4963EE407UL;
        v ^= Rotate(v, 24) ^ Rotate(v, 49);
        v *= 0x9FB21C651E98DF25UL;
        return v ^ v >> 28;
    }
    public static int HashCombine(int a, int b) {
        ulong A = (ulong)a * Prime1 + 0x3b478935e928d361UL;
        ulong B = (ulong)b * Prime2 + 0x8b95bfb1d39ee82cUL;
        ulong Q = A ^ B;
        Q = rrxmrrxmsx(Q);

        return (int)(Q >> 8);

    }
    public static int SequenceHashCode<T>(this IEnumerable<T> seq) {
        int hash = 0;
        foreach (var item in seq) {
            hash = HashCombine(hash, item?.GetHashCode() ?? 0);
        }
        return hash;
    }
    public static byte Rotate(byte x, int k) => (byte)((x >> k) | (x << (8 - k)));
    public static ushort Rotate(ushort x, int k) => (ushort)((x >> k) | (x << (16 - k)));
    public static uint Rotate(uint x, int k) => (x >> k) | (x << (32 - k));
    public static ulong Rotate(ulong x, int k) => (x >> k) | (x << (64 - k));

    public static T? ChooseRandom<T>(this IEnumerable<T> seq) {
        int N = 0;
        T? result = default;
        foreach (var i in seq) {
            ++N;
            // if (Random.Shared.NextDouble() <= 1.0 / N)
            if (Random.Shared.NextDouble() * N <= 1.0) {
                result = i;
            }
        }
        return result;
    }

    public static T? ChooseWeightedRandom<T>(this IEnumerable<(T, double)> seq) {
        double N = 0;
        T? result = default;
        foreach (var (i, weight) in seq) {
            N += weight;
            // if (Random.Shared.NextDouble() <= 1.0 / N)
            if (Random.Shared.NextDouble() * N <= weight) {
                result = i;
            }
        }
        return result;
    }

    public static IReadOnlyList<T> ChooseRandomK<T>(this IEnumerable<T> seq, int k) {
        return ChooseRandomK(seq, k, Random.Shared);
    }

    public static IReadOnlyList<T> ChooseRandomK<T>(this IEnumerable<T> seq, int k, Random rng) {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "k must be non-negative");
        if (k == 0) return Array.Empty<T>();

        // Reservoir sampling for k items (Vitter's algorithm R)
        List<T> reservoir = new(k);
        int count = 0;
        foreach (var item in seq) {
            if (count < k) {
                reservoir.Add(item);
            } else {
                // pick a random index in [0, count] and replace if < k
                int j = rng.Next(0, count + 1);
                if (j < k) reservoir[j] = item;
            }
            count++;
        }

        // If fewer than k items were present, return only what we have.
        if (count <= k) return reservoir.AsReadOnly();

        return reservoir.AsReadOnly();
    }

    public static string CommodityText(this Commodity comm, int count) =>
        count == 0 ? string.Empty :
        comm == Commodity.Scrap ? $"{count,7} ¢¢" :
        $"{count,5} " + comm.ToString().Substring(0, 2);

    private static double _nextGaussian = 0.0;
    private static bool _hasNextGaussian = false;

    public static double NextGaussian(double mean = 0.0, double stdDev = 1.0) {
        if (_hasNextGaussian) {
            _hasNextGaussian = false;
            return _nextGaussian * stdDev + mean;
        } else {
            double u1 = Random.Shared.NextDouble();
            double u2 = Random.Shared.NextDouble();

            double z = Math.Sqrt(-2.0 * Math.Log(u1));
            var (Sin, Cos) = Math.SinCos(Math.Tau * u2);
            double z0 = z * Cos;
            _nextGaussian = z * Sin;
            _hasNextGaussian = true;

            return z0 * stdDev + mean;
        }
    }
    public static IEnumerable<double> Normalize(this IEnumerable<double> e) {
        var sum = e.Sum();
        var recip = 1.0 / sum;
        return e.Select(item => item * recip);
    }

    public static string JoinStyled(this IEnumerable<StyledString> e, string sep = "") {
        return string.Join(sep, e.Select(s => s.Styled));
    }
    public static string TransposeJoinStyled(this IEnumerable<StyledString> e, string sep = "\n") {
        var SourceLines = e.ToArray();
        var MaxSourceWidth = SourceLines.Max(s => s.Text.Length);
        var result = string.Empty;
        for (int x = 0; x < MaxSourceWidth; x++) {
            if (x > 0) {
                result += sep;
            }
            string line = string.Empty;
            Style currentStyle = Style.None;

            for (int y = 0; y < SourceLines.Length; y++) {
                var style = SourceLines[y].Style;
                if (style != currentStyle) {
                    currentStyle = style;
                    result += currentStyle.StyleString();
                }
                if (x < SourceLines[y].Text.Length) {
                    result+= SourceLines[y].Text[x].ToString();
                } else {
                    result += ' ';
                }
            }
            result += CrawlerEx.StyleNone;
        }
        return result;
    }
    public static string Advance(this Style Current, Style Next) {
        if (Current != Next) {
            Current = Next;
            return Next.StyleString();
        } else {
            return "";
        }
    }
    public static Color Dark(this Color c) => Color.FromArgb(c.A, c.R / 2, c.G / 2, c.B / 2);
    public static void Do<T>(this IEnumerable<T> e) {
        foreach (var item in e) { }
    }
    public static void Do<T>(this IEnumerable<T> e, Action<T> action) {
        foreach (var item in e) {
            action(item);
        }
    }
    public static void Do<T, U>(this IEnumerable<T> e, Action<T, U> action, U arg) {
        foreach (var item in e) {
            action(item, arg);
        }
    }
    public static double Frac(double value) => value - Math.Floor(value);
    public static float Frac(float value) => value - (int)Math.Floor(value);
    public static int StochasticInt(this double expectedValue) {
        var result = ( int ) Math.Floor(expectedValue);
        if (Random.Shared.NextDouble() < Frac(expectedValue)) {
            ++result;
        }
        return result;
    }
    public static IEnumerable<(int X, int Y)> Index<T>(this T[,] array) {
        int H = array.GetLength(0);
        int W = array.GetLength(1);
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                yield return (x, y);
            }
        }
    }
    public static double Factorial(int N) => N == 0 ? 1 : Enumerable.Range(1, N).Aggregate(1, (a, b) => a * b);
    public static double PoissonPMF(int K, double lambda) => Math.Pow(lambda, K) * Math.Exp(-lambda) / Factorial(K);
    public static double HaltonSequence(uint b, uint index) {
        double f = 1.0;
        double r = 0.0;
        uint i = index;
        while (i > 0) {
            f = f / b;
            r = r + f * (i % b);
            i = i / b;
        }
        return r;
    }

}
