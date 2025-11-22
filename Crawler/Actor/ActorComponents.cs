namespace Crawler;

/// <summary>
/// Component that handles settlement contraband scanning and seizure.
/// Settlements scan for prohibited goods and create ultimatums to seize them or turn hostile.
/// </summary>
public class SettlementContrabandComponent : ActorComponentBase {
    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, long time) {
        if (Owner is not Crawler settlement) return;
        if (actor == Owner) return;

        SetupContrabandScan(settlement, actor, time);
    }

    void OnActorLeft(IActor actor, long time) {
        if (Owner is not Crawler settlement) return;
        if (actor == Owner) return;

        // Clear ultimatum when target leaves
        settlement.To(actor).Ultimatum = null;
    }

    void SetupContrabandScan(Crawler settlement, IActor target, long time) {
        var contraband = settlement.ScanForContraband(target);
        if (!contraband.IsEmpty && !settlement.Fighting(target)) {
            // Set ultimatum with timeout
            settlement.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = time + 300,
                Type = "ContrabandSeizure",
                Data = contraband
            };
            settlement.To(target).DirtyInteractions = true;
        }
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (Owner is not Crawler settlement) yield break;

        var relation = settlement.To(subject);
        if (relation.Ultimatum?.Type != "ContrabandSeizure") yield break;
        if (relation.Ultimatum.Data is not Inventory contraband) yield break;

        long expirationTime = relation.Ultimatum.ExpirationTime;
        bool expired = expirationTime > 0 && Game.SafeTime > expirationTime;

        if (!expired) {
            // Offer accept option (allow search and seizure)
            yield return new ContrabandAcceptInteraction(settlement, subject, contraband, "CY");
            // Offer refuse option (go hostile)
            yield return new ContrabandRefuseInteraction(settlement, subject, "");
        } else {
            // Time expired - turn hostile
            yield return new ContrabandExpiredInteraction(settlement, subject, "");
        }
    }

    public record ContrabandAcceptInteraction(
        IActor Settlement,
        IActor Target,
        Inventory Contraband,
        string MenuOption
    ) : Interaction(Settlement, Target, MenuOption) {

        public override string Description => $"Allow search: surrender {Contraband}";

        public override int Perform(string args = "") {
            var searchOffer = new SearchOffer("allow boarding and search");
            var inventoryOffer = new InventoryOffer(false, Contraband);

            Settlement.Message($"{Target.Name} allows search and surrenders {Contraband}");
            Target.Message($"You allow {Settlement.Name} to search and seize {Contraband}");

            searchOffer.PerformOn(Settlement, Target);
            inventoryOffer.PerformOn(Target, Settlement);

            // Clear ultimatum
            Settlement.To(Target).Ultimatum = null;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var inventoryOffer = new InventoryOffer(false, Contraband);
            if (inventoryOffer.DisabledFor(Target, Settlement) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }

    public record ContrabandRefuseInteraction(
        IActor Settlement,
        IActor Target,
        string MenuOption
    ) : Interaction(Settlement, Target, MenuOption) {

        public override string Description => $"Refuse search from {Settlement.Name}";

        public override int Perform(string args = "") {
            var hostilityOffer = new HostilityOffer("refuses search");

            Settlement.Message($"{Target.Name} refuses search!");
            Target.Message($"You refuse {Settlement.Name}'s search - now hostile!");

            hostilityOffer.PerformOn(Settlement, Target);

            // Clear ultimatum
            Settlement.To(Target).Ultimatum = null;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;
    }

    public record ContrabandExpiredInteraction(
        IActor Settlement,
        IActor Target,
        string MenuOption
    ) : Interaction(Settlement, Target, MenuOption) {

        public override string Description => $"Search refused - {Settlement.Name} turns hostile!";

        public override int Perform(string args = "") {
            var hostilityOffer = new HostilityOffer("refuses search (timeout)");
            hostilityOffer.PerformOn(Settlement, Target);

            Settlement.To(Target).Ultimatum = null;

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
        IActor Agent,
        IOffer AgentOffer,
        IActor Subject,
        IOffer SubjectOffer,
        string MenuOption,
        string Desc
    ) : Interaction(Agent, Subject, MenuOption) {
        public override string Description => Desc;

        public override int Perform(string args = "") {
            int count = 1;
            if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args, out int parsed)) {
                count = Math.Max(1, parsed);
            }

            Agent.Message($"You gave {Subject.Name} {AgentOffer.Description} and got {SubjectOffer.Description} in return. (x{count})");
            Subject.Message($"You gave {Agent.Name} {SubjectOffer.Description} and got {AgentOffer.Description} in return. (x{count})");

            int performed = 0;
            for (int i = 0; i < count; i++) {
                if (AgentOffer.DisabledFor(Agent, Subject) != null || SubjectOffer.DisabledFor(Subject, Agent) != null) {
                    break;
                }
                AgentOffer.PerformOn(Agent, Subject);
                SubjectOffer.PerformOn(Subject, Agent);
                performed++;
            }

            return performed;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (AgentOffer.DisabledFor(Agent, Subject) == null && SubjectOffer.DisabledFor(Subject, Agent) == null) {
                return Immediacy.Menu;
            }
            return Immediacy.Failed;
        }
    }
}

/// <summary>
/// Component that provides attack interactions for player
/// </summary>
public class AttackComponent : ActorComponentBase {
    string _optionCode;

    public AttackComponent(string optionCode = "A") {
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: only player can attack, target must be alive crawler
        if (Owner != Game.Instance?.Player) yield break;
        if (subject is not Crawler) yield break;
        if (!subject.Lives()) yield break;

        yield return new AttackInteraction(Owner, subject, _optionCode);
    }

    /// <summary>
    /// Attack interaction - initiates combat
    /// </summary>
    public record AttackInteraction(IActor Agent, IActor Subject, string MenuOption)
        : Interaction(Agent, Subject, MenuOption) {

        public override string Description => $"Attack {Subject.Name}";

        public override int Perform(string args = "") {
            if (Agent is Crawler attacker) {
                attacker.Attack(Subject);
                return 1;
            }
            return 0;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Agent.Ended()) return Immediacy.Failed;
            if (Subject.Ended()) return Immediacy.Failed;
            if (Agent is not Crawler attacker) return Immediacy.Failed;
            if (attacker.IsDisarmed) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }
}

/// <summary>
/// Component that provides repair services at settlements
/// </summary>
public class RepairComponent : ActorComponentBase {
    string _optionCode;
    float _markup;

    public RepairComponent(string optionCode = "R", float markup = 1.2f) {
        _optionCode = optionCode;
        _markup = markup;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: owner must be settlement, subject must be damaged crawler, not hostile
        if (!Owner.Flags.HasFlag(EActorFlags.Settlement)) yield break;
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
    public record RepairInteraction(IActor Agent, IActor Subject, Segment SegmentToRepair, float Price, string MenuOption)
        : Interaction(Agent, Subject, MenuOption) {

        public override string Description => $"Repair {SegmentToRepair.Name} for {Price}¢¢";

        public override int Perform(string args = "") {
            long duration = 3600; // 1 hour to repair
            long endTime = Game.SafeTime + duration;

            // Create bidirectional Ultimatums to track the repair relationship
            Subject.To(Agent).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairCustomer",
                Data = SegmentToRepair
            };

            Agent.To(Subject).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "RepairMechanic",
                Data = SegmentToRepair
            };

            // Lock both actors for the duration
            (Subject as Crawler)?.ConsumeTime(duration, customer => {
                // Complete the repair
                SegmentToRepair.Hits = 0;
                customer.Message($"Repair of {SegmentToRepair.Name} completed");
                // Clear the Ultimatums
                customer.To(Agent).Ultimatum = null;
                Agent.To(customer).Ultimatum = null;
            });

            (Agent as Crawler)?.ConsumeTime(duration, mechanic => {
                mechanic.Message($"Finished repairing {Subject.Name}'s vehicle");
            });

            // Pay for the repair upfront
            var scrapOffer = new ScrapOffer(Price);
            scrapOffer.PerformOn(Subject, Agent);

            Agent.Message($"Starting repair of {Subject.Name}'s {SegmentToRepair.Name}...");
            Subject.Message($"{Agent.Name} begins repairing your {SegmentToRepair.Name}");

            return (int)duration;
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Check if there's already an active repair relationship
            var subjectToAgent = Subject.To(Agent).Ultimatum;
            var agentToSubject = Agent.To(Subject).Ultimatum;

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
/// Component that provides license purchases at settlements
/// </summary>
public class LicenseComponent : ActorComponentBase {
    string _optionCode;

    public LicenseComponent(string optionCode = "I") {
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: owner must be settlement, subject must be crawler, not hostile
        if (!Owner.Flags.HasFlag(EActorFlags.Settlement)) yield break;
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
            var policy = Tuning.FactionPolicies.GetPolicy(agentFaction, category);

            // Only offer licenses for Controlled or Restricted goods
            if (policy != TradePolicy.Controlled && policy != TradePolicy.Restricted) {
                continue;
            }

            var prices = policy == TradePolicy.Controlled ? controlledPrices : restrictedPrices;
            var currentLicense = buyerRelation.Licenses[category];

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
        IActor Agent,
        IActor Subject,
        Faction LicenseFaction,
        CommodityCategory Category,
        GameTier Tier,
        float Price,
        string MenuOption
    ) : Interaction(Agent, Subject, MenuOption) {

        public override string Description {
            get {
                var licenseOffer = new LicenseOffer(LicenseFaction, Category, Tier, Price);
                return $"Buy {licenseOffer.Description} for {Price}¢¢";
            }
        }

        public override int Perform(string args = "") {
            var licenseOffer = new LicenseOffer(LicenseFaction, Category, Tier, Price);
            var scrapOffer = new ScrapOffer(Price);

            Agent.Message($"You sold {Subject.Name} a {licenseOffer.Description} for {Price}¢¢");
            Subject.Message($"{Agent.Name} sold you a {licenseOffer.Description} for {Price}¢¢");

            licenseOffer.PerformOn(Agent, Subject);
            scrapOffer.PerformOn(Subject, Agent);

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }
}

/// <summary>
/// Component that allows player to accept surrender from vulnerable enemies
/// </summary>
public class SurrenderComponent : ActorComponentBase {
    XorShift _rng;
    string _optionCode;

    public SurrenderComponent(ulong seed, string optionCode = "S") {
        _rng = new XorShift(seed);
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: subject must be vulnerable crawler, hostile to owner, not already surrendered
        if (subject is not Crawler loser) yield break;
        if (!loser.IsVulnerable || !loser.Lives()) yield break;
        if (Owner == subject) yield break;
        if (!Owner.To(subject).Hostile) yield break;
        if (subject.To(Owner).Surrendered) yield break;

        yield return new SurrenderInteraction(Owner, subject, _rng, _optionCode);
    }

    /// <summary>
    /// Accept surrender interaction - loser gives up cargo/supplies based on damage
    /// </summary>
    public record SurrenderInteraction(IActor Winner, IActor Loser, XorShift Rng, string MenuOption)
        : Interaction(Winner, Loser, MenuOption) {

        public override string Description => $"Accept {Loser.Name}'s surrender";

        Inventory MakeSurrenderInv() {
            Inventory surrenderInv = new();
            float ratio = 0;
            if (Loser is Crawler loser) {
                float totalHits = loser.Segments.Sum(s => s.Hits);
                float totalMaxHits = loser.Segments.Sum(s => s.MaxHits);
                ratio = Math.Min(totalHits, 1) / Math.Min(totalMaxHits, 1);
                ratio = (float)Math.Pow(ratio, 0.8);
            }
            surrenderInv.Add(Loser.Supplies.Loot(Rng/1, ratio));
            if (Loser.Supplies != Loser.Cargo) {
                surrenderInv.Add(Loser.Cargo.Loot(Rng/2, ratio));
            }
            return surrenderInv;
        }

        public override int Perform(string args = "") {
            var surrenderInv = MakeSurrenderInv();
            float value = surrenderInv.ValueAt(Winner.Location);

            var acceptOffer = new AcceptSurrenderOffer(value, Description);
            var inventoryOffer = new InventoryOffer(false, surrenderInv);

            Winner.Message($"{Loser.Name} surrenders and gives you {inventoryOffer.Description}");
            Loser.Message($"You surrender to {Winner.Name} and give {inventoryOffer.Description}");

            acceptOffer.PerformOn(Winner, Loser);
            inventoryOffer.PerformOn(Loser, Winner);

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Loser is not Crawler loser) return Immediacy.Failed;
            if (!loser.IsVulnerable || !loser.Lives()) return Immediacy.Failed;
            if (Loser.To(Winner).Surrendered) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }
}

/// <summary>
/// Shared extortion interaction records used by both player demands and bandit extortion.
/// </summary>
public static class ExtortionInteractions {
    /// <summary>
    /// Interaction where extortioner demands cargo from target.
    /// Target complies and hands over the demanded items.
    /// </summary>
    public record AcceptExtortionInteraction(
        IActor Extortioner,
        IActor Target,
        Func<IOffer> MakeDemand,
        string DescriptionText,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => DescriptionText;

        public override int Perform(string args = "") {
            var demand = MakeDemand();

            Extortioner.Message($"{Target.Name} hands over {demand.Description}");
            Target.Message($"You hand over {demand.Description} to {Extortioner.Name}");

            demand.PerformOn(Target, Extortioner);

            // Clear ultimatum if it exists
            if (Extortioner.To(Target).Ultimatum != null) {
                Extortioner.To(Target).Ultimatum = null;
            }

            // Mark as surrendered
            Target.To(Extortioner).Surrendered = true;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var demand = MakeDemand();
            if (demand.DisabledFor(Target, Extortioner) != null) return Immediacy.Failed;
            if (Target.To(Extortioner).Surrendered) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }

    /// <summary>
    /// Interaction where target refuses extortion demand.
    /// Results in hostility between both parties.
    /// </summary>
    public record RefuseExtortionInteraction(
        IActor Extortioner,
        IActor Target,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => $"Refuse extortion from {Extortioner.Name}";

        public override int Perform(string args = "") {
            Extortioner.To(Target).Hostile = true;
            Target.To(Extortioner).Hostile = true;

            Extortioner.Message($"{Target.Name} refuses your demand!");
            Target.Message($"You refuse {Extortioner.Name}'s demand - now hostile!");

            // Clear ultimatum if it exists
            if (Extortioner.To(Target).Ultimatum != null) {
                Extortioner.To(Target).Ultimatum = null;
            }

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;
    }

    /// <summary>
    /// Automatic interaction when extortion ultimatum expires.
    /// Extortioner attacks the target.
    /// </summary>
    public record ExtortionExpiredInteraction(
        IActor Extortioner,
        IActor Target,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => $"Extortion expired - {Extortioner.Name} attacks!";

        public override int Perform(string args = "") {
            if (Extortioner is Crawler extortioner) {
                extortioner.Attack(Target);
            }

            if (Extortioner.To(Target).Ultimatum != null) {
                Extortioner.To(Target).Ultimatum = null;
            }

            return 0;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Immediate;
    }
}

/// <summary>
/// Component that allows player to threaten vulnerable NPCs for cargo
/// </summary>
public class PlayerDemandComponent : ActorComponentBase {
    XorShift _rng;
    float _demandFraction;
    string _optionCode;

    public PlayerDemandComponent(ulong seed, float demandFraction = 0.5f, string optionCode = "X") {
        _rng = new XorShift(seed);
        _demandFraction = demandFraction;
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: player only, subject is vulnerable non-player, not hostile, not surrendered
        if (Owner.Faction != Faction.Player) yield break;
        if (Owner is not Crawler { IsDisarmed: false }) yield break;
        if (subject is not Crawler { IsVulnerable: true }) yield break;
        if (subject.Faction == Faction.Player) yield break;
        if (Owner.To(subject).Hostile) yield break;
        if (subject.To(Owner).Surrendered) yield break;

        // Use shared extortion interaction
        IOffer MakeDemand() => new CompoundOffer(
            subject.SupplyOffer(_rng/1, _demandFraction),
            subject.CargoOffer(_rng/2, (_demandFraction + 1) / 2)
        );

        yield return new ExtortionInteractions.AcceptExtortionInteraction(
            Owner,
            subject,
            MakeDemand,
            $"Threaten {subject.Name} for cargo",
            _optionCode
        );
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
        encounter.ActorLeft += OnActorLeft;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, long time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} enters");
        }
    }

    void OnActorLeft(IActor actor, long time) {
        if (actor != Owner) {
            Owner.Message($"{actor.Name} leaves");
        }
    }

}

/// <summary>
/// Component that prunes relations when leaving encounters.
/// Keeps only hostile relationships and relationships with settlements.
/// </summary>
public class RelationPrunerComponent : ActorComponentBase {
    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.ActorLeaving += OnActorLeaving;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
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
    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.EncounterTick += OnEncounterTick;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
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

/// <summary>
/// Component that provides contraband scanning capability.
/// Can be added to settlements, police, or other enforcement actors.
/// </summary>
public class ContrabandScannerComponent : ActorComponentBase {
    /// <summary>
    /// Scan actor's inventory for contraband based on this faction's policies
    /// </summary>
    public bool HasContraband(IActor target) {
        if (target is not Crawler crawler) {
            return false;
        }
        return !ScanForContraband(crawler).IsEmpty;
    }

    /// <summary>
    /// Scan and return all contraband items found
    /// </summary>
    public Inventory ScanForContraband(Crawler target) {
        var contraband = new Inventory();

        // Random chance to detect (currently disabled)
        //if (Rng.NextSingle() > Tuning.Civilian.contrabandScanChance) {
        //    return contraband; // Scan failed
        //}

        var targetToFaction = target.To(Owner.Faction);
        // Check each commodity
        foreach (var commodityAmount in target.Supplies.Pairs) {
            var (commodity, amount) = commodityAmount;
            amount += target.Cargo[commodity];
            var policy = Tuning.FactionPolicies.GetPolicy(Owner.Faction, commodity);
            var licensed = targetToFaction.CanTrade(commodity);
            if (!licensed && amount > 0) {
                contraband.Add(commodity, amount);
            }
        }
        foreach (var segment in target.Cargo.Segments) {
            var policy = Tuning.FactionPolicies.GetPolicy(Owner.Faction, segment.SegmentKind);
            var licensed = targetToFaction.CanTrade(segment.SegmentDef);
            if (!licensed) {
                contraband.Add(segment);
            }
        }
        foreach (var segment in target.Supplies.Segments.Where(s => s.IsPackaged)) {
            var policy = Tuning.FactionPolicies.GetPolicy(Owner.Faction, segment.SegmentKind);
            var licensed = targetToFaction.CanTrade(segment.SegmentDef);
            if (!licensed) {
                contraband.Add(segment);
            }
        }
        return contraband;
    }
}

/// <summary>
/// High-priority survival component that makes crawlers flee when vulnerable.
/// Overrides other AI behaviors to prioritize survival.
/// </summary>
public class RetreatComponent : ActorComponentBase {
    public override int Priority => 1000; // Highest priority - survival first

    public override int? ThinkAction(IEnumerable<IActor> actors) {
        if (Owner is not Crawler crawler) return null;

        // Check if depowered first
        if (crawler.IsDepowered) {
            crawler.Message($"{crawler.Name} has no power.");
            return null; // Can't act, but don't let other components try either
        }

        // Flee if vulnerable and not pinned
        if (crawler.IsVulnerable && !crawler.Pinned()) {
            crawler.Message($"{crawler.Name} flees the encounter.");
            crawler.Location.GetEncounter().RemoveActor(crawler);
            return 1; // Consumed time to flee
        }

        return null; // Not vulnerable, let lower priority components handle it
    }
}

/// <summary>
/// Bandit AI component that handles extortion, targeting, and combat.
/// Consolidates all bandit-specific behaviors.
/// </summary>
public class BanditComponent : ActorComponentBase {
    XorShift _rng;
    float _demandFraction;

    public BanditComponent(ulong seed, float demandFraction = 0.5f) {
        _rng = new XorShift(seed);
        _demandFraction = demandFraction;
    }

    public override int Priority => 600; // Faction-specific behavior

    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, long time) {
        if (Owner is not Crawler bandit) return;
        if (actor == Owner) return;

        SetupExtortion(bandit, actor, time);
    }

    void OnActorLeft(IActor actor, long time) {
        if (Owner is not Crawler bandit) return;
        if (actor == Owner) return;

        // Clear ultimatum when target leaves
        bandit.To(actor).Ultimatum = null;
    }

    void SetupExtortion(Crawler bandit, IActor target, long time) {
        // Note: Early return because current code has this disabled
        // Remove this return to enable bandit extortion

        if (bandit.Role != CrawlerRole.Bandit || target.Faction != Faction.Player) return;

        float cargoValue = target.Supplies.ValueAt(bandit.Location);
        if (cargoValue >= Tuning.Bandit.minValueThreshold &&
            _rng.NextSingle() < Tuning.Bandit.demandChance &&
            !bandit.To(target).Hostile &&
            !bandit.To(target).Surrendered &&
            !bandit.IsDisarmed) {

            // Set ultimatum with timeout
            bandit.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = time + 300,
                Type = "BanditExtortion",
                Data = _demandFraction
            };
            bandit.To(target).DirtyInteractions = true;
        }
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        var relation = Owner.To(subject);
        if (relation.Ultimatum?.Type != "BanditExtortion") yield break;

        long expirationTime = relation.Ultimatum.ExpirationTime;
        float demandFraction = relation.Ultimatum.Data as float? ?? _demandFraction;
        bool expired = expirationTime > 0 && Game.SafeTime > expirationTime;

        if (!expired) {
            // Use shared extortion interactions
            IOffer MakeDemand() => subject.SupplyOffer(_rng/1, demandFraction);

            yield return new ExtortionInteractions.AcceptExtortionInteraction(
                Owner,
                subject,
                MakeDemand,
                "Accept extortion: hand over cargo",
                "DY"
            );
            yield return new ExtortionInteractions.RefuseExtortionInteraction(Owner, subject, "");
        } else {
            // Time expired - attack automatically
            yield return new ExtortionInteractions.ExtortionExpiredInteraction(Owner, subject, "");
        }
    }

    public override int? ThinkAction(IEnumerable<IActor> actors) {
        if (Owner is not Crawler bandit) return null;
        if (bandit.IsDisarmed) return null;

        var actorList = actors.ToList();

        // Priority 1: Attack vulnerable hostiles first (easy targets)
        var vulnerableHostile = _rng.ChooseRandom(actorList
            .OfType<Crawler>()
            .Where(a => bandit.To(a).Hostile && a.IsVulnerable && a.Lives()));

        if (vulnerableHostile != null) {
            int ap = bandit.Attack(vulnerableHostile);
            if (ap > 0) {
                bandit.ConsumeTime(ap, null);
                return ap;
            }
        }

        // Priority 2: Attack any hostile
        var hostile = _rng.ChooseRandom(actorList.Where(a => bandit.To(a).Hostile));
        if (hostile != null) {
            int ap = bandit.Attack(hostile);
            if (ap > 0) {
                bandit.ConsumeTime(ap, null);
                return ap;
            }
        }

        return null; // No action taken
    }
}

/// <summary>
/// Generic hostile AI component that attacks enemies.
/// Used as fallback behavior for NPCs that should fight but have no specialized AI.
/// </summary>
public class HostileAIComponent : ActorComponentBase {
    XorShift _rng;

    public HostileAIComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 400; // Generic combat behavior

    public override int? ThinkAction(IEnumerable<IActor> actors) {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDisarmed) return null;

        var hostile = _rng.ChooseRandom(actors.Where(a => crawler.To(a).Hostile));
        if (hostile != null) {
            int ap = crawler.Attack(hostile);
            if (ap > 0) {
                crawler.ConsumeTime(ap, null);
                return ap;
            }
        }

        return null;
    }
}
