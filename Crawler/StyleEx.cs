using System.Drawing;

namespace Crawler;

public static partial class StyleEx {
    public static Dictionary<Style, string> Styles = new();
    public static string StyleNone = "\e[m";

    static StyleEx() {
        InitStyles();
    }

    static void InitStyles() {
        Color bg = Color.DarkSlateGray.Scale(0.25f);
        Color fg = Color.LightGray;

        StyleNone = fg.On(bg);
        Styles[Style.Name] = Color.Yellow.On(bg);
        Styles[Style.Em] = Color.SandyBrown.On(bg);
        Styles[Style.UL] = AnsiEx.StyleUnderline;

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
                    result += SourceLines[y].Text[x].ToString();
                } else {
                    result += ' ';
                }
            }
            result += StyleNone;
        }
        return result;
    }
}
