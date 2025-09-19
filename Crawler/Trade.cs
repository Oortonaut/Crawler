namespace Crawler;

[Flags]
public enum SegmentFamily {
    Weapon = 1,
    Power = 2,
    Traction = 4,
    Defense = 8
}

public record TradeOffer(Inventory Payment, Inventory Goods, Inventory? BuyerGift = null, Inventory? SellerGift = null) {
    public TradeOffer(Inventory Payment, Inventory Goods, double rate): this(Payment, Goods) {
        Balance(rate);
    }
    public TradeOffer(Commodity Payment, Commodity Goods, double rate): this(new Inventory(Payment), new Inventory(Goods)) {
        Balance(rate);
    }
    public TradeOffer(Commodity Payment, Segment Goods, double rate): this(new Inventory(Payment), new Inventory(Goods)) {
        Balance(rate);
    }
    public TradeOffer(Segment Payment, Commodity Goods, double rate): this(new Inventory(Payment), new Inventory(Goods)) {
        Balance(rate);
    }
    public override string ToString() {
        return $"Trade {Payment} for {Goods}";
    }
    public bool CanPerform(Inventory Buyer, Inventory Seller) {
        return Buyer.Contains(Payment) && Seller.Contains(Goods);
    }
    public bool Perform(Inventory Buyer, Inventory Seller) {
        if (!CanPerform(Buyer, Seller)) {
            return false;
        }

        try {
            Buyer.Remove(Payment);
            Seller.Remove(Goods);
            Buyer.Add(Goods);
            if (BuyerGift != null) {
                Buyer.Add(BuyerGift);
            }
            Seller.Add(Payment);
            if (SellerGift != null) {
                Seller.Add(SellerGift);
            }
            return true;
        } catch (Exception) {
            return false;
        }
    }

    public TradeOffer Balance(double sellerReturn = 1.0, double minTradeCost = 30) {
        // Keeps the ratios of items in each inventory the same but scales them to a
        // minimum trade cost. Only scales up.
        // Step 1: Bring the Payment and Goods up to the minimum value.
        double PaymentValue = Payment.Value;
        double PaymentScale = 1;
        if (PaymentValue > 0) {
            PaymentScale = GetScale(PaymentValue);
            PaymentValue *= PaymentScale; // >= min trade
        }

        double GoodsValue = Goods.Value * sellerReturn;
        double GoodsScale = GetScale(GoodsValue);
        if (GoodsValue > 0) {
            GoodsScale = GetScale(GoodsValue);
            GoodsValue *= GoodsScale; // >= min trade
        }

        if (PaymentValue < GoodsValue) {
            PaymentScale *= GoodsValue / PaymentValue;
            PaymentValue *= GoodsValue / PaymentValue;
        } else if (PaymentValue > GoodsValue) {
            GoodsScale *= PaymentValue / GoodsValue;
            GoodsValue *= PaymentValue / GoodsValue;
        }

        if (GoodsScale > 1) {
            Goods.ItemCounts
                .Select(q=> ( int ) Math.Floor(q * GoodsScale))
                .ToArray()
                .CopyTo(Goods.ItemCounts, 0);
            Goods.ItemCounts[( int ) Commodity.Scrap] += (int)Math.Floor(Goods.SegmentValue * (GoodsScale - 1));
        }
        if (PaymentScale > 1) {
            Payment.ItemCounts
                .Select(q=> ( int ) Math.Ceiling(q * PaymentScale))
                .ToArray()
                .CopyTo(Payment.ItemCounts, 0);
            Payment.ItemCounts[( int ) Commodity.Scrap] += (int)Math.Ceiling(Payment.SegmentValue * (PaymentScale - 1));
        }

        // cancel
        for (int i = 0; i < Payment.ItemCounts.Length; ++i) {
            var payCount = Payment.ItemCounts[i];
            var goodCount = Goods.ItemCounts[i];
            if (payCount > goodCount && goodCount > 0) {
                Payment.ItemCounts[i] = goodCount;
                Goods.ItemCounts[i] = 0;
            } else if (payCount < goodCount && payCount > 0) {
                Goods.ItemCounts[i] = payCount;
                Payment.ItemCounts[i] = 0;
            }
        }

        ////////////////////////
        return this;
        ////////////////////////
        double GetScale(double value) {
            double result = Math.Max(1, minTradeCost / value);

            return result;
        }
    }
}

public static partial class CrawlerEx {
    public static TradeOffer NewOffer_SellSegment(Inventory Crawler, double gain) {
        var selectedSegment = Crawler.Segments.ChooseRandom();
        if (selectedSegment is not null) {
            return new TradeOffer(selectedSegment, Commodity.Scrap, gain);
        } else {
            return new TradeOffer(Commodity.Scrap, 0, gain);
        }
    }
}

/*
public abstract record TradeItem(bool Hidden = false) {
    public abstract double Cost(Location Location);
}
public record FungibleTrade(Commodity Type, int Count, bool Hidden = false): TradeItem(Hidden) {
    public override string ToString() => Type is Commodity.Scrap ? $"{Count}¢" : $"{Type} x{Count}" + (Hidden ? " (Hidden)" : "");
    public override double Cost(Location Location) => CommodityCost(Type, Location) * Count;
    public static double CommodityCost(Commodity comm, Location Location) {
        double t = ( double ) Location.Terrain / (double)SimpleTerrainType.Shattered;
        double cost = Math.Pow(1.5, t/2);

        return comm switch {
            Commodity.Scrap => 1,
            Commodity.Fuel => 3 / cost,
            Commodity.Food => 2 * cost,
            Commodity.Crew => 10 * cost,
            Commodity.Morale => 20 * cost * cost, // I guess folks just fucking hate fractured terrain
            _ => 0,
        };
    }
}
public record SegmentTrade(Segment Segment, bool Hidden = false): TradeItem(Hidden) {
    public override string ToString() => $"{Segment}";
    public override double Cost(Location Location) => Segment.Cost;
}

public record TradeOffer(TradeItem[] Give, TradeItem[] Take) {
    public override string ToString() {
        bool isBuy = false;
        bool isSell = false;
        foreach (var item in Give) {
            if (item is FungibleTrade f && f.Type == Commodity.Scrap) {
                isBuy = true;
                break;
            }
        }
        foreach (var item in Take) {
            if (item is FungibleTrade f && f.Type == Commodity.Scrap) {
                isSell = true;
                break;
            }
        }
        var takeString = string.Join(", ", Take.Where(x => !x.Hidden).Select(x => x.ToString()));
        var giveString = string.Join(", ", Give.Where(x => !x.Hidden).Select(x => x.ToString()));
        if (isBuy) {
            return $"Buy {takeString} for {giveString}";
        } else if (isSell) {
            return $"Sell {giveString} for {takeString}";
        } else {
            return $"Trade your {takeString} for {giveString}";
        }
    }
    // Adjust the offer counts
    public void Balance(double returnRate) {
        if (IsBuy) {
            // We are giving money, so scale the Give counts to match the expected value.
        } else if (IsSell) {
            // We are taking money, so scale the Take counts to match the expected value.
        } else {
            // Trade
        }
    }
    public bool IsBuy => Give.All(x => x is FungibleTrade f && f.Type == Commodity.Scrap);
    public bool IsSell => Take.All(x => x is FungibleTrade f && f.Type == Commodity.Scrap);

    public static TradeOffer DefaultTradeOffer(Location Location, int Wealth) {
        return new TradeOffer(new TradeItem[] { }, new TradeItem[] { });
    }

    // The GeneralMerchant will buy and sell all kinds of fungibles and segments for scrap.
    public static TradeOffer GeneralMerchant(Location Location, int Wealth) {
        TradeOffer baseOffer = DefaultTradeOffer(Location, Wealth);
        var result = new TradeOffer(new TradeItem[] { }, new TradeItem[] { });
        return baseOffer.;
    }

    /*
    // The mechanic sells all kinds of segments
    static TradeOffer Mechanic(Location Location, int Wealth) { }

    // The Leg Man  buys and sells traction segments for scrap and fungibles
    static TradeOffer LegMan(Location Location, int Wealth) { }
    // The Gunsmith buys and sells weapons segments for scrap and fungibles
    static TradeOffer Gunsmith(Location Location, int Wealth) { }
    // The Engineer buys and sells power segments for scrap and fungibles
    static TradeOffer Engineer(Location Location, int Wealth) { }
    // The Miner buys and sells defense segments for scrap and fungibles
    static TradeOffer Miner(Location Location, int Wealth) { }

    // The farm sells food for scrap, fungibles, and segments
    static TradeOffer Farm(Location Location, int Wealth) { }
    // The mine sells fuel for scrap, fungibles, and segments
    static TradeOffer Mine(Location Location, int Wealth) { }
    // Settlements buy and sell crew for scrap, fungibles, and segments
    static TradeOffer Settlement(Location Location, int Wealth) { }
    // Circuses sell morale
    static TradeOffer Circus(Location Location, int Wealth) { }
    // Slavers sell crew with a morale cost
    static TradeOffer Slaver(Location Location, int Wealth) { }
}

public interface IInventory {
    public abstract void Open();

    public abstract int AvailFungible(Commodity commodity);
    public abstract IEnumerable<Segment> AvailSegments { get; }

    public abstract void Add(TradeItem item);
    public abstract bool CanRemove(TradeItem item);
    public abstract void Remove(TradeItem item);
    public abstract bool Commit();
    public abstract void Abort();
}

public abstract class TradeInventory {
    public TradeInventory(IInventory Player, IInventory Trader) {
        this.Player = Player;
        this.Trader = Trader;
        // Build some trade offers from the trader inventory

    }
    public IInventory Player { get; private set; }
    public IInventory Trader { get; private set; }
    public List<TradeOffer> Offers = new();
    public List<MenuItem> MenuItems() {
        return new();
    }

    int FindOffer(TradeOffer offer) {
        for (int offerIndex = 0; offerIndex < Offers.Count; ++offerIndex) {
            if (TradeOfferEquals.Instance.Equals(Offers[offerIndex], offer)) {
                return offerIndex;
            }
        }
        return -1;
    }
    public bool CanTrade(TradeOffer offer) {
        int OfferIndex = FindOffer(offer);
        if (OfferIndex < 0) {
            return false;
        }
        bool success = true;
        foreach (var item in offer.Give) {
            success &= Player.CanRemove(item);
        }
        foreach (var item in offer.Take) {
            success &= Trader.CanRemove(item);
        }
        return success;
    }
    public bool Trade(TradeOffer offer) {
        Player.Open();
        Trader.Open();
        if (!CanTrade(offer)) {
            Player.Abort();
            Trader.Abort();
            return false;
        }
        bool success = true;
        try {
            foreach (var item in offer.Give) {
                Player.Add(item);
                Trader.Remove(item);
            }
            foreach (var item in offer.Take) {
                Trader.Add(item);
                Player.Remove(item);
            }
            Offers.RemoveAt(FindOffer(offer));
        } catch (Exception) {
            success = false;
        }
        if (success) {
            success &= Player.Commit();
            success &= Trader.Commit();
        } else {
            Player.Abort();
            Trader.Abort();
        }
        return success;
    }
}


public sealed class TradeOfferEquals: IEqualityComparer<TradeOffer> {
    public static readonly TradeOfferEquals Instance = new();
    private TradeOfferEquals() { }
    public bool Equals(TradeOffer? x, TradeOffer? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return x.Give.SequenceEqual(y.Give) && x.Take.SequenceEqual(y.Take);
    }

    public int GetHashCode(TradeOffer obj) {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        return CrawlerEx.CombineFast(
            obj.Give.AsEnumerable().SequenceHashCode(),
            obj.Take.AsEnumerable().SequenceHashCode());
    }
}
*/
        /*
        Commodity comm = CrawlerEx.ChooseRandom<Commodity>();
        TradeType trade = CrawlerEx.ChooseRandom<TradeType>();
        int numTrades = ( int ) 3.Roll(2.7) + 2;
        int numBuys = 0;
        int numSells = 0;
        int numRand = (int)(numTrades * Random.Shared.NextDouble() * 0.5); // up to half random
        numTrades -= numRand;
        double baseReturn = 1.5;
        double tradeReturn = baseReturn;
        switch (trade) {
        case TradeType.Dealer:
            Location = Location with {
                Name = "Trade",
                Desc = $"A {comm} trade crawler crosses your path.",
            };
            tradeReturn = baseReturn;
            numBuys = numTrades / 2;
            numSells = numTrades - numBuys;
            break;
        case TradeType.Surplus:
            Location = Location with {
                Name = "Trade",
                Desc = $"A community has a surplus of {comm}.",
            };
            numSells = numTrades * 2 / 3;
            numRand = numTrades - numSells;
            tradeReturn = 1.25;
            break;
        case TradeType.Shortage:
            Location = Location with {
                Name = "Trade",
                Desc = $"A community has a shortage of {comm}.",
            };
            numBuys = numTrades * 2 / 3;
            numRand = numTrades - numBuys;
            tradeReturn = 0.75; // trader buys at a loss
            break;
        }
        int Index = 0;
        // BUY  (you <-[:comm]- trader)
        // you buy something from the trader using the commodity
        List<Commodity> sold = new();
        for (int buy = 0; buy < numBuys; ++buy) {
            if (buy == 0) {
                MenuItems.Add(MenuItem.Sep);
            }
            Commodity toSell = CrawlerEx.ChooseRandom<Commodity>();
            while (toSell == comm || sold.Contains(toSell)) {
                toSell = CrawlerEx.ChooseRandom<Commodity>();
            }
            sold.Add(toSell);
            MenuItems.Add(GenerateTrade(++Index, comm, tradeReturn, toSell, 1));
        }
        // SELL  (you -[:comm]-> trader)
        // you sell something to the trader using the commodity
        List<Commodity> bought = new();
        for (int sell = 0; sell < numSells; ++sell) {
            if (sell == 0) {
                MenuItems.Add(MenuItem.Sep);
            }
            Commodity toBuy = CrawlerEx.ChooseRandom<Commodity>();
            while (toBuy == comm || bought.Contains(toBuy) || sold.Contains(toBuy)) {
                toBuy = CrawlerEx.ChooseRandom<Commodity>();
            }
            bought.Add(toBuy);
            MenuItems.Add(GenerateTrade(++Index, toBuy, 1, comm, tradeReturn));
        }
        // RANDOM
        // no bonus to trade / random commodities
        int tries = 100;
        for (int misc = 0; misc < numRand && tries > 0; ++misc) {
            if (misc == 0) {
                MenuItems.Add(MenuItem.Sep);
            }
            Commodity toBuy = CrawlerEx.ChooseRandom<Commodity>();
            Commodity toSell = CrawlerEx.ChooseRandom<Commodity>();
            while (toBuy == comm || toSell == comm || toBuy == toSell || bought.Contains(toSell) || sold.Contains(toBuy)) {
                toBuy = CrawlerEx.ChooseRandom<Commodity>();
                toSell = CrawlerEx.ChooseRandom<Commodity>();
                if (--tries <= 0) {
                    break;
                }
            }
            MenuItems.Add(GenerateTrade(++Index, toBuy, baseReturn, toSell, 1));
        }
    public static MenuItem GenerateTrade(int Index, Commodity a, double aReturn, Commodity b, double bReturn) {
        int crawlerNo = 0;
        int tradeNo = 0;
        var costA = Cost(a) * aReturn;
        var costB = Cost(b) * bReturn;
        Segment? segA;
        Segment? segB;
        if (a > Commodity.LastFungible) {
            segA = Segment.RandomByCommodity(a);
            costA = segA.Cost * aReturn;
        } else {
            segA = null;
        }
        if (b > Commodity.LastFungible) {
            segB = Segment.RandomByCommodity(b);
            costB = segB.Cost * bReturn;
        } else {
            segB = null;
        }

        double Ratio = costA / costB;
        double MinSale = 50;
        if (Ratio > 1) { // If your item is more expensive than the traders
            int mn = Math.Clamp((int)(MinSale / costA), 1, 5);
            crawlerNo = mn;
            tradeNo = (int)Math.Ceiling(mn * Ratio);
        } else {
            int mn = Math.Clamp((int)(MinSale / costB), 1, 5);
            crawlerNo = (int)Math.Floor(mn / Ratio);
            tradeNo = mn;
        }

        void DoTrade(string option, string arg) {

        }

        return new MenuItem($"T{Index}", $"Trade {crawlerNo} {a} for {tradeNo} {b}", DoTrade);
    }
        */
