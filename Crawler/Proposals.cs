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
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
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
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
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
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
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
    public bool SubjectCapable(IActor Subject) => Subject.Faction is not Faction.Trade;
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction is Faction.Player;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new AttackInteraction(Agent, Subject);
    }
    public record AttackInteraction(IActor _attacker, IActor Defender) : IInteraction {
        public bool Enabled() => true;
        public int Perform() {
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
    public bool InteractionCapable(IActor Winner, IActor Loser) =>
        Winner != Loser &&
        Winner.To(Loser).Hostile &&
        !Loser.To(Winner).Surrendered;
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
public record ProposeRepairBuy(string OptionCode = "R"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0;
    public bool SubjectCapable(IActor Subject) =>
        Subject is Crawler damaged &&
        damaged.Segments.Any(IsRepairable);
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction == Faction.Trade ||
        Agent.Faction == Subject.Faction;
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
