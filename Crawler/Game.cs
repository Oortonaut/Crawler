namespace Crawler;

class Game {
    public Game() {
        //var locationSpec = new LocationSpec {
        //    Name = "parched, windy desert",
        //    Description = "The native lichens form thick mats.",
        //};

        _console = new CrawlerConsole(null, CrawlerConsole.ConsoleFlags.Resize);
        Console.Write(Style.None.Format() + CrawlerEx.CursorPosition(1, 1) + CrawlerEx.ClearScreen);
        Console.WriteLine(Logo);

        CurrentLocation = new Location("Spawn point", "You started here.", TerrainType.Flat, SimpleEncounterType.None, 1000, 0.5f, 0.5f);
        Encounter = new Encounter(CurrentLocation);
        _crawler = Encounter.Generate(Faction.Player);
        Encounter.Generate(Faction.Trade);
        Encounter.Generate(Faction.Bandit);
        GenerateMap();
    }

    Map map { get; set; }
    void GenerateMap() {
        map = new Map(6, 12);
        Console.WriteLine(map.DumpCell(0, 3, 9, 16));
        Console.WriteLine();
        Console.WriteLine(map.DumpCell(1, 3, 9, 16));
        Console.WriteLine();
        Console.WriteLine(map.DumpCell(2, 3, 9, 16));
        Console.WriteLine();
        Console.WriteLine(map.DumpCell(3, 3, 9, 16));
        Console.WriteLine();
        Console.WriteLine(map.DumpCell(4, 3, 9, 16));
        Console.WriteLine();
        Console.WriteLine(map.DumpCell(5, 3, 9, 16));
        Console.WriteLine();
    }

    bool quit = false;
    public void Run() {
        Console.WriteLine($"Welcome to {Style.Name.Format(_crawler.Name)}.");

        Look();

        while (!quit) {
            var selected = GameMenu();

            while (AP <= 0) {
                EnemyTurn();
                AP += TurnAP;
            }

            if (PlayerWon || PlayerLost) {
                Save();
                break;
            }
        }
        if (PlayerLost) {
            Console.WriteLine($"You lost! {_crawler?.FailState}");
        } else if (PlayerWon) {
            Console.WriteLine("You won!");
        } else {
            Console.WriteLine("Game Over!");
        }

        Console.WriteLine("Thanks for playing!");
    }

    int AP = 1;
    int TurnAP = 1;
    Location CurrentLocation { get; set; }
    TerrainType CurrentTerrain => CurrentLocation.Terrain;
    int Turn(int ap = 1) {
        return ap;
    }

    int Report() {
        Console.WriteLine(_crawler.Report());
        return 0;
    }

    IEnumerable<MenuItem> EncounterMenuItems() {
        return Encounter?.MenuItems(_crawler) ?? [];
    }

    Encounter? Encounter { get; set; }

    int Look() {
        Console.WriteLine(CurrentLocation.Look());
        if (Encounter != null) {
            Console.WriteLine(Encounter.Look(a => a == _crawler ? 2 : 0));
        }
        return 0;
    }
    int ManageInventory() {
        //AP--;
        return 1;
    }
    void EnemyTurn() {
        Encounter?.Tick(_crawler);
        // AP += ( int)
    }
    bool PlayerWon => false;
    bool PlayerLost => !string.IsNullOrEmpty(_crawler?.FailState);

    MenuItem GameMenu() {
        var (selected, arguments) = CrawlerEx.Menu("Game Menu", [
            new ActionMenuItem("R", "Status Report", _ => Report()),
            new ActionMenuItem("I", "Inventory", _ => ManageInventory()),
            new ActionMenuItem("L", "Look", _ => Look()),
            new ActionMenuItem("K", "Skip Turn", _ => Turn()),
            ActionMenuItem.Sep,
            new ActionMenuItem("CT", "Create Trade Encounter", _ => ResetEncounter()),
            .. EncounterMenuItems(),
            ActionMenuItem.Sep,
            new ActionMenuItem("Q", "Save and Quit", _ => Save() + Quit()),
            new ActionMenuItem("Q!", "Quit without saving", _ => Quit()),
            new ActionMenuItem("C", "Save Checkpoint", _ => Save()),
            new ActionMenuItem("CL", "Load Checkpoint", _ => Load())
        ]);
        if (selected is ActionMenuItem action) {
            if (action.Run != null) {
                AP -= action.Run(arguments);
            }
        }
        return selected;
    }
    int ResetEncounter() {
        Encounter = new Encounter(CurrentLocation);
        Encounter.Generate(Faction.Trade);
        Console.WriteLine(Encounter.Look());
        return 0;
        // while (encounter.Menu().Option != "X") {}
    }

    int Save() {
        Console.WriteLine("Saving...");
        return 0;
    }
    int Load() {
        Console.WriteLine("Loading...");
        return 0;
    }
    int Quit() {
        Console.WriteLine("Quitting...");
        quit = true;
        return 0;
    }

    public string Logo => string.Join("\n", [
        @"+~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~+",
        @"|    XXXX   XXXXX    XXXX   XX  XX  X  XX    XXXXXX  XXXXX       |",
        @"|   XX   X  XX   X  XX   X  XX  XX  X  XX    XX      XX   X      |",
        @"|   X       X   XX  X    X  X   X   X  X     X       X   XX      |",
        @"|   X       XXXXX   XXXXXX  X   X   X  X     XXXX    XXXXX       |",
        @"|    X   X  X   X   X    X   X  XX  X  X     X       XX  X       |",
        @"|     XXX   X   XX  XX   X    XXX XXX  XXXXX XXXXXX  X   XX  0.1 |",
        @"|                                                                |",
        @"|    ©2025 Oort Heavy Industries, LLC. All rights reserved.      |",
        @"|     oort-heavy-2eccindustries.com    @oort-heavy-industries        |",
        @"+~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~+",
    ]);

    public string PrintTestMap() {
        int height = 32;
        for (int y = 0; y < height; ++y) {
            var sin = Math.Sin(Math.Tau / 2 * (y + 0.5) / height);
            if (y < height / 2) {
                char fill = y < height / 5 ? '~' : '.';
                char fill2 = y < height / 6 ? '~' : '.';
                Console.Write(Fill(20, sin, fill));
                Console.Write(Fill(29, sin, fill2));
                Console.Write(Fill(15, sin, fill));
            } else {
                Console.Write(Fill(17, sin, '='));
                Console.Write(Fill(47, sin, '='));
            }
            Console.WriteLine();

        }
        return "";

        ///////////////////////
        string Fill(int width, double sin, char fill = '.') {
            int l = ( int ) Math.Floor(width / 2.0 - width * sin / 2);
            int r = ( int ) Math.Ceiling(width / 2.0 + width * sin / 2);
            int n = r - l;
            string s = string.Empty;
            for (int i = 0; i < n; ++i) {
                double next = Random.Shared.NextDouble();
                if (next < 0.01) {
                    s += "@";
                } else if (next < 0.03) {
                    s += "*";
                } else if (next < 0.07) {
                    s += "+";
                } else if (next < 0.11) {
                    s += "!";
                } else {
                    s += fill;
                }
            }
            return new string(' ', l) + s + new string(' ', width - r);
        }
    }

    IActor _crawler;
    CrawlerConsole _console;
}
