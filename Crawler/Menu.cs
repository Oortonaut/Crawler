namespace Crawler;

public record MenuItem(string Option, string Item) {
    public bool IsSeparator => Option == "" && Item == "";
    public virtual bool IsShow => true;
    public virtual bool IsEnabled => Option.Length > 0;
    public override string ToString() => $"{Option}) {Item}";
    public static MenuItem Sep => new("", "");
    public static MenuItem Cancel => new("X", "Cancel");
    public virtual string Format() {
        if (Option == "") {
            if (Item.StartsWith('\e')) {
                return Item;
            } else {
                return Style.MenuTitle.Format(Item);
            }
        }
        if (!IsEnabled) {
            return "(" + Style.MenuDisabled.Format(Option.ToLower()) + $") {Item}";
        } else {
            return "[" + Style.MenuOption.Format(Option) + $"] {Item}";
        }
    }
}

public enum ShowArg: byte {
    Hide,
    Show,
}

public enum EnableArg: byte {
    Disabled,
    Enabled,
}
public static partial class EnumEx {
    public static EnableArg Enable(this bool b) => b ? EnableArg.Enabled : EnableArg.Disabled;
    public static ShowArg Show(this bool b) => b ? ShowArg.Show : ShowArg.Hide;
    public static EnableArg ToEnableArg(this Immediacy mode) => mode == Immediacy.Failed ? EnableArg.Disabled : EnableArg.Enabled;
}

public record ActionMenuItem(string Option, string Item, Func<string, bool> Run, EnableArg Enabled = EnableArg.Enabled, ShowArg Show = ShowArg.Show): MenuItem(Option, Item) {
    public ActionMenuItem(string Option, string Item, Func<bool> Run, EnableArg Enabled, ShowArg Show): this(Option, Item, _ => Run(), Enabled, Show) { }
    public override bool IsEnabled => Enabled is EnableArg.Enabled;
    public override bool IsShow => Show is ShowArg.Show;
}

public static partial class CrawlerEx {
    public static (MenuItem, bool) MenuRun(string Title, params MenuItem[] items) {
        return MenuRun(Title, "", items);
    }
    public static (MenuItem, bool) MenuRun(string Title, string Dflt, params MenuItem[] items) {
        var (item, args) = Menu(Title, Dflt, items);
        bool did = false;
        if (item is ActionMenuItem action && action.IsEnabled) {
            did = action.Run(args);
        }
        return (item, did);
    }
    public static (MenuItem, string) Menu(string Title, params MenuItem[] items) {
        return Menu(Title, "", items);
    }
    public static (MenuItem, string) Menu(string Title, string Dflt, params MenuItem[] items) {
        if (Title != "") {
            Console.Write(Style.MenuTitle.Format($"{Title}"));
        }
        Console.Write(Style.MenuNormal.StyleString());
        bool start = false;
        int optionCount = items.Count(x => x.IsEnabled);

        while (true) {
            foreach (var item in items) {
                if (!item.IsShow) continue;
                if (item.Option == "") {
                    start = true;
                }
                if (item.Item == "") {
                    start = true;
                } else {
                    Console.Write(start ? '\n' : ' ');
                    var itemFormat = item.Format();
                    Console.Write(itemFormat);
                    start = itemFormat.EndsWith('\n');
                }
            }
            string input;
            if (optionCount == 0) {
                Console.WriteLine("No options available. Press enter to continue.");
                Console.ReadLine();
                return (MenuItem.Sep, Dflt);
            }
            try {
                do {
                    input = CrawlerEx.Input("? ", Dflt);
                } while (input == "");
            } catch (EndOfStreamException eos) {
                return (MenuItem.Sep, eos.Message);
            }
            Console.Write(Style.MenuNormal.StyleString());
            var firstSpace = input.IndexOf(' ');
            string arguments = "";
            if (firstSpace > 0) {
                arguments = input.Substring(firstSpace + 1).Trim();
                input = input.Substring(0, firstSpace);
            }
            foreach (var item in items) {
                if (string.Compare(item.Option, input, StringComparison.InvariantCultureIgnoreCase) == 0) {
                    return (item, arguments);
                }
            }
            Console.WriteLine(Style.SegmentDestroyed.Format($"Unrecognized input '{input}'"));
        }
    }
}
