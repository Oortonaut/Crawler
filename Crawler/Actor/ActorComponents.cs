namespace Crawler;

/// <summary>
/// Component that handles bandit extortion logic.
/// Bandits threaten valuable targets with attack unless they hand over cargo.
/// </summary>
public class BanditExtortionComponent : ActorComponentBase {
    XorShift _rng;

    public BanditExtortionComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override void SubscribeToEncounter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void UnsubscribeFromEncounter(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(object? sender, ActorArrivedEventArgs e) {
        if (Owner is not Crawler bandit) return;

        // When a new actor arrives, check if we should extort them
        if (e.Actor != Owner) {
            SetupExtortion(bandit, e.Actor);
        }
    }

    void OnActorLeft(object? sender, ActorLeftEventArgs e) {
        if (Owner is not Crawler bandit) return;

        // When an actor leaves, clean up any ultimatums
        if (e.Actor != Owner) {
            ExpireProposals(bandit, e.Actor);
        }
    }

    void SetupExtortion(Crawler bandit, IActor target) {
        // Note: Early return because current code has this disabled
        // Remove this return to enable bandit extortion
        return;

        if (bandit.Faction != Faction.Bandit || target.Faction != Faction.Player) return;

        float cargoValue = target.Supplies.ValueAt(bandit.Location);
        if (cargoValue >= Tuning.Bandit.minValueThreshold &&
            _rng.NextSingle() < Tuning.Bandit.demandChance &&
            !bandit.To(target).Hostile &&
            !bandit.To(target).Surrendered &&
            !bandit.IsDisarmed) {

            var extortion = new ProposeAttackOrLoot(_rng / 2, Tuning.Bandit.demandFraction);
            bandit.To(target).AddProposal(extortion);
        }
    }

    void ExpireProposals(Crawler bandit, IActor other) {
        bandit.To(other).StoredProposals.Clear();
    }

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        // Bandit extortion uses ultimatums stored in ActorToActor, not generated proposals
        yield break;
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

    void OnActorArrived(object? sender, ActorArrivedEventArgs e) {
        if (Owner is not Crawler settlement) return;
        if (!settlement.Flags.HasFlag(EActorFlags.Settlement)) return;

        // When a new actor arrives, scan for contraband
        if (e.Actor != Owner) {
            SetupContrabandScan(settlement, e.Actor);
        }
    }

    void OnActorLeft(object? sender, ActorLeftEventArgs e) {
        if (Owner is not Crawler settlement) return;
        if (!settlement.Flags.HasFlag(EActorFlags.Settlement)) return;

        // When an actor leaves, clean up any ultimatums
        if (e.Actor != Owner) {
            ExpireProposals(settlement, e.Actor);
        }
    }

    void SetupContrabandScan(Crawler settlement, IActor target) {
        var seizure = new ProposeSearchSeizeHostile(settlement, target);
        settlement.To(target).AddProposal(seizure);

        // Taxes for settlements in own territory (currently disabled)
        // if (settlement.Faction.IsCivilian() &&
        //     settlement.Location.Sector.ControllingFaction == settlement.Faction) {
        //     var taxes = new ProposeTaxes(Tuning.Civilian.taxRate);
        //     settlement.To(target).AddProposal(taxes);
        // }
    }

    void ExpireProposals(Crawler settlement, IActor other) {
        settlement.To(other).StoredProposals.Clear();
    }

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        // Contraband scanning uses ultimatums stored in ActorToActor, not generated proposals
        yield break;
    }
}

/// <summary>
/// Component that generates trade proposals on-demand.
/// This replaces the StoredProposals caching for trade offers.
/// </summary>
public class TradeOfferComponent : ActorComponentBase {
    float _wealthFraction;
    ulong _seed;

    public TradeOfferComponent(ulong seed, float wealthFraction = 0.25f) {
        _seed = seed;
        _wealthFraction = wealthFraction;
    }

    // Trade offers don't need to subscribe to encounter events

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        // Generate trade proposals fresh each time
        return owner.MakeTradeProposals(_seed, _wealthFraction);
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

    void OnActorArrived(object? sender, ActorArrivedEventArgs e) {
        if (e.Actor != Owner) {
            Owner.Message($"{e.Actor.Name} enters");
        }
    }

    void OnActorLeft(object? sender, ActorLeftEventArgs e) {
        if (e.Actor != Owner) {
            Owner.Message($"{e.Actor.Name} leaves");
        }
    }

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        yield break;
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

    void OnActorLeaving(object? sender, ActorLeavingEventArgs e) {
        if (e.Actor != Owner) return;
        if (Owner is not Crawler crawler) return;

        // Prune relations when this actor is leaving
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

    public override IEnumerable<IProposal> GenerateProposals(IActor owner) {
        yield break;
    }
}
