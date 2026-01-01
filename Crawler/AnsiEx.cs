using System.Drawing;

namespace Crawler;

public static partial class AnsiEx {
    // ANSI Color conversion methods
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
    }

    // ANSI Control sequences - Screen
    public const string ClearScreenToEnd = "\e[0J";
    public const string ClearScreenToStart = "\e[1J";
    public const string ClearScreen = "\e[2J";
    public const string ClearConsole = "\e[3J";
    public const string ClearLine = "\e[2K";
    public const string ClearLineToEnd = "\e[K";
    public const string ClearLineToStart = "\e[1K";

    // ANSI Control sequences - Cursor
    public static string CursorPosition(int x, int y) => $"\e[{y};{x}H";
    public static string CursorHome = "\e[H";
    public static string CursorX(int x) => $"\e[{x}G";
    public const string CursorUp = "\e[1A";
    public const string CursorDown = "\e[1B";
    public const string CursorFwd = "\e[1C";
    public const string CursorBwd = "\e[1D";
    public const string CursorReport = "\e[6n";
    public const string CursorSave = "\e7";
    public const string CursorRestore = "\e8";
    public const string CursorHide = "\e[?25l";
    public const string CursorShow = "\e[?25h";

    // ANSI Control sequences - Margin
    public static string Margin(int l) => $"\e[{l}s";
    public static string Margin(int l, int r) => $"\e[{l};{r}s";
    public const string EnableMargin = "\e[?69h";
    public const string DisableMargin = "\e[?69l";

    // ANSI Control sequences - Tabs
    public const string ClearTabHere = "\e[0g";
    public const string ClearTabs = "\e[3g";
    public const string SetTab = "\eH";

    // ANSI Control sequences - Scrolling
    public static string Scroll(int lines) => lines > 0 ? $"\e[{lines}S" : $"\e[{-lines}T";

    // ANSI Style sequences
    public const string StyleDefault = "\e[m";
    public const string StyleBold = "\e[1m";
    public const string StyleFaint = "\e[2m";
    public const string StyleItalic = "\e[3m";
    public const string StyleUnderline = "\e[4m";
    public const string StyleBlink = "\e[5m";
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

    // Console properties
    public static Point WindowSize => new Point(Console.WindowWidth, Console.WindowHeight);
    public static Point BufferSize => new Point(Console.BufferWidth, Console.BufferHeight);

    // Color transformations
    public static Color Dark(this Color c) => Color.FromArgb(c.A, c.R / 2, c.G / 2, c.B / 2);
    public static Color Bright(this Color c) => Color.FromArgb(c.A, 1 - ( 1 - c.R) / 2, 1 - (1 - c.G) / 2, 1 - ( 1 - c.B) / 2);
    public static Color Scale(this Color c, float s) => Color.FromArgb(
        c.A,
        (byte)Math.Clamp(c.R * s, 0, 255),
        (byte)Math.Clamp(c.G * s, 0, 255),
        (byte)Math.Clamp(c.B * s, 0, 255));
}
