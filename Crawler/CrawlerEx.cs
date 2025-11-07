using System.Numerics;
using System.Runtime.CompilerServices;
using Crawler.Logging;

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
    SegmentPackaged,
    SegmentDeactivated, // white on brown
    SegmentDisabled, // white on brown
    SegmentDestroyed, // white on dark red
    // Menu
    MenuNormal,
    MenuTitle,
    MenuDisabled,
    MenuOption,
    MenuInput,
    // Navigation
    MenuUnvisited,
    MenuSomeVisited,
    MenuVisited,
    MenuEmpty,
}

public record StyledChar(Style Style, char Char) {
    public string Styled => Style.Format(Char.ToString());
    public override string ToString() => Styled;
}

public record StyledString(Style Style, string Text) {
    public string Styled => Style.Format(Text);
    public override string ToString() => Styled;
}

public struct IntDispenser() {
    float _remaining = 0;
    public int Get(int acc) {
        _remaining += acc;
        float result = (float) Math.Floor(_remaining);
        _remaining -= result;
        return (int)result;
    }
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
    public static int GetColorIndex(this Color color) {
        int r = color.R * 6 / 256;
        int g = color.G * 6 / 256;
        int b = color.B * 6 / 256;
        if (r == g && g == b) {
            int gray = color.R * 24 / 256;
            return gray + 216 + 16;
        }
        return 16 + r * 36 + g * 6 + b;
    }
    public static string On(this Color fg, Color bg) {
        return $"\e[38;2;{fg.R};{fg.G};{fg.B}m\e[48;2;{bg.R};{bg.G};{bg.B}m";
//        return $"\e[38;5;{fg.GetColorIndex()}m\e[48;5;{bg.GetColorIndex()}m";
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
    public static string CursorHome = "\e[H";
    public static string CursorX(int x) => $"\e[{x}G";
    public static string Margin(int l) => $"\e[{l}s";
    public static string Margin(int l, int r) => $"\e[{l};{r}s";
    public const string EnableMargin = "\e[?69h";
    public const string DisableMargin = "\e[?69l";
    public const string CursorUp = "\e[1A";
    public const string CursorDown = "\e[1B";
    public const string CursorFwd = "\e[1C";
    public const string CursorBwd = "\e[1D";
    public const string CursorReport = "\e[6n";
    public const string CursorSave =  "\e7";
    public const string CursorRestore =  "\e8";
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
    // windows terminal: inverse: 7, hide: 8, strikethrough: 9, float underline: 21
    public const string Inverse = "\e[7m";
    public const string Hide = "\e[8m";
    public const string Strikethrough = "\e[9m";
    public const string StyleNoUnderline = "\e[21m";
    public const string StyleNormalIntensity = "\e[22m";
    public const string StyleNotBold = "\e[22m";
    public const string StyleNotFaint = "\e[22m";
    public const string StyleNotItalic = "\e[23m";
    public const string StyleNotUnderline = "\e[24m";
    public const string StyleNotBlink = "\e[25m";
    public const string StyleNotInverse = "\e[27m";
    public const string StyleNotHide = "\e[28m";
    public const string StyleNotStrikethrough = "\e[29m";
    public const string StyleNotfloatUnderline = "\e[22m";

    public static string StyleNone = "\e[m";
    public static Point WindowSize => new Point(Console.WindowWidth, Console.WindowHeight);
    public static Point BufferSize => new Point(Console.BufferWidth, Console.BufferHeight);
    static CrawlerEx() {
        InitStyles();
    }
    static void InitStyles() {
        Color bg = Color.DarkSlateGray.Scale(0.25f);
        Color fg = Color.LightGray;

        StyleNone = fg.On(bg);
        Styles[Style.Name] = Color.Yellow.On(bg);
        Styles[Style.Em] = Color.SandyBrown.On(bg);
        Styles[Style.UL] = StyleUnderline;

        Color menuBg = bg;
        Color menuFg = fg;

        Styles[Style.MenuNormal] = menuFg.On(menuBg);
        Styles[Style.MenuDisabled] = Color.OrangeRed.On(menuBg);
        Styles[Style.MenuTitle] = Color.LightGray.On(Color.OrangeRed);
        Styles[Style.MenuOption] = Color.Yellow.On(menuBg);
        Styles[Style.MenuInput] = Color.LightGray.On(menuBg);
        Styles[Style.MenuUnvisited] = Color.Aquamarine.On(menuBg);
        Styles[Style.MenuSomeVisited] = Color.CornflowerBlue.On(menuBg);
        Styles[Style.MenuVisited] = menuFg.On(menuBg);
        Styles[Style.MenuEmpty] = Color.DarkRed.On(menuBg);


        Color segmentBg = Color.LightGray;
        Color segmentBgInactive = Color.Pink;
        Color segmentBgPackaged = Color.LightCyan;
        Color segmentFg = Color.Green.Dark();
        Color damagedFg = Color.Red.Dark();
        Styles[Style.SegmentNone] = segmentFg.On(segmentBg);
        Styles[Style.SegmentActive] = segmentFg.On(segmentBg);
        Styles[Style.SegmentPackaged] = segmentFg.On(segmentBgPackaged);
        Styles[Style.SegmentDeactivated] = segmentFg.On(segmentBgInactive);
        Styles[Style.SegmentDisabled] = damagedFg.On(segmentBgInactive);
        Styles[Style.SegmentDestroyed] = damagedFg.On(segmentBg);

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
        ulong A = ((ulong)a ^ 0x8b95bfb1d39ee82cUL) * Prime1;
        ulong B = (ulong)b;
        ulong Q = A + B;
        Q = rrxmrrxmsx(Q);

        return (int)Q;

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

    public static T? ChooseAt<T>(this IEnumerable<T> seq, float at) {
        return ChooseAt(seq.ToList().AsReadOnly(), at);
    }
    public static T? ChooseAt<T>(this IReadOnlyList<T> seq, float at) {
        return seq.Any() ? seq[( int ) (at * seq.Count)] : default;
    }
    public static T? ChooseAt<T>(this IReadOnlyCollection<T> seq, float at) {
        int index = (int)(at * seq.Count);
        return seq.Skip(index).FirstOrDefault();
    }
    public static T? ChooseRandom<T>(this IReadOnlyList<T> seq) {
        return ChooseAt(seq, Random.Shared.NextSingle());
    }
    public static T? ChooseRandom<T>(this IReadOnlyCollection<T> seq) {
        return ChooseAt(seq, Random.Shared.NextSingle());
    }
    public static T? ChooseRandom<T>(this IEnumerable<T> seq) {
        return ChooseAt(seq, Random.Shared.NextSingle());
    }

    public static T? ChooseWeightedAt<T>(this IEnumerable<(T Item, float Weight)> inSeq, float at) {
        var seq = inSeq.ToArray();
        var weights = new List<float>();
        float totalWeight = 0;
        foreach (var (item, weight) in seq) {
            totalWeight += weight;
            weights.Add(totalWeight);
        }
        if (totalWeight == 0) return default;
        int selected = weights.BinarySearch(at * totalWeight);
        if (selected < 0) {
            selected = ~selected;
        }
        return seq[selected].Item;
    }
    public static T? ChooseWeightedRandom<T>(this IEnumerable<(T Item, float Weight)> seq) {
        return ChooseWeightedAt(seq, Random.Shared.NextSingle());
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

    public static string CommodityTextFull(this Commodity comm, float count) =>
        comm == Commodity.Scrap ? $"{count:F1}¢¢" :
        comm.IsIntegral() ? $"{( int ) count} {comm}" :
        $"{count:F1} {comm}";
    public static string CommodityText(this Commodity comm, float count) =>
        count == 0 ? string.Empty : comm.CommodityTextFull(count);

    static float _nextGaussian = 0;
    private static bool _hasNextGaussian = false;

    public static float NextGaussian() {
        if (_hasNextGaussian) {
            _hasNextGaussian = false;
            return _nextGaussian;
        } else {
            float u1 = Random.Shared.NextSingle();
            float u2 = Random.Shared.NextSingle();

            float z = ( float ) Math.Sqrt(-2.0 * Math.Log(u1));
            var (Sin, Cos) = Math.SinCos(Math.Tau * u2);
            float z0 = z * ( float ) Cos;
            _nextGaussian = z * ( float ) Sin;
            _hasNextGaussian = true;

            return z0;
        }
    }
    public static float NextGaussian(float mean = 0, float stdDev = 1) {
        return NextGaussian() * stdDev + mean;
    }
    public static IEnumerable<float> Normalize(this IEnumerable<float> e) {
        var sum = e.Sum();
        var recip = 1.0f / sum;
        return e.Select(item => item * recip);
    }
    // No interpolation from 0 to 1
    public static float Interpolate(this float[] a, float idx) {
        int i = (int)idx;
        if (i >= a.Length) {
            return a[a.Length - 1];
        }
        if (i < 1) {
            return a[0];
        }
        float a0 = a[i];
        float a1 = a[i + 1];
        float t = Frac(idx);
        return a0 * (1 - t) + a1 * t;
    }

    public static string StringJoin(this IEnumerable<string> e, string sep = "") {
        return string.Join(sep, e);
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
    // Used for streaming styled text. Emits the style string if the next style is
    // different from the current one, and updates the current style.
    public static string Advance(this Style Current, Style Next) {
        if (Current != Next) {
            Current = Next;
            return Next.StyleString();
        } else {
            return "";
        }
    }
    // Helpers for styled strings
    public static string Advance(this Style Current, Style Next, string text) => Current.Advance(Next) + text;
    public static string Advance(this Style Current, StyledString Next) => Current.Advance(Next.Style) + Next.Text;
    public static string Advance(this Style Current, StyledChar Next) => Current.Advance(Next.Style) + Next.Char;
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
    public static float Frac(float value) => value - (float)Math.Floor(value);
    public static int StochasticInt(this float expectedValue) {
        var result = ( int ) Math.Floor(expectedValue);
        if (Random.Shared.NextSingle() < Frac(expectedValue)) {
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
    public static IEnumerable<T> WhereCast<T>(this IEnumerable<object> e) {
        foreach (var item in e) {
            if (item is T t) {
                yield return t;
            }
        }
    }
    public static void Fill<T>(this T[,] array, T value) {
        int H = array.GetLength(0);
        int W = array.GetLength(1);
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                array[y, x] = value;
            }
        }
    }
    public static float Factorial(int N) => N == 0 ? 1 : Enumerable.Range(1, N).Aggregate(1, (a, b) => a * b);
    public static float PoissonPMF(int K, float lambda) => (float)(Math.Pow(lambda, K) * Math.Exp(-lambda) / Factorial(K));
    public static int SamplePoisson(float lambda) {
        if (!(lambda >= 0.0)) throw new ArgumentOutOfRangeException(nameof(lambda));
        // Threshold where we switch from exact inversion to normal approx.
        const float NormalThreshold = 30.0f;

        if (lambda < NormalThreshold) {
            // Exact inverse-transform using iterative PMF (stable, no factorials).
            float u = Random.Shared.NextSingle();
            float p = (float)Math.Exp(-lambda); // p(0)
            int limit = (int)(lambda * 20 + 100);
            float cumulative = p;
            int k = 0;
            while (k < limit) {
                if (u < cumulative) {
                    break;
                }
                ++k;
                p *= lambda / k; // p(k) = p(k-1) * lambda / k
                cumulative += p;
            }
            return k;
        } else {
            float kRaw = lambda + (float)Math.Sqrt(lambda) * NextGaussian();
            int k = (int)Math.Round(kRaw);
            return Math.Max(0, k);
        }
    }

    /// <summary>
    /// Samples from an exponential distribution with the given mean (lambda).
    /// Used for modeling inter-arrival times or remaining lifetimes in a Poisson process.
    /// </summary>
    /// <param name="mean">The mean (expected value) of the distribution</param>
    /// <returns>A random sample from the exponential distribution</returns>
    public static float SampleExponential(float mean) {
        if (mean <= 0) {
            throw new ArgumentException("Mean must be positive", nameof(mean));
        }

        // Use inverse transform sampling: -mean * ln(1 - U) where U ~ Uniform(0,1)
        // We use (1 - NextSingle()) to avoid ln(0)
        double u = 1.0 - Random.Shared.NextDouble();
        return ( float ) (-mean * Math.Log(u));
    }

    public static float HaltonSequence(uint b, uint index) {
        float f = 1;
        float r = 0;
        uint i = index;
        while (i > 0) {
            f = f / b;
            r = r + f * (i % b);
            i = i / b;
        }
        return r;
    }

    public static bool IsNumericType(this Type type) {
        switch (Type.GetTypeCode(type)) {
        case TypeCode.Byte:
        case TypeCode.SByte:
        case TypeCode.UInt16:
        case TypeCode.Int16:
        case TypeCode.UInt32:
        case TypeCode.Int32:
        case TypeCode.UInt64:
        case TypeCode.Int64:
        case TypeCode.Single:
        case TypeCode.Double:
        case TypeCode.Decimal:
            return true;
        default:
            return false;
        }
    }
    public static void Shuffle<T>(this IList<T> list) {
        int n = list.Count;
        while (n > 1) {
            n--;
            int k = Random.Shared.Next(n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
    public static string HomePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Crawler");
    public static string SavesPath = Path.Combine(HomePath, "Saves");
    // For roaming settings
    public static string SharedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Crawler");
    public static string SettingsPath = Path.Combine(SharedPath, "Settings");
    // For machine settings and caches
    public static string MachinePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crawler");
    public static string LogPath = Path.Combine(MachinePath, "Logs");

    public static string DataPath = FindDataPath();
    static string FindDataPath() {
        string cwd = Directory.GetCurrentDirectory();
        while (cwd != "") {
            if (Directory.Exists(Path.Combine(cwd, "Data"))) {
                break;
            } else if (Directory.EnumerateFiles(cwd, "*.csproj", SearchOption.AllDirectories).Any()) {
                break;
            } else {
                var parent = Directory.GetParent(cwd);
                cwd = parent?.FullName ?? "";
            }
        }
        return Path.Combine(cwd, "Data");
    }

    public static StreamReader Read(this string dir, string file) {
        var filePath = Path.Combine(dir, file);
        return new StreamReader(filePath);
    }
    public static IEnumerable<string> ReadAllLines(this StreamReader reader) {
        while (!reader.EndOfStream) {
            yield return reader.ReadLine()!;
        }
    }
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Delerp(float x, float low, float high) => ( x - low ) / ( high - low);
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Delerp(double x, double low, double high) => ( x - low ) / ( high - low);
    public static string Input(string prompt = "", string dflt = "") {
        if (prompt != "") {
            Console.Write(prompt);
        }
        Console.Write(CrawlerEx.CursorSave);
        Console.Write(Style.MenuInput.Format(dflt));
        Console.Write(CrawlerEx.CursorRestore);
        var line = Console.ReadLine();
        if (line == null) {
            throw new EndOfStreamException("User pressed break");
        }
        return string.IsNullOrWhiteSpace(line) ? dflt : line;
    }
    public static IEnumerable<IInteraction> InteractionsWith(this IActor agent, IActor subject) {
        using var activity = LogCat.Interaction.StartActivity($"{nameof(InteractionsWith)} {agent.Name} {subject.Name})")?
            .SetTag("Agent", agent.Name).SetTag("Subject", subject.Name).SetTag("SubjectFaction", subject.Faction)
            .SetTag("AgentToSubject", agent.To(subject).ToString())
            .SetTag("SubjectToAgent", subject.To(agent).ToString());

        foreach (var proposal in agent.Proposals()) {
            foreach (var interaction in proposal.TestGetInteractions(agent, subject)) {
                yield return interaction;
            }
        }
        foreach (var proposal in subject.Proposals()) {
            foreach (var interaction in proposal.TestGetInteractions(subject, agent)) {
                yield return interaction;
            }
        }
        foreach (var proposal in Game.Instance!.StoredProposals) {
            foreach (var interaction in proposal.TestGetInteractions(agent, subject)) {
                yield return interaction;
            }
            foreach (var interaction in proposal.TestGetInteractions(subject, agent)) {
                yield return interaction;
            }
        }
    }
    public static bool Ended(this IActor actor) => actor.EndState != null;
    public static bool Lives(this IActor actor) => actor.EndState == null;
    public static int Length<ENUM>() where ENUM : struct, Enum => Enum.GetValues<ENUM>().Length;
    public static ENUM ChooseRandom<ENUM>() where ENUM : struct, Enum => Enum.GetValues<ENUM>()[Random.Shared.Next(0, Length<ENUM>() - 1)];
    static List<string> _messages = new();
    public static Action<string>? OnMessage = message => _messages.Add(message);
    public static void Message(string message) {
        OnMessage?.Invoke(message);
    }
    public static void ClearMessages() {
        _messages.Clear();
    }
    public static void ShowMessages() {
        foreach (var message in _messages) {
            Console.WriteLine(message);
        }
    }
    public static IEnumerable<string> ZipColumns(this IList<string> left, IList<string> right) {
        var leftWidth = left.Max(x => x.Length);
        var rightWidth = right.Max(x => x.Length);
        int count = Math.Max(left.Count, right.Count);
        for (int i = 0; i < count; i++) {
            string leftString = i < left.Count ? left[i] : string.Empty;
            string rightString = i < right.Count ? right[i] : string.Empty;
            leftString = leftString.PadRight(leftWidth);
            //rightString = rightString.PadRight(rightWidth);
            yield return $"{leftString} {rightString}";
        }
    }
    public static bool HasFlag(this IActor agent, EActorFlags flags) => agent.Flags.HasFlag(flags);
    public static bool SurrenderedTo(this IActor agent, IActor other) => agent.To(other).Surrendered;
    public static int Attack(this Crawler attacker, IActor defender) {
        var fire = attacker.CreateFire();
        if (fire.Any()) {
            attacker.Message($"{attacker.Name} attacks {defender.Name}:");
            var (a2d, d2a) = attacker.ToTo(defender);
            a2d.Hostile = true;

            if (a2d.Spared && a2d.Latch(ActorToActor.EFlags.Betrayed, true)) {
                defender.Message($"You were betrayed by {attacker.Name}");
                d2a.SetFlag(ActorToActor.EFlags.Betrayer);
                attacker.Message($"You betrayed {defender.Name}");
                ++attacker.EvilPoints;
            }

            defender.ReceiveFire(attacker, fire);
            return 60;
        } else {
            attacker.Message($"No fire on {defender.Name} ({attacker.StateString(defender)}");
            return 0;
        }
    }
    public static TValue GetOrAddNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue: new() {
        return dict.GetOrAddNew(key, () => new TValue());
    }
    public static TValue GetOrAddNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> gen) {
        if (!dict.TryGetValue(key, out var value)) {
            value = gen();
            dict[key] = value;
        }
        return value;
    }
    public static bool Visited(this IActor actor, Location location) => actor.Knows(location) && actor.To(location).Visited;
    public static Style StyleFor(this IActor actor, Location location) =>
        actor.Visited(location) ? Style.MenuVisited :
        actor.Knows(location) ? Style.MenuSomeVisited :
        Style.MenuUnvisited;
    public static int Visits(this IActor actor, Sector sector) => sector.Locations.Count(l => actor.Visited(l));
    public static Color Scale(this Color c, float s) => Color.FromArgb(
        c.A,
        ( byte ) Math.Clamp(c.R * s, 0, 255),
        ( byte ) Math.Clamp(c.G* s, 0, 255),
        ( byte ) Math.Clamp(c.B * s, 0, 255));
    public static float Length(this Point point) => MathF.Sqrt(point.X * point.X + point.Y * point.Y);
    public static bool ClearFlag<TEnum>(this TEnum e, TEnum flags) where TEnum : struct, Enum => e.SetFlag(flags, false);
    public static bool SetFlag<TEnum>(this ref TEnum e, TEnum flags, bool p = true)
        where TEnum : struct, Enum
    {
        var underlyingType = Enum.GetUnderlyingType(typeof(TEnum));

        bool TryUnderlying<T>(ref TEnum e) where T : unmanaged {
            if (underlyingType == typeof(T)) {
                ref int eVal = ref Unsafe.As<TEnum, int>(ref e);
                ref int flagsVal = ref Unsafe.As<TEnum, int>(ref flags);
                if (p) {
                    eVal |= flagsVal;
                } else {
                    eVal &= ~flagsVal;
                }
                return true;
            }
            return false;
        }

        bool result = TryUnderlying<int>(ref e) ||
                        TryUnderlying<uint>(ref e) ||
                        TryUnderlying<long>(ref e) ||
                        TryUnderlying<ulong>(ref e) ||
                        TryUnderlying<byte>(ref e) ||
                        TryUnderlying<sbyte>(ref e) ||
                        TryUnderlying<short>(ref e) ||
                        TryUnderlying<ushort>(ref e);

        return result;
    }

    public static int TickInteractions(this List<IInteraction> interactions, IActor agent, string prefix) {
        using var activity = LogCat.Interaction.StartActivity($"{nameof(TickInteractions)} {agent.Name} '{prefix}'")?
            .SetTag("Agent", agent.Name).SetTag("#Interactions", interactions.Count);

        int result = 0;
        foreach (var interaction in interactions) {
            var msg = interaction.MessageFor(agent);
            if (!string.IsNullOrEmpty(msg)) {
                agent.Message(msg);
            }
            if (interaction.Immediacy() == Immediacy.Immediate) {
                result += interaction.Perform();
            }
        }
        return result;
    }
    public static List<MenuItem> InteractionMenuItems(this IActor agent, List<IInteraction> interactions, string title, string prefix) {
        List<MenuItem> result = new();
        if (interactions.Count == 0) {
           result.Add(new MenuItem(prefix, $"{title}\n"));
        } else {
            var show = interactions.Count > 4 ? ShowArg.Hide : ShowArg.Show;
            result.AddRange(interactions.DetailMenuItems(prefix, show));
            result.Add(MenuItem.Sep);


            bool anyEnabled = interactions.Any(i => i.Immediacy() == Immediacy.Menu);
            result.Add(new ActionMenuItem(prefix,
                title,
                args => interactions.InteractionMenu(title, prefix, args).turns,
                anyEnabled ? EnableArg.Enabled : EnableArg.Disabled));
            result.Add(MenuItem.Sep);
        }
        return result;
    }
    public static IEnumerable<MenuItem> DetailMenuItems(this List<IInteraction> interactions, string prefix, ShowArg show, string args = "") {
        var counters = new Dictionary<string, int>();
        foreach (var interaction in interactions) {
            int counter;
            var shortcut = $"{prefix}{interaction.OptionCode}";
            if (counters.ContainsKey(shortcut)) {
                counter = ++counters[shortcut];
            } else {
                counter = counters[shortcut] = 1;
            }
            var immediacy = interaction.Immediacy();
            string description = interaction.Description;
            if (immediacy == Immediacy.Disabled && interaction is ExchangeInteraction exchange) {
                var reason = exchange.FailureReason();
                if (reason != null) {
                    description = $"{description} ({reason})";
                }
            }
            yield return new ActionMenuItem($"{shortcut}{counter}",
                description,
                a => interaction.Perform(a),
                immediacy.ToEnableArg(),
                show);
        }
    }
    public static (MenuItem item, int turns) InteractionMenu(this List<IInteraction> interactions, string Name, string prefix, string args) {
        List<MenuItem> interactionsMenu = [
            MenuItem.Cancel,
            .. interactions.DetailMenuItems(prefix, ShowArg.Show, args),
        ];

        return MenuRun($"{Name}", interactionsMenu.ToArray());
    }
    public static Inventory Loot(this Inventory from, float lootReturn) {
        var loot = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            float x = Random.Shared.NextSingle();
            loot[commodity] += from[commodity] * x * lootReturn;
        }
        var lootableSegments = from.Segments.Where(s => s.Health > 0).ToArray();
        loot.Segments.AddRange(lootableSegments
            .Where(s => Random.Shared.NextDouble() < lootReturn));
        return loot;
    }
    public static (ActorToActor a2s, ActorToActor s2a) ToTo(this IActor attacker, IActor defender) => (attacker.To(defender), defender.To(attacker));
    public static IEnumerable<T?> PadTo<T>(this IEnumerable<T> source, int width) {
        foreach (var i in source) {
            --width;
            yield return i;
        }
        while (width --> 0) {
            yield return default;
        }
    }
    public static IEnumerable<(T a, T b)> Pairwise<T>(this IEnumerable<T> source) {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            yield break;
        }
        T previous = enumerator.Current;
        while (enumerator.MoveNext()) {
            yield return (previous, enumerator.Current);
            previous = enumerator.Current;
        }
    }
    public static float EscapeChance(this Crawler crawler) {
        float chance = 1;
        // are there any faster enemies?
        var encounter = crawler.Location.GetEncounter();
        var enemies = encounter.CrawlersExcept(crawler)
            .OfType<Crawler>()
            .Where(e => e.To(crawler).Hostile && !e.IsDisarmed);

        foreach (var enemy in enemies) {
            if (enemy.Speed > 0) {
                // You can escape 100% of the time if you are 25% faster than your enemy
                float enemyChance = Math.Clamp(crawler.Speed * 0.8f / enemy.Speed, 0, 1);
                chance *= enemyChance;
            }
        }
        return chance;
    }
}
