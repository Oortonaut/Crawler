namespace Crawler;

public record LootOffer(IActor Wreck, Inventory LootInv, string _description): InventoryOffer(LootInv) {
    public LootOffer(IActor Wreck, float lootReturn, string _description): this(Wreck, MakeLootInv(Wreck.Inv, lootReturn), _description) {
    }
    public override void PerformOn(IActor Agent, IActor Subject) {
        base.PerformOn(Agent, Subject);
        Agent.Flags |= EActorFlags.Looted;
    }
    public static Inventory MakeLootInv(Inventory from, float? lootReturn) {
        var loot = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            float x = Random.Shared.NextSingle();
            loot[commodity] += from[commodity] * x * (lootReturn ?? Tuning.Game.LootReturn);
        }
        var lootableSegments = from.Segments.Where(s => s.Health > 0).ToArray();
        loot.Segments.AddRange(lootableSegments
            .Where(s => Random.Shared.NextDouble() < lootReturn));
        return loot;
    }
    public override string Description => _description;
}
// I propose that I give you my loot
record ProposeLootFree(string OptionCode, string verb = "Loot"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        Agent is Crawler wreck &&
        wreck.EndState != null &&
        wreck.Hasnt(EActorFlags.Looted);
    public bool SubjectCapable(IActor Subject) => true;
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) => InteractionCapability.Possible;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, new LootOffer(Agent, Tuning.Game.LootReturn, description), Subject, new EmptyOffer(), OptionCode, description);
    }
    public string Description => $"Offer {verb}";
    public override string ToString() => Description;
}

// I propose that you harvest my resources
record ProposeHarvestFree(IActor Resource, Inventory Amount, string OptionCode, string verb): IProposal {
    public bool AgentCapable(IActor Agent) => Agent == Resource && (Agent.Flags & EActorFlags.Looted) == 0;
    public bool SubjectCapable(IActor Subject) => Subject != Resource;
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) => InteractionCapability.Possible;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, new LootOffer(Agent, Agent.Inv, description), Subject, new EmptyOffer(), OptionCode, description);
    }
    public string Description => $"Propose {verb}";
    public override string ToString() => Description;
    // public record HarvestOffer(IActor Resource, Inventory Amount): InventoryOffer(Resource, Amount) {
    //     public override void PerformOn(IActor Subject) {
    //         base.PerformOn(Subject);
    //         Agent.Flags |= EActorFlags.Looted;
    //         Agent.End(EEndState.Destroyed, "has been harvested");
    //     }
    // }
}

record ProposeLootRisk(IActor Resource, Inventory Risk, float Chance): IProposal {
    public bool AgentCapable(IActor Agent) => Agent == Resource && Agent.Hasnt(EActorFlags.Looted);
    public bool SubjectCapable(IActor Subject) => Subject != Resource;
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) => InteractionCapability.Possible;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var impact = LootOffer.MakeLootInv(Risk, Chance);
        string description = impact.ToString();
        yield return new ExchangeInteraction(
            Agent, new LootOffer(Agent, Agent.Inv, Agent.Inv.ToString()),
            Subject, new InventoryOffer(Risk),
            "E",
            Description);
    }
    public string Description => $"Explore {Resource.Name}";
    public override string ToString() => Description;
}

public record ProposeAttackDefend(string Description): IProposal {
    public bool AgentCapable(IActor Agent) => Agent is Crawler;
    public bool SubjectCapable(IActor Subject) => Subject.Faction is not Faction.Independent;
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction is Faction.Player
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new AttackInteraction(Agent, Subject);
    }
    public record AttackInteraction(IActor _attacker, IActor Defender) : IInteraction {
        public bool Enabled(string args = "") => true;
        public int Perform(string args = "") {
            var attacker = _attacker as Crawler;
            attacker!.Attack(Defender);
            return 1;
        }
        public string Description => $"Attack {Defender}";
        public string OptionCode => "A";
    }
}

// I propose that I surrender to you
public record ProposeAcceptSurrender(string OptionCode): IProposal {
    Inventory MakeSurrenderInv(IActor Loser) {
        Inventory surrenderInv = new();
        if (Loser is Crawler loser) {
            if (loser.IsDepowered) {
                // 2/3 crew will join player, -2 morale
                int crewOffer = ( int ) (loser.CrewInv * 2f / 3f);
                surrenderInv.Add(Commodity.Crew, crewOffer);
                surrenderInv.Add(Commodity.Morale, -2);
            }
            if (loser.IsImmobile) {
                // Offer 2/3 scrap
                float scrapOffer = loser.ScrapInv * 2 / 3;
                surrenderInv.Add(Commodity.Scrap, scrapOffer);
            }
            if (loser.IsDefenseless) {
                float rationsOffer = loser.RationsInv * 2 / 3;
                surrenderInv.Add(Commodity.Rations, rationsOffer);
            }
            if (loser.IsDisarmed) {
                // Offers 2/3 fuel
                float fuelOffer = loser.FuelInv * 2 / 3;
                surrenderInv.Add(Commodity.Fuel, fuelOffer);
            }
        }
        if (surrenderInv.IsEmpty) {
            // Fallback - just offer half of everything
            foreach (var commodity in Enum.GetValues<Commodity>()) {
                surrenderInv.Add(commodity, Loser.Inv[commodity] / 2);
            }

        }

        return surrenderInv;
    }

    public bool AgentCapable(IActor Winner) => true;
    public bool SubjectCapable(IActor Loser) =>
        Loser is Crawler loser && loser.IsVulnerable;
    public InteractionCapability InteractionCapable(IActor Winner, IActor Loser) =>
        Winner != Loser &&
        Winner.To(Loser).Hostile &&
        !Loser.To(Winner).Surrendered
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;
    public IEnumerable<IInteraction> GetInteractions(IActor Winner, IActor Loser) {
        var surrenderInv = MakeSurrenderInv(Loser);
        string Description = $"{Loser.Name} Surrender";
        float value = surrenderInv.ValueAt(Winner.Location);
        yield return new ExchangeInteraction(
            Winner, new AcceptSurrenderOffer(value, Description),
            Loser, new InventoryOffer(surrenderInv), OptionCode, Description);
    }
    public record AcceptSurrenderOffer(float value, string _description): IOffer {
        public string Description => _description;
        public bool EnabledFor(IActor Agent, IActor Subject) => true;
        public void PerformOn(IActor Winner, IActor Loser) {
            Winner.Message($"{Loser.Name} has surrendered to you and a mutual cease fire is in effect. {Tuning.Crawler.MoraleSurrenderedTo} Morale");
            Loser.Message($"You have surrendered to {Winner.Name} and a mutual cease fire is in effect. {Tuning.Crawler.MoraleSurrendered} Morale");
            Loser.To(Winner).Surrendered = true;
            Loser.To(Winner).Hostile = false;
            Winner.To(Loser).Hostile = false;
            Winner.Inv[Commodity.Morale] += Tuning.Crawler.MoraleSurrenderedTo;
            Loser.Inv[Commodity.Morale] += Tuning.Crawler.MoraleSurrendered;
        }
        public float ValueFor(IActor Agent) => value;
    }
    public string Description => $"SurrenderAccept";
    public override string ToString() => Description;
}

// I propose that I repair you
// Forced demand proposal: "Give me A or I'll do B to you"
// This creates an interaction that presents a choice to the subject.
// If they refuse or ignore, the consequence is automatically triggered.
public record ProposeDemand(
    IOffer Demand,
    Func<IActor, IActor, IInteraction> ConsequenceFn,
    string Ultimatum,
    Func<IActor, IActor, bool>? Condition = null): IProposal {

    public bool AgentCapable(IActor Agent) => true;
    public bool SubjectCapable(IActor Subject) => true;
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent == Subject) return InteractionCapability.Disabled;
        if (!(Condition?.Invoke(Agent, Subject) ?? true)) return InteractionCapability.Disabled;

        // Check if there's an active ultimatum
        var relation = Agent.To(Subject);
        if (relation.UltimatumTime > 0 && Game.Instance.TimeSeconds < relation.UltimatumTime) {
            return InteractionCapability.Mandatory;
        }

        return InteractionCapability.Disabled;
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var consequence = ConsequenceFn(Agent, Subject);
        yield return new AcceptDemandInteraction(Agent, Subject, Demand, Ultimatum);
        yield return new RefuseDemandInteraction(Agent, Subject, consequence, Ultimatum);
    }

    public string Description => Ultimatum;
    public override string ToString() => Description;

    public record AcceptDemandInteraction(
        IActor Agent,
        IActor Subject,
        IOffer Demand,
        string Ultimatum): IInteraction {

        public bool Enabled(string args = "") => Demand.EnabledFor(Subject, Agent);

        public int Perform(string args = "") {
            Demand.PerformOn(Subject, Agent);
            Subject.Message($"You comply with {Agent.Name}'s demand and give {Demand.Description}.");
            Agent.Message($"{Subject.Name} complies with your demand.");

            // Clear ultimatum timer
            Agent.To(Subject).UltimatumTime = 0;
            return 1;
        }

        public string Description => $"Accept: {Ultimatum}";
        public string OptionCode => "DA";
    }

    public record RefuseDemandInteraction(
        IActor Agent,
        IActor Subject,
        IInteraction Consequence,
        string Ultimatum): IInteraction {

        public bool Enabled(string args = "") => true;

        public int Perform(string args = "") {
            Subject.Message($"You refuse {Agent.Name}'s demand!");
            Agent.Message($"{Subject.Name} refuses your demand!");

            // Clear ultimatum timer
            Agent.To(Subject).UltimatumTime = 0;

            if (Consequence.Enabled()) {
                Consequence.Perform();
            }
            return 1;
        }

        public string Description => $"Refuse: {Ultimatum}";
        public string OptionCode => "DR";
    }
}

// Bandit extortion: "Hand over cargo or I attack"
public record ProposeExtortion(float DemandFraction = 0.5f): IProposal {
    public bool AgentCapable(IActor Agent) =>
        Agent is Crawler bandit && bandit.Faction == Faction.Bandit && !bandit.IsDisarmed;

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player;

    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent.To(Subject).Hostile) return InteractionCapability.Disabled; // Already hostile
        if (Agent.To(Subject).Surrendered) return InteractionCapability.Disabled; // Surrendered
        if (Subject.Inv.ValueAt(Subject.Location) <= 0) return InteractionCapability.Disabled; // No valuable cargo

        // Check if there's an active ultimatum
        var relation = Agent.To(Subject);
        if (relation.UltimatumTime > 0 && Game.Instance.TimeSeconds < relation.UltimatumTime) {
            return InteractionCapability.Mandatory;
        }

        return InteractionCapability.Disabled; // Not active unless ultimatum is set
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        // Calculate demand: fraction of subject's valuable commodities
        var demand = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity == Commodity.Crew || commodity == Commodity.Morale) continue; // Don't demand crew/morale
            float amount = Subject.Inv[commodity] * DemandFraction;
            if (amount > 0) {
                demand.Add(commodity, amount);
            }
        }

        if (demand.IsEmpty) yield break;

        float value = demand.ValueAt(Subject.Location);
        string ultimatum = $"{Agent.Name} demands {value:F0}¢¢ worth of cargo or they will attack";

        // Create ProposeDemand interactions
        var demandProposal = new ProposeDemand(
            new InventoryOffer(demand),
            (agent, subject) => new ProposeAttackDefend.AttackInteraction(agent, subject),
            ultimatum,
            (agent, subject) => true
        );

        foreach (var interaction in demandProposal.GetInteractions(Agent, Subject)) {
            yield return interaction;
        }
    }

    public string Description => "Extort cargo";
    public override string ToString() => Description;
}

// Civilian faction taxes: "Pay taxes or face hostility"
public record ProposeTaxes(float TaxRate = 0.05f): IProposal {
    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0 && Agent.Faction.IsCivilian();

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player;

    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) {
        // Only tax if player is in the faction's territory
        if (Agent.Location.Sector.ControllingFaction != Agent.Faction) return InteractionCapability.Disabled;

        // Only tax if not already paid or hostile
        if (Agent.To(Subject).Hostile) return InteractionCapability.Disabled;

        // Only tax if player has cargo/scrap
        if (!(Subject.Inv.ValueAt(Subject.Location) > 0 || Subject.Inv[Commodity.Scrap] > 0)) {
            return InteractionCapability.Disabled;
        }

        // Check if there's an active ultimatum
        var relation = Agent.To(Subject);
        if (relation.UltimatumTime > 0 && Game.Instance.TimeSeconds < relation.UltimatumTime) {
            return InteractionCapability.Mandatory;
        }

        return InteractionCapability.Disabled; // Not active unless ultimatum is set
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        float cargoValue = Subject.Inv.ValueAt(Subject.Location);
        float taxAmount = cargoValue * TaxRate;

        if (taxAmount < 1f) yield break; // Don't bother for tiny amounts

        string ultimatum = $"{Agent.Name} demands {taxAmount:F0}¢¢ in taxes for entering their territory";

        var demandProposal = new ProposeDemand(
            new ScrapOffer(taxAmount),
            (agent, subject) => new HostilityInteraction(agent, subject, "refuses to pay taxes"),
            ultimatum,
            (agent, subject) => true
        );

        foreach (var interaction in demandProposal.GetInteractions(Agent, Subject)) {
            yield return interaction;
        }
    }

    public string Description => "Demand taxes";
    public override string ToString() => Description;

    // Simple consequence: mark as hostile
    public record HostilityInteraction(IActor Agent, IActor Subject, string Reason): IInteraction {
        public bool Enabled(string args = "") => true;
        public int Perform(string args = "") {
            Agent.To(Subject).Hostile = true;
            Subject.To(Agent).Hostile = true;
            Agent.Message($"{Subject.Name} {Reason}. You are now hostile.");
            Subject.Message($"{Agent.Name} turns hostile because you {Reason}!");
            Subject.Inv[Commodity.Morale] -= 2;
            return 1;
        }
        public string Description => $"Turn hostile against {Subject.Name}";
        public string OptionCode => "H";
    }
}

// Contraband seizure: "Surrender prohibited goods or pay fine"
public record ProposeContrabandSeizure(Inventory Contraband, float PenaltyAmount): IProposal {
    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0 ||
        (Agent.Faction == Faction.Independent || Agent.Faction.IsCivilian());

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player && Subject.Inv.Contains(Contraband);

    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent.To(Subject).Hostile) return InteractionCapability.Disabled;

        // Check if there's an active ultimatum
        var relation = Agent.To(Subject);
        if (relation.UltimatumTime > 0 && Game.Instance.TimeSeconds < relation.UltimatumTime) {
            return InteractionCapability.Mandatory;
        }

        return InteractionCapability.Disabled; // Not active unless ultimatum is set
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        float contrabandValue = Contraband.ValueAt(Subject.Location);
        string contrabandList = Contraband.ToString();

        string ultimatum = $"{Agent.Name} detected prohibited goods: {contrabandList}. " +
                          $"Surrender them or pay {PenaltyAmount:F0}¢¢ fine";

        // Create a choice: surrender contraband OR pay fine
        yield return new ContrabandInteraction(Agent, Subject, Contraband, PenaltyAmount, ultimatum);
    }

    public string Description => "Seize contraband";
    public override string ToString() => Description;

    public record ContrabandInteraction(
        IActor Agent,
        IActor Subject,
        Inventory Contraband,
        float PenaltyAmount,
        string Ultimatum): IInteraction {

        public bool Enabled(string args = "") => true;

        public int Perform(string args = "") {
            // "pay" means pay the fine, otherwise surrender contraband
            bool payFine = args.Equals("pay", StringComparison.OrdinalIgnoreCase);

            if (payFine && Subject.Inv[Commodity.Scrap] >= PenaltyAmount) {
                // Pay the fine
                Subject.Inv[Commodity.Scrap] -= PenaltyAmount;
                Agent.Inv[Commodity.Scrap] += PenaltyAmount;
                Subject.Message($"You pay {PenaltyAmount:F0}¢¢ fine to {Agent.Name} and keep your contraband.");
                Agent.Message($"{Subject.Name} pays the fine.");
                return 1;
            } else if (!payFine && Subject.Inv.Contains(Contraband)) {
                // Surrender contraband
                Subject.Inv.Remove(Contraband);
                Agent.Inv.Add(Contraband);
                Subject.Message($"You surrender {Contraband} to {Agent.Name}.");
                Agent.Message($"{Subject.Name} surrenders contraband.");
                return 1;
            } else {
                // Can't comply - turn hostile
                Subject.Message($"You can't comply with {Agent.Name}'s demands! They turn hostile.");
                Agent.Message($"{Subject.Name} can't comply. Turning hostile.");
                Agent.To(Subject).Hostile = true;
                Subject.To(Agent).Hostile = true;
                Subject.Inv[Commodity.Morale] -= 3;
                return 1;
            }
        }

        public string Description => $"{Ultimatum}";
        public string OptionCode => "C";
    }
}

// Player demands: Let player threaten vulnerable NPCs
public record ProposePlayerDemand(float DemandFraction = 0.5f, string OptionCode = "X"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        Agent.Faction == Faction.Player && Agent is Crawler player && !player.IsDisarmed;

    public bool SubjectCapable(IActor Subject) =>
        Subject is Crawler target && target.IsVulnerable && Subject.Faction != Faction.Player;

    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) =>
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Surrendered &&
        Subject.Inv.ValueAt(Subject.Location) > 0
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        // Calculate demand: fraction of subject's valuable commodities
        var demand = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity == Commodity.Crew || commodity == Commodity.Morale) continue;
            float amount = Subject.Inv[commodity] * DemandFraction;
            if (amount > 0) {
                demand.Add(commodity, amount);
            }
        }

        if (demand.IsEmpty) yield break;

        float value = demand.ValueAt(Subject.Location);
        string ultimatum = $"Demand {value:F0}¢¢ worth of cargo from {Subject.Name} or attack";

        var demandProposal = new ProposeDemand(
            new InventoryOffer(demand),
            (agent, subject) => new ProposeAttackDefend.AttackInteraction(agent, subject),
            ultimatum,
            (agent, subject) => true
        );

        foreach (var interaction in demandProposal.GetInteractions(Agent, Subject)) {
            yield return interaction;
        }
    }

    public string Description => "Threaten for cargo";
    public override string ToString() => Description;
}

public record ProposeRepairBuy(string OptionCode = "R"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0;
    public bool SubjectCapable(IActor Subject) =>
        Subject is Crawler damaged &&
        damaged.Segments.Any(IsRepairable);
    public InteractionCapability InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction == Faction.Independent ||
        Agent.Faction == Subject.Faction
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;
    public string Description => $"Repair subject segments";
    static bool IsRepairable(Segment segment) => segment.Hits > 0;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        if (Subject is Crawler damaged) {
            foreach (var segment in damaged.Segments.Where(IsRepairable))
            {
                float value = segment.Cost / 8;
                float price = value * Markup;
                var repairOffer = new RepairOffer(Agent, segment, value);
                yield return new ExchangeInteraction(Agent, repairOffer, Subject, new ScrapOffer(price), OptionCode);
            }
        }
    }

    public record RepairOffer(
        IActor _agent,
        Segment SubjectSegment,
        float price): IOffer {
        public virtual string Description => $"Repair {SubjectSegment.StatusLine(_agent.Location)} ({SubjectSegment.Name})";
        public virtual bool EnabledFor(IActor Agent, IActor Subject) =>
            Subject.Inv.Segments.Contains(SubjectSegment) && SubjectSegment.Hits > 0;
        public virtual void PerformOn(IActor Agent, IActor Subject) =>
            --SubjectSegment.Hits;
        public float ValueFor(IActor Agent) => price;
    }
    public float Markup = CrawlerEx.NextGaussian(Tuning.Trade.repairMarkup, Tuning.Trade.repairMarkupSd);
    public override string ToString() => Description;
}
