using System.Text;

namespace Crawler;

public class CrawlerArena {
    public static CrawlerArena Instance { get; private set; } = null!;

    private Location arenaLocation = null!;
    private List<CrawlerDesign> availableDesigns = new();
    private Dictionary<string, TournamentStats> tournamentStats = new();
    private Dictionary<(string, string), HeadToHeadStats> headToHeadStats = new();

    public CrawlerArena() {
        Instance = this;
        InitializeArena();
        InitializeBasicDesigns();
    }


    void InitializeArena() {
        var Rng = new XorShift(12345);
        // Create a minimal map with one sector
        var map = new Map(Rng.Seed(), 1, 1);

        // Access the sector directly since we know it's a 1x1 map
        var sector = new Sector(Rng.Seed(), map, "Arena", 0, 0);
        sector.Terrain = TerrainType.Flat;

        // Create arena location with EncounterType.None to prevent default generation
        arenaLocation = new Location(Rng.Seed(),
            sector, new System.Numerics.Vector2(0.5f, 0.5f), EncounterType.None, 1000.0f, loc => new Encounter(Rng.Seed(), loc, Factions.Independent));

        sector.Locations.Add(arenaLocation);
    }

    void InitializeBasicDesigns() {
        // availableDesigns.Add(new CrawlerDesign("Gunboat", CreateGunboatDesign()));
        // availableDesigns.Add(new CrawlerDesign("Laser Fighter", CreateLaserDesign()));
        // availableDesigns.Add(new CrawlerDesign("Missile Boat", CreateMissileDesign()));
        // availableDesigns.Add(new CrawlerDesign("Heavy Tank", CreateTankDesign()));
        // availableDesigns.Add(new CrawlerDesign("Shield Runner", CreateShieldDesign()));
        SegmentDef[] offense = [
            SegmentEx.NameLookup["Guns"],
            SegmentEx.NameLookup["Lasers"],
            SegmentEx.NameLookup["Missiles"],
        ];
        SegmentDef[] defense = [
            SegmentEx.NameLookup["Armor"],
            SegmentEx.NameLookup["Plating"],
            SegmentEx.NameLookup["Shields"],
        ];
        var reactor = SegmentEx.NameLookup["Reactor"];
        var traction = SegmentEx.NameLookup["Traction"];
        //var wheels = SegmentEx.Lookup['O'];
        //var treads = SegmentEx.Lookup['%'];
        var rng = new XorShift(54321);
        foreach (var o in offense) {
            foreach (var d in defense) {
                var Inv = new Inventory();
                Inv.Add(o.NewSegment(rng.Seed()));
                Inv.Add(d.NewSegment(rng.Seed()));
                Inv.Add(reactor.NewSegment(rng.Seed()));
                Inv.Add(traction.NewSegment(rng.Seed()));
                Inv[Commodity.Scrap] = 1000;
                Inv[Commodity.Crew] = 5;
                Inv[Commodity.Fuel] = 100;
                Inv[Commodity.Rations] = 50;
                Inv[Commodity.Morale] = 10;
                availableDesigns.Add(new CrawlerDesign($"{o.Name} {d.Name}", Inv));
            }
        }
    }

    Inventory CreateGunboatDesign() {
        var inv = new Inventory();
        var rng = new XorShift(1001);
        // Basic power and structure - use existing segment definitions
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor"].NewSegment(rng.Seed())); // Small Reactor
        inv.Add(SegmentEx.NameLookup["Heavy Armor"].NewSegment(rng.Seed())); // Heavy Armor
        inv.Add(SegmentEx.NameLookup["Armor"].NewSegment(rng.Seed())); // Light Armor

        // Gun-focused loadout
        inv.Add(SegmentEx.NameLookup["Heavy Guns"].NewSegment(rng.Seed())); // Heavy Guns
        inv.Add(SegmentEx.NameLookup["Heavy Guns"].NewSegment(rng.Seed())); // Heavy Guns
        inv.Add(SegmentEx.NameLookup["Guns"].NewSegment(rng.Seed())); // Guns

        // Basic movement
        inv.Add(SegmentEx.NameLookup["Wheels"].NewSegment(rng.Seed())); // Wheels

        inv[Commodity.Crew] = 5;
        inv[Commodity.Fuel] = 100;
        inv[Commodity.Rations] = 50;
        inv[Commodity.Morale] = 10;

        return inv;
    }

    Inventory CreateLaserDesign() {
        var inv = new Inventory();
        var rng = new XorShift(1002);
        // High power generation for energy weapons
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor"].NewSegment(rng.Seed())); // Small Reactor

        // Laser-focused loadout
        inv.Add(SegmentEx.NameLookup["Lasers Heavy"].NewSegment(rng.Seed())); // Heavy Lasers
        inv.Add(SegmentEx.NameLookup["Lasers Heavy"].NewSegment(rng.Seed())); // Heavy Lasers
        inv.Add(SegmentEx.NameLookup["Lasers"].NewSegment(rng.Seed())); // Basic Lasers

        // Light protection for mobility
        inv.Add(SegmentEx.NameLookup["Armor"].NewSegment(rng.Seed())); // Light Armor

        // Basic movement
        inv.Add(SegmentEx.NameLookup["Wheels Fast"].NewSegment(rng.Seed())); // High Speed Wheels

        inv[Commodity.Crew] = 4;
        inv[Commodity.Fuel] = 80;
        inv[Commodity.Rations] = 40;
        inv[Commodity.Morale] = 12;

        return inv;
    }

    Inventory CreateMissileDesign() {
        var inv = new Inventory();
        var rng = new XorShift(1003);
        // Moderate power for missile systems
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor"].NewSegment(rng.Seed())); // Small Reactor

        // Missile-focused loadout
        inv.Add(SegmentEx.NameLookup["Missiles Heavy"].NewSegment(rng.Seed())); // Heavy Missiles
        inv.Add(SegmentEx.NameLookup["Missiles Heavy"].NewSegment(rng.Seed())); // Heavy Missiles
        inv.Add(SegmentEx.NameLookup["Missiles"].NewSegment(rng.Seed())); // Basic Missiles

        // Medium armor
        inv.Add(SegmentEx.NameLookup["Armor Heavy"].NewSegment(rng.Seed())); // Heavy Armor
        inv.Add(SegmentEx.NameLookup["Armor"].NewSegment(rng.Seed())); // Light Armor

        // Basic movement
        inv.Add(SegmentEx.NameLookup["Wheels"].NewSegment(rng.Seed())); // Wheels

        inv[Commodity.Crew] = 6;
        inv[Commodity.Fuel] = 120;
        inv[Commodity.Rations] = 60;
        inv[Commodity.Morale] = 8;

        return inv;
    }

    Inventory CreateTankDesign() {
        var inv = new Inventory();
        var rng = new XorShift(1004);
        // Heavy power for sustained combat
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor

        // Mixed weapons
        inv.Add(SegmentEx.NameLookup["Guns Heavy"].NewSegment(rng.Seed())); // Heavy Guns
        inv.Add(SegmentEx.NameLookup["Lasers Heavy"].NewSegment(rng.Seed())); // Heavy Lasers

        // Heavy armor focus
        inv.Add(SegmentEx.NameLookup["Armor Heavy"].NewSegment(rng.Seed())); // Heavy Armor
        inv.Add(SegmentEx.NameLookup["Armor Heavy"].NewSegment(rng.Seed())); // Heavy Armor
        inv.Add(SegmentEx.NameLookup["Armor Heavy"].NewSegment(rng.Seed())); // Heavy Armor
        inv.Add(SegmentEx.NameLookup["Armor"].NewSegment(rng.Seed())); // Light Armor

        // Heavy movement for terrain
        inv.Add(SegmentEx.NameLookup["Legs Heavy"].NewSegment(rng.Seed())); // Heavy Legs

        inv[Commodity.Crew] = 8;
        inv[Commodity.Fuel] = 150;
        inv[Commodity.Rations] = 80;
        inv[Commodity.Morale] = 6;

        return inv;
    }

    Inventory CreateShieldDesign() {
        var inv = new Inventory();
        var rng = new XorShift(1005);
        // High power for shields
        inv.Add(SegmentEx.NameLookup["Reactor Large"].NewSegment(rng.Seed())); // Large Reactor
        inv.Add(SegmentEx.NameLookup["Reactor"].NewSegment(rng.Seed())); // Small Reactor

        // Shield-heavy loadout
        inv.Add(SegmentEx.NameLookup["Shield Heavy"].NewSegment(rng.Seed())); // Heavy Shields
        inv.Add(SegmentEx.NameLookup["Shield"].NewSegment(rng.Seed())); // Light Shields

        // Moderate weapons
        inv.Add(SegmentEx.NameLookup["Lasers Heavy"].NewSegment(rng.Seed())); // Heavy Lasers
        inv.Add(SegmentEx.NameLookup["Guns"].NewSegment(rng.Seed())); // Guns

        // Light armor since shields provide protection
        inv.Add(SegmentEx.NameLookup["Armor"].NewSegment(rng.Seed())); // Light Armor

        // Quick movement
        inv.Add(SegmentEx.NameLookup["Wheels Fast"].NewSegment(rng.Seed())); // High Speed Wheels

        inv[Commodity.Crew] = 5;
        inv[Commodity.Fuel] = 90;
        inv[Commodity.Rations] = 50;
        inv[Commodity.Morale] = 10;

        return inv;
    }

    public void Run() {
        bool quit = false;
        while (!quit) {
            var (selected, args) = CrawlerEx.Menu("Crawler Arena",
                new ActionMenuItem("T", "Run Tournament", _ => RunTournament()),
                new ActionMenuItem("S", "View Statistics", _ => ViewStatistics()),
                new ActionMenuItem("D", "View Designs", _ => ViewDesigns()),
                new ActionMenuItem("Q", "Return to Main Menu", _ => { quit = true; return 0; })
            );

            if (selected is ActionMenuItem action) {
                action.Run(args);
            }
        }
    }

    int RunTournament() {
        Console.WriteLine("Starting Tournament...\n");

        // Run all vs all matches
        var results = new List<MatchResult>();

        for (int i = 0; i < availableDesigns.Count; i++) {
            for (int j = i; j < availableDesigns.Count; j++) {
                var design1 = availableDesigns[i];
                var design2 = availableDesigns[j];

                Console.WriteLine($"{design1.Name} vs {design2.Name}");

                // Run 5 pairs of matches (10 total) with alternating first strikes
                const int matches = 50;
                for (int match = 0; match < matches/2; match++) {
                    // First match: design1 strikes first
                    var result1 = RunMatch(design1, design2, design1First: true);
                    results.Add(result1);
                    /*
                    if (result1.IsDraw) {
                        Console.WriteLine($"Match {match*2 + 1} (D1 first): Draw between {result1.Design1} and {result1.Design2} after {result1.Rounds} rounds");
                    } else {
                        Console.WriteLine($"Match {match*2 + 1} (D1 first): {result1.Winner} defeats {result1.Loser} in {result1.Rounds} rounds");
                    }
                    */

                    // Second match: design2 strikes first
                    var result2 = RunMatch(design1, design2, design1First: false);
                    results.Add(result2);
                    /*
                    if (result2.IsDraw) {
                        Console.WriteLine($"Match {match*2 + 2} (D2 first): Draw between {result2.Design1} and {result2.Design2} after {result2.Rounds} rounds");
                    } else {
                        Console.WriteLine($"Match {match*2 + 2} (D2 first): {result2.Winner} defeats {result2.Loser} in {result2.Rounds} rounds");
                    }
                    */
                }
                //Console.WriteLine();
            }
        }

        // Update tournament statistics
        UpdateTournamentStats(results);

        Console.WriteLine("Tournament complete!");
        return 0;
    }

    MatchResult RunMatch(CrawlerDesign design1, CrawlerDesign design2, bool design1First = true) {
        // Create crawler instances
        var crawler1 = CreateInitializedCrawler(design1, design1.Name + " A", Factions.Bandit);
        var crawler2 = CreateInitializedCrawler(design2, design2.Name + " B", Factions.Player);

        // Set up hostility between the crawlers
        crawler1.To(crawler2).Hostile = true;
        crawler2.To(crawler1).Hostile = true;

        int rounds = 0;
        int maxRounds = 100;

        // Check initial states
        bool crawler1InitiallyDestroyed = crawler1.IsDestroyed;
        bool crawler2InitiallyDestroyed = crawler2.IsDestroyed;

        if (crawler1InitiallyDestroyed || crawler2InitiallyDestroyed) {
            Console.WriteLine($"DEBUG: Crawler destroyed before combat! C1: {crawler1InitiallyDestroyed}, C2: {crawler2InitiallyDestroyed}");
        }

        int win = 0;
        long currentTime = 0;

        // Determine attack order based on design1First parameter
        var (firstAttacker, secondAttacker) = design1First ? (crawler1, crawler2) : (crawler2, crawler1);

        // Initial attack from first attacker
        if (firstAttacker.WeaponDelay().HasValue) {
            var delay = firstAttacker.Attack(secondAttacker);
            firstAttacker.ConsumeTime(delay);
        }

        // Initial attack from second attacker with slight offset
        if (secondAttacker.WeaponDelay().HasValue) {
            var delay = secondAttacker.Attack(firstAttacker);
            secondAttacker.ConsumeTime(delay);
        }

        while (rounds < maxRounds) {
            if (crawler1.IsDestroyed && crawler2.IsDestroyed ||
                crawler1.IsDisarmed && crawler2.IsDisarmed) {
                break;
            }
            if (crawler1.IsDestroyed || crawler1.IsDisarmed) {
                win = 1;
                break;
            }
            if (crawler2.IsDestroyed || crawler2.IsDisarmed) {
                win = -1;
                break;
            }
            rounds++;

            // Determine who attacks next based on scheduled times
            var nextAttacker = crawler1.NextScheduledTime <= crawler2.NextScheduledTime ? crawler1 : crawler2;
            var nextDefender = nextAttacker == crawler1 ? crawler2 : crawler1;

            currentTime = nextAttacker.NextScheduledTime;

            // Simulate both crawlers to current time
            crawler1.SimulateTo(currentTime);
            crawler2.SimulateTo(currentTime);

            // Perform attack if attacker is still capable
            if (!nextAttacker.IsDestroyed && !nextAttacker.IsDisarmed && nextAttacker.WeaponDelay().HasValue) {
                var delay = nextAttacker.Attack(nextDefender);
                nextAttacker.ConsumeTime(delay);
            } else {
                // If can't attack, just advance time
                break;
            }
        }

        // Determine winner
        string? winner, loser;
        if (win < 0) {
            winner = design1.Name;
            loser = design2.Name;
        } else if (win > 0) {
            winner = design2.Name;
            loser = design1.Name;
        } else {
            // Draw - both destroyed/disarmed or max rounds reached
            winner = null;
            loser = null;
        }

        return new MatchResult(winner, loser, design1.Name, design2.Name, rounds);
    }

    private Crawler CreateInitializedCrawler(CrawlerDesign design, string name, Factions faction) {
        var inventory = design.Inventory.Clone();
        var crawler = new Crawler.Builder()
            .WithSeed((ulong)(name.GetHashCode() + faction.GetHashCode()))
            .WithFaction(faction)
            .WithLocation(arenaLocation)
            .WithSupplies(inventory)
            .WithName(name)
            .WithComponentInitialization(false)
            .Build();

        // Assign the crawler as owner to all segments
        foreach (var segment in crawler.Supplies.Segments) {
            segment.Owner = crawler;
        }

        // Initialize the crawler's systems
        crawler.UpdateSegmentCache();

        return crawler;
    }

    void UpdateTournamentStats(List<MatchResult> results) {
        foreach (var result in results) {
            // Ensure both designs have entries in tournament stats
            if (!tournamentStats.ContainsKey(result.Design1)) {
                tournamentStats[result.Design1] = new TournamentStats();
            }
            if (!tournamentStats.ContainsKey(result.Design2)) {
                tournamentStats[result.Design2] = new TournamentStats();
            }

            // Update total rounds and matches for both designs
            tournamentStats[result.Design1].TotalRounds += result.Rounds;
            tournamentStats[result.Design1].Matches++;
            tournamentStats[result.Design2].TotalRounds += result.Rounds;
            tournamentStats[result.Design2].Matches++;

            if (result.IsDraw) {
                // Handle draws
                tournamentStats[result.Design1].Draws++;
                tournamentStats[result.Design2].Draws++;

                // Track head-to-head draws
                var matchup1 = (result.Design1, result.Design2);
                var matchup2 = (result.Design2, result.Design1);

                if (!headToHeadStats.ContainsKey(matchup1)) {
                    headToHeadStats[matchup1] = new HeadToHeadStats();
                }
                if (!headToHeadStats.ContainsKey(matchup2)) {
                    headToHeadStats[matchup2] = new HeadToHeadStats();
                }

                headToHeadStats[matchup1].Draws++;
                headToHeadStats[matchup1].Matches++;
                headToHeadStats[matchup2].Draws++;
                headToHeadStats[matchup2].Matches++;
            } else {
                // Handle wins/losses
                tournamentStats[result.Winner!].Wins++;
                tournamentStats[result.Loser!].Losses++;

                // Track head-to-head performance
                var matchup = (result.Winner!, result.Loser!);
                if (!headToHeadStats.ContainsKey(matchup)) {
                    headToHeadStats[matchup] = new HeadToHeadStats();
                }
                headToHeadStats[matchup].Wins++;
                headToHeadStats[matchup].Matches++;

                // Also track the reverse matchup for losses
                var reverseMatchup = (result.Loser!, result.Winner!);
                if (!headToHeadStats.ContainsKey(reverseMatchup)) {
                    headToHeadStats[reverseMatchup] = new HeadToHeadStats();
                }
                headToHeadStats[reverseMatchup].Losses++;
                headToHeadStats[reverseMatchup].Matches++;
            }
        }
    }

    int ViewStatistics() {
        Console.Clear();
        Console.WriteLine("Tournament Statistics:\n");

        if (!tournamentStats.Any()) {
            Console.WriteLine("No tournament data available. Run a tournament first.");
        } else {
            var table = new Table(
                ("Design", -20),
                ("Matches", 8),
                ("Wins", 8),
                ("Losses", 8),
                ("Draws", 8),
                ("Win%", 8),
                ("Rounds µ", 8));
            foreach (var kvp in tournamentStats.OrderByDescending(x => x.Value.WinRate)) {
                var stats = kvp.Value;
                table.AddRow(
                    kvp.Key,
                    stats.Matches,
                    stats.Wins,
                    stats.Losses,
                    stats.Draws,
                    stats.WinRate.ToString("P1"),
                    stats.AverageRounds.ToString("F1")
                );
            }
            Console.WriteLine(table.ToString());

            // Display head-to-head matrix
            Console.WriteLine("\n\nHead-to-Head Win Matrix (Row vs Column):");
            DisplayHeadToHeadMatrix();
        }
        return 0;
    }

    void DisplayHeadToHeadMatrix() {
        var designs = availableDesigns.Select(d => d.Name).OrderBy(n => n).ToList();

        if (designs.Count == 0) {
            Console.WriteLine("No designs available.");
            return;
        }

        // Calculate column width based on longest design name
        int nameWidth = Math.Max(20, designs.Max(d => d.Length) + 2);
        int cellWidth = 10;

        // Header row
        Console.Write("".PadRight(nameWidth));
        foreach (var design in designs) {
            Console.Write(design.Length <= cellWidth - 1 ? design.PadLeft(cellWidth) : design.Substring(0, cellWidth - 1).PadLeft(cellWidth));
        }
        Console.WriteLine();

        // Separator line
        Console.Write("".PadRight(nameWidth, '-'));
        for (int i = 0; i < designs.Count; i++) {
            Console.Write("".PadRight(cellWidth, '-'));
        }
        Console.WriteLine();

        // Data rows
        foreach (var rowDesign in designs) {
            Console.Write(rowDesign.PadRight(nameWidth));

            foreach (var colDesign in designs) {
                if (rowDesign == colDesign) {
                    Console.Write("-".PadLeft(cellWidth));
                } else {
                    var matchup = (rowDesign, colDesign);
                    if (headToHeadStats.ContainsKey(matchup)) {
                        var stats = headToHeadStats[matchup];
                        var winRate = stats.Matches > 0 ? (double)stats.Wins / stats.Matches : 0.0;
                        Console.Write($"{stats.Wins,2}/{stats.Draws,2}/{stats.Losses,2}".PadLeft(cellWidth));
                    } else {
                        Console.Write("0/0".PadLeft(cellWidth));
                    }
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine("\nMatrix shows wins/total matches. Row design vs Column design.");
    }

    int ViewDesigns() {
        Console.Clear();
        Console.WriteLine("Available Crawler Designs:\n");

        foreach (var design in availableDesigns) {
            Console.WriteLine($"{design.Name}:");

            var segments = design.Inventory.Segments.GroupBy(s => s.GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var kvp in segments) {
                Console.WriteLine($"  {kvp.Value}x {kvp.Key}");
            }

            Console.WriteLine($"  Total Cost: {design.Inventory.Segments.Sum(s => s.Cost)}¢¢");
            Console.WriteLine();
        }
        return 0;
    }
}

public class CrawlerDesign {
    public string Name { get; }
    public Inventory Inventory { get; }

    public CrawlerDesign(string name, Inventory inventory) {
        Name = name;
        Inventory = inventory;
    }
}

public class MatchResult {
    public string? Winner { get; }
    public string? Loser { get; }
    public string Design1 { get; }
    public string Design2 { get; }
    public int Rounds { get; }
    public bool IsDraw => Winner == null && Loser == null;

    public MatchResult(string? winner, string? loser, string design1, string design2, int rounds) {
        Winner = winner;
        Loser = loser;
        Design1 = design1;
        Design2 = design2;
        Rounds = rounds;
    }
}

public class TournamentStats {
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int TotalRounds { get; set; }

    public double WinRate => Matches > 0 ? (double)Wins / Matches : 0.0;
    public double AverageRounds => Matches > 0 ? (double)TotalRounds / Matches : 0.0;
}

public class HeadToHeadStats {
    public int Matches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }

    public double WinRate => Matches > 0 ? (double)Wins / Matches : 0.0;
}
