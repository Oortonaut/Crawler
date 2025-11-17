using System.Drawing;

namespace Crawler;

public enum Faction {
    Player, // eg a Player crawler
    Bandit, // a Bandit crawler
    Independent, // Traveling merchants

    // Civilian factions - regional powers controlling sectors
    // Generated during map creation based on highest population settlements
    Civilian0,
    Civilian1,
    Civilian2,
    Civilian3,
    Civilian4,
    Civilian5,
    Civilian6,
    Civilian7,
    Civilian8,
    Civilian9,
    Civilian10,
    Civilian11,
    Civilian12,
    Civilian13,
    Civilian14,
    Civilian15,
    Civilian16,
    Civilian17,
    Civilian18,
    Civilian19,

    //Hero,
    //Mercenary,
    //World,
    //Creature,
}
public class FactionToFaction {
    public ActorToActor DefaultCrawlerRelation { get; set; } = new();

    public static FactionToFaction Get(Faction from, Faction to) => factionToFaction.GetOrAddNew((from, to));

    static Dictionary<(Faction, Faction), FactionToFaction> factionToFaction = new();
}
public class FactionAndFaction {
    public static FactionAndFaction Get(Faction a, Faction b) {
        if (a > b) {
            (a, b) = (b, a);
        }
        return factionAndFaction.GetOrAddNew((a, b));
    }
    static Dictionary<(Faction, Faction), FactionAndFaction> factionAndFaction = new();
}
public static partial class FactionEx {
    public static FactionToFaction To(this Faction from, Faction to) => FactionToFaction.Get(from, to);
    public static FactionAndFaction And(this Faction a, Faction b) => FactionAndFaction.Get(a, b);
    public static bool IsCivilian(this Faction faction) =>
        faction >= Faction.Civilian0 && faction <= Faction.Civilian19;

    public static int CivilianIndex(this Faction faction) =>
        faction.IsCivilian() ? (int)faction - (int)Faction.Civilian0 : -1;

    public static Faction FromCivilianIndex(int index) =>
        index >= 0 && index <= 19 ? (Faction)((int)Faction.Civilian0 + index) : Faction.Independent;

    public static Color GetColor(this Faction faction) =>
        _factionColors[faction];
    public static string Name(this Faction faction) => faction.GetData()?.Name ?? faction.ToString();
    public static Crawler? Capital(this Faction faction) => faction.GetData()?.Capital?.Settlement;
    public static FactionData? GetData(this Faction faction) => Game.Instance?.Map.FactionData[faction];

    public static EArray<Faction, Color> _factionColors = [
        Color.Red,
        Color.Blue,
        Color.White,
        Color.LightGreen,
        Color.MediumPurple,
        Color.Coral,
        Color.Yellow,
        Color.Pink,
        Color.Orange,
        Color.PaleVioletRed,
        Color.SteelBlue,
        Color.Khaki,
        Color.DarkSeaGreen,
        Color.BlueViolet,
        Color.OrangeRed,
        Color.DarkGoldenrod,
        Color.CadetBlue,
        Color.Brown,
        Color.Gold,
        Color.Tomato,
        Color.BurlyWood,
        Color.Chocolate,
    ];

    private static readonly string[] _policyArchetypes = [
        "Authoritarian", "Libertarian", "Pious", "Debauched",
        "Industrial", "Mercantile", "Militaristic", "Isolationist",
        "Protectionist", "Technocratic", "Xenophobic", "Corporatist"
    ];

    private static void ApplyArchetypePolicy(int archetype,
        EArray<CommodityCategory, TradePolicy> commodityPolicy,
        EArray<SegmentKind, TradePolicy> segmentPolicy) {

        switch (archetype) {
        case 0: // Authoritarian
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Prohibited;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Prohibited;
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Controlled;
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Controlled;
            break;
        case 1: // Libertarian
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Taxed;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Subsidized;
            break;
        case 2: // Pious
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Prohibited;
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Controlled;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Taxed;
            break;
        case 3: // Debauched
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Taxed;
            break;
        case 4: // Industrial
            commodityPolicy[CommodityCategory.Raw] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Refined] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Taxed;
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Taxed;
            break;
        case 5: // Mercantile
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Refined] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Raw] = TradePolicy.Taxed;
            break;
        case 6: // Militaristic
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Controlled;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Taxed;
            break;
        case 7: // Isolationist
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Controlled;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Taxed;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Controlled;
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Controlled;
            break;
        case 8: // Protectionist
            commodityPolicy[CommodityCategory.Raw] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Refined] = TradePolicy.Taxed;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Prohibited;
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Restricted;
            break;
        case 9: // Technocratic
            commodityPolicy[CommodityCategory.Refined] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Taxed;
            break;
        case 10: // Xenophobic
            commodityPolicy[CommodityCategory.Dangerous] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Vice] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Restricted;
            segmentPolicy[SegmentKind.Offense] = TradePolicy.Restricted;
            break;
        case 11: // Corporatist
            commodityPolicy[CommodityCategory.Luxury] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Refined] = TradePolicy.Subsidized;
            commodityPolicy[CommodityCategory.Religious] = TradePolicy.Restricted;
            commodityPolicy[CommodityCategory.Raw] = TradePolicy.Taxed;
            break;
        }

    }
    public static IEnumerable<Policy> GenerateFactionPolicies(int N, ulong seed) {
        var rng = new XorShift(seed);
        float[] policyWeights = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]; // TODO: Use an enum for policies
        for (int i = 0; i < N; i++) {
            yield return GenerateFactionPolicy(policyWeights, rng.Seed());
        }
    }

    public static Policy GenerateFactionPolicy(float[] policyWeights, ulong seed) {
        var rng = new XorShift(seed);
        var commodityPolicy = Tuning.FactionPolicies.CreateCommodityDefaultPolicy(TradePolicy.Legal);
        var segmentPolicy = Tuning.FactionPolicies.CreateSegmentDefaultPolicy(TradePolicy.Legal);

        string description = "";
        int rolls = 3;
        for (int roll = 0; roll < rolls; roll++) {
            int index = policyWeights.Index().ChooseWeightedAt(rng.NextSingle());
            policyWeights[index] *= rng.NextSingle();
            if (description.Length > 0) {
                description += ", ";
            }
            description += _policyArchetypes[index];
            ApplyArchetypePolicy(index, commodityPolicy, segmentPolicy);
        }

        return new(commodityPolicy, segmentPolicy, description);
    }
}
