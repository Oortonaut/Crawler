// See https://aka.ms/new-console-template for more information

using System;
using Crawler;

Console.WriteLine("Welcome to Crawler  (c) 2025 Ace Stapp");

/*
string MainMenu() => CrawlerEx.BasicMenu(
    "[Crawler Main Menu]",
    "N) New",
    "L) Load",
    "X) Exit"
    );

bool done = false;
while (!done) switch (MainMenu())
{
    case "N": new Game().Run(); break;
    case "L": break;
    default:
    case "X": done = true; break;
}
*/
new Game().Run();

Console.WriteLine("Like and Subscribe!");
