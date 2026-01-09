using Crawler.Production;

namespace Crawler;

/// <summary>
/// Component that provides segment manufacturing services.
/// Available at settlements and on crawlers with Fabricator/Assembler industry segments.
/// </summary>
public class ManufacturingComponent : ActorComponentBase {
    readonly XorShift _rng;
    readonly string _optionCode;
    readonly float _markup;

    public ManufacturingComponent(ulong seed, string optionCode = "M", float markup = 1.3f) {
        _rng = new XorShift(seed);
        _optionCode = optionCode;
        _markup = markup;
    }

    /// <summary>
    /// Maximum segment size this provider can manufacture.
    /// Based on largest industry segment size - 2.
    /// </summary>
    public int MaxManufactureSize {
        get {
            if (Owner is Crawler crawler) {
                var fabricators = crawler.Segments
                    .OfType<IndustrySegment>()
                    .Where(i => i.IndustryType is IndustryType.Fabricator or IndustryType.Assembler);
                if (!fabricators.Any()) return 0;
                return (int)fabricators.Max(f => f.SegmentDef.Size.Size) - 2;
            }
            // Settlements have fixed capacity
            if (Owner.Flags.HasFlag(ActorFlags.Settlement)) {
                return Tuning.Manufacturing.SettlementIndustrySize - 2;
            }
            return 0;
        }
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (subject is not Crawler buyer) yield break;
        if (Owner.To(subject).Hostile || subject.To(Owner).Hostile) yield break;

        int maxSize = MaxManufactureSize;
        if (maxSize <= 0) yield break;

        // Check for active manufacturing relationship
        var ownerToSubject = Owner.To(subject).Ultimatum;
        var subjectToOwner = subject.To(Owner).Ultimatum;
        bool hasActiveManufacturing = (ownerToSubject?.Type == "ManufactureMechanic") ||
                                      (subjectToOwner?.Type == "ManufactureCustomer");

        if (hasActiveManufacturing) yield break;

        // Generate manufacturing interactions for available recipes
        foreach (var baseDef in SegmentEx.AllDefs) {
            for (int size = 1; size <= maxSize; size++) {
                var resized = baseDef.Resize(size);
                var recipe = SegmentRecipeEx.CreateRecipe(resized);
                float price = recipe.OutputDef.Cost * _markup;

                yield return new ManufactureInteraction(
                    Owner, subject, recipe, price, _rng.Seed(), _optionCode);
            }
        }
    }

    /// <summary>
    /// Interaction for manufacturing a new segment.
    /// </summary>
    public record ManufactureInteraction(
        IActor Mechanic,
        IActor Subject,
        SegmentRecipe Recipe,
        float Price,
        ulong Seed,
        string MenuOption
    ) : Interaction(Mechanic, Subject, MenuOption) {

        public override string Description =>
            $"Manufacture {Recipe.OutputDef.NameSize} for {Price:F0}¢¢";

        public override bool Perform(string args = "") {
            SynchronizeActors();

            var duration = Recipe.CycleTime;
            var endTime = Mechanic.Time + duration;

            // Create the segment upfront (will be delivered on completion)
            var crawler = (Crawler)Subject;
            var segment = Recipe.ProduceSegment(Seed);
            segment.Packaged = true;

            // Create bidirectional Ultimatums to track the manufacturing relationship
            Subject.To(Mechanic).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "ManufactureCustomer",
                Data = segment
            };

            Mechanic.To(Subject).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = endTime,
                Type = "ManufactureMechanic",
                Data = segment
            };

            // Lock both actors for the duration
            Subject.ConsumeTime("Manufacturing", 300, ExpectedDuration, post: () => {
                // Deliver the manufactured segment to cargo
                crawler.Cargo.Add(segment);
                Subject.Message($"Manufacturing of {segment.NameSize} completed");
                // Clear the Ultimatums
                Subject.To(Mechanic).Ultimatum = null;
                Mechanic.To(Subject).Ultimatum = null;
            });

            Mechanic.ConsumeTime("Manufacturing", 300, ExpectedDuration, post: () => {
                Mechanic.Message($"Finished manufacturing {Recipe.OutputDef.NameSize} for {Subject.Name}");
            });

            // Pay upfront
            var scrapOffer = new ScrapOffer(Price);
            scrapOffer.PerformOn(Subject, Mechanic);

            Mechanic.Message($"Starting manufacture of {Recipe.OutputDef.NameSize} for {Subject.Name}...");
            Subject.Message($"{Mechanic.Name} begins manufacturing your {Recipe.OutputDef.NameSize}");

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Check for active manufacturing relationship
            var subjectToAgent = Subject.To(Mechanic).Ultimatum;
            var agentToSubject = Mechanic.To(Subject).Ultimatum;

            if (subjectToAgent?.Type == "ManufactureCustomer") return Immediacy.Failed;
            if (agentToSubject?.Type == "ManufactureMechanic") return Immediacy.Failed;
            if (Subject.Supplies[Commodity.Scrap] < Price) return Immediacy.Failed;

            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Recipe.CycleTime;
    }
}
