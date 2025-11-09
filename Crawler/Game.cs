using System.Drawing;
using System.Numerics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public class Game {

    static Game? _instance = null;
    public static Game? Instance => _instance;

    Map? _map = null;
    public Map Map => _map!;
    XorShift Rng;
    GaussianSampler Gaussian;

    public static Game NewGame(ulong seed, string crawlerName, int H) {
        using var activity = LogCat.Game.StartActivity($"NewGame for '{crawlerName}' with H={H}");

        var game = new Game(seed);
        game.Construct(crawlerName, H);
        return game;
    }
    public static Game LoadGame(string saveFilePath) {
        using var activity = LogCat.Game.StartActivity($"LoadGame for '{saveFilePath}'");
        var deserializer = new YamlDotNet.Serialization.Deserializer();
        using var reader = new StreamReader(saveFilePath);
        var saveData = deserializer.Deserialize<SaveGameData>(reader);

        var game = new Game(saveData.RngState);
        game.Construct(saveData);
        return game;
    }
    Game(ulong seed) {
        _instance = this;
        Rng = new XorShift(seed);

        StoredProposals = [
            new ProposeLootTake(Rng / 'L', "L"),
            new ProposeAttackDefend("A"),
            new ProposeAcceptSurrender(Rng / "S", "S"),
            new ProposeRepairBuy("R"),
            new ProposePlayerDemand(Rng/'D', 0.5f, "X"),
        ];
        Welcome();
    }
    protected void Welcome() {
        Console.Write(Style.None.Format() + CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
    }

    void Construct(string crawlerName, int H) {
        if (string.IsNullOrWhiteSpace(crawlerName)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(crawlerName));
        }
        Gaussian = new GaussianSampler(Rng.Seed());
        _map = new Map(Rng.Seed(), H, H * 5 / 2);
        var currentLocation = Map.GetStartingLocation();
        _player = currentLocation.GetEncounter().GeneratePlayerActor(Rng.Seed());
        float wealth = currentLocation.Wealth * 1.25f;
        int crew = 10;
        float goodsWealth = wealth;
        float segmentWealth = wealth * 0.5f;
        _player = Crawler.NewRandom(Rng.Seed(), Faction.Player, currentLocation, crew, 10, goodsWealth, segmentWealth, [1, 1, 1, 1]);
        _player.Name = crawlerName;
        _player.Flags |= EActorFlags.Player;
        currentLocation.GetEncounter().AddActor(_player);
    }
    void Construct(SaveGameData saveData) {
        Rng.SetState(saveData.RngState);
        Gaussian.SetRngState(saveData.GaussianRngState);
        Gaussian.SetPrimed(saveData.GaussianPrimed);
        Gaussian.SetZSin(saveData.GaussianZSin);

        TimeSeconds = saveData.Hour;
        quit = saveData.Quit;

        // Reconstruct the map from save data
        _map = saveData.Map.ToGameMap();

        // Find current location in the reconstructed map
        var currentLocation = Map.FindLocationByPosition(saveData.CurrentLocationPos);

        // Reconstruct player from save data
        _player = saveData.Player.ToGameCrawler(Map);
        _player.Location = currentLocation;

        // Add player to map
        currentLocation.GetEncounter().AddActor(Player);
    }

    bool quit = false;
    public void Schedule(Encounter encounter) {
        Schedule(encounter, Math.Max(TimeSeconds, encounter.NextEvent));
    }
    void Schedule(Encounter encounter, long nextTurn) {
        if (turnForEncounter.TryGetValue(encounter, out var scheduledTurn)) {
            if (nextTurn < scheduledTurn) {
                LogCat.Log.LogTrace($"Schedule {encounter.Name}: rescheduling to {nextTurn}");
                Unschedule(encounter);
            } else {
                LogCat.Log.LogTrace($"Schedule {encounter.Name}: already scheduled for {scheduledTurn}");
                return;
            }
        }
        encountersByTurn.GetOrAddNew(nextTurn).Add(encounter);
        turnForEncounter[encounter] = nextTurn;
    }
    void Unschedule(Encounter encounter) {
        if (turnForEncounter.TryGetValue(encounter, out var scheduledTurn)) {
            LogCat.Log.LogTrace($"Unschedule {encounter.Name}: {scheduledTurn}");
            var encounters = encountersByTurn[scheduledTurn];
            encounters.Remove(encounter);
            if (encounters.Count == 0) {
                encountersByTurn.Remove(scheduledTurn);
            }
            turnForEncounter.Remove(encounter);
        } else {
            LogCat.Log.LogTrace($"Unschedule {encounter.Name}: not scheduled");
        }
    }
    (long turn, List<Encounter>) TurnEncounters() {
        var kv = encountersByTurn.First();
        var turn = kv.Key;
        var result = kv.Value;
        encountersByTurn.Remove(turn);
        foreach (var encounter in result) {
            // TODO: use a 0 sentinel instead of removing
            turnForEncounter.Remove(encounter);
        }
        LogCat.Log.LogTrace($"Turn {turn}: {result.Count} encounters");
        return (turn, result);
    }
    SortedDictionary<long, List<Encounter>> encountersByTurn = new();
    Dictionary<Encounter, long> turnForEncounter = new();

    public void Run() {

        while (!quit && encountersByTurn.Any()) {
            var (turn, turnEncounters) = TurnEncounters();
            using var activity = LogCat.Encounter.StartActivity($"Game::Turn {turn}: {turnEncounters.Count} encounters");
            TimeSeconds = turn;
            foreach (var encounter in turnEncounters) {
                encounter.Tick(turn);
                Schedule(encounter);
            }

            if (PlayerWon || PlayerLost) {
                Save();
                break;
            }
        }
        if (PlayerLost) {
            Console.WriteLine($"You lost! {Player?.EndMessage}");
        } else if (PlayerWon) {
            Console.WriteLine("You won!");
        } else {
            Console.WriteLine("Game Over!");
        }

        Console.WriteLine("Thanks for playing!");
    }

    public Location PlayerLocation => _player!.Location;
    public TerrainType CurrentTerrain => PlayerLocation.Terrain;

    int Report() {
        Player.Message(Player.Report());
        return 0;
    }

    int SegmentDefinitionsReport() {
        IEnumerable<SegmentDef> Variants(SegmentDef def) {
            yield return def;
            yield return def.Resize(2);
            yield return def.Resize(3);
            yield return def.Resize(4);
            yield return def.Resize(5);
        }
        // Create a segment for each definition
        var rng = new XorShift(999);
        var segments = SegmentEx.AllDefs.SelectMany(def => Variants(def)).Select(def => def.NewSegment(rng.Seed())).ToList();
        Player.Message(segments.SegmentReport(PlayerLocation));
        return 0;
    }

    IEnumerable<MenuItem> EncounterMenuItems() {
        return PlayerEncounter().MenuItems(Player) ?? [];
    }

    IEnumerable<MenuItem> SectorMenuItems(ShowArg showOption = ShowArg.Hide) {
        var sector = PlayerLocation.Sector;

        bool isPinned = Player.Pinned();
        if (isPinned) {
            Player.Message("You are pinned.");
        }

        yield return MenuItem.Cancel;

        float fuel = -1, time = -1;

        int index = 0;
        foreach (var location in sector.Locations) {
            ++index;
            float dist = PlayerLocation.Distance(location);

            string locationName = location.EncounterName(Player);
            if (!location.HasEncounter || !Player.Visited(location)) {
                locationName = Style.MenuUnvisited.Format(locationName);
            }

            (fuel, time) = Player.FuelTimeTo(location);
            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            var enableArg = enabled ? EnableArg.Enabled : EnableArg.Disabled;
            yield return new ActionMenuItem($"M{index}", $"To {locationName} {dist:F0}km {time:F0}h {fuel:F1}FU", _ => GoTo(location), enableArg, showOption);
        }
    }
    int GoTo(Location loc) {
        Player.Travel(loc);
        return 0;
    }

    IEnumerable<MenuItem> GlobeMenuItems(ShowArg showOption = ShowArg.Hide) {
        var sector = PlayerLocation.Sector;

        yield return MenuItem.Cancel;

        bool isPinned = Player.Pinned();
        if (isPinned) {
            Player.Message("You are pinned.");
        }

        float fuel = -1, time = -1;

        foreach (var neighbor in sector.Neighbors) {
            if (!neighbor.Locations.Any()) {
                continue;
            }
            var locations = string.Join(", ", neighbor.Locations.Select(x => x.Type.ToString().Substring(0, 1)).Distinct());
            var destinations = neighbor.Settlements.ToList();
            if (destinations.Count == 0) {
                destinations = neighbor.Locations;
            }
            var location = destinations.MinBy(x => PlayerLocation.Distance(x))!;
            string dir = "N";
            var D = neighbor.Offset(sector);
            var DX = D.X;
            var DY = D.Y;
            if (DX < 0 && DY < 0) dir = "NW";
            if (DX < 0 && DY == 0) dir = "W";
            if (DX < 0 && DY > 0) dir = "SW";
            if (DX == 0 && DY < 0) dir = "N";
            if (DX == 0 && DY == 0) dir = "KK";
            if (DX == 0 && DY > 0) dir = "S";
            if (DX > 0 && DY < 0) dir = "NE";
            if (DX > 0 && DY == 0) dir = "E";
            if (DX > 0 && DY > 0) dir = "SE";

            (fuel, time) = Player.FuelTimeTo(location);

            string neighborName = $"{neighbor.Name} ({neighbor.Terrain})";
            int visits = Player.Visits(neighbor);
            if (!neighbor.Locations.Any()) {
                neighborName = Style.MenuEmpty.Format(neighborName);
            } else if (visits == 0) {
                neighborName = Style.MenuUnvisited.Format(neighborName);
            } else if (visits == neighbor.Locations.Count) {
                neighborName = Style.MenuVisited.Format(neighborName);
            } else {
                //
            }

            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            var enableArg = enabled ? EnableArg.Enabled : EnableArg.Disabled;
            if (fuel > 0) {
                yield return new ActionMenuItem(dir, $"to {neighborName} ({locations}) {time:F0}h Fuel: {fuel:F1}", _ => GoTo(location), enableArg, showOption);
            } else {
                yield return new ActionMenuItem(dir, $"to {neighborName} ({locations})", _ => GoTo(location), enableArg, showOption);
            }
        }
    }
    Encounter PlayerEncounter() => PlayerLocation.GetEncounter();

    public string DateString() {
        long seconds = TimeSeconds;
        long minutes = TimeSeconds / 60;
        long hours = minutes / 60;
        long days = hours / 24;
        long months = days / 30;
        long years = months / 12;
        seconds %= 60;
        minutes %= 60;
        hours %= 24;
        days %= 30;
        months %= 12;
        return $"{years:D4}/{months + 1:D2}/{days + 1:D2}";
    }

    public string TimeString() {
        long seconds = TimeSeconds;
        long minutes = TimeSeconds / 60;
        long hours = minutes / 60;
        seconds %= 60;
        minutes %= 60;
        hours %= 24;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    int Look() {
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(PlayerLocation.Sector.Look() + " " + PlayerLocation.PosString);

        Console.Write($"DATE {DateString()} TIME {TimeString()}   ");
        Console.WriteLine(PlayerEncounter().ViewFrom(Player));
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();
        return 0;
    }
    string DrawSectorMap() {
        var height = 10;
        var width = (5 * height) / 3;
        var sectorMapLines = Map.DumpSector(PlayerLocation.Sector.X, PlayerLocation.Sector.Y, width, height).Split('\n');
        var sectorMapWidth = sectorMapLines.Max(x => x.Length);
        var header = $"┌[Sector Map|{PlayerLocation.EncounterName(Player)}|{PlayerLocation.Terrain}]";
        header += new string('─', Math.Max(0, sectorMapWidth - header.Length + 1)) + "╖";
        var footer = $"╘{new string('═', sectorMapWidth)}╝";
        var mapLines = sectorMapLines.Select(line => $"│{line}║").StringJoin("\n");
        return $"{header}\n{mapLines}\n{footer}";
    }
    int Turn(string args) {
        float count = float.TryParse(args, out float parsed) ? parsed * 60 : 3600;
        return (int) count;
    }

    public bool IsMinute() => TimeSeconds % 60 == 0;
    public bool IsHour() => TimeSeconds % 3600 == 0;
    public bool IsDay() => TimeSeconds % 86400 == 0;


    public void RegisterEncounter(Encounter encounter) {
        allEncounters.Add(encounter);
        Schedule(encounter);
    }

    public IEnumerable<Encounter> Encounters() {
        return allEncounters;
    }

    bool PlayerWon => false;
    bool PlayerLost => !string.IsNullOrEmpty(Player?.EndMessage);

    // seconds -
    public long TimeSeconds { get; private set; } = 100_000_000_000; // appx 3168 years

    public static long SafeTime => Instance == null ? 0 : Instance.TimeSeconds;

    public MenuItem GameMenu() {
        List<MenuItem> items = [
            .. GameMenuItems(),
            .. PlayerMenuItems(),
            .. SectorMenuItems(),
            .. GlobeMenuItems(),
            .. EncounterMenuItems(),
            MenuItem.Sep,
            new MenuItem("", "Choose"),
        ];

        Look();

        var (selected, ap) = CrawlerEx.MenuRun("Game Menu", items.ToArray());
        return selected;
    }
    int SectorMap() {
        var sector = PlayerLocation.Sector;
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(DrawSectorMap());
        Console.WriteLine(sector.Look() + " " + PlayerLocation.PosString);
        Console.WriteLine(PlayerEncounter().ViewFrom(Player));
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();

        var (selected, ap) = CrawlerEx.MenuRun("Sector Map", [
            .. SectorMenuItems(ShowArg.Show),
        ]);
        return ap;
    }
    int WorldMap() {
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(Map.DumpMap(Player));
        Console.WriteLine(PlayerLocation.Sector.Look() + " " + PlayerLocation.PosString);
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();

        var (selected, ap) = CrawlerEx.MenuRun("Global Map", [
            .. GlobeMenuItems(ShowArg.Show),
        ]);
        return ap;
    }
    string _SectorMapName() {
        var sector = PlayerLocation.Sector;
        var sectorMapName = $"Sector {sector.Name} Map (";
        foreach (var location in sector.Locations) {
            var locationCode = Player.StyleFor(location).Format(location.Code);
            sectorMapName += locationCode;
        }
        sectorMapName += Style.MenuNormal.Format(")");
        return sectorMapName;
    }
    IEnumerable<MenuItem> GameMenuItems() => [
        new ActionMenuItem("M", _SectorMapName(), _ => SectorMap()),
        new ActionMenuItem("G", "Global Map", _ => WorldMap()),
        new ActionMenuItem("R", "Status Report", _ => Report()),
        new ActionMenuItem("K", "Skip Turn/Wait", args => Turn(args)),
        new ActionMenuItem("Q", "Save and Quit", _ => Save() + Quit()),
        new ActionMenuItem("QQ", "Quit ", _ => Quit()),
        new ActionMenuItem("TEST", "Test Menu", _ => TestMenu()),
        new ActionMenuItem("GG", "", args => {
            Player.ScrapInv += 1000;
            return 0;
        }, EnableArg.Enabled, ShowArg.Hide),
        new ActionMenuItem("GIVE", "", args => GiveCommodity(args), EnableArg.Enabled, ShowArg.Hide),
    ];
    IEnumerable<MenuItem> PlayerMenuItems() {
        yield return MenuItem.Sep;
        yield return new MenuItem("", Style.MenuTitle.Format("Player Menu"));
        yield return new ActionMenuItem("PP", "Power...", _ => PowerMenu());
        yield return new ActionMenuItem("PK", "Packaging...", _ => PackagingMenu());
        yield return new ActionMenuItem("PT", "Trade Cargo...", _ => TradeInventoryMenu());

        foreach (var item in PowerMenuItems(ShowArg.Hide)) {
            yield return item;
        }
        foreach (var item in PackagingMenuItems(ShowArg.Hide)) {
            yield return item;
        }
        foreach (var item in TradeInventoryMenuItems(ShowArg.Hide)) {
            yield return item;
        }
    }

    int PowerMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Power Menu", [
            MenuItem.Cancel,
            .. PowerMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> PowerMenuItems(ShowArg show = ShowArg.Show) {
        var segments = Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;

            string toggleLabel = segment.Activated ? "Deactivate" : "Activate";
            yield return new ActionMenuItem($"PP{index + 1}", $"{segment.StateName} - {toggleLabel}", _ => ToggleSegmentPower(index), segment.IsUsable.Enable(), show);
        }
    }

    int ToggleSegmentPower(int index) {
        var segment = Player.Segments[index];
        segment.Activated = !segment.Activated;
        Player.Message($"{segment.Name} {(segment.Activated ? "activated" : "deactivated")}");
        Player.UpdateSegmentCache();
        return 0;
    }

    int PackagingMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Packaging Menu", [
            MenuItem.Cancel,
            .. PackagingMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> PackagingMenuItems(ShowArg show = ShowArg.Show) {
        var segments= Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;

            string packageLabel = segment.Packaged ? "Unpackage" : "Package";
            string label = $"{packageLabel} {segment.Name}";
            bool canPackage = segment.Packaged || segment.IsPristine;
            if (!canPackage) {
                label += " (Damaged)";
            }
            yield return new ActionMenuItem($"PK{index + 1}", label, _ => TogglePackage(index), canPackage.Enable(), show);
        }
    }

    int TogglePackage(int index) {
        var segment = Player.Segments[index];
        segment.Packaged = !segment.Packaged;
        Player.Message($"{segment.Name} {(segment.Packaged ? "packaged" : "unpackaged")}");
        Player.UpdateSegmentCache();
        return 0;
    }

    int TradeInventoryMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Trade Cargo Menu", [
            MenuItem.Cancel,
            .. TradeInventoryMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> TradeInventoryMenuItems(ShowArg showOption = ShowArg.Show) {
        // Show packaged segments in supplies that can be moved to cargo
        var packagedSegments = Player.Supplies.Segments.Where(s => s.IsPackaged).ToList();

        for (int i = 0; i < packagedSegments.Count; i++) {
            var segment = packagedSegments[i];
            yield return new ActionMenuItem($"PT{i + 1}M", $"{segment.StateName} to Cargo", _ => MoveToCargo(segment), EnableArg.Enabled, showOption);
        }

        // Show segments in cargo that can be moved back to supplies
        var tradeSegments = Player.Cargo.Segments.ToList();
        for (int i = 0; i < tradeSegments.Count; i++) {
            var segment = tradeSegments[i];
            int index = i;
            yield return new ActionMenuItem($"PT{i + 1}R", $"{segment.StateName} to Supplies", _ => MoveFromCargo(segment), EnableArg.Enabled, showOption);
        }
    }

    int MoveToCargo(Segment segment) {
        Player.Supplies.Remove(segment);
        Player.Cargo.Add(segment);
        Player.Message($"{segment.Name} moved to cargo");
        Player.UpdateSegmentCache();
        return 0;
    }

    int MoveFromCargo(Segment segment) {
        Player.Cargo.Remove(segment);
        Player.Supplies.Add(segment);
        Player.Message($"{segment.Name} returned from cargo");
        Player.UpdateSegmentCache();
        return 0;
    }

    int Save() {
        try {
            var serializer = new YamlDotNet.Serialization.Serializer();
            var saveData = this.ToSaveData();

            string saveDirectory = CrawlerEx.SavesPath;
            Directory.CreateDirectory(saveDirectory);

            string fileName = $"{Player.Name}.crawler.yaml";
            string filePath = Path.Combine(saveDirectory, fileName);

            using var writer = new StreamWriter(filePath);
            serializer.Serialize(writer, saveData);

            Console.WriteLine($"Game saved to: {filePath}");
        } catch (Exception ex) {
            Console.WriteLine($"Failed to save game: {ex.Message}");
        }
        return 0;
    }

    int Quit() {
        Console.WriteLine("Quitting...");
        quit = true;
        return 0;
    }

    int GiveCommodity(string args) {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            Player.Message("Usage: give <commodity> <amount>");
            return 0;
        }

        string commodityPrefix = parts[0].ToLower();
        if (!float.TryParse(parts[1], out float amount)) {
            Player.Message($"Invalid amount: {parts[1]}");
            return 0;
        }

        // Find first commodity that starts with the input
        var matchingCommodity = Enum.GetValues<Commodity>()
            .FirstOrDefault(c => c.ToString().ToLower().StartsWith(commodityPrefix));

        if (matchingCommodity == default(Commodity) && !commodityPrefix.StartsWith("scrap")) {
            Player.Message($"No commodity found starting with '{parts[0]}'");
            return 0;
        }

        Player.Supplies[matchingCommodity] += amount;
        Player.Message($"Added {amount} {matchingCommodity}");
        return 0;
    }

    int TestMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Test Menu", [
            MenuItem.Cancel,
            new ActionMenuItem("SD", "Segment Definitions", _ => SegmentDefinitionsReport()),
            new ActionMenuItem("PS", "Price Statistics", _ => PriceStatisticsReport()),
        ]);
        return ap;
    }

    int PriceStatisticsReport() {
        XorShift rng = new XorShift(1);
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine("Surveying all locations for price statistics...");
        Console.WriteLine("\nPrice Factors Analysis (NEW BID-ASK MODEL):");
        Console.WriteLine("MidPrice = BaseValue * LocationMarkup * ScarcityPremium * RestrictedMarkup");
        Console.WriteLine("Spread = MidPrice * BaseBidAskSpread * FactionMultiplier");
        Console.WriteLine("AskPrice (Buy) = MidPrice + Spread/2");
        Console.WriteLine("BidPrice (Sell) = MidPrice - Spread/2");
        Console.WriteLine();
        Console.WriteLine("Factors:");
        Console.WriteLine("- LocationMarkup: EncounterMarkup * TerrainMarkup * ScrapInflation");
        Console.WriteLine("- ScarcityPremium: 1 + (1-Availability) * Weight (Essential=0.3x, Luxury=1.5x)");
        Console.WriteLine("- BaseBidAskSpread: 20% base");
        Console.WriteLine("- FactionMultiplier: Trade=0.8±0.05 (16% spread), Bandit=1.5±0.1 (30% spread)");
        Console.WriteLine("- Result: Symmetric variance, location patterns preserved\n");

        // Dictionary to store buy/sell prices for each commodity
        var buyPrices = new Dictionary<Commodity, List<float>>();
        var sellPrices = new Dictionary<Commodity, List<float>>();
        var baseValues = new Dictionary<Commodity, float>();
        var tradeTraders = new Dictionary<Commodity, int>();
        var banditTraders = new Dictionary<Commodity, int>();

        // Initialize the dictionaries and track base values
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            buyPrices[commodity] = new List<float>();
            sellPrices[commodity] = new List<float>();
            baseValues[commodity] = commodity.BaseCost();
            tradeTraders[commodity] = 0;
            banditTraders[commodity] = 0;
        }

        int locationsProcessed = 0;
        int crawlersProcessed = 0;
        int totalTradeTraders = 0;
        int totalBanditTraders = 0;

        // Survey every location on the map
        for (int y = 0; y < Map.Height; y++) {
            for (int x = 0; x < Map.Width; x++) {
                var sector = Map.GetSector(x, y);
                foreach (var location in sector.Locations) {
                    locationsProcessed++;

                    // Instantiate the encounter
                    var encounter = location.GetEncounter();

                    // Gather statistics from any crawler's trade proposals
                    foreach (var crawler in encounter.Actors.OfType<Crawler>()) {
                        crawlersProcessed++;
                        var tradeProposals = crawler.MakeTradeProposals(Rng.Seed(), 1.0f);

                        // Track which commodities this trader offers
                        var offeredCommodities = new HashSet<Commodity>();

                        foreach (var proposal in tradeProposals.OfType<ProposeSellBuy>()) {
                            // Crawler is selling to player (player buys)
                            if (proposal.Stuff is CommodityOffer commodityOffer) {
                                float pricePerUnit = proposal.Cash / commodityOffer.Amount;
                                buyPrices[commodityOffer.commodity].Add(pricePerUnit);
                                offeredCommodities.Add(commodityOffer.commodity);
                            }
                        }

                        foreach (var proposal in tradeProposals.OfType<ProposeBuySell>()) {
                            // Crawler is buying from player (player sells)
                            if (proposal.Stuff is CommodityOffer commodityOffer) {
                                float pricePerUnit = proposal.Cash / commodityOffer.Amount;
                                sellPrices[commodityOffer.commodity].Add(pricePerUnit);
                                offeredCommodities.Add(commodityOffer.commodity);
                            }
                        }

                        // Increment trader count per commodity
                        foreach (var commodity in offeredCommodities.Distinct()) {
                            if (crawler.Faction == Faction.Independent) {
                                tradeTraders[commodity]++;
                            } else if (crawler.Faction == Faction.Bandit) {
                                banditTraders[commodity]++;
                            }
                        }

                        if (offeredCommodities.Count > 0) {
                            if (crawler.Faction == Faction.Independent) {
                                totalTradeTraders++;
                            } else if (crawler.Faction == Faction.Bandit) {
                                totalBanditTraders++;
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"Processed {locationsProcessed} locations and {crawlersProcessed} crawlers\n");
        Console.WriteLine("Price Statistics (from Player Perspective):\n");
        Console.WriteLine($"{"Commodity",-15} {"Base",8} {$"T%{totalTradeTraders}",5} {$"B%{totalBanditTraders}",5} {"Buy Mean",10} {"Buy SD",10} {"Buy Max",10} {"Buy Min",10} . {"Sell Max",10} {"Sell Min",10} {"Sell SD",10} {"Sell Mean",10}");
        Console.WriteLine(new string('-', 137));

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity == Commodity.Scrap) continue; // Skip scrap as it's the currency

            var buyList = buyPrices[commodity];
            var sellList = sellPrices[commodity];

            if (buyList.Count > 0 || sellList.Count > 0) {
                string baseVal = $"{baseValues[commodity]:F1}";
                int tradeCount = tradeTraders[commodity];
                int banditCount = banditTraders[commodity];

                string tradePercent = totalTradeTraders > 0 ? $"{(100.0 * tradeCount / totalTradeTraders):F0}%" : "N/A";
                string banditPercent = totalBanditTraders > 0 ? $"{(100.0 * banditCount / totalBanditTraders):F0}%" : "N/A";

                string buyMin = buyList.Count > 0 ? $"{buyList.Min():F1}" : "N/A";
                string buyMax = buyList.Count > 0 ? $"{buyList.Max():F1}" : "N/A";
                string buyMean = buyList.Count > 0 ? $"{buyList.Average():F1}" : "N/A";
                string buySD = buyList.Count > 1 ? $"{CalculateStandardDeviation(buyList):F1}" : "N/A";

                string sellMin = sellList.Count > 0 ? $"{sellList.Min():F1}" : "N/A";
                string sellMax = sellList.Count > 0 ? $"{sellList.Max():F1}" : "N/A";
                string sellMean = sellList.Count > 0 ? $"{sellList.Average():F1}" : "N/A";
                string sellSD = sellList.Count > 1 ? $"{CalculateStandardDeviation(sellList):F1}" : "N/A";

                Console.WriteLine($"{commodity,-15} {baseVal,8} {tradePercent,5} {banditPercent,5} {buyMean,10} {buySD,10} {buyMax,10} {buyMin,10} . {sellMax,10} {sellMin,10} {sellSD,10} {sellMean,10}");
            }
        }

        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(true);
        return 0;
    }

    double CalculateStandardDeviation(List<float> values) {
        if (values.Count <= 1) return 0;

        double mean = values.Average();
        double sumOfSquares = values.Sum(val => Math.Pow(val - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }

    Crawler? _player;
    public Crawler Player => _player!;
    public List<IProposal> StoredProposals { get; }

    List<Encounter> allEncounters = new();
    // Accessor methods for save/load
    public long GetTime() => TimeSeconds;
    public Crawler GetPlayer() => Player;
    public Map GetMap() => Map;
    public bool GetQuit() => quit;
    public ulong GetRngState() => Rng.GetState();
    public void SetRngState(ulong state) => Rng.SetState(state);
    public ulong GetGaussianRngState() => Gaussian.GetRngState();
    public void SetGaussianRngState(ulong state) => Gaussian.SetRngState(state);
    public bool GetGaussianPrimed() => Gaussian.GetPrimed();
    public void SetGaussianPrimed(bool primed) => Gaussian.SetPrimed(primed);
    public double GetGaussianZSin() => Gaussian.GetZSin();
    public void SetGaussianZSin(double zSin) => Gaussian.SetZSin(zSin);
}
