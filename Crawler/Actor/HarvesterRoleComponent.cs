namespace Crawler;

/// <summary>
/// AI component for NPC harvester crawlers.
/// Finds resource deposits, extracts with harvest segments, sells at settlements.
/// </summary>
public class HarvesterRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    HarvesterAction _currentAction;

    /// <summary>Minimum cargo fullness before seeking a settlement to sell.</summary>
    const float SellThreshold = 0.7f;

    /// <summary>Maximum distance to search for resources.</summary>
    const float MaxSearchRadius = 500f;

    enum HarvesterAction {
        Idle,
        TravelingToResource,
        Extracting,
        TravelingToSell,
        Selling,
        Exploring
    }

    public HarvesterRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 200; // Below combat, above default

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDepowered) return null;

        var time = crawler.Time;

        // If we have a destination, travel there
        if (_destination != null && _destination != crawler.Location) {
            return CreateTravelEvent(crawler, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case HarvesterAction.TravelingToResource:
                // We've arrived at resource location
                _currentAction = HarvesterAction.Extracting;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case HarvesterAction.Extracting:
                return TryExtract(crawler, time);

            case HarvesterAction.TravelingToSell:
                // We've arrived at settlement
                _currentAction = HarvesterAction.Selling;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case HarvesterAction.Selling:
                TrySellResources(crawler);
                _currentAction = HarvesterAction.Idle;
                break;

            case HarvesterAction.Exploring:
                _destination = null;
                _currentAction = HarvesterAction.Idle;
                break;
        }

        // Plan next action
        return PlanNextAction(crawler, time);
    }

    ActorEvent? PlanNextAction(Crawler crawler, TimePoint time) {
        // Check if we should sell (cargo getting full)
        float cargoFullness = 1 - (crawler.Cargo.AvailableVolume / crawler.Cargo.MaxVolume);
        if (cargoFullness >= SellThreshold) {
            var settlement = FindNearestSettlement(crawler);
            if (settlement != null) {
                _destination = settlement;
                _currentAction = HarvesterAction.TravelingToSell;
                return CreateTravelEvent(crawler, settlement, time);
            }
        }

        // Find a resource to harvest
        var resourceLoc = FindBestResource(crawler);
        if (resourceLoc != null) {
            _destination = resourceLoc;
            _currentAction = HarvesterAction.TravelingToResource;
            return CreateTravelEvent(crawler, resourceLoc, time);
        }

        // No resources found, explore
        if (_rng.NextSingle() < 0.5f) {
            var exploreLoc = PickExploreDestination(crawler);
            if (exploreLoc != null) {
                _destination = exploreLoc;
                _currentAction = HarvesterAction.Exploring;
                return CreateTravelEvent(crawler, exploreLoc, time);
            }
        }

        // Wait and retry later
        return crawler.NewEventFor("Waiting for resources", Priority,
            TimeDuration.FromHours(1), Post: () => { });
    }

    ActorEvent? TryExtract(Crawler crawler, TimePoint time) {
        var encounter = crawler.Location.GetEncounter();

        // Find resource actor at this location
        var resource = encounter.Actors.OfType<ResourceActor>().FirstOrDefault(r => !r.IsExhausted);
        if (resource == null) {
            // Resource exhausted or not here, find another
            _currentAction = HarvesterAction.Idle;
            return GetNextEvent();
        }

        // Get compatible harvest segments
        var harvestSegments = crawler.HarvestSegments
            .Where(h => h.ExtractableCommodities.Contains(resource.ResourceType))
            .ToList();

        if (harvestSegments.Count == 0) {
            // Can't harvest this resource type, find another
            _currentAction = HarvesterAction.Idle;
            return GetNextEvent();
        }

        // Check cargo space
        float volume = resource.ResourceType.Volume();
        if (crawler.Cargo.AvailableVolume < volume) {
            // Cargo full, go sell
            _currentAction = HarvesterAction.Idle;
            return GetNextEvent();
        }

        // Perform extraction
        float totalYield = harvestSegments.Sum(h => h.Yield);
        float extracted = resource.Extract(totalYield, harvestSegments.Count);

        if (extracted > 0) {
            crawler.Supplies.Add(resource.ResourceType, extracted);
            crawler.Message($"{crawler.Name} extracted {extracted:F1} {resource.ResourceType}");
        }

        if (resource.IsExhausted) {
            resource.SetEndState(EEndState.Looted, "depleted");
            crawler.Message($"{resource.Name} is exhausted");
            _currentAction = HarvesterAction.Idle;
        }

        // Schedule next extraction cycle
        return crawler.NewEventFor("Extracting", Priority,
            Tuning.Resource.ExtractionTime, Post: () => { });
    }

    void TrySellResources(Crawler crawler) {
        // Sell all raw materials in supplies (where extracted resources are stored)
        Commodity[] sellable = [
            Commodity.Ore,
            Commodity.Biomass,
            Commodity.Silicates,
            Commodity.Isotopes,
            Commodity.Gems
        ];

        foreach (var commodity in sellable) {
            float quantity = crawler.Supplies[commodity];
            if (quantity <= 0) continue;

            // Sell through settlement (affects settlement inventory)
            SettlementTrade.TrySellToSettlement(crawler, commodity, quantity);
        }
    }

    Location? FindBestResource(Crawler crawler) {
        var map = crawler.Location.Map;
        var candidates = map.FindLocationsInRadius(crawler.Location.Position, MaxSearchRadius)
            .Where(loc => loc.Type == EncounterType.Resource)
            .Where(loc => loc.HasEncounter)
            .Where(loc => {
                var encounter = loc.GetEncounter();
                var resource = encounter.Actors.OfType<ResourceActor>().FirstOrDefault();
                if (resource == null || resource.IsExhausted) return false;

                // Check if we can harvest this resource type
                return crawler.CanHarvest(resource.ResourceType);
            })
            .OrderBy(loc => crawler.Location.Distance(loc))
            .ToList();

        return candidates.FirstOrDefault();
    }

    Location? FindNearestSettlement(Crawler crawler) {
        var map = crawler.Location.Map;
        return map.FindLocationsInRadius(crawler.Location.Position, MaxSearchRadius * 2)
            .Where(loc => loc.Type == EncounterType.Settlement)
            .OrderBy(loc => crawler.Location.Distance(loc))
            .FirstOrDefault();
    }

    Location? PickExploreDestination(Crawler crawler) {
        var map = crawler.Location.Map;
        var candidates = map.FindLocationsInRadius(crawler.Location.Position, MaxSearchRadius)
            .Where(loc => loc != crawler.Location)
            .Where(loc => loc.Type is EncounterType.Resource or EncounterType.Settlement or EncounterType.Crossroads)
            .ToList();

        if (candidates.Count == 0) return null;
        return _rng.ChooseRandom(candidates);
    }

    ActorEvent? CreateTravelEvent(Crawler crawler, Location destination, TimePoint time) {
        var (_, travelHours) = crawler.FuelTimeTo(destination);
        if (travelHours < 0) return null; // Cannot reach destination

        var arrivalTime = time + TimeDuration.FromHours(travelHours);
        return new ActorEvent.TravelEvent(crawler, arrivalTime, destination);
    }
}
