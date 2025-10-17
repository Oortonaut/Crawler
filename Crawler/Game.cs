using System.Drawing;
using System.Numerics;

namespace Crawler;

public class Game {

    static Game? _instance = null;
    public static Game Instance => _instance!;

    Map? _map = null;
    Map Map => _map!;

    public static Game NewGame(string crawlerName, int H) {
        var game = new Game();
        game.Construct(crawlerName, H);
        return game;
    }
    public static Game LoadGame(string saveFilePath) {
        var deserializer = new YamlDotNet.Serialization.Deserializer();
        using var reader = new StreamReader(saveFilePath);
        var saveData = deserializer.Deserialize<SaveGameData>(reader);

        var game = new Game();
        game.Construct(saveData);
        return game;
    }
    Game() {
        _instance = this;
        Welcome();
    }
    protected void Welcome() {
        Console.Write(Style.None.Format() + CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
    }

    void Construct(string crawlerName, int H) {
        if (string.IsNullOrWhiteSpace(crawlerName)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(crawlerName));
        }
        _map = new Map(H, 2 * H);
        var currentLocation = Map.GetStartingLocation();
        _player = Crawler.NewRandom(currentLocation, 1.25f, 0.5f, [1, 1, 1, 1]);
        _player.Name = crawlerName;
        _player.Faction = Faction.Player;
        _player.Recharge(20);
        Map.AddActor(_player);
    }
    void Construct(SaveGameData saveData) {
        TimeSeconds = saveData.Hour;
        AP = saveData.AP;
        TurnAP = saveData.TurnAP;
        quit = saveData.Quit;

        // Reconstruct the map from save data
        _map = saveData.Map.ToGameMap();

        // Find current location in the reconstructed map
        var currentLocation = Map.FindLocationByPosition(saveData.CurrentLocationPos);

        // Reconstruct player from save data
        _player = saveData.Player.ToGameCrawler(Map);
        _player.Location = currentLocation;

        // Add player to map
        Map.AddActor(Player);
    }

    bool quit = false;
    public bool Moving => PendingLocation != null;
    public void Run() {

        while (!quit) {
            if (Moving) {
                Player.Location = PendingLocation!;
                Map.AddActor(Player);
                PendingLocation = null;
            }

            GameMenu();

            while (AP <= 0) {
                Tick();
                AP += TurnAP;
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

    int AP = 1;
    int TurnAP = 1;
    public Location CurrentLocation => Player.Location;
    public TerrainType CurrentTerrain => CurrentLocation.Terrain;

    int Report() {
        Player.Message(Player.Report());
        return 0;
    }

    IEnumerable<MenuItem> EncounterMenuItems() {
        return Encounter?.MenuItems(Player) ?? [];
    }

    IEnumerable<MenuItem> LocalMenuItems(ShowArg showOption = ShowArg.Hide) {
        var sector = CurrentLocation.Sector;

        bool isPinned = Player.Pinned();
        if (isPinned) {
            Player.Message("You are pinned.");
        }

        float fuel = -1, time = -1;

        int index = 0;
        foreach (var location in sector.Locations) {
            ++index;
            if (location == CurrentLocation) {
                continue;
            }
            float dist = CurrentLocation.Distance(location);

            string locationName = location.EncounterName(Player);
            if (!location.HasEncounter || !Player.Visited(location)) {
                locationName = Style.MenuUnvisited.Format(locationName);
            }

            (fuel, time) = Player.FuelTimeTo(location);
            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            var enableArg = enabled ? EnableArg.Enabled : EnableArg.Disabled;
            yield return new ActionMenuItem($"L{index}", $"To {locationName} {dist:F0}km {time:F0}h {fuel:F1}FU", _ => GoTo(location), enableArg, showOption);
        }

        //////////////////////////
        yield break;
        ////////////////////////////
        int GoTo(Location loc) {
            Map.RemoveActor(Player);
            PendingLocation = loc;
            Player.FuelInv -= fuel;
            return (int)Math.Ceiling(time);
        }
    }

    IEnumerable<MenuItem> WorldMapMenuItems(ShowArg showOption = ShowArg.Hide) {
        var sector = CurrentLocation.Sector;

        bool isPinned = Player.Pinned();
        if (isPinned) {
            Player.Message("You are pinned.");
        }

        float fuel = -1, time = -1;

        int mapWidth = sector.Map.Width;
        foreach (var neighbor in sector.Neighbors) {
            if (!neighbor.Locations.Any()) {
                continue;
            }
            var locations = string.Join(", ", neighbor.Locations.Select(x => x.Type.ToString().Substring(0, 1)).Distinct());
            var location = neighbor.Locations.MinBy(x => CurrentLocation.Distance(x))!;
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
        //////////////////////////
        yield break;
        ////////////////////////////
        int GoTo(Location loc) {
            Map.RemoveActor(Player);
            PendingLocation = loc;
            Player.FuelInv -= fuel;
            return (int)Math.Ceiling(time);
        }
    }
    Location? PendingLocation = null;

    Encounter Encounter => CurrentLocation.Encounter;

    int Look() {
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        //Console.WriteLine(DrawMap());
        Console.WriteLine(CurrentLocation.Sector.Look() + " " + CurrentLocation.PosString);
        Console.WriteLine(Encounter.ViewFrom(Player));
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();
        return 0;
    }
    string DrawMap() {
        var worldMapLines = Map.DumpMap(Player).Split('\n');
        var worldMapWidth = worldMapLines.Max(x => x.Length);
        var height = worldMapLines.Length;
        var width = (5 * height) / 3;
        var sectorMapLines = Map.DumpSector(CurrentLocation.Sector.X, CurrentLocation.Sector.Y, width, height).Split('\n');
        var sectorMapWidth = sectorMapLines.Max(x => x.Length);
        var zippedLines = worldMapLines.Zip(sectorMapLines, (a, b) => $"│{a}│{b}║");
        // ┌─┬╖
        // │ │║
        // ├─┼╢
        // ╘═╧╝
        var worldHeader = $"[Sector {CurrentLocation.Sector.Name}|Turn {TimeSeconds/1000.0:F3}]";
        worldHeader += new string('─', Math.Max(0, worldMapWidth - worldHeader.Length));
        var sectorHeader = $"[{CurrentLocation.EncounterName(Player)}|{CurrentLocation.Terrain}]";
        sectorHeader += new string('─', Math.Max(0, sectorMapWidth - sectorHeader.Length));
        var header = $"┌{worldHeader}┬{sectorHeader}╖";
        var footer = $"╘{new string('═', worldMapWidth)}╧{new string('═', sectorMapWidth)}╝";

        var mapLines = zippedLines.StringJoin("\n");
        var result = $"{header}\n{mapLines}\n{footer}";
        return result;
    }
    string DrawWorldMap() {
        var worldMapLines = Map.DumpMap(Player).Split('\n');
        var worldMapWidth = worldMapLines.Max(x => x.Length);
        var header = $"┌[World Map|Sector {CurrentLocation.Sector.Name}|Turn {TimeSeconds/1000.0:F3}]";
        header += new string('─', Math.Max(0, worldMapWidth - header.Length + 1)) + "╖";
        var footer = $"╘{new string('═', worldMapWidth)}╝";
        var mapLines = worldMapLines.Select(line => $"│{line}║").StringJoin("\n");
        return $"{header}\n{mapLines}\n{footer}";
    }
    string DrawSectorMap() {
        var height = 20;
        var width = (5 * height) / 3;
        var sectorMapLines = Map.DumpSector(CurrentLocation.Sector.X, CurrentLocation.Sector.Y, width, height).Split('\n');
        var sectorMapWidth = sectorMapLines.Max(x => x.Length);
        var header = $"┌[Sector Map|{CurrentLocation.EncounterName(Player)}|{CurrentLocation.Terrain}]";
        header += new string('─', Math.Max(0, sectorMapWidth - header.Length + 1)) + "╖";
        var footer = $"╘{new string('═', sectorMapWidth)}╝";
        var mapLines = sectorMapLines.Select(line => $"│{line}║").StringJoin("\n");
        return $"{header}\n{mapLines}\n{footer}";
    }
    int ManageInventory() {
        //AP--;
        return 1;
    }
    int Turn(string args) {
        float count = float.TryParse(args, out float parsed) ? parsed * 60 : 3600;
        return (int) count;
    }
    void Tick() {
        ++TimeSeconds;
        if (Moving) {
            Player.Tick();
        } else {
            Encounter?.Tick();
        }
    }

    bool PlayerWon => false;
    bool PlayerLost => !string.IsNullOrEmpty(Player?.EndMessage);

    // seconds -
    public long TimeSeconds { get; private set; } = 1; // start at 1 so 0 can be an invalid time

    MenuItem GameMenu() {
        Look();

        var (selected, ap) = CrawlerEx.MenuRun("Game Menu", [
            .. GameMenuItems(),
            .. PlayerMenuItems(),
            .. LocalMenuItems(),
            .. WorldMapMenuItems(),
            .. EncounterMenuItems(),
            MenuItem.Sep,
            new MenuItem("", "Choose"),
        ]);
        AP -= ap;
        return selected;
    }
    int LocalMenu() {
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(DrawSectorMap());
        Console.WriteLine(CurrentLocation.Sector.Look() + " " + CurrentLocation.PosString);
        if (Encounter != null) {
            Console.WriteLine(Encounter.ViewFrom(Player));
        }
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();

        var (selected, ap) = CrawlerEx.MenuRun("Sector Map", [
            .. LocalMenuItems(ShowArg.Show),
        ]);
        return ap;
    }
    int WorldMapMenu() {
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(DrawWorldMap());
        Console.WriteLine(CurrentLocation.Sector.Look() + " " + CurrentLocation.PosString);
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();

        var (selected, ap) = CrawlerEx.MenuRun("World Map", [
            .. WorldMapMenuItems(ShowArg.Show),
        ]);
        return ap;
    }
    IEnumerable<MenuItem> GameMenuItems() => [
        new ActionMenuItem("M", "Sector Map", _ => LocalMenu()),
        new ActionMenuItem("W", "World Map", _ => WorldMapMenu()),
        new ActionMenuItem("R", "Status Report", _ => Report()),
        new ActionMenuItem("K", "Skip Turn/Wait", args => Turn(args)),
        new ActionMenuItem("Q", "Save and Quit", _ => Save() + Quit()),
        new ActionMenuItem("QQ", "Quit ", _ => Quit()),
        new ActionMenuItem("GG", "", args => {
            Player.ScrapInv += 1000;
            return 0;
        }, EnableArg.Enabled, ShowArg.Hide),
    ];
    IEnumerable<MenuItem> PlayerMenuItems() {
        yield return MenuItem.Sep;
        yield return new MenuItem("", Style.MenuTitle.Format("Player Menu"));
        yield return new ActionMenuItem("PP", "Power...", _ => PowerMenu());
        yield return new ActionMenuItem("PK", "Packaging...", _ => PackagingMenu());
        yield return new ActionMenuItem("PT", "Trade Inv...", _ => TradeInventoryMenu());

        // Hidden detail items for power control
        var segments = Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;

            string toggleLabel = segment.Activated ? "Deactivate" : "Activate";
            bool canToggle = segment.State != Segment.Working.Destroyed && segment.State != Segment.Working.Packaged;
            yield return new ActionMenuItem($"PP{index + 1}", $"{segment.StateName} - {toggleLabel}", _ => ToggleSegmentPower(index), canToggle.Enable(), ShowArg.Hide);
        }

        // Hidden detail items for packaging control
        foreach (var item in PackagingMenuItems(ShowArg.Hide)) {
            yield return item;
        }

        // Hidden detail items for trade inventory control
        foreach (var item in TradeInventoryMenuItems(ShowArg.Hide)) {
            yield return item;
        }
    }

    int PowerMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Power Menu", [
            .. PowerMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> PowerMenuItems() {
        var segments = Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;

            string toggleLabel = segment.Activated ? "Deactivate" : "Activate";
            bool canToggle = segment.State != Segment.Working.Destroyed && segment.State != Segment.Working.Packaged;
            yield return new ActionMenuItem($"PP{index + 1}", $"{segment.StateName} - {toggleLabel}", _ => ToggleSegmentPower(index), canToggle.Enable());
        }
    }

    int PackagingMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Packaging Menu", [
            .. PackagingMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> PackagingMenuItems(ShowArg showOption = ShowArg.Show) {
        var segments = Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;

            string packageLabel = !segment.Packaged ? "Package" : "Unpackage";
            bool canPackage = segment.Hits == 0 && segment.State != Segment.Working.Destroyed;
            yield return new ActionMenuItem($"PK{index + 1}", $"{segment.StateName} - {packageLabel}", _ => TogglePackage(index), canPackage.Enable(), showOption);
        }
    }

    int ToggleSegmentPower(int index) {
        var segment = Player.Segments[index];
        segment.Activated = !segment.Activated;
        Player.Message($"{segment.Name} {(segment.Activated ? "activated" : "deactivated")}");
        Player.UpdateSegments();
        return 0;
    }

    int TogglePackage(int index) {
        var segment = Player.Segments[index];
        segment.Packaged = !segment.Packaged;
        Player.Message($"{segment.Name} {(segment.Packaged ? "packaged" : "unpackaged")}");
        Player.UpdateSegments();
        return 0;
    }

    int TradeInventoryMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Trade Inventory Menu", [
            .. TradeInventoryMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> TradeInventoryMenuItems(ShowArg showOption = ShowArg.Show) {
        // Show packaged segments in main inventory that can be moved to trade inventory
        var packagedSegments = Player.Segments.Where(s => s.State == Segment.Working.Packaged).ToList();
        for (int i = 0; i < packagedSegments.Count; i++) {
            var segment = packagedSegments[i];
            yield return new ActionMenuItem($"PT{i + 1}M", $"{segment.StateName} - Move to Trade", _ => MoveToTradeInventory(segment), EnableArg.Enabled, showOption);
        }

        // Show segments in trade inventory that can be moved back to main inventory
        var tradeSegments = Player.TradeInv.Segments.ToList();
        for (int i = 0; i < tradeSegments.Count; i++) {
            var segment = tradeSegments[i];
            int index = i;
            yield return new ActionMenuItem($"PT{i + 1}R", $"{segment.StateName} (Trade) - Return to Inventory", _ => MoveFromTradeInventory(segment), EnableArg.Enabled, showOption);
        }
    }

    int MoveToTradeInventory(Segment segment) {
        Player.Inv.Remove(segment);
        Player.TradeInv.Add(segment);
        Player.Message($"{segment.Name} moved to trade inventory");
        Player.UpdateSegments();
        return 0;
    }

    int MoveFromTradeInventory(Segment segment) {
        Player.TradeInv.Remove(segment);
        Player.Inv.Add(segment);
        Player.Message($"{segment.Name} returned from trade inventory");
        Player.UpdateSegments();
        return 0;
    }
    /*
    IEnumerable<MenuItem> MapScreen() {
        var worldMapLines = Map.DumpMap(Player).Split('\n');
        var sector = Player.Location.Sector;
        var destinations = new List<string>();
        foreach (var neighbor in sector.Neighbors) {
            if (!neighbor.Locations.Any()) {
                continue;
            }
            var locations = string.Join(", ", neighbor.Locations.Select(x => x.Type).Distinct());
            var location = neighbor.Locations.MinBy(x => CurrentLocation.Distance(x))!;
            string dir = "N";
            Point D = neighbor.Offset(sector);
            if (D.X == 0 && D.Y == 0) {
                dir = "KK";
            } else {
                var angle = (int)Math.Floor(Math.Atan2(D.Y, D.X) * 8 / Math.Tau + 0.5);
                dir = angle switch {
                    -4 => "W",
                    -3 => "SW",
                    -2 => "S",
                    -1 => "SE",
                    0 => "E",
                    1 => "NE",
                    2 => "N",
                    3 => "NW",
                    4 => "W",
                    _ => throw new ArithmeticException("Invalid angle calculated"),
                };
            }

            var (fuel, time) = Player.FuelTimeTo(location);

            string neighborName = $"{neighbor.Name} ({neighbor.Terrain})";
            if (!neighbor.VisitedBy.Contains(Player)) {
                neighborName = Style.MenuUnvisited.Format(neighborName);
            } else if (!neighbor.Locations.Any()) {
                neighborName = Style.MenuEmpty.Format(neighborName);
            }

            bool isPinned = Player.Pinned();
            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            var enableArg = enabled ? EnableArg.Enabled : EnableArg.Disabled;
            var fmtDir = (enabled ? Style.MenuOption : Style.MenuDisabled).Format(dir);
            if (fuel > 0) {

                destinations.Add($"{fmtDir} Sector {neighborName} ({locations}) {time:F0}h Fuel: {fuel:F1}");
                yield return new ActionMenuItem(dir, $"Sector {neighborName} ({locations}) {time:F0}h Fuel: {fuel:F1}", _ => Player.Embark(location), enableArg, ShowArg.Hide);
            } else {
                destinations.Add($"{fmtDir} Sector {neighborName} ({locations})");
                yield return new ActionMenuItem(dir, $"Sector {neighborName} ({locations})", _ => Player.Embark(location), enableArg, ShowArg.Hide);
            }
        }
        var result = worldMapLines.ZipColumns(destinations).StringJoin("\n");
        Console.Write(CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(result);
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();
    }
    */

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

    Crawler? _player;
    public Crawler Player => _player!;
    public List<IProposal> StoredProposals { get; } = [
        new ProposeLootFree("L"),
        new ProposeAttackDefend("A"),
        new ProposeAcceptSurrender("S"),
        new ProposeRepairBuy( "R"),
    ];
    // Accessor methods for save/load
    public long GetTime() => TimeSeconds;
    public int GetAP() => AP;
    public int GetTurnAP() => TurnAP;
    public Crawler GetPlayer() => Player;
    public Map GetMap() => Map;
    public bool GetQuit() => quit;
}
