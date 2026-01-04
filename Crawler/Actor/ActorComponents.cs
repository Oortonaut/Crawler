using System.Diagnostics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

/// <summary>
/// Component that handles contraband scanning, enforcement, and seizure.
/// Actors with this component scan for prohibited goods and create ultimatums to seize them or turn hostile.
/// Can be added to Owners, customs officers, or other enforcement actors.
/// </summary>
public class CustomsComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
        LogCat.Log.LogInformation($"CustomsComponent.Enter: Owner={Owner.Name} subscribing to encounter {encounter.Name} events");
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, TimePoint time) {
        LogCat.Log.LogInformation($"CustomsComponent.OnActorArrived: actor={actor.Name}, time={Game.TimeString(time)}, Owner={Owner.Name}, actor==Owner={actor == Owner}");
        if (actor == Owner) {
            var encounter = actor.Location.GetEncounter();
            // Scan all actors already present in the encounter
            // Issue ultimatums starting from NOW (when customs officer arrived), not from their historical arrival
            LogCat.Log.LogInformation($"CustomsComponent: {Owner.Name} arrived, scanning {encounter.ActorsExcept(Owner).Count()} existing actors");
            foreach (var other in encounter.ActorsExcept(Owner)) {
                LogCat.Log.LogInformation($"CustomsComponent: {Owner.Name} issuing ultimatum to {other.Name} at time {Game.TimeString(time)}");
                SetupContrabandUltimatum(other, time);
            }
        } else {
            LogCat.Log.LogInformation($"CustomsComponent: {Owner.Name} issuing ultimatum to arriving {actor.Name} at time {Game.TimeString(time)}");
            SetupContrabandUltimatum(actor, time);
        }
    }

    void OnActorLeft(IActor actor, TimePoint time) {
        if (actor == Owner) {
            // TODO: clear all ultimatums we have offered
            foreach (var target in Targets) {
                Owner.To(target).Ultimatum = null;
            }
            Targets.Clear();
        } else {
            // Clear ultimatum when target leaves
            Owner.To(actor).Ultimatum = null;
        }
    }

    List<IActor> Targets = new();
    void SetupContrabandUltimatum(IActor target, long time) {
        LogCat.Log.LogInformation($"CustomsComponent.SetupContrabandUltimatum: target={target.Name}, time={Game.TimeString(time)}, isPlayer={target.IsPlayer()}, fighting={Owner.Fighting(target)}");
        if (!target.IsPlayer()) {
            LogCat.Log.LogInformation($"CustomsComponent: {target.Name} is not player, skipping");
            return;
        }

        if (!Owner.Fighting(target)) {
            // CRITICAL: Event handlers receive historical time but owner may have advanced
            // Use owner's current time for ultimatum expiration
            long effectiveTime = Math.Max(time, Owner.Time);
            long expirationTime = effectiveTime + 300;

            long timeDelta = Owner.Time - time;
            if (timeDelta > 60) {
                LogCat.Log.LogWarning($"CustomsComponent: WARNING! Event fired {timeDelta}s ({Game.TimeString(timeDelta)}) after arrival. event time={Game.TimeString(time)}, owner time={Game.TimeString(Owner.Time)}");
            }

            LogCat.Log.LogInformation($"CustomsComponent: {Owner.Name} setting ultimatum on {target.Name}, event time={Game.TimeString(time)}, effective time={Game.TimeString(effectiveTime)}, expires at {Game.TimeString(expirationTime)} (owner time: {Game.TimeString(Owner.Time)})");
            // Set ultimatum with timeout
            Targets.Add(target);
            Owner.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = expirationTime,
                Type = "ContrabandSeizure",
            };
        } else {
            LogCat.Log.LogInformation($"CustomsComponent: {Owner.Name} is fighting {target.Name}, skipping ultimatum");
        }
    }

    /// <summary>
    /// Scan and return all contraband items found
    /// </summary>
    public Inventory Scan(IActor target) {
        if (target is not Crawler targetCrawler) {
            return new Inventory();
        }

        var contraband = new Inventory();

        // Random chance to detect (currently disabled)
        //if (Rng.NextSingle() > Tuning.Civilian.contrabandScanChance) {
        //    return contraband; // Scan failed
        //}

        var targetToFaction = targetCrawler.To(Owner.Faction);
        // Check each commodity
        foreach (var commodityAmount in targetCrawler.Supplies.Pairs) {
            var (commodity, amount) = commodityAmount;
            amount += targetCrawler.Cargo[commodity];
            var policy = Owner.Faction.GetPolicy(commodity);
            var licensed = targetToFaction.CanTrade(commodity);
            if (!licensed && amount > 0) {
                contraband.Add(commodity, amount);
            }
        }
        foreach (var segment in targetCrawler.Cargo.Segments) {
            var policy = Owner.Faction.GetPolicy(segment.SegmentKind);
            var licensed = targetToFaction.CanTrade(segment.SegmentDef);
            if (!licensed) {
                contraband.Add(segment);
            }
        }
        foreach (var segment in targetCrawler.Supplies.Segments.Where(s => s.IsPackaged)) {
            var policy = Owner.Faction.GetPolicy(segment.SegmentKind);
            var licensed = targetToFaction.CanTrade(segment.SegmentDef);
            if (!licensed) {
                contraband.Add(segment);
            }
        }
        return contraband;
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        var relation = Owner.To(subject);
        if (relation.Ultimatum?.Type != "ContrabandSeizure") yield break;

        // Rescan to update the interaction text
        var contraband = Scan(subject);
        long expirationTime = relation.Ultimatum.ExpirationTime;
        bool expired = expirationTime > 0 && Owner.Time > expirationTime;

        LogCat.Log.LogInformation($"CustomsComponent.EnumerateInteractions: Owner={Owner.Name}, subject={subject.Name}, expirationTime={Game.TimeString(expirationTime)}, currentTime={Game.TimeString(Owner.Time)}, expired={expired}");

        if (!expired) {
            // Offer accept option (allow search and seizure)
            LogCat.Log.LogInformation($"CustomsComponent: Offering accept/refuse interactions (not expired)");
            yield return new ContrabandAcceptInteraction(Owner, subject, contraband, "Y");
            // Offer refuse option (go hostile)
            yield return new ContrabandRefuseInteraction(Crawler, subject, "N");
        } else {
            // Time expired - turn hostile
            LogCat.Log.LogInformation($"CustomsComponent: Offering expired interaction (ultimatum expired)");
            yield return new ContrabandExpiredInteraction(Crawler, subject, "");
        }
    }

    public record ContrabandAcceptInteraction(
        IActor Owner,
        IActor Target,
        Inventory Contraband,
        string MenuOption
    ) : Interaction(Owner, Target, MenuOption) {

        public override string Description => $"Allow search: surrender {Contraband}";

        public override string? MessageFor(IActor viewer) {
            if (viewer != Target) return null;
            var ultimatum = Owner.To(Target).Ultimatum;
            if (ultimatum == null || ultimatum.ExpirationTime == 0) return null;

            return $"{Owner.Name} demands customs inspection. You have until {Game.TimeString(ultimatum.ExpirationTime)} to comply.";
        }

        public override bool Perform(string args = "") {
            SynchronizeActors();

            Owner.Message($"{Target.Name} allows search and surrenders {Contraband}");
            Target.Message($"You allow {Owner.Name} to search and seize {Contraband}");

            Target.Supplies.Remove(Contraband);
            // TODO: Generalize different inventories into components
            // TODO: Move to evidence inventory
            Owner.Cargo.Add(Contraband);

            // Clear ultimatum
            Owner.To(Target).Ultimatum = null;

            // Contraband search takes time: 1 hour if clean, 3 hours if contraband found
            Owner.ConsumeTime("Searching", 300, ExpectedDuration);
            Target.ConsumeTime("Searched", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var inventoryOffer = new InventoryOffer(false, Contraband);
            if (inventoryOffer.DisabledFor(Target, Owner) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration {
            get {
                bool hasContraband = !Contraband.IsEmpty;
                return hasContraband ? Tuning.Crawler.ContrabandSearchFound : Tuning.Crawler.ContrabandSearchClean;
            }
        }
    }

    public record ContrabandRefuseInteraction(
        Crawler Owner,
        IActor Target,
        string MenuOption
    ) : Interaction(Owner, Target, MenuOption) {

        public override string Description => $"Refuse search from {Owner.Name}";

        public override string? MessageFor(IActor viewer) => null;

        public override bool Perform(string args = "") {
            Owner.Message($"{Target.Name} refuses search!");
            Target.Message($"You refuse {Owner.Name}'s search - now hostile!");

            Owner.To(Target).Ultimatum = null;
            Owner.SetHostileTo(Target, true);

            // Time consumption for refusing contraband search
            Owner.ConsumeTime("RefusedBy", 300, ExpectedDuration);
            Target.ConsumeTime("Refusing", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;

        public override TimeDuration ExpectedDuration => Tuning.Crawler.RefuseTime;
    }

    public record ContrabandExpiredInteraction(
        Crawler Owner,
        IActor Target,
        string MenuOption
    ) : Interaction(Owner, Target, MenuOption) {

        public override string Description => $"Search refused - {Owner.Name} turns hostile!";

        public override bool Perform(string args = "") {
            Owner.Message($"{Target.Name} has passed the deadline!");
            Target.Message($"You wait out {Owner.Name}'s search.");

            Owner.To(Target).Ultimatum = null;
            Owner.SetHostileTo(Target, true);

            return false;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Immediate;

        public override TimeDuration ExpectedDuration => 0; // Auto-trigger, no time consumed
    }
}

/// <summary>
/// Component that generates trade interactions on-demand.
/// Trade offers are generated once and cached, interactions are enumerated fresh each time.
/// </summary>
public class TradeOfferComponent : ActorComponentBase {
    float _wealthFraction;
    ulong _seed;
    List<TradeOffer>? _offers;

    public float Markup { get; set; } = 1.0f;
    public float Spread { get; set; } = 1.0f;

    public TradeOfferComponent(ulong seed, float wealthFraction = 0.25f, float? markup = null, float? spread = null) {
        _seed = seed;
        _wealthFraction = wealthFraction;
        if (markup.HasValue) Markup = markup.Value;
        if (spread.HasValue) Spread = spread.Value;
    }

    /// <summary>
    /// Get cached trade offers, generating them on first call.
    /// Public to allow InteractionContext to access offers for market display in TUI.
    /// </summary>
    public List<TradeOffer> GetOrCreateOffers() {
        if (_offers == null) {
            _offers = Owner.MakeTradeOffers(_seed, _wealthFraction);
        }
        return _offers;
    }

    // Trade offers don't need to subscribe to encounter events

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility check
        if (Owner.Fighting(subject)) yield break;

        var offers = GetOrCreateOffers();

        foreach (var offer in offers) {
            if (offer.IsCommodity) {
                var commodity = offer.Commodity!.Value;
                var totalPrice = Commodity.Scrap.Round(offer.PricePerUnit * offer.Quantity);

                if (offer.Direction == TradeDirection.Sell) {
                    // Owner sells to subject (subject buys)
                    var sellOffer = new CommodityOffer(commodity, offer.Quantity);
                    yield return new TradeInteraction(
                        subject,  // buyer
                        new ScrapOffer(totalPrice),
                        Owner,    // seller
                        sellOffer,
                        "B",
                        $"Buy {sellOffer.Description} for {totalPrice}¢¢"
                    );
                } else {
                    // Owner buys from subject (subject sells)
                    var buyOffer = new CommodityOffer(commodity, offer.Quantity);
                    yield return new TradeInteraction(
                        subject,  // seller
                        buyOffer,
                        Owner,    // buyer
                        new ScrapOffer(totalPrice),
                        "S",
                        $"Sell {buyOffer.Description} for {totalPrice}¢¢"
                    );
                }
            } else if (offer.IsSegment) {
                var segment = offer.Segment!;
                var price = Commodity.Scrap.Round(offer.PricePerUnit);

                if (offer.Direction == TradeDirection.Sell) {
                    // Owner sells segment to subject
                    yield return new TradeInteraction(
                        subject,
                        new ScrapOffer(price),
                        Owner,
                        new SegmentOffer(segment),
                        "B",
                        $"Buy {segment.Name} for {price}¢¢"
                    );
                }
            }
        }
    }

    /// <summary>
    /// Trade interaction using NewInteraction base class
    /// </summary>
    public record TradeInteraction(
        IActor Attacker,
        IOffer AgentOffer,
        IActor Subject,
        IOffer SubjectOffer,
        string MenuOption,
        string Desc
    ) : Interaction(Attacker, Subject, MenuOption) {
        public override string Description => Desc;

        public override bool Perform(string args = "") {
            SynchronizeActors();

            int count = 1;
            if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args, out int parsed)) {
                count = Math.Max(1, parsed);
            }

            Mechanic.Message($"You gave {Subject.Name} {AgentOffer.Description} and got {SubjectOffer.Description} in return. (x{count})");
            Subject.Message($"You gave {Mechanic.Name} {SubjectOffer.Description} and got {AgentOffer.Description} in return. (x{count})");

            int performed = 0;
            for (int i = 0; i < count; i++) {
                if (AgentOffer.DisabledFor(Mechanic, Subject) != null || SubjectOffer.DisabledFor(Subject, Mechanic) != null) {
                    break;
                }
                AgentOffer.PerformOn(Mechanic, Subject);
                SubjectOffer.PerformOn(Subject, Mechanic);
                performed++;
            }

            // Time consumption for trading
            long tradeDuration = ExpectedDuration * performed;
            Mechanic.ConsumeTime("Trading", 300, tradeDuration);
            Subject.ConsumeTime("Trading", 300, tradeDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (AgentOffer.DisabledFor(Mechanic, Subject) == null && SubjectOffer.DisabledFor(Subject, Mechanic) == null) {
                return Immediacy.Menu;
            }
            return Immediacy.Failed;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.TradeTime;
    }
}

/// <summary>
/// Component that provides repair services at Owners
/// </summary>
public class RepairComponent : ActorComponentBase {
    string _optionCode;
    float _markup;

    public RepairComponent(string optionCode = "R", float markup = 1.2f) {
        _optionCode = optionCode;
        _markup = markup;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (subject is not Crawler damaged) yield break;
        if (Owner.To(subject).Hostile || subject.To(Owner).Hostile) yield break;

        // Check if there's already an active repair relationship
        var ownerToSubject = Owner.To(subject).Ultimatum;
        var subjectToOwner = subject.To(Owner).Ultimatum;
        bool hasActiveRepair = (ownerToSubject?.Type == "RepairMechanic") ||
                               (subjectToOwner?.Type == "RepairCustomer");

        if (hasActiveRepair) yield break;

        // Enumerate repair interactions for each damaged segment
        foreach (var segment in damaged.Segments.Where(IsRepairable)) {
            float value = segment.Cost / 8;
            float price = value * _markup;
            yield return new RepairInteraction(Owner, subject, segment, price, _optionCode);
        }
    }

    static bool IsRepairable(Segment segment) => segment is { Hits: > 0, IsDestroyed: false };

    /// <summary>
    /// Repair interaction - fixes a damaged segment for scrap
    /// </summary>
    public record RepairInteraction(IActor Attacker, IActor Subject, Segment SegmentToRepair, float Price, string MenuOption)
        : Interaction(Attacker, Subject, MenuOption) {

        public override string Description => $"Repair {SegmentToRepair.Name} for {Price}¢¢";

        public override bool Perform(string args = "") {
            SynchronizeActors();

            int duration = 3600; // 1 hour to repair
            long endTime = Mechanic.Time + duration;

            // Create bidirectional Ultimatums to track the repair relationship
            Subject.To(Mechanic).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairCustomer",
                Data = SegmentToRepair
            };

            Mechanic.To(Subject).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairMechanic",
                Data = SegmentToRepair
            };

            // Lock both actors for the duration
            Subject.ConsumeTime("Repairing", 300, ExpectedDuration, post: () => {
                // Complete the repair
                SegmentToRepair.Hits = 0;
                Subject.Message($"Repair of {SegmentToRepair.Name} completed");
                // Clear the Ultimatums
                Subject.To(Mechanic).Ultimatum = null;
                Mechanic.To(Subject).Ultimatum = null;
            });

            Mechanic.ConsumeTime("Repaired", 300, ExpectedDuration, post: () => {
                Mechanic.Message($"Finished repairing {Subject.Name}'s vehicle");
            });

            // Pay for the repair upfront
            var scrapOffer = new ScrapOffer(Price);
            scrapOffer.PerformOn(Subject, Mechanic);

            Mechanic.Message($"Starting repair of {Subject.Name}'s {SegmentToRepair.Name}...");
            Subject.Message($"{Mechanic.Name} begins repairing your {SegmentToRepair.Name}");

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Check if there's already an active repair relationship
            var subjectToAgent = Subject.To(Mechanic).Ultimatum;
            var agentToSubject = Mechanic.To(Subject).Ultimatum;

            // Check if this specific segment is being repaired
            bool segmentBeingRepaired =
                (subjectToAgent?.Type == "RepairCustomer" && subjectToAgent.Data == SegmentToRepair) ||
                (agentToSubject?.Type == "RepairMechanic" && agentToSubject.Data == SegmentToRepair);

            if (segmentBeingRepaired) return Immediacy.Failed;
            if (!IsRepairable(SegmentToRepair)) return Immediacy.Failed;
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        static bool IsRepairable(Segment segment) => segment is { Hits: > 0, IsDestroyed: false };

        public override TimeDuration ExpectedDuration => 3600; // 1 hour repair time
    }
}

/// <summary>
/// Component that provides license purchases at Owners
/// </summary>
public class LicenseComponent : ActorComponentBase {
    string _optionCode;

    public LicenseComponent(string optionCode = "I") {
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (subject is not Crawler buyer) yield break;
        if (Owner.To(subject).Hostile || subject.To(Owner).Hostile) yield break;

        var agentFaction = Owner.Faction;
        var buyerRelation = buyer.To(agentFaction);

        // Define license prices
        var controlledPrices = new EArray<GameTier, float> {
            [GameTier.None] = 0f,
            [GameTier.Early] = 1000f,
            [GameTier.Mid] = 5000f,
            [GameTier.Late] = 25000f
        };

        var restrictedPrices = new EArray<GameTier, float> {
            [GameTier.None] = 0f,
            [GameTier.Early] = 5000f,
            [GameTier.Mid] = 20000f,
            [GameTier.Late] = 80000f
        };

        // Iterate through all commodity categories
        foreach (CommodityCategory category in Enum.GetValues<CommodityCategory>()) {
            var policy = agentFaction.GetPolicy(category);

            // Only offer licenses for Controlled or Restricted goods
            if (policy != TradePolicy.Controlled && policy != TradePolicy.Restricted) {
                continue;
            }

            var prices = policy == TradePolicy.Controlled ? controlledPrices : restrictedPrices;
            var currentLicense = buyerRelation.GetLicense(category);

            // Offer licenses at each tier higher than what the player currently has
            foreach (GameTier tier in Enum.GetValues<GameTier>()) {
                if (tier > currentLicense) {
                    var price = prices[tier];
                    yield return new LicenseInteraction(Owner, subject, agentFaction, category, tier, price, _optionCode);
                }
            }
        }
    }

    /// <summary>
    /// License purchase interaction
    /// </summary>
    public record LicenseInteraction(
        IActor Attacker,
        IActor Subject,
        Factions LicenseFaction,
        CommodityCategory Category,
        GameTier Tier,
        float Price,
        string MenuOption
    ) : Interaction(Attacker, Subject, MenuOption) {

        public override string Description {
            get {
                var licenseOffer = new LicenseOffer(LicenseFaction, Category, Tier, Price);
                return $"Buy {licenseOffer.Description} for {Price}¢¢";
            }
        }

        public override bool Perform(string args = "") {
            var licenseOffer = new LicenseOffer(LicenseFaction, Category, Tier, Price);
            var scrapOffer = new ScrapOffer(Price);

            Mechanic.Message($"You sold {Subject.Name} a {licenseOffer.Description} for {Price}¢¢");
            Subject.Message($"{Mechanic.Name} sold you a {licenseOffer.Description} for {Price}¢¢");

            licenseOffer.PerformOn(Mechanic, Subject);
            scrapOffer.PerformOn(Subject, Mechanic);

            // Time consumption for license purchase
            Mechanic.ConsumeTime("SellingLicense", 300, ExpectedDuration);
            Subject.ConsumeTime("BuyingLicense", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.LicenseTime;
    }
}

/// <summary>
/// Component for harvesting specific resources from static actors
/// </summary>
public class HarvestComponent : ActorComponentBase {
    Inventory _amount;
    string _optionCode;
    string _verb;

    public HarvestComponent(Inventory amount, string verb, string optionCode = "H") {
        _amount = amount;
        _verb = verb;
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Can harvest if not already looted
        if (Owner.EndState == EEndState.Looted) yield break;

        yield return new HarvestInteraction(Owner, subject, _amount, _verb, _optionCode);
    }

    public record HarvestInteraction(
        IActor Resource,
        IActor Harvester,
        Inventory Amount,
        string Verb,
        string MenuOption
    ) : Interaction(Resource, Harvester, MenuOption) {

        public override string Description => Verb;

        public override bool Perform(string args = "") {
            var inventoryOffer = new InventoryOffer(false, Amount);

            Resource.Message($"{Harvester.Name} harvests you");
            Harvester.Message($"You harvest {Resource.Name} and take {Amount}");

            inventoryOffer.PerformOn(Resource, Harvester);
            Resource.SetEndState(EEndState.Looted, $"harvested by {Harvester.Name}");

            // Time consumption for harvesting (only harvester consumes time, resource is static)
            Harvester.ConsumeTime("Harvesting", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Resource.EndState == EEndState.Looted) return Immediacy.Failed;
            var inventoryOffer = new InventoryOffer(false, Amount);
            if (inventoryOffer.DisabledFor(Resource, Harvester) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.HarvestTime;
    }
}

/// <summary>
/// Component for hazardous exploration with risk/reward
/// </summary>
public class HazardComponent : ActorComponentBase {
    XorShift _rng;
    Inventory _risk;
    float _riskChance;
    string _description;
    string _optionCode;

    public HazardComponent(ulong seed, Inventory risk, float riskChance, string description, string optionCode = "H") {
        _rng = new XorShift(seed);
        _risk = risk;
        _riskChance = riskChance;
        _description = description;
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Can explore if not already looted
        if (Owner.EndState == EEndState.Looted) yield break;
        if (subject == Owner) yield break;

        yield return new HazardInteraction(Owner, subject, _rng, _risk, _riskChance, _description, _optionCode);
    }

    public record HazardInteraction(
        IActor Hazard,
        IActor Explorer,
        XorShift Rng,
        Inventory Risk,
        float RiskChance,
        string Desc,
        string MenuOption
    ) : Interaction(Hazard, Explorer, MenuOption) {

        public override string Description => Desc;

        public override bool Perform(string args = "") {
            // Determine loss amount
            var risked = Risk.Loot(Rng/1, RiskChance);

            Hazard.Message($"{Explorer.Name} explores the hazard");

            // Check if Explorer can afford the full loss (has >= risked amount)
            bool canAfford = Explorer.Supplies.Contains(risked) != FromInventory.None;

            // Always lose min(risked, current inventory)
            var actualLoss = new Inventory();
            foreach (var commodity in Enum.GetValues<Commodity>()) {
                if (risked[commodity] > 0) {
                    float loss = Math.Min(risked[commodity], Explorer.Supplies[commodity]);
                    if (loss > 0) {
                        actualLoss.Add(commodity, loss);
                        Explorer.Supplies[commodity] -= loss;
                    }
                }
            }

            if (!actualLoss.IsEmpty) {
                Explorer.Message($"You lose {actualLoss} exploring {Hazard.Name}!");
            }

            // Only give reward if could afford the full risked amount
            if (canAfford) {
                var rewardOffer = Hazard.CargoOffer();
                Explorer.Message($"You gain {rewardOffer.Description} from {Hazard.Name}");
                rewardOffer.PerformOn(Hazard, Explorer);
            } else {
                Explorer.Message($"You couldn't afford the full loss and gain nothing from {Hazard.Name}");
            }

            Hazard.SetEndState(EEndState.Looted, $"explored by {Explorer.Name}");

            // Time consumption for exploring hazard (only explorer consumes time, hazard is static)
            Explorer.ConsumeTime("Exploring", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Hazard.EndState == EEndState.Looted) return Immediacy.Failed;

            // Check if Explorer has > 0 of any risked resource (allows attempting the hazard)
            bool hasAnyRiskedResource = false;
            foreach (var commodity in Enum.GetValues<Commodity>()) {
                if (Risk[commodity] > 0 && Explorer.Supplies[commodity] > 0) {
                    hasAnyRiskedResource = true;
                    break;
                }
            }
            if (!hasAnyRiskedResource) return Immediacy.Failed;

            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.HazardTime;
    }
}

/// <summary>
/// Component that displays arrival/departure messages.
/// </summary>
public class EncounterMessengerComponent : ActorComponentBase {
    public override void Attach(IActor owner) {
        base.Attach(owner);
        Owner.ActorInitialized += OnActorInitialized;
        Owner.ActorDestroyed += OnActorDestroyed;
        Owner.HostilityChanged += OnHostilityChanged;
        Owner.ReceivingFire += OnReceivingFire;
    }

    void OnActorInitialized() {
        Owner.Message($"{Owner.Name} initialized");
    }

    void OnActorDestroyed() {
        Owner.Message($"{Owner.Name} destroyed");
    }

    public override void Enter(Encounter encounter) {
        base.Enter(encounter);
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
        encounter.EncounterTicked += OnEncounterTicked;
    }

    void OnEncounterTicked(TimePoint then, TimePoint now) {
        // TODO: Add message levels
        var thenString = Game.TimeString(then);
        var nowString = Game.TimeString(now);
        Owner.Message($"{Owner?.Location.GetEncounter()} ticked {thenString}->{nowString}");
    }

    void OnActorArrived(IActor actor, TimePoint time) {
        if (actor != Owner) {
            if (time < Owner.Time) {
                // Historical arrival - was already here
                Owner.Message($"{actor.Name} is here");
            } else {
                // Real-time arrival
                Owner.Message($"{actor.Name} enters");
            }
        } else {
            var encounter = GetEncounter();
            Owner.Message($"You entered {encounter.Name}");
            // List all actors already present
            foreach (var other in encounter.ActorsExcept(Owner)) {
                Owner.Message($"{other.Name} is here");
            }
        }
    }

    void OnActorLeft(IActor actor, TimePoint time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} leaves");
        } else {
            Owner.Message($"You leave {GetEncounter().Name}");
        }
    }

    void OnHostilityChanged(IActor other, bool hostile) {
        if (hostile) {
            Owner.Message($"{other.Name} became Hostile");
        } else {
            Owner.Message($"{other.Name} became Peaceful");
        }
    }

    void OnReceivingFire(IActor from, List<HitRecord> fire) {
        Owner.Message($"{from.Name} fired {fire.Count} shots at you.");
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
        encounter.EncounterTicked -=  OnEncounterTicked;
        base.Leave(encounter);
    }

    public override void Detach() {
        Owner.ActorInitialized -= OnActorInitialized;
        Owner.ActorDestroyed -= OnActorDestroyed;
        Owner.HostilityChanged -= OnHostilityChanged;
        Owner.ReceivingFire -= OnReceivingFire;
        base.Detach();
    }
    public override void OnComponentsDirty() {
        Owner.Message($"{Owner.Name} component list changed.");
        base.OnComponentsDirty();
    }

    public override ActorEvent? GetNextEvent() {
        Owner.Message($"{Owner.Name} think action");
        return base.GetNextEvent();
    }
}

/// <summary>
/// Component that prunes relations when leaving encounters.
/// Keeps only hostile relationships and relationships with Owners.
/// </summary>
public class RelationPrunerComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
        encounter.ActorLeft += OnActorLeft;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorLeft(IActor actor, TimePoint time) {
        if (actor != Owner) return;
        if (Owner is not Crawler crawler) return;

        PruneRelations(crawler);
    }

    void PruneRelations(Crawler crawler) {
        var relations = crawler.GetRelations();
        Dictionary<IActor, ActorToActor> pruned = new();
        foreach (var (actor, relation) in relations) {
            if (actor is Crawler { IsSettlement: true, IsDestroyed: false } || relation.Hostile) {
                pruned.Add(actor, relation);
            }
        }
        crawler.SetRelations(pruned);
    }

}

/// <summary>
/// Component that manages automatic departure from encounters.
/// Used by dynamic actors to leave after their scheduled time.
/// </summary>
public class LeaveEncounterComponent : ActorComponentBase {
    TimePoint _exitTime;
    ActorEvent? _leaveEvent;

    public LeaveEncounterComponent() {
        _exitTime = TimePoint.Zero;
    }

    public LeaveEncounterComponent(TimePoint exitTime) {
        ExitAt(exitTime);
    }

    public TimePoint ExitTime => _exitTime;

    public override ActorEvent? GetNextEvent() {
        if (_exitTime.IsValid && _leaveEvent == null) {
            _leaveEvent = new ActorEvent.EncounterEvent(Owner, _exitTime, "LeaveEncounter", null,
                () => Owner.Destroy(), 10);
        }
        return _leaveEvent;
    }

    public void ExitAfter(TimeDuration duration) {
        ExitAt(Owner.Time + duration);
    }

    public void ExitAt(TimePoint exitTime) {
        if (_exitTime.IsValid && exitTime == _exitTime) {
            return;
        }
        _exitTime = exitTime;
        _leaveEvent = null;
    }
}

/// <summary>
/// Component that handles life support resource consumption and crew survival.
/// Manages fuel, rations, water, and air consumption, as well as crew death from deprivation.
/// </summary>
public class LifeSupportComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
    }

    public override void Leave(Encounter encounter) {
    }

    public override void Tick() {
        if (Owner is not Crawler crawler) return;

        ConsumeResources(crawler, Owner.Elapsed);
    }

    /// <summary>
    /// Consume life support resources based on elapsed time
    /// </summary>
    public void ConsumeResources(Crawler crawler, int elapsed) {
        float hours = elapsed / 3600f;

        crawler.ScrapInv -= crawler.WagesPerHr * hours;
        crawler.RationsInv -= crawler.RationsPerHr * hours;
        crawler.WaterInv -= crawler.WaterRecyclingLossPerHr * hours;
        crawler.AirInv -= crawler.AirLeakagePerHr * hours;
        crawler.FuelInv -= crawler.FuelPerHr * hours;
    }
}

/// <summary>
/// Component that automatically repairs damaged segments using excess power, crew, and scrap.
/// </summary>
public class AutoRepairComponent : ActorComponentBase {
    float _repairProgress = 0;
    RepairMode _repairMode;

    public AutoRepairComponent(RepairMode repairMode = RepairMode.RepairLowest) {
        _repairMode = repairMode;
    }

    public RepairMode RepairMode { get; set; }

    /// <summary>
    /// Attempt to repair damaged segments using excess power
    /// </summary>
    public float PerformRepair(float power, int elapsed) {
        if (Owner is not Crawler crawler)
            return power;

        // TODO: We should consume the energy if we have undestroyed segments and are adding repair progress
        // At the moment the energy is only consumed when we do the repair
        // Not a problem because nothing follows this to use it.

        float maxRepairsCrew = crawler.CrewInv / Tuning.Crawler.RepairCrewPerHp;
        float maxRepairsPower = power / Tuning.Crawler.RepairPowerPerHp + 0.25f; // Some repairs possible without power
        float maxRepairsScrap = crawler.ScrapInv / Tuning.Crawler.RepairScrapPerHp;
        float maxRepairs = Math.Min(Math.Min(maxRepairsCrew, maxRepairsPower), maxRepairsScrap);
        float maxRepairsHr = maxRepairs * elapsed / Tuning.Crawler.RepairTime;
        float repairHitsFloat = maxRepairsHr + _repairProgress;
        int repairHits = (int)repairHitsFloat;
        _repairProgress = repairHitsFloat - repairHits;
        var candidates = crawler.UndestroyedSegments.Where(s => s.Hits > 0);
        if (_repairMode is RepairMode.Off || !candidates.Any()) {
            _repairProgress = 0;
            return power;
        } else if (_repairMode is RepairMode.RepairHighest) {
            var damagedSegments = candidates.OrderByDescending(s => s.Hits).ToStack();
            while (repairHits > 0 && damagedSegments.Count > 0) {
                var segment = damagedSegments.Pop();
                --repairHits;
                --segment.Hits;
            }
        } else if (_repairMode is RepairMode.RepairLowest) {
            var damagedSegments = candidates.OrderBy(s => s.Health).ToList();
            while (repairHits > 0 && damagedSegments.Count > 0) {
                int health = damagedSegments[0].Health;
                foreach (var segment in damagedSegments.TakeWhile(s => s.Health <= health).Where(s => s.Hits > 0)) {
                    if (repairHits <= 0) {
                        break;
                    }
                    --repairHits;
                    --segment.Hits;
                }
            }
        }
        int repaired = (int)repairHitsFloat - repairHits;

        if (repaired > 0) {
            float repairScrap = Tuning.Crawler.RepairScrapPerHp * repaired;
            float repairPower = Tuning.Crawler.RepairPowerPerHp * repaired;
            crawler.Message($"Repaired {repaired} Hits for {repairScrap:F1}¢¢.");

            power -= repairPower;
            crawler.ScrapInv -= repairScrap;
        }

        return power;
    }
}
