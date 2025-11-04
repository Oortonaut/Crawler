using System;
using Crawler;
using Crawler.Logging;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Initialize OpenTelemetry tracing
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(LogCat.ServiceName, serviceVersion: LogCat.ServiceVersion))
    .AddSource(LogCat.Interaction.Name)
    .AddSource(LogCat.Game.Name)
    .AddSource(LogCat.Console.Name)
    .AddSource(LogCat.Encounter.Name)
    .AddDebugExporter()
    .AddOtlpExporter(initOptions => { initOptions.Protocol = OtlpExportProtocol.Grpc; })
    .Build();

var loggerFactory = LoggerFactory.Create(builder => {
    builder.AddProvider(new DebugLoggerProvider());
    builder.AddOpenTelemetry( /*...*/);
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

Console.WriteLine(Logo);
Console.WriteLine("Welcome to Crawler  (c) 2025 Ace Stapp");

var newGame = new ActionMenuItem("N", "New Game", args => {
    string? name = args;
    if (string.IsNullOrWhiteSpace(name)) {
        name = Names.HumanName();
        name = CrawlerEx.Input("Crawler Name: ", name);
    }
    if (string.IsNullOrWhiteSpace(name)) {
        name = Names.HumanName();
    }
    int minSize = 4;
    int maxSize = 9;
    int size = (minSize + maxSize) / 2;
    var sizeStr = CrawlerEx.Input($"Size ({minSize}-{maxSize}): ", size.ToString());
    if (!int.TryParse(sizeStr, out size)) {
        size = minSize;
    }
    size = Math.Clamp(size, minSize, maxSize);
    Game.NewGame(name, size);
    return 0;
});
var loadGame = new ActionMenuItem("L", "Load Game", _ => {
    return LoadGame();
});
var arena = new ActionMenuItem("A", "Crawler Arena", _ => {
    var crawlerArena = new CrawlerArena();
    crawlerArena.Run();
    return 0;
});
var quit = new MenuItem("Q", "Quit");


// Main Menu
var (choice, mainArgs) = CrawlerEx.Menu(
    "Main Menu", newGame.Option,
    newGame, loadGame, arena, quit);

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

int LoadGame() {
    var quit = new MenuItem("Q", "Quit");
    string loadPath = CrawlerEx.SavesPath;
    Directory.CreateDirectory(loadPath);
    var saveFiles = Directory.GetFiles(loadPath, "*.yaml");
    var menuItems = saveFiles.Index().Select(
        f => new ActionMenuItem($"{f.Index+1}", Path.GetFileNameWithoutExtension(f.Item), _ => {
            Game.LoadGame(f.Item);
            return 1;
        }));
    var (item, result) = CrawlerEx.MenuRun("Load Menu", [ .. menuItems, quit ]);
    return result;
}
