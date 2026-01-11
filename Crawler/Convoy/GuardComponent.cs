namespace Crawler.Convoy;

/// <summary>
/// AI component for guard crawlers that escort convoys.
/// Guards prioritize protecting convoy members over personal profit.
/// </summary>
public class GuardComponent : ActorComponentBase {
    readonly XorShift _rng;

    // Contract state
    Convoy? _contract;
    IActor? _employer;
    Location? _contractEndLocation;
    float _totalPayment;
    float _depositPaid;
    bool _contractComplete;

    public GuardComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    /// <summary>Priority between combat (600) and trade (500).</summary>
    public override int Priority => 550;

    /// <summary>Whether this guard is currently under contract.</summary>
    public bool IsHired => _contract != null;

    /// <summary>The convoy this guard is escorting.</summary>
    public Convoy? Contract => _contract;

    /// <summary>Who hired this guard.</summary>
    public IActor? Employer => _employer;

    /// <summary>Where the contract ends.</summary>
    public Location? ContractEndLocation => _contractEndLocation;

    /// <summary>Total payment for the contract.</summary>
    public float TotalPayment => _totalPayment;

    /// <summary>Accept a guard contract for convoy escort.</summary>
    public void AcceptContract(Convoy convoy, IActor employer, Location endLocation, float payment) {
        if (IsHired) {
            // Already under contract - reject
            return;
        }

        _contract = convoy;
        _employer = employer;
        _contractEndLocation = endLocation;
        _totalPayment = payment;
        _depositPaid = payment / 2; // Half paid upfront
        _contractComplete = false;

        // Add to convoy as guard
        convoy.AddMember(Owner, ConvoyRole.Guard);

        Owner.Message($"{Owner.Name} accepted escort contract to {endLocation.Description}.");
    }

    /// <summary>Complete the contract and receive final payment.</summary>
    public void CompleteContract() {
        if (_contract == null || _contractComplete) return;

        // Receive remaining payment
        float remainingPayment = _totalPayment - _depositPaid;
        if (_employer != null && remainingPayment > 0) {
            if (_employer.Supplies[Commodity.Scrap] >= remainingPayment) {
                _employer.Supplies[Commodity.Scrap] -= remainingPayment;
                Owner.Supplies[Commodity.Scrap] += remainingPayment;
                Owner.Message($"{Owner.Name} received {remainingPayment:F0} scrap for completing escort.");
            } else {
                Owner.Message($"{Owner.Name}'s employer couldn't pay the remaining contract fee.");
            }
        }

        _contractComplete = true;

        // Leave convoy
        _contract.RemoveMember(Owner);

        // Clear contract state
        _contract = null;
        _employer = null;
        _contractEndLocation = null;
    }

    /// <summary>Terminate contract early (guard abandons or employer dismisses).</summary>
    public void TerminateContract(bool refundDeposit = false) {
        if (_contract == null) return;

        if (refundDeposit && _employer != null && _depositPaid > 0) {
            // Return deposit to employer
            Owner.Supplies[Commodity.Scrap] -= Math.Min(Owner.Supplies[Commodity.Scrap], _depositPaid);
            _employer.Supplies[Commodity.Scrap] += _depositPaid;
        }

        _contract.RemoveMember(Owner);
        _contract = null;
        _employer = null;
        _contractEndLocation = null;
        _depositPaid = 0;
        _totalPayment = 0;
    }

    public override void Enter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
    }

    void OnActorArrived(IActor actor, TimePoint time) {
        if (actor != Owner) return;

        // Check if we've arrived at contract end
        if (IsHired && Owner.Location == _contractEndLocation) {
            CompleteContract();
        }
    }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler guard) return null;

        // If not hired, no special behavior (let other components handle)
        if (!IsHired || _contract == null) return null;

        // Check if contract should end
        if (guard.Location == _contractEndLocation) {
            CompleteContract();
            return null;
        }

        // Check if convoy leader is gone
        if (_contract.Leader == null || !_contract.AllParticipants.Contains(_contract.Leader)) {
            Owner.Message($"{Owner.Name}'s convoy disbanded - contract ended.");
            TerminateContract(refundDeposit: false);
            return null;
        }

        // Check if employer left the convoy
        if (_employer != null && !_contract.AllParticipants.Contains(_employer)) {
            Owner.Message($"{Owner.Name}'s employer left the convoy - contract ended.");
            TerminateContract(refundDeposit: false);
            return null;
        }

        // Guards stay with convoy - movement handled by ConvoyComponent
        // Combat handled by CombatComponentAdvanced
        return null;
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Guards don't offer direct interactions while under contract
        // They fight for the convoy via combat components
        yield break;
    }
}

/// <summary>
/// Component for crawlers that want to hire guards.
/// Provides interaction to check guard availability and hire.
/// </summary>
public class GuardEmployerComponent : ActorComponentBase {
    public override int Priority => 400;

    /// <summary>Get guards hired by this actor.</summary>
    public IEnumerable<IActor> HiredGuards {
        get {
            var convoy = ConvoyRegistry.GetConvoy(Owner);
            if (convoy == null) yield break;

            foreach (var member in convoy.Members) {
                if (ConvoyRegistry.GetRole(member) == ConvoyRole.Guard) {
                    yield return member;
                }
            }
        }
    }

    /// <summary>Dismiss a hired guard.</summary>
    public void DismissGuard(IActor guard) {
        var guardComponent = (guard as Crawler)?.Components.OfType<GuardComponent>().FirstOrDefault();
        if (guardComponent?.Employer == Owner) {
            guardComponent.TerminateContract(refundDeposit: false);
            Owner.Message($"Dismissed {guard.Name} from escort duty.");
        }
    }
}
