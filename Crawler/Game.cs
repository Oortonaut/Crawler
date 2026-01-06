using System.Diagnostics;
using System.Diagnostics.Metrics;
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
        using var activity = Scope($"NewGame for '{crawlerName}' with H={H}");

        var game = new Game(seed);
        game.Construct(crawlerName, H);
        return game;
    }
    public static Game LoadGame(string saveFilePath) {
        using var activity = Scope($"LoadGame for '{saveFilePath}'");
        var deserializer = new YamlDotNet.Serialization.Deserializer();
        using var reader = new StreamReader(saveFilePath);
        var saveData = deserializer.Deserialize<SaveGameData>(reader);

        var game = new Game(saveData.RngState);
        game.Construct(saveData);
        return game;
    }
    ulong Seed = 0;
    Game(ulong seed) {
        _instance = this;
        Seed = seed;
        Rng = new XorShift(seed);

        // Initialize crawler scheduler
        scheduler = new (this);

        Welcome();
    }
    protected void Welcome() {
        Console.Write(Style.None.Format() + AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        Console.WriteLine($"Game Seed: {Seed}");
    }

    void Construct(string crawlerName, int H) {
        if (string.IsNullOrWhiteSpace(crawlerName)) {
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(crawlerName));
        }
        Gaussian = new GaussianSampler(Rng.Seed());
        _map = new Map(Rng.Seed(), H, H * 5 / 2);
        _map.Construct();
        var currentLocation = Map.GetStartingLocation();
        _player = currentLocation.GetEncounter().GeneratePlayerActor(Rng.Seed());
        _player.Name = crawlerName;
        _player.Location = currentLocation;

        // Schedule the player's first turn so the game loop starts (ProcessCrawlerArrivals will add them)
        // Use initial time from the encounter
        ScheduleTravel(_player, currentLocation.GetEncounter().EncounterTime, currentLocation);
    }
    void Construct(SaveGameData saveData) {
        Seed = saveData.Seed;
        Rng.SetState(saveData.RngState);
        Gaussian.SetRngState(saveData.GaussianRngState);
        Gaussian.SetPrimed(saveData.GaussianPrimed);
        Gaussian.SetZSin(saveData.GaussianZSin);

        quit = saveData.Quit;

        // Reconstruct the map from save data
        _map = saveData.Map.ToGameMap();

        // Find current location in the reconstructed map
        var currentLocation = Map.FindLocationByPosition(saveData.CurrentLocationPos);

        // Reconstruct player from save data
        _player = saveData.Player.ToGameCrawler(Map);
        _player.Location = currentLocation;

        // Schedule player at their saved time (ProcessCrawlerArrivals will add them to encounter)
        ScheduleTravel(_player, _player.Time, currentLocation);
    }

    bool quit = false;

    // Use generic Scheduler for crawler travel scheduling
    Scheduler<Game, ActorEvent, IActor, TimePoint> scheduler;

    // TODO: use this for log scoping too
    static Activity? Scope(string name, ActivityKind kind = ActivityKind.Internal) => null; // LogCat.Game.StartActivity(name, kind);
    static ILogger Log => LogCat.Log;
    static Meter Metrics => LogCat.GameMetrics;

    public void ScheduleTravel(IActor crawler, TimePoint time, Location location) {
        //Log.LogInformation($"ScheduleCrawlerTravel: {crawler.Name} will arrive to {location} at {time}");

        // Use the generic Scheduler
        var travelEvent = new ActorEvent.TravelEvent(crawler, time, location);
        Schedule(travelEvent);
    }
    public void ScheduleEncounter(IActor crawler, TimePoint time, string description, Action? pre = null, Action? post = null, int priority = ActorEvent.DefaultPriority) {
        //Log.LogInformation($"ScheduleCrawlerEncounter: {crawler.Name} scheduled {description} until {time}");

        // Use the generic Scheduler
        var encounterEvent = new ActorEvent.EncounterEvent(crawler, time, description, pre, post, priority);
        Schedule(encounterEvent);
    }

    public void Schedule(ActorEvent evt) {
        Log.LogInformation("Game.Schedule: event {Evt} , {Count} pending", evt, scheduler.Count);
        if (evt.Tag.HasFlag(ActorFlags.Destroyed)) {
            Log.LogInformation("Tried to schedule destroyed actor {Actor}", evt.Tag);
            return;
        }
        evt.OnStart();
        scheduler.Schedule(evt);
        //Log.LogInformation("Game.Schedule: after scheduling, scheduler has {Count} events", scheduler.Count);
    }

    public void Unschedule(IActor Actor) {
        scheduler.Unschedule(Actor);
    }

    HashSet<Encounter> updatedEncounters = new();

    void ProcessSchedule(TimePoint currentTime) {
        // Track which encounters we've already updated this tick to avoid redundant updates
        // Note: This HashSet is cleared when the player gets control (start of their turn)

        while (!quit && scheduler.Peek(out var evt, out var time)) {
            if (time!.Value > currentTime) {
                break;
            }

            var nextEvent = scheduler.Dequeue();
            if (nextEvent != null) {
                //Log.LogInformation($"ProcessCrawlerArrivals: {nextEvent.Tag.Name} arrived at {nextEvent.Time}");

                var actor = nextEvent.Tag;

                // Update the actor's encounter to current game time FIRST, but only once per encounter per turn
                // This spawns dynamic crawlers and fires EncounterTicked before player enters
                var encounter = actor.Location.GetEncounter();
                if (updatedEncounters.Add(encounter)) {
                    encounter.UpdateTo(currentTime);
                }

                // Then tick the crawler to the current game time
                actor.HandleEvent(nextEvent);

                // If this is the player, check if all pending events are processed before giving control
                if (actor == Player) {
                    // Continue processing any events that are scheduled at or before current time
                    // (e.g., dynamic crawlers spawned by UpdateTo with past arrival times)
                    if (scheduler.Peek(out var nextEvt, out var nextTime) && nextTime!.Value <= currentTime) {
                        // Don't give player control yet - continue processing past events
                        if (!actor.HasFlag(ActorFlags.Destroyed) && !scheduler.Any(actor)) {
                            Schedule(actor.NewIdleEvent());
                        }
                        continue;
                    }

                    // All past events processed - give player control
                    updatedEncounters.Clear();
                    //while (!ShowGameMenu()) { }
                    while (!ShowGameMenuWithContext()) { }
                }

                if (!actor.HasFlag(ActorFlags.Destroyed) && !scheduler.Any(actor)) {
                    Schedule(actor.NewIdleEvent());
                }
            }
        }
    }

    // Legacy menu system. Time advancement is handled by event scheduling, not return values.
    bool ShowGameMenu() {
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

        return ap;
    }

    /// <summary>
    /// Build complete MenuContext with all available actions.
    /// Organizes actions into categories for panel-based TUI display.
    /// </summary>
    MenuContext BuildMenuContext() {
        var context = new MenuContext { Agent = Player };

        // System actions
        context.SystemActions.AddRange([
            new MenuAction("M", _SectorMapName(), _ => SectorMap()),
            new MenuAction("G", "Global Map", _ => WorldMap()),
            new MenuAction("R", "Status Report", _ => Report()),
            new MenuAction("K", "Skip Turn/Wait", args => Turn(args)),
            new MenuAction("Q", "Save and Quit", _ => { Save(); return Quit(); }),
            new MenuAction("QQ", "Quit", _ => Quit()),
            new MenuAction("TEST", "Test Menu", _ => TestMenu()),
            new MenuAction("GG", "Give 1000 scrap", args => { Player.ScrapInv += 1000; return false; }, IsVisible: false),
            new MenuAction("GIVE", "Give commodity", args => GiveCommodity(args), IsVisible: false),
            new MenuAction("DMG", "Damage segment", args => DamageSegment(args), IsVisible: false),
        ]);

        // Register system actions
        foreach (var action in context.SystemActions) {
            context.RegisterAction(action.OptionCode, action);
        }

        // Player actions (segment management)
        BuildPlayerActions(context);

        // Navigation actions
        BuildNavigationActions(context);

        // Interaction groups from encounter
        BuildInteractionGroups(context);

        return context;
    }

    void BuildPlayerActions(MenuContext context) {
        // Power menu actions
        var segments = Player.Segments;
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;
            string toggleLabel = segment.Activated ? "Deactivate" : "Activate";
            var code = $"PP{index + 1}";
            var action = new MenuAction(
                code,
                $"{segment.StateName} - {toggleLabel}",
                _ => ToggleSegmentPower(index),
                segment.IsUsable,
                false // Hidden in main menu
            );
            context.PlayerActions.Add(action);
            context.RegisterAction(code, action);
        }

        // Packaging menu actions
        for (int i = 0; i < segments.Count; i++) {
            var segment = segments[i];
            int index = i;
            string packageLabel = segment.Packaged ? "Unpackage" : "Package";
            string label = $"{packageLabel} {segment.Name}";
            bool canPackage = segment.Packaged || segment.IsPristine;
            if (!canPackage) label += " (Damaged)";

            var code = $"PK{index + 1}";
            var action = new MenuAction(code, label, _ => TogglePackage(index), canPackage, false);
            context.PlayerActions.Add(action);
            context.RegisterAction(code, action);
        }

        // Add submenu actions for power and packaging
        context.PlayerActions.Insert(0, new MenuAction("PP", "Seg Power", _ => PowerMenu()));
        context.PlayerActions.Insert(1, new MenuAction("PK", "Seg Packaging", _ => PackagingMenu()));
        context.PlayerActions.Insert(2, new MenuAction("PT", "Supplies<->Cargo", _ => TradeInventoryMenu()));
        context.PlayerActions.Insert(3, new MenuAction("PR", "Repair", _ => RepairMenu()));

        context.RegisterAction("PP", context.PlayerActions[0]);
        context.RegisterAction("PK", context.PlayerActions[1]);
        context.RegisterAction("PT", context.PlayerActions[2]);
        context.RegisterAction("PR", context.PlayerActions[3]);

        // TODO: Add inventory transfer actions (PT*, PS*, PC*)
    }

    void BuildNavigationActions(MenuContext context) {
        var sector = PlayerLocation.Sector;
        bool isPinned = Player.Pinned();
        if (isPinned) {
            Player.Message("You are pinned.");
        }

        // Sector navigation
        var sectorGroup = new NavigationGroup {
            GroupName = "Sector Map",
            Prefix = "M"
        };

        int index = 0;
        foreach (var location in sector.Locations) {
            ++index;
            float dist = PlayerLocation.Distance(location);
            string locationName = location.EncounterName(Player);
            if (!location.HasEncounter || !Player.Visited(location)) {
                locationName = Style.MenuUnvisited.Format(locationName);
            }

            var (fuel, time) = Player.FuelTimeTo(location);
            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            var code = $"M{index}";
            var action = new MenuAction(
                code,
                $"To {locationName} {dist:F0}km {time:F0}h {fuel:F1}FU",
                _ => GoTo(location),
                enabled,
                false // Hidden in main menu, shown in sector map
            );
            sectorGroup.Destinations.Add(action);
            context.RegisterAction(code, action);
        }
        context.NavigationGroups.Add(sectorGroup);

        // Globe navigation
        var globeGroup = new NavigationGroup {
            GroupName = "Global Map",
            Prefix = "G"
        };

        foreach (var neighbor in sector.Neighbors) {
            if (!neighbor.Locations.Any()) continue;

            var locations = string.Join(", ", neighbor.Locations.Select(x => x.Type.ToString().Substring(0, 1)).Distinct());
            var destinations = neighbor.Settlements.ToList();
            if (destinations.Count == 0) continue;

            var location = destinations.MinBy(x => PlayerLocation.Distance(x))!;
            var (fuel, time) = Player.FuelTimeTo(location);

            // Calculate direction
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

            string neighborName = $"{neighbor.Name} ({neighbor.Terrain})";
            int visits = Player.Visits(neighbor);
            if (!neighbor.Locations.Any()) {
                neighborName = Style.MenuEmpty.Format(neighborName);
            } else if (visits == 0) {
                neighborName = Style.MenuUnvisited.Format(neighborName);
            } else if (visits == neighbor.Locations.Count) {
                neighborName = Style.MenuVisited.Format(neighborName);
            }

            bool enabled = !isPinned && fuel > 0 && fuel < Player.FuelInv;
            string desc = fuel > 0 ?
                $"to {neighborName} ({locations}) {time:F0}h Fuel: {fuel:F1}" :
                $"to {neighborName} ({locations})";

            var action = new MenuAction(dir, desc, _ => GoTo(location), enabled, false);
            globeGroup.Destinations.Add(action);
            context.RegisterAction(dir, action);
        }
        context.NavigationGroups.Add(globeGroup);
    }

    void BuildInteractionGroups(MenuContext context) {
        var encounter = PlayerEncounter();

        foreach (var (index, subject) in encounter.ActorsExcept(Player)
                     .OrderBy(a => a.Faction)
                     .Index()) {
            string prefix = "C" + (char)('A' + index);

            // Collect interactions
            var agentToSubject = Player.InteractionsWith(subject).ToList();
            var subjectToAgent = subject.InteractionsWith(Player).ToList();
            var interactions = agentToSubject.Concat(subjectToAgent).ToList();

            // Get trade offers if available
            List<TradeOffer>? tradeOffers = null;
            if (subject is ActorBase subjectBase) {
                var tradeComponent = subjectBase.Components.OfType<TradeOfferComponent>().FirstOrDefault();
                tradeOffers = tradeComponent?.GetOrCreateOffers();
            }

            // Create interaction actions
            var actions = new List<InteractionAction>();
            int counter = 1;
            foreach (var interaction in interactions) {
                var code = $"{prefix}{counter}";
                var action = new InteractionAction(interaction, code) {
                    IsVisible = interactions.Count <= 4 // Show directly if 4 or fewer
                };
                actions.Add(action);
                context.RegisterAction(code, action);
                counter++;
            }

            var group = new InteractionGroupEx {
                Subject = subject,
                Label = subject.Brief(Player),
                Prefix = prefix,
                Actions = actions,
                RawInteractions = interactions,
                TradeOffers = tradeOffers
            };

            context.InteractionGroups.Add(group);

            // Register group prefix for submenu
            var summaryAction = new MenuAction(
                prefix,
                group.Label,
                args => ShowInteractionSubmenu(group),
                group.HasEnabledActions
            );
            context.RegisterAction(prefix, summaryAction);
        }
    }

    bool ShowInteractionSubmenu(InteractionGroupEx group) {
        // Show submenu for this interaction group
        var items = new List<MenuItem> { MenuItem.Cancel };
        items.AddRange(group.Actions.Select((a, i) =>
            new ActionMenuItem(
                a.OptionCode,
                a.Description,
                args => a.Perform(args),
                a.IsEnabled ? EnableArg.Enabled : EnableArg.Disabled,
                ShowArg.Show
            )
        ));
        var (selected, ap) = CrawlerEx.MenuRun($"{group.Label}", items.ToArray());
        return ap;
    }

    /// <summary>
    /// Alternative game menu using InteractionContext and IMenuRenderer.
    /// Demonstrates new architecture for future TUI migration.
    /// Currently unused - keeping old ShowGameMenu() for stability.
    /// </summary>
    bool ShowGameMenuWithContext() {
        Look();

        // Build complete menu context
        var context = BuildMenuContext();

        // Use console renderer (could be injected for TUI in future)
        var renderer = new ConsoleMenuRenderer();
        var selection = renderer.Render(context, "Game Menu", "");

        // Check if menu action scheduled something and wants to exit
        if (selection.ShouldExitMenu) {
            return true;
        }

        // For interactions, perform and use return value
        bool ap = false;
        if (selection.SelectedInteraction != null) {
            ap = selection.SelectedInteraction.Perform(selection.Arguments);
        }

        return ap;
    }

    public void Run() {
        TimePoint lastTime = TimePoint.Zero;
        while (!quit) {
            // Get the next crawler event time
            if (!scheduler.Peek(out var evt, out var nextTime)) {
                Log.LogInformation("Game.Run: scheduler.Peek returned false, breaking");
                break;
            }

            //Log.LogInformation("Game.Run: processing events at {Time}, scheduler has {Count} events", Game.TimeString(nextTime!.Value), scheduler.Count);

            if (nextTime!.Value < lastTime) {
                throw new InvalidOperationException($"CRITICAL: Time moved backwards! Current={TimeString(lastTime)}, Next={TimeString(nextTime.Value)}");
            }
            lastTime = nextTime!.Value;

            using var activity = Scope($"Game::Turn")?
                .AddTag("start", nextTime.Value);

            ProcessSchedule(nextTime.Value);

            // Log.LogInformation("Game.Run: after ProcessSchedule, scheduler has {Count} events, quit={Quit}, won={Won}, lost={Lost}", scheduler.Count, quit, PlayerWon, PlayerLost);

            if (PlayerWon || PlayerLost) {
                Save();
                break;
            }
        }
        Log.LogInformation("Game.Run: exited loop, quit={Quit}, scheduler.Any={Count}", quit, scheduler.Count);
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

    bool Report() {
        Player.Message(Player.Report());
        return false;
    }

    bool SegmentDefinitionsReport() {
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
        return false;
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
    bool GoTo(Location loc) {
        Player.Travel(loc);
        return false;
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

    public static string DateString(TimePoint t) => t.ToString("D");
    public string DateString() => DateString(Player.Time);

    public static string TimeString(TimePoint t) => t.ToString("T");

    public string TimeString() => TimeString(Player.Time);

    bool Look() {
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        Console.WriteLine(PlayerLocation.Sector.Look() + " " + PlayerLocation.PosString);

        Console.Write($"DATE {DateString()} TIME {TimeString()}   ");
        Console.WriteLine(PlayerEncounter().ViewFrom(Player));
        CrawlerEx.ShowMessages();
        CrawlerEx.ClearMessages();
        return false;
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
    bool Turn(string args) {
        float seconds = float.TryParse(args, out float parsed) ? parsed * 60 : 3600;
        var duration = TimeDuration.FromSeconds((long)seconds);
        Player.ConsumeTime("Wait", 0, duration);
        return true;
    }

    public void RegisterEncounter(Encounter encounter) {
        allEncounters.Add(encounter);
    }

    public IEnumerable<Encounter> Encounters() {
        return allEncounters;
    }

    bool PlayerWon => false;
    bool PlayerLost => !string.IsNullOrEmpty(Player?.EndMessage);

    bool SectorMap() {
        var sector = PlayerLocation.Sector;
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
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
    bool WorldMap() {
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
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
        new ActionMenuItem("Q", "Save and Quit", _ => { Save(); return Quit(); }),
        new ActionMenuItem("QQ", "Quit ", _ => Quit()),
        new ActionMenuItem("TEST", "Test Menu", _ => TestMenu()),
        new ActionMenuItem("GG", "", args => {
            Player.ScrapInv += 1000;
            return false;
        }, EnableArg.Enabled, ShowArg.Hide),
        new ActionMenuItem("GIVE", "", args => GiveCommodity(args), EnableArg.Enabled, ShowArg.Hide),
        new ActionMenuItem("DMG", "", args => DamageSegment(args), EnableArg.Enabled, ShowArg.Hide),
    ];
    IEnumerable<MenuItem> PlayerMenuItems() {
        yield return MenuItem.Sep;
        yield return new MenuItem("", Style.MenuTitle.Format("Player Menu"));
        yield return new ActionMenuItem("PP", "Seg Power", _ => PowerMenu());
        yield return new ActionMenuItem("PK", "Seg Packaging", _ => PackagingMenu());
        yield return new ActionMenuItem("PT", "Supplies<->Cargo", _ => TradeInventoryMenu());
        yield return new ActionMenuItem("PR", "Repair", _ => RepairMenu());

        foreach (var item in PowerMenuItems(ShowArg.Hide)) {
            yield return item;
        }
        foreach (var item in PackagingMenuItems(ShowArg.Hide)) {
            yield return item;
        }
        foreach (var item in TradeInventoryMenuItems(ShowArg.Hide)) {
            yield return item;
        }
        foreach (var item in RepairMenuItems(ShowArg.Hide)) {
            yield return item;
        }
    }

    bool PowerMenu() {
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

    bool ToggleSegmentPower(int index) {
        var segment = Player.Segments[index];
        segment.Activated = !segment.Activated;
        Player.Message($"{segment.Name} {(segment.Activated ? "activated" : "deactivated")}");
        Player.UpdateSegmentCache();
        return true;
    }

    bool RepairMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Repair Menu", [
            MenuItem.Cancel,
            .. RepairMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> RepairMenuItems(ShowArg show = ShowArg.Show) {
        var repairMode = Player.Components.OfType<AutoRepairComponent>().FirstOrDefault()?.RepairMode ?? RepairMode.Off;
        yield return new ActionMenuItem("PR0", $"Repair Off {(repairMode == RepairMode.Off ? "[Active]" : "")}", _ => SetRepairMode(RepairMode.Off), EnableArg.Enabled, show);
        yield return new ActionMenuItem("PR1", $"Repair Lowest {(repairMode == RepairMode.RepairLowest ? "[Active]" : "")}", _ => SetRepairMode(RepairMode.RepairLowest), EnableArg.Enabled, show);
        yield return new ActionMenuItem("PR2", $"Repair Highest {(repairMode == RepairMode.RepairHighest ? "[Active]" : "")}", _ => SetRepairMode(RepairMode.RepairHighest), EnableArg.Enabled, show);
    }

    bool SetRepairMode(RepairMode mode) {
        var repairComponent = Player.Components.OfType<AutoRepairComponent>().FirstOrDefault();
        if (repairComponent == null) {
            Player.Message($"No self repair system.");
            return false;
        } else if (repairComponent.RepairMode == mode) {
            Player.Message($"Repair mode unchanged: {mode}");
            return true;
        } else {
            repairComponent.RepairMode = mode;
            Player.Message($"Repair mode set to: {mode}");
            return true;
        }
    }

    bool PackagingMenu() {
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

    bool TogglePackage(int index) {
        var segment = Player.Segments[index];
        segment.Packaged = !segment.Packaged;
        Player.Message($"{segment.Name} {(segment.Packaged ? "packaged" : "unpackaged")}");
        Player.UpdateSegmentCache();
        return true;
    }

    bool TradeInventoryMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Trade Cargo Menu", [
            MenuItem.Cancel,
            .. TradeInventoryMenuItems(),
        ]);
        return ap;
    }

    IEnumerable<MenuItem> TradeInventoryMenuItems(ShowArg showOption = ShowArg.Show) {
        // Commodities
        foreach (var pair in Player.Supplies.Pairs) {
            var commodity = pair.Item1;
            int i = ( int ) commodity;
            var amt = pair.Item2;
            if (amt > 0) {
                yield return new ActionMenuItem($"PC{i + 1}C", $"{commodity} to Cargo", args => MoveToCargo(commodity, args), EnableArg.Enabled, showOption);
            }
            var amt2 = Player.Cargo[commodity];
            if (amt2 > 0) {
                yield return new ActionMenuItem($"PC{i + 1}S", $"{commodity} to Supplies", args => MoveFromCargo(commodity, args), EnableArg.Enabled, showOption);
            }
        }

        // Working segments that can be packaged
        var workingSegments = Player.Segments.ToList();
        for (int i = 0; i < workingSegments.Count; i++) {
            var segment = workingSegments[i];
            yield return new ActionMenuItem($"PW{i + 1}S", $"Package {segment.StateName} to Supplies", _ => PackageToSupplies(segment), EnableArg.Enabled, showOption);
            yield return new ActionMenuItem($"PW{i + 1}C", $"Package {segment.StateName} to Cargo", _ => PackageToCargo(segment), EnableArg.Enabled, showOption);
        }

        // Packaged segments in supplies (all should be packaged now)
        var suppliesSegments = Player.Supplies.Segments.ToList();
        for (int i = 0; i < suppliesSegments.Count; i++) {
            var segment = suppliesSegments[i];
            yield return new ActionMenuItem($"PS{i + 1}I", $"Install {segment.StateName}", _ => InstallSegment(segment), EnableArg.Enabled, showOption);
            yield return new ActionMenuItem($"PS{i + 1}C", $"{segment.StateName} to Cargo", _ => MoveToCargo(segment), EnableArg.Enabled, showOption);
        }

        // Packaged segments in cargo that can be moved or installed
        var cargoSegments = Player.Cargo.Segments.ToList();
        for (int i = 0; i < cargoSegments.Count; i++) {
            var segment = cargoSegments[i];
            yield return new ActionMenuItem($"PC{i + 1}I", $"Install {segment.StateName}", _ => InstallSegment(segment), EnableArg.Enabled, showOption);
            yield return new ActionMenuItem($"PC{i + 1}S", $"{segment.StateName} to Supplies", _ => MoveFromCargo(segment), EnableArg.Enabled, showOption);
        }
    }

    bool MoveToCargo(Segment segment) {
        Player.Supplies.Remove(segment);
        Player.Cargo.Add(segment);
        Player.Message($"{segment.Name} moved to cargo");
        Player.UpdateSegmentCache();
        return true;
    }

    bool MoveToCargo(Commodity commodity, string amount) {
        var amt = Player.Cargo[commodity];

        if (float.TryParse(amount, out float parsed)) {
            amt = Math.Min(parsed, amt);
        }

        return MoveToCargo(commodity, amt);
    }

    bool MoveToCargo(Commodity commodity, float amount) {
        Player.Supplies[commodity] -= amount;
        Player.Cargo[commodity] += amount;
        Player.Message($"{amount} {commodity} moved to cargo");
        return true;
    }

    bool MoveFromCargo(Segment segment) {
        Player.Cargo.Remove(segment);
        Player.Supplies.Add(segment);
        Player.Message($"{segment.Name} returned from cargo");
        Player.UpdateSegmentCache();
        return true;
    }

    bool MoveFromCargo(Commodity commodity, string amount) {
        var amt = Player.Cargo[commodity];
        if (float.TryParse(amount, out float parsed)) {
            amt = Math.Min(parsed, amt);
        }

        return MoveFromCargo(commodity, amt);
    }

    bool MoveFromCargo(Commodity commodity, float amount) {
        Player.Cargo[commodity] -= amount;
        Player.Supplies[commodity] += amount;
        Player.Message($"{amount} {commodity} moved to cargo");
        return true;
    }

    bool InstallSegment(Segment segment) {
        Player.InstallSegment(segment);
        Player.Message($"{segment.Name} installed");
        return true;
    }

    bool PackageToSupplies(Segment segment) {
        Player.PackageToSupplies(segment);
        Player.Message($"{segment.Name} packaged to supplies");
        return true;
    }

    bool PackageToCargo(Segment segment) {
        Player.PackageToCargo(segment);
        Player.Message($"{segment.Name} packaged to cargo");
        return true;
    }

    bool Save() {
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
            return true;
        } catch (Exception ex) {
            Console.WriteLine($"Failed to save game: {ex.Message}");
            return false;
        }
    }

    bool Quit() {
        Console.WriteLine("Quitting...");
        quit = true;
        return false;
    }

    bool GiveCommodity(string args) {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            Player.Message("Usage: give <commodity> <amount>");
            return false;
        }

        string commodityPrefix = parts[0].ToLower();
        if (!float.TryParse(parts[1], out float amount)) {
            Player.Message($"Invalid amount: {parts[1]}");
            return false;
        }

        // Find first commodity that starts with the input
        var matchingCommodity = Enum.GetValues<Commodity>()
            .FirstOrDefault(c => c.ToString().ToLower().StartsWith(commodityPrefix));

        if (matchingCommodity == default(Commodity) && !commodityPrefix.StartsWith("scrap")) {
            Player.Message($"No commodity found starting with '{parts[0]}'");
            return false;
        }

        Player.Supplies[matchingCommodity] += amount;
        Player.Message($"Added {amount} {matchingCommodity}");
        return true;
    }

    bool DamageSegment(string args) {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            Player.Message("Usage: DMG <segment index> <damage amount>");
            return false;
        }

        if (!int.TryParse(parts[0], out int segmentIndex)) {
            Player.Message($"Invalid segment index: {parts[0]}");
            return false;
        }

        if (!int.TryParse(parts[1], out int damageAmount)) {
            Player.Message($"Invalid damage amount: {parts[1]}");
            return false;
        }

        if (segmentIndex < 0 || segmentIndex >= Player.Segments.Count) {
            Player.Message($"Segment index {segmentIndex} out of range (0-{Player.Segments.Count - 1})");
            return false;
        }

        var segment = Player.Segments[segmentIndex];
        segment.Hits += damageAmount;
        Player.Message($"Applied {damageAmount} damage to {segment.Name} (HP: {segment.Health:F1}/{segment.MaxHealth})");
        Player.UpdateSegmentCache();
        return true;
    }

    bool TestMenu() {
        var (selected, ap) = CrawlerEx.MenuRun("Test Menu", [
            MenuItem.Cancel,
            new ActionMenuItem("SD", "Segment Definitions", _ => SegmentDefinitionsReport()),
            new ActionMenuItem("PS", "Price Statistics", _ => PriceStatisticsReport()),
            new ActionMenuItem("XS", "XorShift Tests", _ => XorShiftTests()),
        ]);
        return ap;
    }

    bool XorShiftTests() {
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
        Console.WriteLine("XorShift Random Number Generator - Comprehensive Unit Tests");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();

        int passedTests = 0;
        int totalTests = 0;

        void RunTest(string testName, Func<bool> test) {
            totalTests++;
            Console.Write($"[{totalTests,2}] {testName,-50} ");
            try {
                bool passed = test();
                if (passed) {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASS");
                    Console.ResetColor();
                    passedTests++;
                } else {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAIL");
                    Console.ResetColor();
                }
            } catch (Exception ex) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Test 1: Determinism - same seed produces same sequence
        RunTest("Determinism: Same seed produces same sequence", () => {
            var rng1 = new XorShift(12345);
            var rng2 = new XorShift(12345);
            for (int i = 0; i < 100; i++) {
                if (rng1.Next() != rng2.Next()) return false;
            }
            return true;
        });

        // Test 2: Different seeds produce different sequences
        RunTest("Different seeds produce different sequences", () => {
            var rng1 = new XorShift(12345);
            var rng2 = new XorShift(54321);
            int matches = 0;
            for (int i = 0; i < 100; i++) {
                if (rng1.Next() == rng2.Next()) matches++;
            }
            return matches < 5; // Very unlikely to match more than 5 times
        });

        // Test 3: NextInt range [0, int.MaxValue]
        RunTest("Next() returns values in [0, int.MaxValue]", () => {
            var rng = new XorShift(999);
            for (int i = 0; i < 1000; i++) {
                int val = rng.Next();
                if (val < 0 || val > int.MaxValue) return false;
            }
            return true;
        });

        // Test 4: NextInt(max) range [0, max)
        RunTest("NextInt(max) returns values in [0, max)", () => {
            var rng = new XorShift(777);
            int max = 100;
            for (int i = 0; i < 1000; i++) {
                int val = rng.NextInt(max);
                if (val < 0 || val >= max) return false;
            }
            return true;
        });

        // Test 5: NextInt(min, max) range [min, max)
        RunTest("NextInt(min, max) returns values in [min, max)", () => {
            var rng = new XorShift(888);
            int min = 50, max = 150;
            for (int i = 0; i < 1000; i++) {
                int val = rng.NextInt(min, max);
                if (val < min || val >= max) return false;
            }
            return true;
        });

        // Test 6: NextDouble() range [0, 1)
        RunTest("NextDouble() returns values in [0, 1)", () => {
            var rng = new XorShift(111);
            for (int i = 0; i < 1000; i++) {
                double val = rng.NextDouble();
                if (val < 0.0 || val >= 1.0) return false;
            }
            return true;
        });

        // Test 7: NextSingle() range [0, 1)
        RunTest("NextSingle() returns values in [0, 1)", () => {
            var rng = new XorShift(222);
            for (int i = 0; i < 1000; i++) {
                float val = rng.NextSingle();
                if (val < 0.0f || val >= 1.0f) return false;
            }
            return true;
        });

        // Test 8: NextDouble(max) range [0, max)
        RunTest("NextDouble(max) returns values in [0, max)", () => {
            var rng = new XorShift(333);
            double max = 100.0;
            for (int i = 0; i < 1000; i++) {
                double val = rng.NextDouble(max);
                if (val < 0.0 || val >= max) return false;
            }
            return true;
        });

        // Test 9: NextDouble(min, max) range [min, max)
        RunTest("NextDouble(min, max) returns values in [min, max)", () => {
            var rng = new XorShift(444);
            double min = 50.0, max = 150.0;
            for (int i = 0; i < 1000; i++) {
                double val = rng.NextDouble(min, max);
                if (val < min || val >= max) return false;
            }
            return true;
        });

        // Test 10: NextBytes fills array correctly
        RunTest("NextBytes() fills array correctly", () => {
            var rng = new XorShift(555);
            byte[] buffer = new byte[100];
            rng.NextBytes(buffer);
            // Check that not all bytes are zero (very unlikely)
            int nonZero = 0;
            foreach (var b in buffer) {
                if (b != 0) nonZero++;
            }
            return nonZero > 50; // At least half should be non-zero
        });

        // Test 11: NextBytes determinism
        RunTest("NextBytes() is deterministic", () => {
            var rng1 = new XorShift(666);
            var rng2 = new XorShift(666);
            byte[] buffer1 = new byte[100];
            byte[] buffer2 = new byte[100];
            rng1.NextBytes(buffer1);
            rng2.NextBytes(buffer2);
            return buffer1.SequenceEqual(buffer2);
        });

        // Test 12: Branch creates independent generator
        RunTest("Branch() creates independent generator", () => {
            var rng1 = new XorShift(777);
            var rng2 = rng1.Branch();
            // After branching, parent and child should produce different sequences
            int matches = 0;
            for (int i = 0; i < 100; i++) {
                if (rng1.Next() == rng2.Next()) matches++;
            }
            return matches < 10; // Should be very few matches
        });

        // Test 13: Seed() creates reproducible branch
        RunTest("Seed() creates reproducible branch", () => {
            var rng1 = new XorShift(888);
            ulong seed = rng1.Seed();
            var branch1 = new XorShift(seed);
            var branch2 = new XorShift(seed);
            for (int i = 0; i < 100; i++) {
                if (branch1.Next() != branch2.Next()) return false;
            }
            return true;
        });

        // Test 14: GetState/SetState preserves sequence
        RunTest("GetState/SetState preserves sequence", () => {
            var rng1 = new XorShift(999);
            for (int i = 0; i < 50; i++) rng1.Next(); // Advance state
            ulong state = rng1.GetState();
            int val1 = rng1.Next();

            var rng2 = new XorShift(1);
            rng2.SetState(state);
            int val2 = rng2.Next();

            return val1 == val2;
        });

        // Test 15: Division operator creates new generator
        RunTest("Division operator creates new generator", () => {
            var rng1 = new XorShift(1111);
            var rng2 = new XorShift(2222);
            var rng3 = rng1 / rng2;
            // Should be a valid generator producing values
            int val = rng3.Next();
            return val >= 0 && val <= int.MaxValue;
        });

        // Test 16: Division operator with int
        RunTest("Division operator with int creates new generator", () => {
            var rng1 = new XorShift(1234);
            var rng2 = rng1 / 42;
            int val = rng2.Next();
            return val >= 0 && val <= int.MaxValue;
        });

        // Test 17: Division operator with string
        RunTest("Division operator with string creates new generator", () => {
            var rng1 = new XorShift(5678);
            var rng2 = rng1 / "test";
            int val = rng2.Next();
            return val >= 0 && val <= int.MaxValue;
        });

        // Test 18: NextUint64 produces full range values
        RunTest("NextUint64() produces values across full range", () => {
            var rng = new XorShift(1357);
            bool hasLarge = false; // > 2^63
            for (int i = 0; i < 1000; i++) {
                ulong val = rng.NextUint64();
                if (val > (ulong)long.MaxValue) hasLarge = true;
            }
            return hasLarge;
        });

        // Test 19: NextUint64(max) stays in bounds
        RunTest("NextUint64(max) returns values in [0, max)", () => {
            var rng = new XorShift(2468);
            ulong max = 1000000;
            for (int i = 0; i < 1000; i++) {
                ulong val = rng.NextUint64(max);
                if (val >= max) return false;
            }
            return true;
        });

        // Test 20: GaussianSampler determinism
        RunTest("GaussianSampler is deterministic", () => {
            var gauss1 = new GaussianSampler(12345);
            var gauss2 = new GaussianSampler(12345);
            for (int i = 0; i < 100; i++) {
                if (Math.Abs(gauss1.NextDouble() - gauss2.NextDouble()) > 1e-10) return false;
            }
            return true;
        });

        // Test 21: GaussianSampler produces reasonable values
        RunTest("GaussianSampler produces values in reasonable range", () => {
            var gauss = new GaussianSampler(54321);
            int inRange = 0;
            for (int i = 0; i < 1000; i++) {
                double val = gauss.NextDouble();
                if (val >= -4.0 && val <= 4.0) inRange++; // ~99.99% should be in this range
            }
            return inRange > 990; // Allow for rare outliers
        });

        // Test 22: GaussianSampler mean near zero
        RunTest("GaussianSampler has mean near zero", () => {
            var gauss = new GaussianSampler(99999);
            double sum = 0;
            int count = 10000;
            for (int i = 0; i < count; i++) {
                sum += gauss.NextDouble();
            }
            double mean = sum / count;
            return Math.Abs(mean) < 0.1; // Mean should be close to 0
        });

        // Test 23: GaussianSampler state save/restore
        RunTest("GaussianSampler state save/restore works", () => {
            var gauss1 = new GaussianSampler(11111);
            for (int i = 0; i < 25; i++) gauss1.NextDouble(); // Advance state

            ulong rngState = gauss1.GetRngState();
            bool primed = gauss1.GetPrimed();
            double zSin = gauss1.GetZSin();

            double val1 = gauss1.NextDouble();

            var gauss2 = new GaussianSampler(1);
            gauss2.SetRngState(rngState);
            gauss2.SetPrimed(primed);
            gauss2.SetZSin(zSin);

            double val2 = gauss2.NextDouble();

            return Math.Abs(val1 - val2) < 1e-10;
        });

        // Test 24a: GaussianSampler.CDF is monotonic
        RunTest("GaussianSampler.CDF is monotonic", () => {
            double lastP = 0.0;
            for (double x = -5.0; x <= 5.0; x += 1.0/16) {
                double p = GaussianSampler.CDF(x);
                if (p < lastP) return false;
                lastP = p;
            }
            return true;
        });
        // Test 24b: GaussianSampler.Quantile is inverse of Gaussian CDF
        RunTest("GaussianSampler.Quantile is inverse of Gaussian CDF", () => {
            for (double t = 1.0 / 256; t < 1.0; t += 1.0 / 128) {
                double x = GaussianSampler.Quantile(t);
                double p = GaussianSampler.CDF(x);
                if (Math.Abs(p - t) > 0.01)
                    return false;
            }
            return true;
        });

        // Test 25: GaussianSampler with mean and stddev
        RunTest("GaussianSampler NextDouble(mean, stddev) works", () => {
            var gauss = new GaussianSampler(77777);
            double mean = 100.0;
            double stddev = 15.0;
            double sum = 0;
            int count = 10000;
            for (int i = 0; i < count; i++) {
                sum += gauss.NextDouble(mean, stddev);
            }
            double actualMean = sum / count;
            return Math.Abs(actualMean - mean) < 2.0; // Within 2 units of target mean
        });

        // Test 26: NextInt edge case - max = 1
        RunTest("NextInt(1) always returns 0", () => {
            var rng = new XorShift(123);
            for (int i = 0; i < 100; i++) {
                if (rng.NextInt(1) != 0) return false;
            }
            return true;
        });

        // Test 27: Zero seed gets remapped
        RunTest("Zero seed gets remapped to non-zero", () => {
            var rng = new XorShift(0);
            // Should still produce valid values
            int val = rng.Next();
            return val >= 0 && val <= int.MaxValue;
        });

        // Test 28: MixState produces different values
        RunTest("MixState transforms state correctly", () => {
            ulong state1 = 12345;
            ulong state2 = XorShift.MixState(state1);
            ulong state3 = XorShift.MixState(state2);
            return state1 != state2 && state2 != state3 && state1 != state3;
        });

        // Test 29: Copy constructor works
        RunTest("Copy constructor creates identical generator", () => {
            var rng1 = new XorShift(9876);
            for (int i = 0; i < 50; i++) rng1.Next();
            var rng2 = new XorShift(rng1);
            for (int i = 0; i < 100; i++) {
                if (rng1.Next() != rng2.Next()) return false;
            }
            return true;
        });

        // Test 30: Statistical uniformity test for NextInt
        RunTest("NextInt(100) has roughly uniform distribution", () => {
            var rng = new XorShift(13579);
            int[] buckets = new int[10];
            int samples = 10000;
            for (int i = 0; i < samples; i++) {
                int val = rng.NextInt(100);
                buckets[val / 10]++;
            }
            // Each bucket should have roughly samples/10 = 1000 values
            // Allow 20% deviation
            int expected = samples / 10;
            foreach (int count in buckets) {
                if (count < expected * 0.8 || count > expected * 1.2) return false;
            }
            return true;
        });

        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine($"Test Results: {passedTests}/{totalTests} passed");

        if (passedTests == totalTests) {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("ALL TESTS PASSED!");
            Console.ResetColor();
        } else {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{totalTests - passedTests} test(s) failed.");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey(true);
        return false;
    }

    bool PriceStatisticsReport() {
        XorShift rng = new XorShift(1);
        Console.Write(AnsiEx.CursorPosition(1, 1) + AnsiEx.ClearScreen);
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

                    // Gather statistics from any crawler's trade offers
                    foreach (var crawler in encounter.Actors.OfType<Crawler>()) {
                        crawlersProcessed++;
                        var tradeOffers = crawler.MakeTradeOffers(Rng.Seed(), 1.0f);

                        // Track which commodities this trader offers
                        var offeredCommodities = new HashSet<Commodity>();

                        foreach (var offer in tradeOffers.Where(o => o.IsCommodity)) {
                            var commodity = offer.Commodity!.Value;
                            if (offer.Direction == TradeDirection.Sell) {
                                // Crawler is selling to player (player buys)
                                buyPrices[commodity].Add(offer.PricePerUnit);
                                offeredCommodities.Add(commodity);
                            } else {
                                // Crawler is buying from player (player sells)
                                sellPrices[commodity].Add(offer.PricePerUnit);
                                offeredCommodities.Add(commodity);
                            }
                        }

                        // Increment trader count per commodity
                        foreach (var commodity in offeredCommodities.Distinct()) {
                            if (crawler.Role == Roles.Trader) {
                                tradeTraders[commodity]++;
                            } else if (crawler.Role == Roles.Bandit) {
                                banditTraders[commodity]++;
                            }
                        }

                        if (offeredCommodities.Count > 0) {
                            if (crawler.Role == Roles.Trader) {
                                totalTradeTraders++;
                            } else if (crawler.Role == Roles.Bandit) {
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
        return false;
    }

    double CalculateStandardDeviation(List<float> values) {
        if (values.Count <= 1) return 0;

        double mean = values.Average();
        double sumOfSquares = values.Sum(val => Math.Pow(val - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }

    Crawler? _player;
    public Crawler Player => _player!;

    List<Encounter> allEncounters = new();
    // Accessor methods for save/load
    public ulong GetSeed() => Seed;
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
