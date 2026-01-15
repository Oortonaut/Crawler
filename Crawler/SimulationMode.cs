namespace Crawler;

/// <summary>
/// Simulation mode runs the game world without a player, allowing observation
/// of emergent NPC behaviors, economic dynamics, and world evolution.
/// Uses the same seeding system so identical worlds can be generated for both
/// simulation observation and actual gameplay.
/// </summary>
public class SimulationMode {
    readonly Game _game;
    readonly ulong _seed;
    readonly int _size;
    bool _quit = false;
    double _emaSpeed = 0;
    const double EmaAlpha = 0.3; // Higher = more responsive to recent changes

    SimulationMode(Game game, ulong seed, int size) {
        _game = game;
        _seed = seed;
        _size = size;
    }

    public static SimulationMode New(ulong seed, int size) {
        var game = Game.NewSimulation(seed, size);
        return new SimulationMode(game, seed, size);
    }

    public void Run() {
        Console.WriteLine($"\nSimulation initialized. Seed: {_seed}, Size: {_size}");
        Console.WriteLine($"World has {CountActors()} actors across {_game.Map.AllLocations.Count()} locations.\n");

        while (!_quit) {
            ShowMenu();
        }
    }

    void ShowMenu() {
        Console.WriteLine($"\n=== Simulation Control === Time: {Game.TimeString(_game.CurrentTime)}");

        var (selected, args) = CrawlerEx.Menu("",
            new ActionMenuItem("R", "Run (continuous, any key stops)", _ => RunContinuous()),
            new ActionMenuItem("T", "Run for duration (e.g., 1d, 6h, 30m)", _ => RunFor()),
            MenuItem.Sep,
            new ActionMenuItem("S", "Status summary", _ => ShowStatus()),
            new ActionMenuItem("A", "Actor census", _ => ShowActorCensus()),
            new ActionMenuItem("E", "Economy report", _ => ShowEconomyReport()),
            new ActionMenuItem("M", "World map", _ => ShowMap()),
            MenuItem.Sep,
            new ActionMenuItem("Q", "Exit simulation", _ => { _quit = true; return false; })
        );
        if (selected is ActionMenuItem action) {
            action.Run(args);
        }
    }

    bool RunContinuous() {
        Console.WriteLine("\nRunning simulation... Press any key to stop.\n");
        _emaSpeed = 0; // Reset EMA for fresh run
        int batches = 0;
        var lastReportWall = DateTime.Now;
        var lastReportSim = _game.CurrentTime;

        // Clear any pending key
        while (Console.KeyAvailable) Console.ReadKey(true);

        while (!Console.KeyAvailable) {
            // Process events in small batches for responsiveness
            var batchEnd = _game.CurrentTime + TimeDuration.FromMinutes(1);
            _game.ProcessEventsUntil(batchEnd, () => Console.KeyAvailable);
            batches++;

            // Periodic report every 5 seconds wall-clock
            if ((DateTime.Now - lastReportWall).TotalSeconds >= 5) {
                PrintPeriodicStats(lastReportWall, lastReportSim, batches);
                lastReportWall = DateTime.Now;
                lastReportSim = _game.CurrentTime;
            }
        }

        // Consume the key that stopped us
        Console.ReadKey(true);
        PrintPeriodicStats(lastReportWall, lastReportSim, batches);
        Console.WriteLine("\nSimulation paused.");
        return false;
    }

    bool RunFor() {
        var input = CrawlerEx.Input("Duration (e.g., 1d, 6h, 30m): ", "1h");
        if (!TimeDuration.TryParse(input, out var duration)) {
            Console.WriteLine("Invalid duration format. Use formats like: 1d, 6h, 30m, 1d12h");
            return false;
        }

        var endTime = _game.CurrentTime + duration;
        Console.WriteLine($"Running until {Game.TimeString(endTime)}... (press any key to interrupt)");

        var startWall = DateTime.Now;
        var startSim = _game.CurrentTime;

        // Check if we have a real console (not redirected input)
        bool hasConsole = !Console.IsInputRedirected;

        // Clear any pending key
        if (hasConsole) {
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        _game.ProcessEventsUntil(endTime, () => hasConsole && Console.KeyAvailable);

        if (hasConsole && Console.KeyAvailable) {
            Console.ReadKey(true);
            Console.WriteLine("Interrupted.");
        }

        var wallElapsed = DateTime.Now - startWall;
        var simElapsed = _game.CurrentTime - startSim;
        Console.WriteLine($"Completed. Simulated {simElapsed} in {wallElapsed.TotalSeconds:F1}s wall-clock.");
        return false;
    }

    void PrintPeriodicStats(DateTime lastReportWall, TimePoint lastReportSim, int batches) {
        // Current speed since last report
        var recentWall = DateTime.Now - lastReportWall;
        var recentSim = _game.CurrentTime - lastReportSim;
        var currentSpeed = recentWall.TotalSeconds > 0 ? recentSim.TotalSeconds / recentWall.TotalSeconds : 0;

        // Update exponential moving average
        _emaSpeed = _emaSpeed == 0 ? currentSpeed : EmaAlpha * currentSpeed + (1 - EmaAlpha) * _emaSpeed;

        // Calculate wealth statistics
        var (settlements, crawlers) = GetActorsByType();
        var settlementWealth = settlements.Select(GetWealth).ToList();
        var crawlerWealth = crawlers.Select(GetWealth).ToList();
        var totalWealth = settlementWealth.Sum() + crawlerWealth.Sum();

        var settlementStats = ComputeStats(settlementWealth);
        var crawlerStats = ComputeStats(crawlerWealth);

        Console.WriteLine($"[{Game.TimeString(_game.CurrentTime)}] Speed: {currentSpeed:F0}x (avg {_emaSpeed:F0}x) | " +
            $"Actors: {settlements.Count}S/{crawlers.Count}C | Events: {_game.ScheduledEventCount}");
        Console.WriteLine($"  Wealth: {totalWealth:N0} total | " +
            $"Settlements: {settlementStats.Sum:N0} (med {settlementStats.Median:N0}, σ {settlementStats.StdDev:N0}) | " +
            $"Crawlers: {crawlerStats.Sum:N0} (med {crawlerStats.Median:N0}, σ {crawlerStats.StdDev:N0})");

        // Production/consumption summary
        var prodTotal = _game.GrossProduction.ItemValueAt(_game.Map.AllLocations.First());
        var consTotal = _game.GrossConsumption.ItemValueAt(_game.Map.AllLocations.First());
        var segCount = _game.SegmentProduction.Values.Sum();
        var ammoCount = _game.AmmoConsumption.Values.Sum();
        Console.WriteLine($"  Production: {prodTotal:N0} value | Consumption: {consTotal:N0} value | Segments: {segCount} | Ammo: {ammoCount}");
    }

    bool ShowStatus() {
        Console.WriteLine($"\n=== Simulation Status ===");
        Console.WriteLine($"Seed: {_seed}");
        Console.WriteLine($"Size: {_size}");
        Console.WriteLine($"Current Time: {Game.TimeString(_game.CurrentTime)}");
        Console.WriteLine($"Scheduled Events: {_game.ScheduledEventCount}");
        Console.WriteLine($"Total Locations: {_game.Map.AllLocations.Count()}");

        var actorCount = CountActors();
        var settlementCount = CountSettlements();
        Console.WriteLine($"Total Actors: {actorCount}");
        Console.WriteLine($"Settlements: {settlementCount}");
        Console.WriteLine($"Mobile Actors: {actorCount - settlementCount}");
        return false;
    }

    bool ShowActorCensus() {
        var actors = GetAllActors().ToList();
        Console.WriteLine($"\n=== Actor Census at {Game.TimeString(_game.CurrentTime)} ===");
        Console.WriteLine($"Total: {actors.Count}");

        // By Role
        Console.WriteLine("\nBy Role:");
        var byRole = actors.OfType<Crawler>()
            .GroupBy(c => c.Role)
            .OrderByDescending(g => g.Count());
        foreach (var group in byRole) {
            Console.WriteLine($"  {group.Key,-12}: {group.Count(),4}");
        }

        // By Faction
        Console.WriteLine("\nBy Faction:");
        var byFaction = actors
            .GroupBy(a => a.Faction)
            .OrderByDescending(g => g.Count());
        foreach (var group in byFaction) {
            Console.WriteLine($"  {group.Key,-12}: {group.Count(),4}");
        }

        return false;
    }

    bool ShowEconomyReport() {
        Console.WriteLine($"\n=== Economy Report at {Game.TimeString(_game.CurrentTime)} ===");

        var (settlements, crawlers) = GetActorsByType();

        if (settlements.Count == 0) {
            Console.WriteLine("No settlements found.");
            return false;
        }

        // === WEALTH SUMMARY ===
        Console.WriteLine("\n--- Wealth Distribution ---");
        var settlementWealth = settlements.Select(s => (actor: s, wealth: GetWealth(s))).OrderByDescending(x => x.wealth).ToList();
        var crawlerWealth = crawlers.Select(c => (actor: c, wealth: GetWealth(c))).OrderByDescending(x => x.wealth).ToList();

        var totalSettlementWealth = settlementWealth.Sum(x => x.wealth);
        var totalCrawlerWealth = crawlerWealth.Sum(x => x.wealth);
        var totalWealth = totalSettlementWealth + totalCrawlerWealth;

        Console.WriteLine($"Total World Wealth: {totalWealth:N0}");
        Console.WriteLine($"  Settlements ({settlements.Count}): {totalSettlementWealth:N0} ({totalSettlementWealth / totalWealth * 100:F1}%)");
        Console.WriteLine($"  Crawlers ({crawlers.Count}): {totalCrawlerWealth:N0} ({totalCrawlerWealth / totalWealth * 100:F1}%)");

        // Top/bottom settlements by wealth
        Console.WriteLine("\nWealthiest Settlements:");
        foreach (var (actor, wealth) in settlementWealth.Take(5)) {
            Console.WriteLine($"  {actor.Name,-20}: {wealth,12:N0}");
        }
        if (settlementWealth.Count > 5) {
            Console.WriteLine("Poorest Settlements:");
            foreach (var (actor, wealth) in settlementWealth.TakeLast(3)) {
                Console.WriteLine($"  {actor.Name,-20}: {wealth,12:N0}");
            }
        }

        // Settlement wealth distribution stats
        var sWealth = settlementWealth.Select(x => x.wealth).ToList();
        var sStats = ComputeStats(sWealth);
        Console.WriteLine($"\nSettlement Stats: min {sStats.Min:N0}, max {sStats.Max:N0}, median {sStats.Median:N0}, mean {sStats.Mean:N0}, σ {sStats.StdDev:N0}");

        // Crawler wealth by role
        Console.WriteLine("\nCrawler Wealth by Role:");
        var byRole = crawlers.GroupBy(c => c.Role).OrderByDescending(g => g.Sum(c => GetWealth(c)));
        foreach (var group in byRole) {
            var roleWealth = group.Sum(c => GetWealth(c));
            var roleStats = ComputeStats(group.Select(GetWealth).ToList());
            Console.WriteLine($"  {group.Key,-12}: {group.Count(),3} actors, {roleWealth,10:N0} total (med {roleStats.Median:N0})");
        }

        // === COMMODITY STOCKS ===
        Console.WriteLine("\n--- Commodity Stocks (Settlement Totals) ---");
        var commodities = new[] {
            Commodity.Scrap, Commodity.Fuel, Commodity.Rations,
            Commodity.Ore, Commodity.Metal, Commodity.Chemicals,
            Commodity.Electronics, Commodity.Alloys, Commodity.Polymers
        };

        foreach (var commodity in commodities) {
            var stocks = settlements.Select(s => s.Supplies[commodity]).ToList();
            var total = stocks.Sum();
            var stats = ComputeStats(stocks);
            Console.WriteLine($"  {commodity,-12}: {total,10:N0} (per-settlement: med {stats.Median:N0}, σ {stats.StdDev:N0})");
        }

        // === PRICE ANALYSIS ===
        Console.WriteLine("\n--- Price Ranges Across Settlements ---");
        var priceCommodities = new[] {
            Commodity.Fuel, Commodity.Rations, Commodity.Scrap,
            Commodity.Metal, Commodity.Electronics, Commodity.Chemicals
        };

        foreach (var commodity in priceCommodities) {
            var prices = settlements.Select(s => commodity.MidAt(s.Location)).ToList();
            var pStats = ComputeStats(prices);
            Console.WriteLine($"  {commodity,-12}: {pStats.Min:F2} - {pStats.Max:F2} (spread {(pStats.Max - pStats.Min) / pStats.Mean * 100:F0}%)");
        }

        // === POPULATION ===
        Console.WriteLine("\n--- Population ---");
        var populations = settlements.Select(s => s.Supplies[Commodity.Crew]).ToList();
        var popStats = ComputeStats(populations);
        Console.WriteLine($"Total Population: {popStats.Sum:N0}");
        Console.WriteLine($"Per Settlement: min {popStats.Min:N0}, max {popStats.Max:N0}, median {popStats.Median:N0}");

        // === INDUSTRY STATUS ===
        Console.WriteLine("\n--- Industry Status ---");
        var allIndustry = settlements.SelectMany(s => s.IndustrySegments).ToList();
        var activeIndustry = allIndustry.Where(i => i.IsActive).ToList();
        var withRecipe = activeIndustry.Where(i => i.CurrentRecipe != null).ToList();
        var stalled = withRecipe.Where(i => i.IsStalled).ToList();
        var producing = withRecipe.Where(i => !i.IsStalled && i.ProductionProgress > 0).ToList();

        Console.WriteLine($"Total Industry Segments: {allIndustry.Count}");
        Console.WriteLine($"  Active: {activeIndustry.Count}");
        Console.WriteLine($"  With Recipe: {withRecipe.Count}");
        Console.WriteLine($"  Stalled: {stalled.Count}");
        Console.WriteLine($"  Producing: {producing.Count}");

        // Diagnostics: Why aren't recipes being assigned?
        if (withRecipe.Count == 0 && activeIndustry.Count > 0) {
            Console.WriteLine("\n  DIAGNOSTICS - Why no recipes assigned?");

            // Find a settlement with diverse industry (preferably with refineries)
            var sample = settlements.FirstOrDefault(s => s.IndustrySegments.Any(i => i.IndustryType == Production.IndustryType.Refinery))
                ?? settlements.First();
            Console.WriteLine($"  Sample Settlement: {sample.Name}");
            Console.WriteLine($"    PowerBalance: {sample.PowerBalance:F1} (gen={sample.TotalGeneration:F1} - drain={sample.TotalDrain:F1})");
            // Show power segments
            var reactors = sample.Segments.OfType<ReactorSegment>().ToList();
            var chargers = sample.Segments.OfType<ChargerSegment>().ToList();
            var drainByKind = sample.ActiveSegments.GroupBy(s => s.SegmentDef.SegmentKind)
                .Select(g => $"{g.Key}:{g.Sum(s => s.Drain):F0}")
                .ToList();
            Console.WriteLine($"    Power: {reactors.Count} reactors ({reactors.Sum(r => r.Generation):F0} gen), {chargers.Count} chargers ({chargers.Sum(c => c.Generation):F0} gen)");
            Console.WriteLine($"    Drain by kind: {string.Join(", ", drainByKind)}");
            Console.WriteLine($"    Crew: {sample.CrewInv:F0}");
            Console.WriteLine($"    Has Stock: {sample.Stock != null}");
            Console.WriteLine($"    Has ProductionAI: {sample.Components.Any(c => c.GetType().Name == "ProductionAIComponent")}");

            // Show key commodities
            Console.WriteLine($"    Key supplies: Ore={sample.Supplies[Commodity.Ore]:F0}, Biomass={sample.Supplies[Commodity.Biomass]:F0}, Silicates={sample.Supplies[Commodity.Silicates]:F0}");
            Console.WriteLine($"    Refined: Metal={sample.Supplies[Commodity.Metal]:F0}+{sample.Cargo[Commodity.Metal]:F0}, Chemicals={sample.Supplies[Commodity.Chemicals]:F0}+{sample.Cargo[Commodity.Chemicals]:F0}");
            Console.WriteLine($"    Fuel={sample.Supplies[Commodity.Fuel]:F0}, Scrap={sample.Supplies[Commodity.Scrap]:F0}+{sample.Cargo[Commodity.Scrap]:F0}");

            // Check all industry segments for runnable recipes
            var sampleIndustry = sample.IndustrySegments.ToList();
            Console.WriteLine($"\n  Sample has {sampleIndustry.Count} industry segments: {string.Join(", ", sampleIndustry.GroupBy(s => s.IndustryType).Select(g => $"{g.Key}:{g.Count()}"))}");
            Console.WriteLine($"  Checking all industry types for runnable recipes:");
            foreach (var industryType in Enum.GetValues<Production.IndustryType>()) {
                var segmentsOfType = sample.IndustrySegments.Where(s => s.IndustryType == industryType).ToList();
                if (segmentsOfType.Count == 0) continue;

                var segment = segmentsOfType.First();
                var recipes = Production.RecipeEx.RecipesFor(industryType).ToList();
                Console.WriteLine($"\n    {industryType} ({segmentsOfType.Count} segments, Drain={segment.Drain:F1}, Batch={segment.BatchSize:F1}):");

                foreach (var recipe in recipes) {
                    var missingInputs = recipe.Inputs
                        .Where(kv => sample.Supplies[kv.Key] + sample.Cargo[kv.Key] < kv.Value * segment.BatchSize)
                        .Select(kv => $"{kv.Key}({sample.Supplies[kv.Key] + sample.Cargo[kv.Key]:F0}/{kv.Value * segment.BatchSize:F0})")
                        .ToList();
                    var missingConsumables = recipe.Consumables
                        .Where(kv => sample.Supplies[kv.Key] < kv.Value * segment.BatchSize)
                        .Select(kv => $"{kv.Key}({sample.Supplies[kv.Key]:F0}/{kv.Value * segment.BatchSize:F1})")
                        .ToList();

                    bool canPower = sample.PowerBalance >= segment.Drain;
                    bool hasCrew = sample.CrewInv >= recipe.CrewRequired;
                    bool hasInputs = missingInputs.Count == 0;
                    bool hasConsumables = missingConsumables.Count == 0;

                    var reasons = new List<string>();
                    if (!canPower) reasons.Add($"Power({sample.PowerBalance:F0}<{segment.Drain:F0})");
                    if (!hasCrew) reasons.Add($"Crew({sample.CrewInv}<{recipe.CrewRequired})");
                    if (!hasInputs) reasons.Add($"Inputs({string.Join(",", missingInputs)})");
                    if (!hasConsumables) reasons.Add($"Consumables({string.Join(",", missingConsumables)})");

                    string status = reasons.Count == 0 ? "[CAN RUN]" : $"[BLOCKED: {string.Join("; ", reasons)}]";
                    Console.WriteLine($"      {recipe.Name}: {status}");
                }
            }

            // Industry type breakdown
            Console.WriteLine("\n  Industry by Type:");
            var byType = activeIndustry.GroupBy(i => i.IndustryType);
            foreach (var group in byType) {
                Console.WriteLine($"    {group.Key}: {group.Count()} segments");
            }
        }

        if (withRecipe.Count > 0) {
            Console.WriteLine("\nActive Recipes:");
            var byRecipe = withRecipe.GroupBy(i => i.CurrentRecipe!.Name);
            foreach (var group in byRecipe.OrderByDescending(g => g.Count())) {
                var stalledCount = group.Count(i => i.IsStalled);
                var avgProgress = group.Average(i => i.ProductionProgress);
                Console.WriteLine($"  {group.Key,-20}: {group.Count(),3} segments (stalled: {stalledCount}, avg progress: {avgProgress:P0})");
            }
        }

        // === PRODUCTION/CONSUMPTION ===
        Console.WriteLine("\n--- Gross Production (cumulative) ---");
        var prodLoc = _game.Map.AllLocations.First();
        var topProduced = Enum.GetValues<Commodity>()
            .Where(c => _game.GrossProduction[c] > 0)
            .OrderByDescending(c => _game.GrossProduction[c] * c.MidAt(prodLoc))
            .Take(10);
        foreach (var c in topProduced) {
            var amt = _game.GrossProduction[c];
            var val = amt * c.MidAt(prodLoc);
            Console.WriteLine($"  {c,-14}: {amt,12:N0} ({val,10:N0} value)");
        }

        Console.WriteLine("\n--- Gross Consumption (cumulative) ---");
        var topConsumed = Enum.GetValues<Commodity>()
            .Where(c => _game.GrossConsumption[c] > 0)
            .OrderByDescending(c => _game.GrossConsumption[c] * c.MidAt(prodLoc))
            .Take(10);
        foreach (var c in topConsumed) {
            var amt = _game.GrossConsumption[c];
            var val = amt * c.MidAt(prodLoc);
            Console.WriteLine($"  {c,-14}: {amt,12:N0} ({val,10:N0} value)");
        }

        // Net production
        Console.WriteLine("\n--- Net Production (produced - consumed) ---");
        var netByValue = Enum.GetValues<Commodity>()
            .Select(c => (commodity: c, net: _game.GrossProduction[c] - _game.GrossConsumption[c]))
            .Where(x => Math.Abs(x.net) > 0.1f)
            .OrderByDescending(x => x.net * x.commodity.MidAt(prodLoc))
            .Take(10);
        foreach (var (c, net) in netByValue) {
            var val = net * c.MidAt(prodLoc);
            var sign = net >= 0 ? "+" : "";
            Console.WriteLine($"  {c,-14}: {sign}{net,12:N0} ({sign}{val,10:N0} value)");
        }

        // Segments produced
        if (_game.SegmentProduction.Count > 0) {
            Console.WriteLine("\n--- Segments Manufactured ---");
            foreach (var (name, count) in _game.SegmentProduction.OrderByDescending(kv => kv.Value).Take(10)) {
                Console.WriteLine($"  {name,-20}: {count,5}");
            }
        }

        // Ammo consumed
        if (_game.AmmoConsumption.Count > 0) {
            Console.WriteLine("\n--- Ammunition Consumed ---");
            foreach (var (name, count) in _game.AmmoConsumption.OrderByDescending(kv => kv.Value)) {
                Console.WriteLine($"  {name,-14}: {count,8:N0}");
            }
        }

        return false;
    }

    bool ShowMap() {
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        Console.WriteLine(_game.Map.DumpMap());
        return false;
    }

    // Helper methods
    IEnumerable<IActor> GetAllActors() {
        return _game.Map.AllLocations
            .Select(loc => loc.GetEncounter())
            .SelectMany(enc => enc.Actors);
    }

    (List<Crawler> settlements, List<Crawler> crawlers) GetActorsByType() {
        var all = GetAllActors().OfType<Crawler>().ToList();
        var settlements = all.Where(a => a.Flags.HasFlag(ActorFlags.Settlement)).ToList();
        var crawlers = all.Where(a => !a.Flags.HasFlag(ActorFlags.Settlement)).ToList();
        return (settlements, crawlers);
    }

    float GetWealth(Crawler actor) {
        return actor.Supplies.ValueAt(actor.Location) + actor.Cargo.ValueAt(actor.Location);
    }

    int CountActors() => GetAllActors().Count();

    int CountSettlements() => GetAllActors().Count(a => a.Flags.HasFlag(ActorFlags.Settlement));

    record struct DistStats(float Sum, float Min, float Max, float Mean, float Median, float StdDev);

    DistStats ComputeStats(List<float> values) {
        if (values.Count == 0) return new DistStats(0, 0, 0, 0, 0, 0);

        var sorted = values.OrderBy(v => v).ToList();
        var sum = sorted.Sum();
        var min = sorted[0];
        var max = sorted[^1];
        var mean = sum / sorted.Count;
        var median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

        // Standard deviation
        var variance = sorted.Sum(v => (v - mean) * (v - mean)) / sorted.Count;
        var stdDev = (float)Math.Sqrt(variance);

        return new DistStats(sum, min, max, mean, median, stdDev);
    }
}
