namespace Crawler;

/// <summary>
/// Component that handles contraband scanning, enforcement, and seizure.
/// Actors with this component scan for prohibited goods and create ultimatums to seize them or turn hostile.
/// Can be added to Owners, customs officers, or other enforcement actors.
/// </summary>
public class CustomsComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, long time) {
        if (actor == Owner) return;

        SetupContrabandUltimatum(actor, time);
    }

    void OnActorLeft(IActor actor, long time) {
        if (actor == Owner) return;

        // Clear ultimatum when target leaves
        Owner.To(actor).Ultimatum = null;
    }

    void SetupContrabandUltimatum(IActor target, long time) {
        if (!target.IsPlayer()) {
            return;
        }

        var contraband = Scan(target);
        if (!Owner.Fighting(target)) {
            // Set ultimatum with timeout
            Owner.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = time + 300,
                Type = "ContrabandSeizure",
                Data = contraband,
            };
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
        if (relation.Ultimatum.Data is not Inventory contraband) yield break;

        // Rescan to update the interaction text
        contraband = Scan(subject);
        if (contraband.IsEmpty) {
            // No contraband found, clear ultimatum
            relation.Ultimatum = null;
            yield break;
        }

        long expirationTime = relation.Ultimatum.ExpirationTime;
        bool expired = expirationTime > 0 && Game.SafeTime > expirationTime;

        if (!expired) {
            // Offer accept option (allow search and seizure)
            yield return new ContrabandAcceptInteraction(Owner, subject, contraband, "CY");
            // Offer refuse option (go hostile)
            yield return new ContrabandRefuseInteraction(Crawler, subject, "");
        } else {
            // Time expired - turn hostile
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
            if (ultimatum == null) return null;
            var contraband = ultimatum.Data as Inventory;
            if (contraband != null && ultimatum?.ExpirationTime > 0) {
                return $"You have until {Game.TimeString(ultimatum.ExpirationTime)} to submit to customs search.\n" +
                       $"You are carrying {contraband.Brief()}";

            }
            return null;
        }

        public override int Perform(string args = "") {
            Owner.Message($"{Target.Name} allows search and surrenders {Contraband}");
            Target.Message($"You allow {Owner.Name} to search and seize {Contraband}");

            Target.Supplies.Remove(Contraband);
            // TODO: Generalize different inventories into components
            // TODO: Move to evidence inventory
            Owner.Cargo.Add(Contraband);

            // Clear ultimatum
            Owner.To(Target).Ultimatum = null;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var inventoryOffer = new InventoryOffer(false, Contraband);
            if (inventoryOffer.DisabledFor(Target, Owner) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }

    public record ContrabandRefuseInteraction(
        Crawler Owner,
        IActor Target,
        string MenuOption
    ) : Interaction(Owner, Target, MenuOption) {

        public override string Description => $"Refuse search from {Owner.Name}";

        public override string? MessageFor(IActor viewer) => null;

        public override int Perform(string args = "") {
            Owner.Message($"{Target.Name} refuses search!");
            Target.Message($"You refuse {Owner.Name}'s search - now hostile!");

            Owner.To(Target).Ultimatum = null;
            Owner.SetHostileTo(Target, true);

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;
    }

    public record ContrabandExpiredInteraction(
        Crawler Owner,
        IActor Target,
        string MenuOption
    ) : Interaction(Owner, Target, MenuOption) {

        public override string Description => $"Search refused - {Owner.Name} turns hostile!";

        public override int Perform(string args = "") {
            Owner.Message($"{Target.Name} has passed the deadline!");
            Target.Message($"You wait out {Owner.Name}'s search.");

            Owner.To(Target).Ultimatum = null;
            Owner.SetHostileTo(Target, true);

            return 0;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Immediate;
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

    public TradeOfferComponent(ulong seed, float wealthFraction = 0.25f) {
        _seed = seed;
        _wealthFraction = wealthFraction;
    }

    List<TradeOffer> GetOrCreateOffers() {
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

        public override int Perform(string args = "") {
            int count = 1;
            if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args, out int parsed)) {
                count = Math.Max(1, parsed);
            }

            Attacker.Message($"You gave {Subject.Name} {AgentOffer.Description} and got {SubjectOffer.Description} in return. (x{count})");
            Subject.Message($"You gave {Attacker.Name} {SubjectOffer.Description} and got {AgentOffer.Description} in return. (x{count})");

            int performed = 0;
            for (int i = 0; i < count; i++) {
                if (AgentOffer.DisabledFor(Attacker, Subject) != null || SubjectOffer.DisabledFor(Subject, Attacker) != null) {
                    break;
                }
                AgentOffer.PerformOn(Attacker, Subject);
                SubjectOffer.PerformOn(Subject, Attacker);
                performed++;
            }

            return performed;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (AgentOffer.DisabledFor(Attacker, Subject) == null && SubjectOffer.DisabledFor(Subject, Attacker) == null) {
                return Immediacy.Menu;
            }
            return Immediacy.Failed;
        }
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
        // Feasibility: owner must be Owner, subject must be damaged crawler, not hostile
        if (!Owner.Flags.HasFlag(ActorFlags.Settlement)) yield break;
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

        public override int Perform(string args = "") {
            long duration = 3600; // 1 hour to repair
            long endTime = Game.SafeTime + duration;

            // Create bidirectional Ultimatums to track the repair relationship
            Subject.To(Attacker).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairCustomer",
                Data = SegmentToRepair
            };

            Attacker.To(Subject).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairMechanic",
                Data = SegmentToRepair
            };

            // Lock both actors for the duration
            (Subject as ActorScheduled)?.ConsumeTime(duration, customer => {
                // Complete the repair
                SegmentToRepair.Hits = 0;
                customer.Message($"Repair of {SegmentToRepair.Name} completed");
                // Clear the Ultimatums
                customer.To(Attacker).Ultimatum = null;
                Attacker.To(customer).Ultimatum = null;
            });

            (Attacker as ActorScheduled)?.ConsumeTime(duration, mechanic => {
                mechanic.Message($"Finished repairing {Subject.Name}'s vehicle");
            });

            // Pay for the repair upfront
            var scrapOffer = new ScrapOffer(Price);
            scrapOffer.PerformOn(Subject, Attacker);

            Attacker.Message($"Starting repair of {Subject.Name}'s {SegmentToRepair.Name}...");
            Subject.Message($"{Attacker.Name} begins repairing your {SegmentToRepair.Name}");

            return (int)duration;
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Check if there's already an active repair relationship
            var subjectToAgent = Subject.To(Attacker).Ultimatum;
            var agentToSubject = Attacker.To(Subject).Ultimatum;

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
        // Feasibility: owner must be Owner, subject must be crawler, not hostile
        if (!Owner.HasFlag(ActorFlags.Settlement)) yield break;
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
        Faction LicenseFaction,
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

        public override int Perform(string args = "") {
            var licenseOffer = new LicenseOffer(LicenseFaction, Category, Tier, Price);
            var scrapOffer = new ScrapOffer(Price);

            Attacker.Message($"You sold {Subject.Name} a {licenseOffer.Description} for {Price}¢¢");
            Subject.Message($"{Attacker.Name} sold you a {licenseOffer.Description} for {Price}¢¢");

            licenseOffer.PerformOn(Attacker, Subject);
            scrapOffer.PerformOn(Subject, Attacker);

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;
            return Immediacy.Menu;
        }
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

        public override int Perform(string args = "") {
            var inventoryOffer = new InventoryOffer(false, Amount);

            Resource.Message($"{Harvester.Name} harvests you");
            Harvester.Message($"You harvest {Resource.Name} and take {Amount}");

            inventoryOffer.PerformOn(Resource, Harvester);
            Resource.End(EEndState.Looted, $"harvested by {Harvester.Name}");

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Resource.EndState == EEndState.Looted) return Immediacy.Failed;
            var inventoryOffer = new InventoryOffer(false, Amount);
            if (inventoryOffer.DisabledFor(Resource, Harvester) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }
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
        // Can explore if not already looted and subject has enough inventory to risk
        if (Owner.EndState == EEndState.Looted) yield break;
        if (subject == Owner) yield break;
        if (subject.Supplies.Contains(_risk) == FromInventory.None) yield break;

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

        public override int Perform(string args = "") {
            var rewardOffer = Hazard.CargoOffer();
            var risked = Risk.Loot(Rng/1, RiskChance);
            var riskOffer = new InventoryOffer(false, risked);

            Hazard.Message($"{Explorer.Name} explores the hazard");
            Explorer.Message($"You explore {Hazard.Name} and gain {rewardOffer.Description}");

            rewardOffer.PerformOn(Hazard, Explorer);

            if (!risked.IsEmpty) {
                Explorer.Message($"But you lose {risked} in the process!");
                riskOffer.PerformOn(Explorer, Hazard);
            }

            Hazard.End(EEndState.Looted, $"explored by {Explorer.Name}");

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Hazard.EndState == EEndState.Looted) return Immediacy.Failed;
            if (Explorer.Supplies.Contains(Risk) == FromInventory.None) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }
}

/// <summary>
/// Component that displays arrival/departure messages.
/// </summary>
public class EncounterMessengerComponent : ActorComponentBase {
    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeaving += OnActorLeaving;
        encounter.ActorLeft += OnActorLeft;
        encounter.EncounterTick += OnEncounterTick;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeaving -= OnActorLeaving;
        encounter.ActorLeft -= OnActorLeft;
        encounter.EncounterTick -=  OnEncounterTick;
    }

    public override void Attach(IActor owner) {
        base.Attach(owner);
        Owner.HostilityChanged += HostilityChanged;
        Owner.ReceivingFire += ReceivingFire;
    }

    public override void Detach() {
        Owner.HostilityChanged -= HostilityChanged;
        Owner.ReceivingFire -= ReceivingFire;
        base.Detach();
    }

    void OnActorArrived(IActor actor, long time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} enters");
        }
    }

    void OnActorLeaving(IActor actor, long time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} about to leave");
        }
    }

    void OnActorLeft(IActor actor, long time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} leaves");
        }
    }

    void HostilityChanged(IActor other, bool hostile) {
        if (hostile) {
            Owner.Message($"{other.Name} became Hostile");
        } else {
            Owner.Message($"{other.Name} became Peaceful");
        }
    }

    void ReceivingFire(IActor from, List<HitRecord> fire) {
        Owner.Message($"{from.Name} fired {fire.Count} shots.");
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeaving -= OnActorLeaving;
        encounter.ActorLeft -= OnActorLeft;
        encounter.EncounterTick -=  OnEncounterTick;
        base.Leave(encounter);
    }

    public override void Detach() {
        Owner.ActorInitialized -= ActorInitialized;
        Owner.HostilityChanged -= HostilityChanged;
        Owner.ReceivingFire -= ReceivingFire;
        base.Detach();
    }
    public override void OnComponentsDirty() {
        Owner.Message($"{Owner.Name} component list changed.");
        base.OnComponentsDirty();
    }

    public override int ThinkAction() {
        Owner.Message($"{Owner.Name} think action");
        return base.ThinkAction();
    }
}

/// <summary>
/// Component that prunes relations when leaving encounters.
/// Keeps only hostile relationships and relationships with Owners.
/// </summary>
public class RelationPrunerComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
        encounter.ActorLeaving += OnActorLeaving;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorLeaving -= OnActorLeaving;
    }

    void OnActorLeaving(IActor actor, long time) {
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
/// Component that handles life support resource consumption and crew survival.
/// Manages fuel, rations, water, and air consumption, as well as crew death from deprivation.
/// </summary>
public class LifeSupportComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) {
        encounter.EncounterTick += OnEncounterTick;
    }

    public override void Leave(Encounter encounter) {
        encounter.EncounterTick -= OnEncounterTick;
    }

    void OnEncounterTick(long time) {
        if (Owner is not Crawler crawler) return;

        // This runs during Tick, which already calculates elapsed
        // We'll handle this in a direct call from Crawler.Tick instead
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

    /// <summary>
    /// Handle crew death from resource deprivation
    /// </summary>
    public void ProcessSurvival(Crawler crawler, int elapsed) {
        // Check rations
        if (crawler.RationsInv <= 0) {
            crawler.Message("You are out of rations and your crew is starving.");
            float liveRate = 0.99f;
            liveRate = (float)Math.Pow(liveRate, elapsed / 3600);
            crawler.CrewInv *= liveRate;
            crawler.RationsInv = 0;
        }

        // Check water
        if (crawler.WaterInv <= 0) {
            crawler.Message("You are out of water. People are dying of dehydration.");
            float keepRate = 0.98f;
            keepRate = (float)Math.Pow(keepRate, elapsed / 3600);
            crawler.CrewInv *= keepRate;
            crawler.WaterInv = 0;
        }

        // Check air
        float maxPopulation = (int)(crawler.AirInv / Tuning.Crawler.AirPerPerson);
        if (maxPopulation < crawler.TotalPeople) {
            crawler.Message("You are out of air. People are suffocating.");
            var died = crawler.TotalPeople - maxPopulation;
            if (died >= crawler.CrewInv) {
                died -= crawler.CrewInv;
                crawler.CrewInv = 0;
            } else {
                crawler.CrewInv -= died;
                died = 0;
            }
        }

        if (crawler.IsDepowered) {
            crawler.Message("Your life support systems are offline.");
            crawler.MoraleInv -= 1.0f;
        }
    }
}

/// <summary>
/// Component that automatically repairs damaged segments using excess power, crew, and scrap.
/// </summary>
public class AutoRepairComponent : ActorComponentBase {
    float _repairProgress = 0;
    RepairMode _repairMode;

    public AutoRepairComponent(RepairMode repairMode = RepairMode.Off) {
        _repairMode = repairMode;
    }

    public RepairMode RepairMode {
        get => _repairMode;
        set => _repairMode = value;
    }

    /// <summary>
    /// Attempt to repair damaged segments using excess power
    /// </summary>
    public float PerformRepair(Crawler crawler, float power, int elapsed) {
        // TODO: We should consume the energy if we have undestroyed segments and are adding repair progress
        // At the moment the energy is only consumed when we do the repair
        // Not a problem because nothing follows this to use it.

        float maxRepairsCrew = crawler.CrewInv / Tuning.Crawler.RepairCrewPerHp;
        float maxRepairsPower = power / Tuning.Crawler.RepairPowerPerHp;
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
