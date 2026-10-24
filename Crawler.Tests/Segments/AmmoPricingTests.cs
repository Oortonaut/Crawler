using Crawler;
using Crawler.Production;
using FluentAssertions;
using Xunit.Abstractions;

namespace Crawler.Tests.Segments;

/// <summary>
/// Ammunition should cost the same per point of expected damage, whichever weapon family it feeds.
/// </summary>
public class AmmoPricingTests(ITestOutputHelper output)
{
    // Average expected damage per unit of ammo across every base weapon variation that uses it
    static Dictionary<Commodity, float> ExpectedDamagePerAmmo() =>
        SegmentEx.WeaponDefs
            .Where(w => w.RequiresAmmo)
            .GroupBy(w => w.AmmoType!.Value)
            .ToDictionary(g => g.Key, g => g.Average(w => w.ExpectedDamagePerAmmo));

    [Fact]
    public void AmmoPrice_PerExpectedDamage_IsEqualAcrossAmmoTypes()
    {
        foreach (var w in SegmentEx.WeaponDefs.Where(w => w.RequiresAmmo)) {
            output.WriteLine($"{w.Name,-22} {w.AmmoType,-8} dmg {w.Damage,4} aim {w.Aim:F2} hit {w.HitChance:P0} " +
                             $"ammo/shot {w.AmmoPerShot:F2} → {w.ExpectedDamagePerAmmo:F2} dmg/ammo");
        }

        var costPerDamage = ExpectedDamagePerAmmo().ToDictionary(
            kv => kv.Key,
            kv => CommodityEx.Data[kv.Key].InitialValue / kv.Value);
        foreach (var (ammo, cost) in costPerDamage) {
            output.WriteLine($"{ammo,-8} {CommodityEx.Data[ammo].InitialValue,7:F1}¢¢ → {cost:F3}¢¢ per expected damage");
        }

        var reference = costPerDamage.Values.Average();
        costPerDamage.Values.Should().OnlyContain(c => Math.Abs(c - reference) / reference < 0.05f,
            "every ammo type should cost the same per point of expected damage");
    }

    // Same rule as RecipeEx.SuggestedPrice (cost × 1.5 margin, 1¢¢ per charge), but at base values so it's location-free
    static float SuggestedBasePrice(ProductionRecipe recipe) {
        float Value(IReadOnlyDictionary<Commodity, float> items) =>
            items.Sum(kv => CommodityEx.Data[kv.Key].InitialValue * kv.Value);
        float cost = Value(recipe.Inputs) + Value(recipe.Consumables) + Value(recipe.Maintenance) + recipe.ActivateCharge;
        return cost * 1.5f / recipe.Outputs.First().Value;
    }

    [Theory]
    [InlineData(Commodity.Slugs)]
    [InlineData(Commodity.Cells)]
    [InlineData(Commodity.Rockets)]
    public void AmmoRecipe_SuggestedPrice_MatchesAmmoPrice(Commodity ammo)
    {
        var recipe = RecipeEx.AllRecipes.Single(r => r.Outputs.First().Key == ammo);
        var suggested = SuggestedBasePrice(recipe);
        var price = CommodityEx.Data[ammo].InitialValue;
        output.WriteLine($"{recipe.Name}: suggested {suggested:F1}¢¢, price {price:F1}¢¢");

        Math.Abs(suggested - price).Should().BeLessThan(price * 0.15f,
            "an ammo recipe's components should cost about what the ammo sells for, less the production margin");
    }
}
