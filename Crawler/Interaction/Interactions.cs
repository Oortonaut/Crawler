using Crawler.Logging;

namespace Crawler;

public enum Immediacy {
    Failed,
    Menu,
    Immediate, // Perform now
}

/// <summary>
/// Concrete action that can be performed.
/// </summary>
public abstract record Interaction(IActor Mechanic, IActor Subject, string MenuOption) {
    /// <summary>Can this interaction be performed right now?</summary>
    public abstract Immediacy GetImmediacy(string args = "");

    /// <summary>Execute the interaction. Returns true if successful.</summary>
    public abstract bool Perform(string args = "");

    /// <summary>Display message for viewer. Primarily used for customs and extortion demands.</summary>
    public virtual string? MessageFor(IActor viewer) => null;

    /// <summary>Display description for menus</summary>
    public abstract string Description { get; }

    /// <summary>Shortcut key code (e.g., "T" for trade, "DA" for demand accept)</summary>
    public string OptionCode => MenuOption;

    /// <summary>Expected duration in seconds for this interaction. Used for UI display and planning.</summary>
    public virtual long ExpectedDuration => 0;

    /// <summary>Synchronize both actors to a common time before multi-actor interaction.</summary>
    protected void SynchronizeActors() {
        long commonTime = Math.Max(Mechanic.Time, Subject.Time);
        if (Mechanic.Time < commonTime) Mechanic.SimulateTo(commonTime);
        if (Subject.Time < commonTime) Subject.SimulateTo(commonTime);
        System.Diagnostics.Debug.Assert(Mechanic.Time == Subject.Time);
    }
}

// Simple consequence: mark as hostile
public record HostilityInteraction(IActor Attacker, IActor Subject, string Reason): Interaction(Attacker, Subject, "H") {
    public override Immediacy GetImmediacy(string args = "") => global::Crawler.Immediacy.Menu;
    public override bool Perform(string args = "") {
        SynchronizeActors();

        Mechanic.SetHostileTo(Subject, true);
        Subject.SetHostileTo(Mechanic, true);
        Mechanic.Message($"{Subject.Name} {Reason}. You are now hostile.");
        Subject.Message($"{Mechanic.Name} turns hostile because you {Reason.Replace("refuses", "refused")}!");
        Subject.Supplies[Commodity.Morale] -= 2;

        // Time consumption for hostility declaration
        Mechanic.ConsumeTime("DeclareHostile", 300, ExpectedDuration);
        Subject.ConsumeTime("BecomeHostile", 300, ExpectedDuration);

        return true;
    }
    public override string Description => $"Turn hostile against {Subject.Name}";
    public override long ExpectedDuration => Tuning.Crawler.HostilityTime;
}

public record ExchangeInteraction: Interaction {
    public ExchangeInteraction(IActor attacker,
        IOffer agentOffer,
        IActor subject,
        IOffer subjectOffer,
        string optionCode,
        string? description = null,
        Immediacy mode = global::Crawler.Immediacy.Menu) : base(attacker, subject, optionCode) {
        AgentOffer = agentOffer;
        SubjectOffer = subjectOffer;
        Description = description ?? MakeDescription();
        _mode = mode;
    }
    readonly Immediacy _mode;

    public override Immediacy GetImmediacy(string args = "") {
        string? aoe = AgentOffer.DisabledFor(Mechanic, Subject);
        string? soe = SubjectOffer.DisabledFor(Subject, Mechanic);

        using var activity = LogCat.Interaction.StartActivity(nameof(GetImmediacy));
        activity?.SetTag("interaction.description", Description);
        activity?.SetTag("agent.name", Mechanic.Name);
        activity?.SetTag("subject.name", Subject.Name);
        activity?.SetTag("agent.offer.enabled", aoe == null);
        activity?.SetTag("subject.offer.enabled", soe == null);

        if (aoe == null && soe == null) {
            activity?.SetTag("mode", _mode.ToString());
            return _mode;
        } else {
            var failures = new List<string>();
            if (aoe != null) failures.Add($"Agent: {aoe}");
            if (soe != null) failures.Add($"Subject: {soe}");
            activity?.SetTag("mode", "Disabled");
            activity?.SetTag("failures", string.Join(", ", failures));
            return global::Crawler.Immediacy.Failed;
        }
    }

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
            AgentOffer.PerformOn(Mechanic, Subject);
            SubjectOffer.PerformOn(Subject, Mechanic);
            performed++;
        }

        // Time consumption for trade
        long tradeDuration = ExpectedDuration * count;
        Mechanic.ConsumeTime("Trading", 300, tradeDuration);
        Subject.ConsumeTime("Trading", 300, tradeDuration);

        return true;
    }
    public override string Description { get; }
    public override string ToString() => Description;
    public IOffer AgentOffer { get; init; }
    public IOffer SubjectOffer { get; init; }
    public override long ExpectedDuration => Tuning.Crawler.TradeTime;
    public string MakeDescription() {
        // Note: Can't access Value here without Agent binding
        var buyerDesc = AgentOffer.Description;
        var sellerDesc = SubjectOffer.Description;
        if (AgentOffer is EmptyOffer) {
            return $"{sellerDesc}";
        } else if (SubjectOffer is EmptyOffer) {
            return $"{buyerDesc}";
        } else if (AgentOffer is ScrapOffer) {
            return $"Sell {sellerDesc} for {buyerDesc}";
        } else if (SubjectOffer is ScrapOffer) {
            return $"Buy {buyerDesc} for {sellerDesc}";
        }
        return $"Give {sellerDesc} for {buyerDesc}";
    }

    public string? FailureReason() {
        string? aoe = AgentOffer.DisabledFor(Mechanic, Subject);
        string? soe = SubjectOffer.DisabledFor(Subject, Mechanic);
        string result = aoe == null ? "" :  $"{Mechanic.Name} {aoe}" ;
        if (soe != null) {
            if (aoe != null) {
                result += ", ";
            }
            result += $"{Subject.Name} {soe}";
        }
        return result;
    }
}
