using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Core;

public class SaveLoadInventoryTests
{
    [Fact]
    public void SavedStacks_RoundTripThroughYaml()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 10, Account.Supplies);
        inventory.Add(Commodity.Fuel, 20, Account.Supplies | Account.Trade);
        inventory.Add(Commodity.Fuel, 5, Account.Cargo);

        var saved = new SavedCrawler { Inventory = inventory.ToSaveData() };
        var yaml = new YamlDotNet.Serialization.Serializer().Serialize(saved);
        var loaded = new YamlDotNet.Serialization.Deserializer().Deserialize<SavedCrawler>(yaml);

        var restored = new Inventory();
        foreach (var (stack, savedInv) in loaded.Inventory) {
            savedInv.AddTo(restored, stack);
        }
        restored.Stack(Commodity.Fuel, Account.Supplies).Should().Be(10);
        restored.Stack(Commodity.Fuel, Account.Supplies | Account.Trade).Should().Be(20);
        restored.Stack(Commodity.Fuel, Account.Cargo).Should().Be(5);
        // Legacy fields are load-only and left out of new saves
        yaml.Split('\n').Should().NotContain(line => line.StartsWith("Supplies:") || line.StartsWith("Cargo:"));
    }

    [Fact]
    public void LegacySave_LoadsSuppliesAndCargoIntoStacks()
    {
        var yaml = "Supplies:\n  Fuel: 10\nCargo:\n  Fuel: 20\n";
        var loaded = new YamlDotNet.Serialization.Deserializer().Deserialize<SavedCrawler>(yaml);

        var restored = new Inventory();
        loaded.Supplies!.AddTo(restored, Account.Supplies);
        loaded.Cargo!.AddTo(restored, Account.Supplies | Account.Trade);

        restored.Stack(Commodity.Fuel, Account.Supplies).Should().Be(10);
        restored.Stack(Commodity.Fuel, Account.Supplies | Account.Trade).Should().Be(20);
    }
}
