namespace Crawler;

/// <summary>
/// Component that handles bandit extortion logic.
/// Bandits threaten valuable targets with attack unless they hand over cargo.
/// </summary>
public class BanditExtortionComponent : ActorComponentBase {
    XorShift _rng;
    float _demandFraction;

    public BanditExtortionComponent(ulong seed, float demandFraction = 0.5f) {
        _rng = new XorShift(seed);
        _demandFraction = demandFraction;
    }

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

        if (bandit.Faction != Faction.Bandit || target.Faction != Faction.Player) return;

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
            // Offer accept option
            yield return new BanditExtortionAcceptInteraction(Owner, subject, _rng, demandFraction, "DY");
            // Offer refuse option (auto-hostile)
            yield return new BanditExtortionRefuseInteraction(Owner, subject, "");
        } else {
            // Time expired - attack automatically
            yield return new BanditExtortionExpiredInteraction(Owner, subject, "");
        }
    }

    public record BanditExtortionAcceptInteraction(
        IActor Bandit,
        IActor Target,
        XorShift Rng,
        float DemandFraction,
        string MenuOption
    ) : Interaction(Bandit, Target, MenuOption) {

        public override string Description => $"Accept extortion: hand over cargo";

        IOffer MakeDemand() => Target.SupplyOffer(Rng/1, DemandFraction);

        public override int Perform(string args = "") {
            var demand = MakeDemand();

            Bandit.Message($"{Target.Name} hands over {demand.Description}");
            Target.Message($"You hand over {demand.Description} to {Bandit.Name}");

            demand.PerformOn(Target, Bandit);

            // Clear ultimatum
            Bandit.To(Target).Ultimatum = null;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var demand = MakeDemand();
            if (demand.DisabledFor(Target, Bandit) != null) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }

    public record BanditExtortionRefuseInteraction(
        IActor Bandit,
        IActor Target,
        string MenuOption
    ) : Interaction(Bandit, Target, MenuOption) {

        public override string Description => $"Refuse extortion from {Bandit.Name}";

        public override int Perform(string args = "") {
            Bandit.To(Target).Hostile = true;
            Target.To(Bandit).Hostile = true;

            Bandit.Message($"{Target.Name} refuses your demand!");
            Target.Message($"You refuse {Bandit.Name}'s demand - now hostile!");

            // Clear ultimatum
            Bandit.To(Target).Ultimatum = null;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;
    }

    public record BanditExtortionExpiredInteraction(
        IActor Bandit,
        IActor Target,
        string MenuOption
    ) : Interaction(Bandit, Target, MenuOption) {

        public override string Description => $"Extortion expired - {Bandit.Name} attacks!";

        public override int Perform(string args = "") {
            if (Bandit is Crawler bandit) {
                bandit.Attack(Target);
            }

            Bandit.To(Target).Ultimatum = null;

            return 0;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Immediate;
    }
}

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
            float value = SegmentToRepair.Cost / 8;
            var repairOffer = new RepairOffer(Agent, SegmentToRepair, value);
            var scrapOffer = new ScrapOffer(Price);

            Agent.Message($"You repaired {Subject.Name}'s {SegmentToRepair.Name} for {Price}¢¢");
            Subject.Message($"{Agent.Name} repaired your {SegmentToRepair.Name} for {Price}¢¢");

            repairOffer.PerformOn(Agent, Subject);
            scrapOffer.PerformOn(Subject, Agent);

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
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

        yield return new PlayerDemandInteraction(Owner, subject, _rng, _demandFraction, _optionCode);
    }

    /// <summary>
    /// Player threatens vulnerable NPC for cargo
    /// </summary>
    public record PlayerDemandInteraction(
        IActor Agent,
        IActor Subject,
        XorShift Rng,
        float DemandFraction,
        string MenuOption
    ) : Interaction(Agent, Subject, MenuOption) {

        public override string Description => $"Threaten {Subject.Name} for cargo";

        IOffer MakeDemand() => new CompoundOffer(
            Subject.SupplyOffer(Rng/1, DemandFraction),
            Subject.CargoOffer(Rng/2, (DemandFraction + 1) / 2)
        );

        public override int Perform(string args = "") {
            var demand = MakeDemand();

            Agent.Message($"You threaten {Subject.Name} and demand {demand.Description}");
            Subject.Message($"{Agent.Name} threatens you and demands {demand.Description}");

            // They give up the cargo
            demand.PerformOn(Subject, Agent);

            // Mark as surrendered
            Subject.To(Agent).Surrendered = true;

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Subject is not Crawler { IsVulnerable: true }) return Immediacy.Failed;
            if (Subject.To(Agent).Surrendered) return Immediacy.Failed;
            if (Agent is Crawler { IsDisarmed: true }) return Immediacy.Failed;
            return Immediacy.Menu;
        }
    }
}

/// <summary>
/// Component for looting/harvesting from dead actors or resource nodes
/// </summary>
public class LootComponent : ActorComponentBase {
    XorShift _rng;
    string _optionCode;
    string _verb;
    float _lootFraction;

    public LootComponent(ulong seed, string verb = "Loot", string optionCode = "L", float lootFraction = 1.0f) {
        _rng = new XorShift(seed);
        _verb = verb;
        _optionCode = optionCode;
        _lootFraction = lootFraction;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Can loot if owner is ended but not yet looted
        if (!Owner.Ended()) yield break;
        if (Owner.EndState == EEndState.Looted) yield break;

        yield return new LootInteraction(Owner, subject, _rng, _verb, _optionCode, _lootFraction);
    }

    public record LootInteraction(
        IActor Source,
        IActor Taker,
        XorShift Rng,
        string Verb,
        string MenuOption,
        float LootFraction
    ) : Interaction(Source, Taker, MenuOption) {

        public override string Description => $"{Verb} {Source.Name}";

        public override int Perform(string args = "") {
            var lootOffer = Source.SupplyOffer(Rng / 1, LootFraction);

            Source.Message($"{Taker.Name} {Verb.ToLower()}s you");
            Taker.Message($"You {Verb.ToLower()} {Source.Name} and take {lootOffer.Description}");

            lootOffer.PerformOn(Source, Taker);
            Source.End(EEndState.Looted, $"looted by {Taker.Name}");

            return 1;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (!Source.Ended()) return Immediacy.Failed;
            if (Source.EndState == EEndState.Looted) return Immediacy.Failed;
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
