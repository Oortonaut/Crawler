using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Crawler.Logging;
using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Logging;

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
        float result = (float)Math.Floor(_remaining);
        _remaining -= result;
        return (int)result;
    }
}
public static partial class CrawlerEx {

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

    public static string CommodityTextFull(this Commodity comm, float count) =>
        comm == Commodity.Scrap ? $"{count:F1}¢¢" :
        comm.IsIntegral() ? $"{(int)count} {comm}" :
        $"{count:F1} {comm}";
    public static string CommodityText(this Commodity comm, float count) =>
        count == 0 ? string.Empty : comm.CommodityTextFull(count);
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

    public static double Frac(double value) => value - Math.Floor(value);
    public static float Frac(float value) => value - (float)Math.Floor(value);
    public static int StochasticInt(this float expectedValue, ref XorShift rng) {
        var result = (int)Math.Floor(expectedValue);
        if (rng.NextSingle() < Frac(expectedValue)) {
            ++result;
        }
        return result;
    }
    public static float Factorial(int N) => N == 0 ? 1 : Enumerable.Range(1, N).Aggregate(1, (a, b) => a * b);
    public static int PoissonQuantileAt(float lambda, float y) {
        if (lambda <= 0) return 0;

        int k = 0;
        // e^lambda ~= 2^23
        if (lambda > 16) {
            // Normal approximation with continuity correction
            // mean = lambda, variance = lambda
            double z = Math.Sqrt(-2 * Math.Log(1 - y)); // Inverse standard normal CDF
            k = (int)(lambda + Math.Sqrt(lambda) * z + 0.5);
        } else {
            float p = (float)Math.Exp(-lambda);
            var sum = p;
            while (sum < y) {
                k++;
                p *= lambda / k;
                sum += p;
            }
        }
        return k;
    }
    public static int PoissonQuantileAt(double lambda, double y) {
        if (lambda <= 0) return 0;

        int k = 0;
        if (lambda > 37) {
            // Normal approximation with continuity correction
            // mean = lambda, variance = lambda
            double z = Math.Sqrt(-2 * Math.Log(1 - y)); // Inverse standard normal CDF
            k = (int)(lambda + Math.Sqrt(lambda) * z + 0.5);
        } else {
            double p = Math.Exp(-lambda);
            var sum = p;
            while (sum < y) {
                k++;
                p *= lambda / k;
                sum += p;
            }
        }
        return k;
    }
    public static int PoissonQuantile(float lambda, ref XorShift rng) => PoissonQuantileAt(lambda, rng.NextSingle());

    /// <summary>
    /// Samples from an exponential distribution with a mean of 1.
    /// Used for modeling inter-arrival times or remaining lifetimes in a Poisson process.
    /// </summary>
    /// <param name="t">The cumulative probability</param>
    /// <returns>A random sample from the exponential distribution</returns>
    public static float SampleExponential(float t) {
        // Use inverse transform sampling: -mean * ln(1 - U) where U ~ Uniform(0,1)
        // We use (1 - NextSingle()) to avoid ln(0)
        double u = 1.0 - t;
        return (float)(-Math.Log(u));
    }

    public static float HaltonSequence(uint b, uint index) {
        double f = 1;
        double r = 0;
        uint i = index;
        while (i > 0) {
            f = f / b;
            r = r + f * (i % b);
            i = i / b;
        }
        return (float)r;
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
    public static float Delerp(float x, float low, float high) => (x - low) / (high - low);
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double Delerp(double x, double low, double high) => (x - low) / (high - low);
    public static string Input(string prompt = "", string dflt = "") {
        if (prompt != "") {
            Console.Write(prompt);
        }
        Console.Write(AnsiEx.CursorSave);
        Console.Write(Style.MenuInput.Format(dflt));
        Console.Write(AnsiEx.CursorRestore);
        var line = Console.ReadLine();
        if (line == null) {
            throw new EndOfStreamException("User pressed break");
        }
        return string.IsNullOrWhiteSpace(line) ? dflt : line;
    }
    /// <summary>
    /// Get all interactions between agent and subject using component-based system
    /// </summary>
    public static IEnumerable<Interaction> InteractionsWith(this IActor agent, IActor subject) {
        using var activity = LogCat.Interaction.StartActivity($"{nameof(InteractionsWith)} {agent.Name} {subject.Name})")?
            .SetTag("Agent", agent.Name).SetTag("Subject", subject.Name).SetTag("SubjectFaction", subject.Faction)
            .SetTag("AgentToSubject", agent.To(subject).ToString())
            .SetTag("SubjectToAgent", subject.To(agent).ToString());

        // Get interactions from agent's components
        if (agent is Crawler agentCrawler) {
            foreach (var component in agentCrawler.Components) {
                foreach (var interaction in component.EnumerateInteractions(subject)) {
                    yield return interaction;
                }
            }
        }

        // Get interactions from subject's components
        if (subject is Crawler subjectCrawler) {
            foreach (var component in subjectCrawler.Components) {
                foreach (var interaction in component.EnumerateInteractions(agent)) {
                    yield return interaction;
                }
            }
        }
    }
    public static bool Ended(this IActor actor) => actor.EndState != null;
    public static bool Lives(this IActor actor) => actor.EndState == null;
    public static int Length<ENUM>() where ENUM: struct, Enum => Enum.GetValues<ENUM>().Length;
    public static ENUM ChooseRandom<ENUM>(ref this XorShift rng) where ENUM: struct, Enum => Enum.GetValues<ENUM>()[rng.NextInt(Length<ENUM>())];
    static List<string> _messages = new();
    public static Action<string>? OnMessage = message => {
        LogCat.Log.LogInformation(message);
        _messages.Add(message);
    };
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
    public static bool IsPlayer(this IActor agent) => agent.HasFlag(ActorFlags.Player);
    public static bool HasFlag(this IActor agent, ActorFlags flags) => agent.Flags.HasFlag(flags);
    public static bool SurrenderedTo(this IActor agent, IActor other) => agent.To(other).Surrendered;
    public static int Attack(this Crawler attacker, IActor defender) {
        var fire = attacker.CreateFire();
        if (fire.Any()) {
            attacker.Message($"{attacker.Name} attacks {defender.Name}:");
            var (a2d, d2a) = attacker.ToTo(defender);
            attacker.SetHostileTo(defender, true);

            if (a2d.Spared && a2d.Latch(ActorToActor.EFlags.Betrayed, true)) {
                defender.Message($"You were betrayed by {attacker.Name}");
                d2a.SetFlag(ActorToActor.EFlags.Betrayer);
                attacker.Message($"You betrayed {defender.Name}");
                ++attacker.EvilPoints;
            }

            defender.ReceiveFire(attacker, fire);

            // Schedule weapon cooldown delay with follow-up attack
            var delay = attacker.WeaponDelay();
            Debug.Assert(delay.HasValue);
            return delay.Value;
        } else {
            attacker.Message($"No fire on {defender.Name} ({attacker.StateString(defender)}");
            return 60;
        }
    }
    public static bool Visited(this IActor actor, Location location) => actor.Knows(location) && actor.To(location).Visited;
    public static Style StyleFor(this IActor actor, Location location) =>
        actor.Location == location ? Style.MenuInput :
        actor.Visited(location) ? Style.MenuVisited :
        actor.Knows(location) ? Style.MenuSomeVisited :
        Style.MenuUnvisited;
    public static int Visits(this IActor actor, Sector sector) => sector.Locations.Count(l => actor.Visited(l));
    public static float Length(this Point point) => MathF.Sqrt(point.X * point.X + point.Y * point.Y);
    public static bool ClearFlag<TEnum>(this TEnum e, TEnum flags) where TEnum: struct, Enum => e.SetFlag(flags, false);
    public static bool SetFlag<TEnum>(this ref TEnum e, TEnum flags, bool p = true)
        where TEnum: struct, Enum {
        var underlyingType = Enum.GetUnderlyingType(typeof(TEnum));

        bool TryUnderlying<T>(ref TEnum e) where T: unmanaged {
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

    public static int TickInteractions(this List<Interaction> interactions, IActor agent, string prefix) {
        using var activity = LogCat.Interaction.StartActivity($"{nameof(TickInteractions)} {agent.Name} '{prefix}'")?
            .SetTag("Agent", agent.Name).SetTag("#Interactions", interactions.Count);

        int result = 0;
        foreach (var interaction in interactions) {
            var msg = interaction.MessageFor(agent);
            if (!string.IsNullOrEmpty(msg)) {
                agent.Message(Style.Em.Format(msg));
            }
            if (interaction.GetImmediacy() == Immediacy.Immediate) {
                result += interaction.Perform();
            }
        }
        return result;
    }
    public static List<MenuItem> InteractionMenuItems(this IActor agent, List<Interaction> interactions, string title, string prefix) {
        List<MenuItem> result = new();
        if (interactions.Count == 0) {
            result.Add(new MenuItem(prefix, $"{title}\n"));
        } else {
            var show = interactions.Count > 4 ? ShowArg.Hide : ShowArg.Show;
            result.AddRange(interactions.DetailMenuItems(prefix, show));
            result.Add(MenuItem.Sep);


            bool anyEnabled = interactions.Any(i => i.GetImmediacy() == Immediacy.Menu);
            result.Add(new ActionMenuItem(prefix,
                title,
                args => interactions.InteractionMenu(title, prefix, args).turns,
                anyEnabled ? EnableArg.Enabled : EnableArg.Disabled));
            result.Add(MenuItem.Sep);
        }
        return result;
    }
    public static IEnumerable<MenuItem> DetailMenuItems(this List<Interaction> interactions, string prefix, ShowArg show, string args = "") {
        var counters = new Dictionary<string, int>();
        foreach (var interaction in interactions) {
            int counter;
            var shortcut = $"{prefix}{interaction.OptionCode}";
            if (counters.ContainsKey(shortcut)) {
                counter = ++counters[shortcut];
            } else {
                counter = counters[shortcut] = 1;
            }
            var immediacy = interaction.GetImmediacy();
            string description = interaction.Description;
            if (immediacy == Immediacy.Failed && interaction is ExchangeInteraction exchange) {
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
    public static (MenuItem item, int turns) InteractionMenu(this List<Interaction> interactions, string Name, string prefix, string args) {
        List<MenuItem> interactionsMenu = [
            MenuItem.Cancel,
            .. interactions.DetailMenuItems(prefix, ShowArg.Show, args),
        ];

        return MenuRun($"{Name}", interactionsMenu.ToArray());
    }
    public static Inventory Loot(this Inventory from, XorShift rng, float lootReturn) {
        var loot = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            float t = (float)(Math.Sqrt(rng.NextDouble()) * lootReturn * 1.5);
            t = Math.Clamp(t, 0, 1);
            loot[commodity] += from[commodity] * t;
        }
        var lootableSegments = from.Segments
            .Where(s => s.Health > 0)
            .Where(s => rng.NextSingle() < lootReturn);
        return loot;
    }
    public static (ActorToActor a2s, ActorToActor s2a) ToTo(this IActor attacker, IActor defender) => (attacker.To(defender), defender.To(attacker));
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
