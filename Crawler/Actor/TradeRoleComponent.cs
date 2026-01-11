namespace Crawler;

using Convoy;
using Economy;
using Network;

/// <summary>
/// AI component for NPC trade crawlers.
/// Plans routes based on price knowledge, buys low and sells high.
/// </summary>
public class TradeRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    Commodity _targetCommodity;
    TradeAction _currentAction;

    /// <summary>Maximum age of price information to consider reliable.</summary>
    static readonly TimeDuration MaxPriceAge = TimeDuration.FromDays(3);

    /// <summary>Minimum profit margin to consider a trade worthwhile.</summary>
    const float MinProfitMargin = 0.15f;

    enum TradeAction {
        Idle,
        TravelingToBuy,
        Buying,
        TravelingToSell,
        Selling,
        Exploring
    }

    public TradeRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 200; // Below combat, above default

    PriceKnowledge? GetKnowledge() {
        return (Owner as ActorBase)?.Components.OfType<TraderKnowledgeComponent>().FirstOrDefault()?.Knowledge;
    }

    public override void Enter(Encounter encounter) {
        // Update price knowledge for current location
        var knowledge = GetKnowledge();
        if (knowledge != null && Owner?.Location != null) {
            knowledge.UpdatePrice(Owner.Location, encounter.EncounterTime);
        }
    }

    public override void Leave(Encounter encounter) { }

    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDepowered) return null;

        var encounter = crawler.Location.GetEncounter();
        var time = encounter.EncounterTime;
        var knowledge = GetKnowledge();

        if (knowledge == null) return null;

        // If we have a destination, travel there
        if (_destination != null && _destination != crawler.Location) {
            return CreateTravelEvent(crawler, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case TradeAction.TravelingToBuy:
            case TradeAction.TravelingToSell:
                // We've arrived, update action
                _currentAction = _currentAction == TradeAction.TravelingToBuy
                    ? TradeAction.Buying
                    : TradeAction.Selling;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case TradeAction.Buying:
                // Execute buy if we can
                if (TryBuy(crawler, _targetCommodity)) {
                    // Find where to sell
                    var (_, sellLoc, _, _) = FindBestSellLocation(knowledge, crawler.Location, _targetCommodity, time);
                    if (sellLoc != null) {
                        _destination = sellLoc;
                        _currentAction = TradeAction.TravelingToSell;
                        return CreateTravelEvent(crawler, sellLoc, time);
                    }
                }
                _currentAction = TradeAction.Idle;
                break;

            case TradeAction.Selling:
                // Execute sell
                TrySell(crawler, _targetCommodity);
                _currentAction = TradeAction.Idle;
                break;

            case TradeAction.Exploring:
                // Pick a random nearby location to explore
                _destination = PickExploreDestination(crawler, time);
                if (_destination != null) {
                    return CreateTravelEvent(crawler, _destination, time);
                }
                _currentAction = TradeAction.Idle;
                break;
        }

        // Plan next trade
        return PlanNextTrade(crawler, knowledge, time);
    }

    ActorEvent? PlanNextTrade(Crawler crawler, PriceKnowledge knowledge, TimePoint time) {
        // Find best trade opportunity
        var opportunities = knowledge.FindTradeOpportunities(time, MaxPriceAge, 5).ToList();

        foreach (var (buyLoc, sellLoc, commodity, profit, margin) in opportunities) {
            if (margin < MinProfitMargin) continue;

            // Check if we can afford to buy
            float buyPrice = knowledge.GetSnapshot(buyLoc)?.Prices[commodity] ?? 0;
            float available = crawler.Supplies[Commodity.Scrap];

            if (available >= buyPrice) {
                _targetCommodity = commodity;
                _destination = buyLoc;
                _currentAction = TradeAction.TravelingToBuy;
                return CreateTravelEvent(crawler, buyLoc, time);
            }
        }

        // No good trades found, explore for fresh prices
        if (_rng.NextSingle() < 0.3f) {
            _currentAction = TradeAction.Exploring;
            _destination = PickExploreDestination(crawler, time);
            if (_destination != null) {
                return CreateTravelEvent(crawler, _destination, time);
            }
        }

        // Wait and retry later
        return crawler.NewEventFor("Waiting for trade opportunity", Priority,
            TimeDuration.FromHours(1), Post: () => { });
    }

    Location? PickExploreDestination(Crawler crawler, TimePoint time) {
        var network = crawler.Location.Map.TradeNetwork;
        if (network == null) return null;

        var knowledge = GetKnowledge();

        // Find locations with stale or no price info
        var candidates = network.RoadsFrom(crawler.Location)
            .Select(r => r.To)
            .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads)
            .Where(loc => knowledge == null || knowledge.IsStale(loc, time, MaxPriceAge))
            .ToList();

        if (candidates.Count == 0) {
            // Just pick a random connected location
            var roads = network.RoadsFrom(crawler.Location).ToList();
            return _rng.ChooseRandom(roads)?.To;
        }

        return _rng.ChooseRandom(candidates);
    }

    (float price, Location? location, float profit, float margin) FindBestSellLocation(
        PriceKnowledge knowledge,
        Location currentLocation,
        Commodity commodity,
        TimePoint time) {

        Location? bestLocation = null;
        float bestProfit = 0;
        float bestMargin = 0;
        float bestPrice = 0;

        var buyPrice = knowledge.GetSnapshot(currentLocation)?.Prices[commodity] ?? commodity.BaseCost();

        foreach (var (location, snapshot) in knowledge.Snapshots) {
            if (location == currentLocation) continue;
            if (time - snapshot.Timestamp > MaxPriceAge) continue;

            float sellPrice = snapshot.Prices[commodity];
            float profit = sellPrice - buyPrice;
            float margin = profit / buyPrice;

            if (margin > bestMargin) {
                bestLocation = location;
                bestProfit = profit;
                bestMargin = margin;
                bestPrice = sellPrice;
            }
        }

        return (bestPrice, bestLocation, bestProfit, bestMargin);
    }

    bool TryBuy(Crawler crawler, Commodity commodity) {
        // Calculate how much we can buy (capped at 100 per trade)
        float price = commodity.CostAt(crawler.Location);
        if (price <= 0) return false;

        float available = crawler.Supplies[Commodity.Scrap];
        float maxQuantity = available / price;
        float volumeLimit = crawler.Cargo.AvailableVolume / commodity.Volume();
        float quantity = Math.Min(maxQuantity, volumeLimit);
        quantity = Math.Min(quantity, 100); // Cap per trade

        if (quantity < 1) return false;

        // Execute trade through settlement (affects settlement inventory)
        return SettlementTrade.TryBuyFromSettlement(crawler, commodity, quantity);
    }

    bool TrySell(Crawler crawler, Commodity commodity) {
        float quantity = crawler.Cargo[commodity];
        if (quantity <= 0) return false;

        // Execute trade through settlement (affects settlement inventory)
        return SettlementTrade.TrySellToSettlement(crawler, commodity, quantity);
    }

    ActorEvent? CreateTravelEvent(Crawler crawler, Location destination, TimePoint time) {
        // Check if we should form/join a convoy instead of traveling solo
        var convoyDecision = crawler.Components
            .OfType<ConvoyDecisionComponent>().FirstOrDefault();

        if (convoyDecision != null) {
            convoyDecision.SetDestination(destination);

            // Check if we're already in a convoy going our way
            var existingConvoy = ConvoyRegistry.GetConvoy(crawler);
            if (existingConvoy != null && existingConvoy.Route.Contains(destination)) {
                // Let ConvoyComponent handle travel
                return null;
            }

            // Try to join an existing convoy at this location
            foreach (var convoy in ConvoyRegistry.ConvoysAt(crawler.Location)) {
                if (convoyDecision.ShouldJoinConvoy(convoy)) {
                    convoy.AddMember(crawler);
                    crawler.Message($"{crawler.Name} joined convoy to {convoy.Destination?.Description}.");
                    return null; // ConvoyComponent will handle travel
                }
            }

            // Try to form a new convoy if route is risky
            if (convoyDecision.ShouldFormConvoy(destination)) {
                var tradeNet = crawler.Location.Map?.TradeNetwork;
                if (tradeNet != null) {
                    var route = tradeNet.FindPath(crawler.Location, destination);
                    if (route != null && route.Count >= 2) {
                        ConvoyRegistry.Create(crawler, route);
                        crawler.Message($"{crawler.Name} formed convoy to {destination.Description}.");
                        return null; // ConvoyComponent will handle travel
                    }
                }
            }
        }

        // No convoy - travel solo
        var network = crawler.Location.Map?.TradeNetwork;
        if (network == null) return null;

        // Find path to destination
        var path = network.FindPath(crawler.Location, destination);
        if (path == null || path.Count < 2) {
            // Direct travel if no network path - use FuelTimeTo for proper calculation
            var (_, travelHours) = crawler.FuelTimeTo(destination);
            if (travelHours < 0) return null; // Cannot reach destination

            var arrivalTime = time + TimeDuration.FromHours(travelHours);
            return new ActorEvent.TravelEvent(crawler, arrivalTime, destination);
        }

        // Travel to next hop in path - use FuelTimeTo for proper terrain-aware calculation
        var nextHop = path[1];
        var (_, hopHours) = crawler.FuelTimeTo(nextHop);
        if (hopHours < 0) return null; // Cannot reach next hop

        var hopArrivalTime = time + TimeDuration.FromHours(hopHours);
        _destination = path.Count > 2 ? destination : null; // Clear if last hop

        return new ActorEvent.TravelEvent(crawler, hopArrivalTime, nextHop);
    }
}
