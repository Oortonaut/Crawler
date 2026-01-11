using Crawler.Production;

namespace Crawler;

/// <summary>
/// Component that provides segment upgrade services.
/// Upgrades apply modifications to existing segments, creating new upgraded versions.
/// </summary>
public class UpgradeComponent : ActorComponentBase {
    readonly string _optionCode;
    readonly float _markup;

    public UpgradeComponent(string optionCode = "U", float markup = 1.2f) {
        _optionCode = optionCode;
        _markup = markup;
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (subject is not Crawler buyer) yield break;
        if (Owner.To(subject).Hostile || subject.To(Owner).Hostile) yield break;

        // Check for active upgrade relationship
        var ownerToSubject = Owner.To(subject).Ultimatum;
        var subjectToOwner = subject.To(Owner).Ultimatum;
        bool hasActiveUpgrade = (ownerToSubject?.Type == "UpgradeMechanic") ||
                                (subjectToOwner?.Type == "UpgradeCustomer");

        if (hasActiveUpgrade) yield break;

        // For each working segment the buyer has, enumerate available upgrades
        foreach (var segment in buyer.Segments.Where(s => s.IsUsable)) {
            foreach (var recipe in SegmentRecipeEx.GetUpgradesFor(segment)) {
                float price = recipe.TotalCost(segment) * _markup;
                yield return new UpgradeInteraction(
                    Owner, subject, segment, recipe, price, _optionCode);
            }
        }
    }

    /// <summary>
    /// Interaction for upgrading an existing segment.
    /// Creates a new segment with upgraded def, copies instance state.
    /// </summary>
    public record UpgradeInteraction(
        IActor Mechanic,
        IActor Subject,
        Segment TargetSegment,
        UpgradeRecipe Recipe,
        float Price,
        string MenuOption
    ) : Interaction(Mechanic, Subject, MenuOption) {

        public override string Description =>
            $"Upgrade {TargetSegment.NameSize}: {Recipe.Name} for {Price:F0}¢¢";

        public override bool Perform(string args = "") {
            SynchronizeActors();

            var duration = Recipe.CycleTime;
            var endTime = Mechanic.Time + duration;

            // Create bidirectional Ultimatums to track the upgrade relationship
            Subject.To(Mechanic).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "UpgradeCustomer",
                Data = (TargetSegment, Recipe)
            };

            Mechanic.To(Subject).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "UpgradeMechanic",
                Data = (TargetSegment, Recipe)
            };

            var crawler = (Crawler)Subject;

            // Lock both actors for the duration
            Subject.ConsumeTime("Upgrading", 300, ExpectedDuration, post: () => {
                // Create upgraded segment and replace the old one
                var upgradedDef = Recipe.ApplyUpgrade(TargetSegment.SegmentDef);
                var upgradedSegment = upgradedDef.NewSegment(TargetSegment.Seed);

                // Copy instance state from old to new
                CopySegmentState(TargetSegment, upgradedSegment);

                // Replace in crawler's working segments
                crawler.ReplaceSegment(TargetSegment, upgradedSegment);

                Subject.Message($"Upgrade of {TargetSegment.Name} to {upgradedSegment.Name} completed");

                // Clear the Ultimatums
                Subject.To(Mechanic).Ultimatum = null;
                Mechanic.To(Subject).Ultimatum = null;
            });

            Mechanic.ConsumeTime("Upgrading", 300, ExpectedDuration, post: () => {
                Mechanic.Message($"Finished upgrading {Subject.Name}'s segment");
            });

            // Pay upfront
            var scrapOffer = new ScrapOffer(Price);
            scrapOffer.PerformOn(Subject, Mechanic);

            Mechanic.Message($"Starting upgrade of {Subject.Name}'s {TargetSegment.Name}...");
            Subject.Message($"{Mechanic.Name} begins upgrading your {TargetSegment.Name}");

            return true;
        }

        /// <summary>
        /// Copy instance state from old segment to new upgraded segment.
        /// </summary>
        static void CopySegmentState(Segment source, Segment target) {
            target.Hits = source.Hits;
            target.Cycle = source.Cycle;
            target.Packaged = source.Packaged;
            target.Activated = source.Activated;

            // Copy type-specific state
            switch ((source, target)) {
                case (ReactorSegment srcReactor, ReactorSegment tgtReactor):
                    tgtReactor.Charge = srcReactor.Charge;
                    break;
                case (ShieldSegment srcShield, ShieldSegment tgtShield):
                    tgtShield.ShieldLeft = srcShield.ShieldLeft;
                    break;
                case (StorageSegment srcStorage, StorageSegment tgtStorage):
                    tgtStorage.UsedCapacity = srcStorage.UsedCapacity;
                    break;
                case (IndustrySegment srcIndustry, IndustrySegment tgtIndustry):
                    tgtIndustry.CurrentRecipe = srcIndustry.CurrentRecipe;
                    tgtIndustry.ProductionProgress = srcIndustry.ProductionProgress;
                    tgtIndustry.IsStalled = srcIndustry.IsStalled;
                    break;
            }
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Check for active upgrade relationship
            var subjectToAgent = Subject.To(Mechanic).Ultimatum;
            var agentToSubject = Mechanic.To(Subject).Ultimatum;

            if (subjectToAgent?.Type == "UpgradeCustomer") return Immediacy.Failed;
            if (agentToSubject?.Type == "UpgradeMechanic") return Immediacy.Failed;
            if (!Recipe.CanApplyTo(TargetSegment)) return Immediacy.Failed;
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;

            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Recipe.CycleTime;
    }
}
