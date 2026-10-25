# Economy

How value flows through the game, and a log of balance decisions with their reasoning.
Price formulas and trade mechanics live in [SYSTEMS.md](SYSTEMS.md#trading-system); this file covers
*why* the numbers are what they are.

## Where Things Live

- **Base prices:** `CommodityEx.Data` in `Commodity.cs` (`CommodityData.InitialValue`)
- **Local pricing / stock:** `Economy/SettlementStock.cs`, `Economy/MarketOfferEx.cs`
- **Trade offers:** `TradeEx.MakeTradeOffers` in `Interaction/Trade.cs`
- **Production:** `Production/Recipe*.cs`, `Economy/ProductionChain.cs`
- **Tuning knobs:** `Tuning.cs` (`Tuning.Crawler`, `Tuning.Trader`, `Tuning.Segments`)
- **Player starting kit:** `Encounter.GeneratePlayerActor`

## Balance Principles

- **Ammunition is priced by expected damage.** Every ammo type costs the same per point of expected
  damage (damage × hit chance ÷ ammo per shot, averaged over the base weapon variations that use it).
  `AmmoPricingTests` enforces this within 5%; rerun it after changing weapon tiers or ammo prices.
- **Starting ammo is a damage budget, not a unit count.** The player gets enough ammo per starting weapon
  for `Tuning.Crawler.StartingAmmoDamage` expected damage, so weapon choice doesn't change starting value.
- **Ammo recipes cost about what the ammo sells for.** A recipe's base-value cost × 1.5 margin ÷ output
  should be within 15% of the output's price; `AmmoRecipe_SuggestedPrice_MatchesAmmoPrice` checks ammo
  (the in-game `RCP` report shows the same figures at local prices).
- **Traders sell what they carry and buy what they don't.** Offers include carried cargo and bids on goods
  the trader doesn't stock, so hauling goods between markets can pay.
- **Every settlement trades essentials.** Population-based availability can leave small settlements with
  nothing to trade; essentials (fuel, crew, morale, air, water, rations) are always offered.

## Balance Log

Record balance changes here: date, what changed, and why. Newest first.

### 2026-10-25 — Price variation fixes

- **Local markups vary per commodity again.** `GaussianSampler` is a struct and the markup samplers took it by
  value, so a copy was drawn from on every call and never advanced: every commodity at a location got the same
  `LocalMarkup` (one location had 0.8881 for all of them), and a crawler's markup and spread came from the same
  draw. Samplers now take it by `ref`. Expect per-commodity price differences between locations of about ±7%,
  which is what makes hauling particular goods pay.
- **Trader and bandit spreads restored.** `TradeSpread`/`BanditSpread` sampled the absolute spread (0.16, 0.30)
  but prices multiply by `baseBidAskSpread` again, so traders quoted ~3% and bandits ~6%. They now sample a
  multiplier: traders 16% ± 5%, bandits 30% ± 10%, settlements 20%. `TradePricingTests` checks the averages.

### 2026-10-24 — Ammo recipes and trade loop

- **Ammo recipes** cut to match the repriced ammo (the `RCP` report showed Cells −49% and Rockets −83%):
  Manufacture Cells Electronics 0.5 → 0.2 (suggested 29.2 vs 28.9¢¢);
  Manufacture Rockets now yields 12 instead of 3 from Alloys 0.3, Explosives 0.7, Electronics 0.12
  (was 0.5 / 1.0 / 0.2; suggested 35.3 vs 35.4¢¢). Slugs already matched (15.1 vs 15¢¢).
- **Trade loop:** outside capitals, traders only offered the six essentials, because offers came from what the
  location produces. They now also sell their carried cargo and bid on everything else they don't list.

### 2026-10-24 — Ammo repricing and starting ammo

- **Ammo prices** set to an equal 6¢¢ per expected damage, anchored on Slugs:
  Slugs 15 (unchanged), Cells 80 → 28.9, Rockets 250 → 35.4.
  Before: 6.0 / 16.6 / 42.4 ¢¢ per expected damage — Rockets were ~7× Slugs for the same damage.
- **Hit chance** is `clamp(Aim + 0.5, 0, 1)` (`WeaponDef.HitChance`, from `HitRecord.Hit`). All current weapons
  have Aim 0.73–0.77, so every shot lands; Aim only affects pierce chance (~25%). The pricing formula still
  includes hit chance so it tracks future aim changes. Note the status report's "Aim %" is raw Aim, not hit chance.
- **Starting ammo:** 200 expected damage per starting weapon, rounded up to whole volleys
  (worth ~1,400–1,500¢¢ at local prices — may still be generous to sell).
- **Starting scrap** +500¢¢ (`Tuning.Crawler.StartingScrap`) on top of the ~30¢¢ from generated goods, which
  couldn't buy a single ration (~150¢¢). 500 covers supplies plus an upgrade or two (70–140¢¢), not a new
  segment (390¢¢+).
- **Not modeled:** weapon specials in `Gun/Laser/MissileSegment.Adjust` (extra shot, double damage, +2 aim)
  are never called, and pierce value depends on the target's armor.

### 2026-10-24 — Early-game fixes

- **Customs deadline** 3 → 30 game minutes (`Tuning.Crawler.CustomsTimeout`); trading first used to turn the
  starting settlement hostile. Bandit demands keep `UltimatumTimeout` (3 minutes).
- **Essentials always traded** at every settlement (see principles above).
- **Out-of-ammo weapons** no longer count as armed, ending endless empty NPC volleys that dominated simulation time.
