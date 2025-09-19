namespace Crawler;

public record MenuItem(string Option, string Item) {
    public bool IsSeparator => Option == "" && Item == "";
    public virtual bool IsEnabled => Option.Length > 0;
    public override string ToString() => $"{Option}) {Item}";
    public static MenuItem Sep => new("", "");

    public bool IsCancel => Option == "X";

}

public record ActionMenuItem(string Option, string Item, Func<string, int>? Run): MenuItem(Option, Item) {
    public int TryRun(string arguments) {
        if (Run != null) {
            return Run(arguments);
        }
        return 0;
    }
}

public static partial class CrawlerEx {
    public static (MenuItem, string)            Menu(string Title, params MenuItem[] items) {
        Console.Write(Style.MenuTitle.Format($"[{Title}]") + " ");
        Console.Write(Style.MenuNormal.StyleString());
        bool wasSep = true;
        int optionCount = items.Count(x => x.IsEnabled);

        while (true) {
            int i = 0;
            foreach (var item in items) {
                if (item.IsSeparator) {
                    if (!wasSep) {
                        Console.WriteLine();
                        wasSep = true;
                    }
                    i = 0;
                } else {
                    if (i++ > 0) {
                        Console.Write("; ");
                    }
                    Style OptionStyle = Style.MenuOption;
                    if (!item.IsEnabled) {
                        OptionStyle = Style.MenuDisabled;
                    }
                    Console.Write(OptionStyle.Format(item.Option) + $"-{item.Item}");
                    wasSep = false;
                }
            }
            string input = "";
            if (optionCount == 0) {
                Console.WriteLine("No options available.");
                return (MenuItem.Sep, "");
            }
            do {
                Console.Write(Style.MenuInput.StyleString() + "? ");
                input = (Console.ReadLine() ?? "").ToUpper().Trim();
            } while (input == "");
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
