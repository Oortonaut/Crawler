namespace Crawler;

public static class Names {
    // Substellar point
    public static string[] StormSettlementNames = [
        "Tempest Gate", "Stormhold", "Cyclonus", "Maelstrom Point", "Thunderfall", "Gale Spire", "Eyehaven", "Vortex Reach", "Typhon Ridge", "Squall's End", "Zephyr Crown", "Monsoon Port", "Hurricane Rest", "Tornado Vale", "Stormbreak", "Lightning Steppe", "Thunderhead", "Spiral Bastion", "Sirocco Keep", "Downburst Station"
    ];

    // Day side
    public static string[] DaySettlementNames = [
        "Aurora", "Solenne", "Flarehold", "Daywatch", "Solaris", "Lucidia", "Emberfast", "Phoebis", "Radiantum", "Pyrrhos", "Heliorum", "Ashmere", "Zenithon", "Caloris", "Brightmarch", "Ignis", "Dawnspire", "Cindralith", "Solforge", "Goldenreach", "Flamora", "Sunvale", "Scorchhaven", "Lumenna", "Pyraxis", "Solith", "Auremax", "Glareford", "Thermis", "Clarion", "Sunhaven", "Brillara", "Heliotropis", "Firesend", "Daycrest", "Sunspear", "Ignera", "Luxor", "Fulgora", "Solstice", "Pyronis", "Brighthelm", "Vulcanis", "Emberwatch", "Torchspire", "Cindrith", "Radiara", "Lightmere", "Dayport", "Luxmere"
    ];

    // Rim side
    public static string[] DuskSettlementNames = [
        "Evenfall", "Duskreach", "Gloamstead", "Penumbria", "Umbravale", "Crepuscis", "Shadeward", "Emberline", "Horizon’s Rest", "Halfhaven", "Edgewatch", "Twilighton", "Midvale", "Gloaminge", "Sundermarch", "Borderfall", "Meridian", "Equinoxia", "Faintlight", "Vergehold", "Duskmire", "Shadowmarch", "Halfmoor", "Balancia", "Crevus", "Halcyon Verge", "Eventide", "Dimlight", "Graymarch", "Horizonhold", "Dämmerung", "Noctivale", "Soluntra", "Duskenford", "Shadewater", "Evenfort", "Crestaline", "Liminalis", "Duskspire", "Tempora", "Luminara", "Perilune", "Crossvale", "Vespertide", "Umbryss", "Dawnhollow", "Nightspire", "Equilibria", "Shademeet", "Bivium"
    ];

    // Night side
    public static string[] NightSettlementNames = [
        "Umbrahold", "Nyxia", "Tenebris", "Nocturne", "Blackreach", "Frostvale", "Obscura", "Chiaroscuro", "Gloomspire", "Hadeshold", "Erebon", "Darkmere", "Vantabluff", "Moros", "Noxford", "Cryopolis", "Coldharrow", "Umbracrest", "Penumbrum", "Erebith", "Frigidum", "Nightmarch", "Murkendown", "Frostmarch", "Caligora", "Nihilis", "Noctis", "Shiverfast", "Netherholt", "Cryptvale", "Gloombreak", "Wintermere", "Stygis", "Moritura", "Frozenreach", "Umbrathorn", "Nightrise", "Frostspire", "Hiberna", "Glacialis", "Cimmeris", "Darkmoor", "Borealis", "Niflheim", "Hallowdeep", "Murkhold", "Cryogate", "Voidmere", "Necros", "Blackice"
    ];

    // Anywhere
    public static string[] ClassicSettlementNames = [
        "Alexandria Nova", "Babylonis", "Urukmar", "Thebeson", "Carthagis", "Byzantor", "Delphi Prime", "Troiath", "Athenara", "Ephesmere", "Persepolis II", "Mycenaeon", "Lugdunum", "Tarraco", "Carthara", "Ctesiphon", "Ninevale", "Memphisca", "Nivaria", "Sybaris", "Parnassum", "Roma Ultima", "Antiochra", "Corinthis", "Petra Nova", "Babylonica", "Saguntum", "Palmyraxis", "Heliopolis", "Tarsora", "Tyros", "Gades", "Sidonum", "Damascra", "Alephkara", "Larsa-Port", "Kishmar", "Akkadion", "Urbarra", "Mariath", "Jericho Vale", "Luxorion", "Sparta’s Gate", "Delphi Verge", "Pergamos", "Sardisia", "Byzantis", "Troadae", "Knossoson", "Rhodos", "Thassara", "Mileth"
    ];

    static string[]? _humanNames = null;
    static string[] HumanNames {
        get {
            if (_humanNames == null) {
                _humanNames = CrawlerEx.DataPath.Read("human_names.txt").ReadAllLines().ToArray();
            }
            return _humanNames;
        }
    }
    public static string AHumanName() {
        var result = HumanNames.ChooseRandom();
        var uFirst = char.ToUpperInvariant(result![0]);
        return uFirst + result[1..];
    }
    public static string HumanName() {
        int t = Random.Shared.Next(0, 100);
        if (t < 80) {
            return AHumanName() + " " + AHumanName();
        } else if (t < 95) {
            return AHumanName() + "-" + HumanNames.ChooseRandom();
        } else {
            return AHumanName();
        }
    }
    public static string MakeFactionName(this string crawlerName) {
        List<string> options = new List<string>() {
            $"Kingdom of {crawlerName}",
            $"Clan of {crawlerName}",
            $"Alliance of {crawlerName}",
            $"Federation of {crawlerName}",
            $"{crawlerName}'s Bane",
            $"{crawlerName}'s Heart",
            $"{crawlerName}'s Alliance",
            $"{crawlerName}'s Fury",
        };
        return options.ChooseRandom()!;
    }
    public static string MakeCapitalName(this string settlementName) {
        List<string> options = new List<string>() {
            $"{settlementName}",
            $"Grand {settlementName}",
            $"New {settlementName}",
            $"Second {settlementName}",
            $"Upper {settlementName}",
            $"Lower {settlementName}",
            $"{settlementName} Nouveau",
            $"{settlementName} Camp",
            $"Fort {settlementName}",
        };
        return options.ChooseRandom()!;
    }
}
