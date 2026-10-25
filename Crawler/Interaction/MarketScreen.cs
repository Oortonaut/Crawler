namespace Crawler;

/// <summary>
/// Full-screen, key-driven commodity market for trading with one actor.
/// The player edits a basket with the arrow keys and confirms it once, instead of
/// picking one "Buy 1 X" / "Sell 1 X" menu entry per unit.
/// </summary>
public class MarketScreen(IActor player, IActor trader, IEnumerable<TradeOffer> offers) {
    class Row(Commodity commodity, TradeOffer? ask, TradeOffer? bid) {
        public Commodity Commodity => commodity;
        public TradeOffer? Ask => ask; // Trader sells, player buys
        public TradeOffer? Bid => bid; // Trader buys, player sells
        public int Lots; // Positive buys, negative sells

        public float AskLotPrice => ask == null ? 0 : Commodity.Scrap.Round(ask.PricePerUnit * ask.Quantity);
        public float BidLotPrice => bid == null ? 0 : Commodity.Scrap.Round(bid.PricePerUnit * bid.Quantity);
        public float LotSize => Lots >= 0 ? ask?.Quantity ?? 1 : bid?.Quantity ?? 1;
        public float Amount => Math.Abs(Lots) * LotSize;
        // Cash change for the player
        public float Net => Lots > 0 ? -Lots * AskLotPrice : -Lots * BidLotPrice;
    }

    readonly List<Row> _rows = offers
        .Where(o => o.IsCommodity && o.Commodity != Commodity.Scrap)
        .GroupBy(o => o.Commodity!.Value)
        .Select(g => new Row(g.Key,
            g.FirstOrDefault(o => o.Direction == TradeDirection.Sell),
            g.FirstOrDefault(o => o.Direction == TradeDirection.Buy)))
        // Traders bid on nearly everything; only show buy-only rows for goods the player has to sell
        .Where(r => r.Ask != null || Held(player, r.Commodity) > 0)
        .ToList();
    int _cursor;
    int _scroll;
    string _status = "";

    public bool HasRows => _rows.Count > 0;

    // Only stacks marked for trade are bought and sold
    static float Held(IActor actor, Commodity commodity) => actor.Inventory[commodity, Account.Trade];

    float BasketNet => _rows.Sum(r => r.Net);
    float CashAfter => player.Inventory[Commodity.Scrap] + BasketNet;

    /// <summary>Runs the screen. Returns true if a trade was made (time was consumed).</summary>
    public bool Run() {
        if (!HasRows) return false;
        Console.CursorVisible = false;
        try {
            while (true) {
                Draw();
                var key = Console.ReadKey(true);
                int step = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? 10 : 1;
                _status = "";
                switch (key.Key) {
                case ConsoleKey.UpArrow or ConsoleKey.K:
                    _cursor = (_cursor + _rows.Count - 1) % _rows.Count;
                    break;
                case ConsoleKey.DownArrow or ConsoleKey.J:
                    _cursor = (_cursor + 1) % _rows.Count;
                    break;
                case ConsoleKey.PageUp:
                    _cursor = Math.Max(0, _cursor - 10);
                    break;
                case ConsoleKey.PageDown:
                    _cursor = Math.Min(_rows.Count - 1, _cursor + 10);
                    break;
                case ConsoleKey.RightArrow or ConsoleKey.OemPlus or ConsoleKey.Add:
                    Adjust(_rows[_cursor], step);
                    break;
                case ConsoleKey.LeftArrow or ConsoleKey.OemMinus or ConsoleKey.Subtract:
                    Adjust(_rows[_cursor], -step);
                    break;
                case ConsoleKey.Oem6: // ]
                    Adjust(_rows[_cursor], 10);
                    break;
                case ConsoleKey.Oem4: // [
                    Adjust(_rows[_cursor], -10);
                    break;
                case ConsoleKey.M:
                    Adjust(_rows[_cursor], int.MaxValue / 2);
                    break;
                case ConsoleKey.A:
                    Adjust(_rows[_cursor], int.MinValue / 2);
                    break;
                case ConsoleKey.D0 or ConsoleKey.Backspace:
                    _rows[_cursor].Lots = 0;
                    break;
                case ConsoleKey.C:
                    _rows.ForEach(r => r.Lots = 0);
                    break;
                case ConsoleKey.Enter:
                    if (_rows.All(r => r.Lots == 0)) return false;
                    return Execute();
                case ConsoleKey.Escape or ConsoleKey.Q or ConsoleKey.X:
                    return false;
                }
            }
        } finally {
            Console.CursorVisible = true;
            Console.Write(Style.None.StyleString() + AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        }
    }

    // Move the row's basket quantity by delta lots, clamped to stock, holdings and cash.
    void Adjust(Row row, int delta) {
        int target = row.Lots + delta;
        int maxBuy = row.Ask == null ? 0 : (int)(Held(trader, row.Commodity) / row.Ask.Quantity);
        int maxSell = row.Bid == null ? 0 : (int)(Held(player, row.Commodity) / row.Bid.Quantity);
        target = Math.Clamp(target, -maxSell, maxBuy);
        if (target > 0 && row.AskLotPrice > 0) {
            // Cash available for this row once every other row in the basket settles
            float cash = player.Inventory[Commodity.Scrap] + BasketNet - row.Net;
            int affordable = (int)(cash / row.AskLotPrice);
            if (target > affordable) {
                target = Math.Max(0, affordable);
                _status = $"Limited by cash: {cash:F1}¢¢";
            }
        }
        row.Lots = target;
    }

    void Draw() {
        int height = Math.Max(10, Console.WindowHeight);
        int visible = Math.Max(3, height - 9);
        if (_cursor < _scroll) _scroll = _cursor;
        if (_cursor >= _scroll + visible) _scroll = _cursor - visible + 1;

        var sb = new System.Text.StringBuilder();
        sb.Append(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        sb.AppendLine(Style.MenuTitle.Format($" Market: {trader.Name} ") + $"  {Game.TimeString(player.Time)}");
        sb.AppendLine($"{"Commodity",-14}{"Have",9}{"Stock",9}{"Sell@",9}{"Buy@",9}  {"Basket",-16}{"Total",10}");
        for (int i = _scroll; i < Math.Min(_rows.Count, _scroll + visible); i++) {
            var row = _rows[i];
            string basket = row.Lots switch {
                > 0 => $"buy {row.Commodity.CommodityTextFull(row.Amount)}",
                < 0 => $"sell {row.Commodity.CommodityTextFull(row.Amount)}",
                _ => "",
            };
            string bid = row.Bid == null ? "-" : $"{row.BidLotPrice:F1}";
            string ask = row.Ask == null ? "-" : $"{row.AskLotPrice:F1}";
            string total = row.Lots == 0 ? "" : $"{row.Net:+0.0;-0.0}";
            string line = $"{row.Commodity,-14}{Held(player, row.Commodity),9:F1}{Held(trader, row.Commodity),9:F1}{bid,9}{ask,9}  {basket,-16}{total,10}";
            bool idle = Held(player, row.Commodity) <= 0 && Held(trader, row.Commodity) <= 0;
            sb.AppendLine(i == _cursor ? Style.MenuOption.Format("> " + line) :
                idle ? Style.MenuDisabled.Format("  " + line) : "  " + line);
        }
        sb.AppendLine();
        sb.AppendLine($"Cash {player.Inventory[Commodity.Scrap]:F1}¢¢   Basket {BasketNet:+0.0;-0.0}¢¢   After {CashAfter:F1}¢¢   " +
            $"({_cursor + 1}/{_rows.Count})");
        sb.AppendLine(Style.MenuDisabled.Format(
            "↑↓ select  →/+ buy  ←/- sell  Shift or ]/[ ×10  M max buy  A sell all  0 clear row  C clear  Enter confirm  Esc cancel"));
        if (_status != "") sb.AppendLine(Style.Em.Format(_status));
        Console.Write(sb.ToString());
    }

    bool Execute() {
        // Sync both actors like other interactions do before moving goods
        var common = player.Time > trader.Time ? player.Time : trader.Time;
        if (player.Time < common) player.SimulateTo(common);
        if (trader.Time < common) trader.SimulateTo(common);

        List<string> bought = [], sold = [];
        float net = 0;
        int deals = 0;
        // Sells first so their proceeds can fund the buys
        foreach (var row in _rows.Where(r => r.Lots != 0).OrderBy(r => r.Lots)) {
            bool buying = row.Lots > 0;
            var offer = buying ? row.Ask! : row.Bid!;
            float lotPrice = buying ? row.AskLotPrice : row.BidLotPrice;
            IOffer goods = new GoodsOffer(row.Commodity, offer.Quantity);
            IOffer scrap = new ScrapOffer(lotPrice);
            var (playerGives, traderGives) = buying ? (scrap, goods) : (goods, scrap);

            int done = 0;
            for (int i = 0; i < Math.Abs(row.Lots); i++) {
                if (playerGives.DisabledFor(player, trader) != null || traderGives.DisabledFor(trader, player) != null) break;
                playerGives.PerformOn(player, trader);
                traderGives.PerformOn(trader, player);
                done++;
            }
            if (done == 0) continue;
            ++deals;
            float amount = done * offer.Quantity;
            (buying ? bought : sold).Add(row.Commodity.CommodityTextFull(amount));
            net += buying ? -done * lotPrice : done * lotPrice;
        }
        if (deals == 0) {
            player.Message($"No trade with {trader.Name} went through.");
            return false;
        }

        string Join(List<string> items) => items.Count == 0 ? "nothing" : string.Join(", ", items);
        player.Message($"Traded with {trader.Name}: bought {Join(bought)}; sold {Join(sold)}. Net {net:+0.0;-0.0}¢¢.");
        trader.Message($"Traded with {player.Name}: sold {Join(bought)}; bought {Join(sold)}.");

        // One trade session: time scales with the number of goods traded, not the units
        var duration = Tuning.Crawler.TradeTime * deals;
        player.ConsumeTime("Trading", 300, duration);
        trader.ConsumeTime("Trading", 300, duration);
        return true;
    }
}
