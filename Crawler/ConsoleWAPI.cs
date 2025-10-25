using PInvoke;
using Crawler.Logging;

namespace Crawler;

using CHAR_INFO = Kernel32.CHAR_INFO;
using CONSOLE_MODE = Kernel32.ConsoleBufferModes;
using CONSOLE_SCREEN_BUFFER_INFO = Kernel32.CONSOLE_SCREEN_BUFFER_INFO;
using CONSOLE_TEXTMODE = Kernel32.ConsoleScreenBufferFlag;
using FILE_ATTRIBUTE = Kernel32.CreateFileFlags;
using FILE_CREATE = Kernel32.CreationDisposition;
using FILE_SHARE = Kernel32.FileShare;
using INPUT_RECORD = Kernel32.INPUT_RECORD;
using SafeObjectHandle = Kernel32.SafeObjectHandle;
using CellAttributes = Kernel32.CharacterAttributesFlags;

public static partial class CrawlerEx {
    public static Coord Size<T>(this T[,] array) => new Coord(array.GetLength(1), array.GetLength(0));
    public static Rect Bounds<T>(this T[,] array) => new Rect(Coord.Zero, Size(array));
    public static void Scroll(this ScreenCell[,] buffer, Rect region, Coord delta, ScreenCell source) {
        buffer.Scroll(region, delta, (_, _) => source);
    }
    public static void ScrollWrap(this ScreenCell[,] buffer, Rect region, Coord delta, ScreenCell source) {
        var buff = buffer;
        buffer.Scroll(region, delta, (C, R) => {
            Coord wrapped = (C - R.o) % R.Size + R.o;
            return buff[wrapped.y, wrapped.x];
        });
    }
    public static void Scroll(this ScreenCell[,] buffer, Rect region, Coord delta, Func<Coord, Rect, ScreenCell> emptySource) {
        var bounds = buffer.Bounds();
        region = region.Intersect(bounds);
        if (delta.x == 0 && delta.y == 0) {
            return;
        }
        if (delta.y < 0 || delta.y == 0 && delta.x < 0) {
            for (int y = region.o.y; y < region.e.y; ++y) {
                for (int x = region.o.x; x < region.e.x; ++x) {
                    var dest = new Coord(x, y);
                    var src = dest + delta;
                    if (bounds.Contains(src)) {
                        buffer[y, x] = buffer[src.y, src.x];
                    } else {
                        buffer[y, x] = emptySource(dest, region);
                    }
                }
            }
        } else {
            for (int y = region.e.y - 1; y >= region.o.y; --y) {
                for (int x = region.e.y - 1; x >= region.o.x; --x) {
                    var dest = new Coord(x, y);
                    var src = dest + delta;
                    if (bounds.Contains(src)) {
                        buffer[y, x] = buffer[src.y, src.x];
                    } else {
                        buffer[y, x] = emptySource(dest, region);
                    }
                }
            }
        }
    }
    public static Coord Write(this ScreenCell[,] buffer, Rect bounds, Coord cursor, CellAttributes attr, string text) {
        var (x, y) = bounds.Clamp(cursor);
        if (x >= bounds.e.x) {
            (x, y) = buffer.Newline(bounds, new Coord(x, y), attr);
        }
        foreach (var c in text) {
            switch (c) {
            case '\n': (x, y) = buffer.Newline(bounds, new Coord(x, y), attr); break;
            case '\r': x = bounds.o.x; break;
            default:
                buffer[y, x] = new ScreenCell(c, attr);
                ++x;
                if (x >= bounds.e.x) {
                    (x, y) = buffer.Newline(bounds, new Coord(x, y), attr);
                }
                break;
            }
        }
        return new (x, y);
    }
    public static Coord Newline(this ScreenCell[,] buffer, Rect bounds, Coord cursor, CellAttributes attr) {
        var (x, y) = cursor;
        x = bounds.o.x;
        ++y;
        if (y >= bounds.e.y) {
            int lines = y - (bounds.e.y - 1);
            buffer.Scroll(bounds, new Coord(0, lines), new ScreenCell(' ', attr));
            y -= lines;
        }
        return new(x, y);
    }
}

public record Coord(int x, int y) {
    static public Coord Zero = new Coord(0, 0);
    static public Coord One = new Coord(1, 1);
    static public Coord operator +(Coord a, Coord b) => new Coord(a.x + b.x, a.y + b.y);
    static public Coord operator -(Coord a, Coord b) => new Coord(a.x - b.x, a.y - b.y);
    static public Coord operator -(Coord a) => new Coord(-a.x, -a.y);
    static public Coord operator *(Coord a, int b) => new Coord(a.x * b, a.y * b);
    static public Coord operator *(Coord a, Coord b) => new Coord(a.x * b.x, a.y * b.y);
    static public Coord operator /(Coord a, int b) => new Coord(a.x / b, a.y / b);
    static public Coord operator /(Coord a, Coord b) => new Coord(a.x / b.x, a.y / b.y);
    static public Coord operator %(Coord a, int b) => new Coord(a.x % b, a.y / b);
    static public Coord operator %(Coord a, Coord b) => new Coord(a.x % b.x, a.y % b.y);
    static public Coord Max(Coord a, Coord b) => new Coord(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
    static public Coord Min(Coord a, Coord b) => new Coord(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
}
public record Rect(Coord o, Coord e) {
    static public Rect Zero = new Rect(Coord.Zero, Coord.Zero);
    static public Rect One = new Rect(Coord.One, Coord.One);
    static public Rect operator +(Rect a, Coord b) => new Rect(a.o + b, a.e + b);
    static public Rect operator -(Rect a, Coord b) => new Rect(a.o - b, a.e - b);
    public bool Contains(Coord c) => c.x >= o.x && c.x < e.x && c.y >= o.y && c.y < e.y;
    public Coord Clamp(Coord c) => new Coord(Math.Clamp(c.x, o.x, e.x), Math.Clamp(c.y, o.y, e.y));
    public Coord ClampInterior(Coord c) => new Coord(Math.Clamp(c.x, o.x, e.x - 1), Math.Clamp(c.y, o.y, e.y - 1));
    public Rect Union(Rect other) => new Rect(Coord.Min(o, other.o), Coord.Max(e, other.e));
    public Rect Intersect(Rect other) => new Rect(Coord.Max(o, other.o), Coord.Min(e, other.e));
    public int Width => e.x - o.x;
    public int Height => e.y - o.y;
    public Coord Size => new Coord(Width, Height);
}
public record ScreenCell(char Glyph, CellAttributes Attr);
public record Window(Rect bounds) {
    public Coord Size {
        get {
            return _bounds.Size;
        }
        set {
            _bounds = new Rect(bounds.o, value);
        }
    }
    public Rect Bounds {
        get { return _bounds;}
        set {
            if (value.Size != Size) {
                var newBuffer = new ScreenCell[value.Size.y, value.Size.x];
                var copySize = Coord.Min(value.Size, Size);
            }
            _bounds = value;
        }
    }
    Rect _bounds = bounds;
    public Coord Origin => Bounds.o;
    public Coord End => Bounds.e;
    public ScreenCell[,] Buffer { get; set;  } = new ScreenCell[bounds.Size.y, bounds.Size.x];
    public ScreenCell Background = new ScreenCell(' ', (CellAttributes)0x1F);
    public void Clear() {
        for (int y = 0; y < Size.y; ++y) {
            for (int x = 0; x < Size.x; ++x) {
                Buffer[y, x] = Background;
            }
        }
        CursorPos = Coord.Zero;
    }
    public void Write(string text) {
        CursorPos = Buffer.Write(Bounds, CursorPos + Bounds.o, Background.Attr, text) - Bounds.o;
    }
    public void WriteLine(string text) {
        Write(text);
        CursorPos = Buffer.Newline(Bounds, CursorPos + Bounds.o, Background.Attr) - Bounds.o;
    }
    public Coord CursorPos { get; set; } = Coord.Zero;
}


public class CrawlerConsole {

    CONSOLE_SCREEN_BUFFER_INFO GetScreenBufferInfo() {
        CONSOLE_SCREEN_BUFFER_INFO result;
        bool success = Kernel32.GetConsoleScreenBufferInfo(screenBufferHandle, out result);
        return result;
    }

    [Flags]
    public enum ConsoleFlags {
        None = 0,
        Resize = 1,
        Raw = 2,
        HalfDuplex = 4,
        AllInput = 8,
    }

    IntPtr screenBufferHandle = Kernel32.GetStdHandle(Kernel32.StdHandle.STD_OUTPUT_HANDLE);
    IntPtr deviceInputHandle = Kernel32.GetStdHandle(Kernel32.StdHandle.STD_INPUT_HANDLE);
    SafeObjectHandle templateFile = new SafeObjectHandle();
    ConsoleFlags flags;

    public CrawlerConsole(Coord? size, ConsoleFlags flags) {
        this.flags = flags;

        var genericReadWrite = new Kernel32.ACCESS_MASK((uint)(Kernel32.ACCESS_MASK.GenericRight.GENERIC_READ | Kernel32.ACCESS_MASK.GenericRight.GENERIC_WRITE));
        //Kernel32.SECURITY_ATTRIBUTES? attr = null;
        //deviceInputHandle = Kernel32.CreateFile("CONIN$",
        //    genericReadWrite,
        //    FILE_SHARE.FILE_SHARE_READ | FILE_SHARE.FILE_SHARE_WRITE,
        //    attr,
        //    FILE_CREATE.OPEN_EXISTING, FILE_ATTRIBUTE.FILE_ATTRIBUTE_NORMAL, templateFile);

        //screenBufferHandle = Kernel32.CreateConsoleScreenBuffer(
        //    genericReadWrite,
        //    FILE_SHARE.FILE_SHARE_READ | FILE_SHARE.FILE_SHARE_WRITE,
        //    IntPtr.Zero,
        //    CONSOLE_TEXTMODE.CONSOLE_TEXTMODE_BUFFER, IntPtr.Zero
        //    );
        //Kernel32.SetConsoleActiveScreenBuffer(screenBufferHandle);
        if (size != null) {
            Size = size; // Force resize even if flag is not set
        } else {
            Size = makeCoord(GetScreenBufferInfo().dwSize);
        }
        ConsoleSize = Size;
        EnableEchoInput = ((flags & ConsoleFlags.HalfDuplex) == ConsoleFlags.None);
        EnableWindowInput = EnableMouseInput = ((flags & ConsoleFlags.AllInput) != ConsoleFlags.None);
        bool Processed = !((flags & ConsoleFlags.Raw) == ConsoleFlags.Raw);
        ProcessedInput = ProcessedOutput = EnableLineInput = Processed;
        EnableWrap = EnableQuickEditMode = Processed;
        EnableAnsi = Processed;

        EnableInsertMode = false;
        EnableNewlineAutoReturn = Processed;
        EnableLVBGridWorldwide = true;
    }

    public bool HasInputModeFlag(CONSOLE_MODE flag) {
        CONSOLE_MODE currentMode = 0;
        Kernel32.GetConsoleMode(deviceInputHandle, out currentMode);
        return (currentMode & flag) == flag;
    }
    public bool HasOutputModeFlag(CONSOLE_MODE flag) {
        CONSOLE_MODE currentMode = 0;
        Kernel32.GetConsoleMode(screenBufferHandle, out currentMode);
        return (currentMode & flag) == flag;
    }
    public bool SetInputModeFlag(CONSOLE_MODE flag, bool value) {
        CONSOLE_MODE currentMode = 0;
        Kernel32.GetConsoleMode(deviceInputHandle, out currentMode);
        if (value) {
            currentMode |= flag;
        } else {
            currentMode &= ~flag;
        }
        Kernel32.SetConsoleMode(deviceInputHandle, currentMode);
        return value;
    }
    public bool SetOutputModeFlag(CONSOLE_MODE flag, bool value) {
        CONSOLE_MODE currentMode = 0;
        Kernel32.GetConsoleMode(screenBufferHandle, out currentMode);
        if (value) {
            currentMode |= flag;
        } else {
            currentMode &= ~flag;
        }
        Kernel32.SetConsoleMode(screenBufferHandle, currentMode);
        return value;
    }
    public bool ProcessedInput {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_PROCESSED_INPUT);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_PROCESSED_INPUT, value);
    }
    public bool ProcessedOutput {
        get => HasOutputModeFlag(CONSOLE_MODE.ENABLE_PROCESSED_OUTPUT);
        set => SetOutputModeFlag(CONSOLE_MODE.ENABLE_PROCESSED_OUTPUT, value);
    }
    public bool EnableLineInput {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_LINE_INPUT);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_LINE_INPUT, value);
    }
    public bool EnableEchoInput {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_ECHO_INPUT);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_ECHO_INPUT, value);
    }
    public bool EnableWindowInput {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_WINDOW_INPUT);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_WINDOW_INPUT, value);
    }
    public bool EnableMouseInput {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_MOUSE_INPUT);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_MOUSE_INPUT, value);
    }
    public bool EnableInsertMode {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_INSERT_MODE);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_INSERT_MODE, value);
    }
    public bool EnableQuickEditMode {
        get => HasInputModeFlag(CONSOLE_MODE.ENABLE_QUICK_EDIT_MODE);
        set => SetInputModeFlag(CONSOLE_MODE.ENABLE_QUICK_EDIT_MODE, value);
    }
    public bool EnableWrap {
        get => HasOutputModeFlag(CONSOLE_MODE.ENABLE_WRAP_AT_EOL_OUTPUT);
        set => SetOutputModeFlag(CONSOLE_MODE.ENABLE_WRAP_AT_EOL_OUTPUT, value);
    }
    public bool EnableAnsi {
        get => HasOutputModeFlag(CONSOLE_MODE.ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        set => SetOutputModeFlag(CONSOLE_MODE.ENABLE_VIRTUAL_TERMINAL_PROCESSING, value);
    }
    public bool EnableNewlineAutoReturn {
        get => !HasOutputModeFlag(CONSOLE_MODE.DISABLE_NEWLINE_AUTO_RETURN);
        set => SetOutputModeFlag(CONSOLE_MODE.DISABLE_NEWLINE_AUTO_RETURN, !value);
    }
    public bool EnableLVBGridWorldwide {
        get => HasOutputModeFlag(CONSOLE_MODE.ENABLE_LVB_GRID_WORLDWIDE);
        set => SetOutputModeFlag(CONSOLE_MODE.ENABLE_LVB_GRID_WORLDWIDE, value);
    }

    public unsafe int Write(string text) {
        fixed (char* p = text) {
            Kernel32.WriteConsole(screenBufferHandle, p, text.Length, out var written, IntPtr.Zero);
            return written;
        }
    }

    public int WriteLine(string text = "") {
        return Write(text + "\n");
    }

    internal static COORD makeCOORD(Coord a) {
        return new COORD { X = (short)a.x, Y = (short)a.y };
    }

    internal static Coord makeCoord(COORD a) {
        return new Coord(a.X, a.Y);
    }

    internal SMALL_RECT makeSMALL_RECT(Rect value) {
        var o = value.o;
        var e = value.e;
        return new SMALL_RECT {
            Left = (short)o.x,
            Top = (short)o.y,
            Right = (short)(e.x - 1),
            Bottom = (short)(e.y - 1)
        };
    }

    internal Rect makeRect(SMALL_RECT value) {
        return new Rect(
            new Coord(value.Left, value.Top),
            new Coord(value.Right + 1, value.Bottom + 1));
    }

    public Coord Size {
        get {
            return new Coord(mirror.GetLength(1), mirror.GetLength(0));
        }
        set {
            if (value != Size) {
                mirror = new CHAR_INFO[value.y, value.x];
                Console.SetWindowSize(value.x, value.y);
            }
        }
    }

    public Rect Bounds {
        get {
            var sbi = GetScreenBufferInfo();
            return makeRect(sbi.srWindow);
        }
        set {
            if (Bounds.o != value.o || Bounds.e != value.e) {
                var sbi = GetScreenBufferInfo();
                if (sbi.srWindow.Left != value.o.x || sbi.srWindow.Top != value.o.y) {
                    Console.SetWindowPosition(Bounds.o.x, Bounds.o.y);
                }
                if (value.Size != Size) {
                    mirror = new CHAR_INFO[Size.y, Size.x];
                    Console.SetWindowSize(value.Size.x, value.Size.y);
                }
            }
        }
    }

    public Coord ConsoleSize {
        get {
            var sbi = GetScreenBufferInfo();
            return makeCoord(sbi.dwSize);
        }
        set {
            var sbi = GetScreenBufferInfo();
            if (makeCoord(sbi.dwSize) != value) {
                Console.SetBufferSize(value.x, value.y);
            }
        }
    }

    public Coord CursorPosition {
        get {
            return makeCoord(GetScreenBufferInfo().dwCursorPosition);
        }
        set {
            Kernel32.SetConsoleCursorPosition(screenBufferHandle, makeCOORD(value));
        }
    }

    public CellAttributes CharacterAttributes {
        get {
            return GetScreenBufferInfo().wAttributes;
        }
        set {
            Kernel32.SetConsoleTextAttribute(screenBufferHandle, value);
        }
    }

#region Back buffer and commit
    CHAR_INFO[,] mirror = new CHAR_INFO[0, 0];
    CHAR_INFO makeCHAR_INFO(ScreenCell cell) {
        var result = new CHAR_INFO();
        result.Char.UnicodeChar = cell.Glyph;
        result.Attributes = cell.Attr;
        return result;
    }

    static bool same(CHAR_INFO a, CHAR_INFO b) {
        return a.Attributes == b.Attributes &&
            a.Char.UnicodeChar == b.Char.UnicodeChar;
    }

    IEnumerable<Tuple<int, int>> Commit_GetDirtySpans(ScreenCell[,] buffer, int y) {
        bool started = false;
        int first = 0;
        int last = 0;
        int maxGap = 5;
        int width = Math.Min(Size.x, buffer.GetLength(0));
        for (int x = 0; x < width; ++x) {
            var cell = buffer[y, x];
            var charInfo = makeCHAR_INFO(cell);
            if (!same(mirror[y, x], charInfo)) {
                mirror[y, x] = charInfo;
                if (!started) {
                    first = x;
                    started = true;
                }
                last = x;
            }
            if (started && x - last > maxGap) {
                yield return Tuple.Create(first, last);
                started = false;
            }
        }
        if (started) {
            yield return Tuple.Create(first, last);
        }
    }
    bool ResizeFlag { get { return (flags & ConsoleFlags.Resize) != ConsoleFlags.None; } }
    public unsafe void Commit(ScreenCell[,] buffer) {
        if (Size != ConsoleSize) {
            if (ResizeFlag) {
                Size = ConsoleSize;
            } else {
                ConsoleSize = Size;
            }
        }
        bool result = false;
        int height = Math.Min(Size.y, buffer.Size().y);
        var consoleSizeCOORD = makeCOORD(ConsoleSize);
        for (int y = height - 1; y >= 0; --y) {
            foreach (var dirtySpan in Commit_GetDirtySpans(buffer, y)) {
                var targetRegion = new SMALL_RECT {
                    Left = (short)dirtySpan.Item1,
                    Top = (short)y,
                    Right = (short)dirtySpan.Item2,
                    Bottom = (short)y,
                };

                fixed (CHAR_INFO* p = &mirror[y, 0]) {
                    result = Kernel32.WriteConsoleOutput(screenBufferHandle,
                        p,
                        consoleSizeCOORD,
                        makeCOORD(new Coord(dirtySpan.Item1, y)),
                        &targetRegion);
                }
            }
        }
    }
#endregion

#region Poll and process events into standard events
    //public enum EVENT_TYPE : ushort {
    //    KEY_EVENT = 1, // keyboard input
    //    MOUSE_EVENT = 2, // mouse input
    //    WINDOW_BUFFER_SIZE_EVENT = 4, // scrn buf. resizing
    //    FOCUS_EVENT = 8,  // disregard focus events
    //    MENU_EVENT = 16,   // disregard menu events
    //}

    List<INPUT_RECORD> events = new ();
    IEnumerable<INPUT_RECORD> ReadConsoleInput() {
        int eventCount = 0;
        Kernel32.GetNumberOfConsoleInputEvents(deviceInputHandle, out eventCount);
        while (eventCount > 0) {
            INPUT_RECORD inputRecord = new INPUT_RECORD();
            int eventsFound;
            if (Kernel32.ReadConsoleInput(deviceInputHandle, out inputRecord, 1, out eventsFound)) {
                yield return inputRecord;
                --eventCount;
            }
        }
    }
    // TODO - save/restore event stream including timing events
    public IEnumerable<INPUT_RECORD> Events {
        get {
            using var activity = ActivitySources.Console.StartActivity("console.events_tick",
                System.Diagnostics.ActivityKind.Internal);
            activity?.SetTag("queued.count", events.Count);

            foreach (var queued in events) {
                yield return queued;
            }
            events.Clear();

            foreach (var inputEvent in ReadConsoleInput()) {
                yield return inputEvent;
            }
        }
    }

    #endregion

}
