using System;
using System.Text;
using Crawler;
using Crawler.Logging;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

// Initialize OpenTelemetry
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(LogCat.ServiceName, serviceVersion: LogCat.ServiceVersion))
    .AddSource(LogCat.Interaction.Name)
    .AddSource(LogCat.Game.Name)
    .AddSource(LogCat.Console.Name)
    .AddSource(LogCat.Encounter.Name)
    .AddDebugExporter()
    //.AddOtlpExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(LogCat.GameMetrics.Name)
    .AddMeter(LogCat.EncounterMetrics.Name)
    //.AddOtlpExporter()
    .Build();

//using var loggerProvider = Sdk.CreateLoggerProviderBuilder()
//    .AddInstrumentation()
//    .AddOtlpExporter()
//    .Build();

var loggerFactory = LoggerFactory.Create(builder => {
    builder.AddProvider(new DebugLoggerProvider());
    //builder.AddOpenTelemetry( /*...*/);
});


LogCat.Log = loggerFactory.CreateLogger("Crawler");

string Logo = string.Join("\n", [
    @"+~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~+",
    @"|    XXXX   XXXXX    XXXX   XX  XX  X  XX    XXXXXX  XXXXX       |",
    @"|   XX   X  XX   X  XX   X  XX  XX  X  XX    XX      XX   X      |",
    @"|   X       X   XX  X    X  X   X   X  X     X       X   XX      |",
    @"|   X       XXXXX   XXXXXX  X   X   X  X     XXXX    XXXXX       |",
    @"|    X   X  X   X   X    X   X  XX  X  X     X       XX  X       |",
    @"|     XXX   X   XX  XX   X    XXX XXX  XXXXX XXXXXX  X   XX  0.1 |",
    @"|                                                                |",
    @"|    ©2025 Oort Heavy Industries, LLC. All rights reserved.      |",
    @"|     oort-heavy-industries.com    @oort-heavy-industries        |",
    @"+~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~+",
]);

Console.OutputEncoding = new UTF8Encoding();
Console.InputEncoding = new UTF8Encoding();
Console.WriteLine(Logo);
Console.WriteLine("Welcome to Crawler  (c) 2025 Ace Stapp");

ulong seed = GetSeed();

var newGame = new ActionMenuItem("N", "New Game", args => {
    string? name = args;
    var tempRng = new XorShift((ulong)Random.Shared.NextInt64());
    if (string.IsNullOrWhiteSpace(name)) {
        name = Names.HumanName(tempRng.Seed());
        name = CrawlerEx.Input("Crawler Name: ", name);
    }
    if (string.IsNullOrWhiteSpace(name)) {
        name = Names.HumanName(tempRng.Seed());
    }
    int minSize = 2;
    int maxSize = 9;
    int size = 6;
    var sizeStr = CrawlerEx.Input($"Size ({minSize}-{maxSize}): ", size.ToString());
    if (!int.TryParse(sizeStr, out size)) {
        size = minSize;
    }
    size = Math.Clamp(size, minSize, maxSize);
    var seedStr = CrawlerEx.Input($"Seed: ", seed.ToString());
    ulong.TryParse(seedStr, out seed);
    Game.NewGame(seed, name, size);
    return false;
});
var loadGame = new ActionMenuItem("L", "Load Game", _ => {
    return LoadGame();
});
var arena = new ActionMenuItem("A", "Crawler Arena", _ => {
    var crawlerArena = new CrawlerArena();
    crawlerArena.Run();
    return false;
});
var simulation = new ActionMenuItem("S", "Simulation Mode", _ => {
    int minSize = 2;
    int maxSize = 9;
    int size = 6;
    var sizeStr = CrawlerEx.Input($"Size ({minSize}-{maxSize}): ", size.ToString());
    if (!int.TryParse(sizeStr, out size)) {
        size = minSize;
    }
    size = Math.Clamp(size, minSize, maxSize);
    var seedStr = CrawlerEx.Input($"Seed: ", seed.ToString());
    ulong.TryParse(seedStr, out seed);
    var sim = SimulationMode.New(seed, size);
    sim.Run();
    return false;
});
var quit = new MenuItem("Q", "Quit");


// Main Menu
var (choice, mainArgs) = CrawlerEx.Menu(
    "Main Menu", newGame.Option,
    newGame, loadGame, arena, simulation, quit);

if (choice is ActionMenuItem action) {
    action.Run(mainArgs);
}

if (Game.Instance != null) {
    Game.Instance.Run();
}

Console.WriteLine("Like and Subscribe!");

///////////////////////////////////////////////
return;
///////////////////////////////////////////////

bool LoadGame() {
    var quit = new MenuItem("Q", "Quit");
    string loadPath = CrawlerEx.SavesPath;
    Directory.CreateDirectory(loadPath);
    var saveFiles = Directory.GetFiles(loadPath, "*.yaml");
    var menuItems = saveFiles.Index().Select(
        f => new ActionMenuItem($"{f.Index+1}", Path.GetFileNameWithoutExtension(f.Item), _ => {
            Game.LoadGame(f.Item);
            return true;
        }));
    var (item, result) = CrawlerEx.MenuRun("Load Menu", [ .. menuItems, quit ]);
    return result;
}

ulong GetSeed() {
    // First check for `--seed=<seed>` argument
    var args = Environment.GetCommandLineArgs();
    if (args.FirstOrDefault(a => a.StartsWith("--seed")) is {} seedArg) {
        var split = seedArg.Split('=');
        if (split.Length > 1 && ulong.TryParse(split[1], out ulong _seed)) {
            return _seed;
        }
    }
    return (ulong)DateTime.Now.Ticks + (ulong)Random.Shared.NextInt64();
}
