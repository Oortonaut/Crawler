namespace Crawler;

public enum Faction {
    Player, // eg a Player crawler
    Bandit, // a Bandit crawler
    Trade,
    //Hero,
    //Mercenary,
    //Civilian,
    //World,
}
/*
public class FactionToFaction {
    public delegate IEnumerable<MenuItem> MenuItemFunc(IActor agent, IActor subject);
    MenuItemFunc? menuItemsFor = null;
    public IEnumerable<MenuItem> MenuItemsFor(IActor agent, IActor subject) => menuItemsFor?.Invoke(agent, subject) ?? [];
    public void SetMenuFunc(MenuItemFunc func) => menuItemsFor = func;
    public CrawlerToCrawler DefaultCrawlerRelation { get; set; } = new();

    public static FactionToFaction Get(Faction from, Faction to) {
        if (!factionToFaction.TryGetValue((from, to), out FactionToFaction factionFaction)) {
            factionFaction = new FactionToFaction();
            factionToFaction.Add((from, to), factionFaction);
        }
        return factionFaction;
    }

    static Dictionary<(Faction, Faction), FactionToFaction> factionToFaction = new ();
}

public class FactionAndFaction {
    public static FactionAndFaction Get(Faction a, Faction b) {
        if (a > b) {
            (a, b) = (b, a);
        }
        if (!factionAndFaction.TryGetValue((a, b), out var factionFaction)) {
            factionFaction = new();
            factionAndFaction.Add((a, b), factionFaction);
        }
        return factionFaction;
    }
    static Dictionary<(Faction, Faction), FactionAndFaction> factionAndFaction = new();
}

public static class FactionEx {
    public static FactionToFaction To(this Faction from, Faction to) => FactionToFaction.Get(from, to);
    public static FactionAndFaction And(this Faction a, Faction b) => FactionAndFaction.Get(a, b);
    public static IEnumerable<MenuItem> FactionMenuItems(this IActor from, IActor to) {
        var f2f = from.Faction.To(to.Faction);

        return from.Faction.To(to.Faction).MenuItemsFor(from, to);
    }
}
*/
