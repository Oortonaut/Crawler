namespace Crawler;

using Production;

/// <summary>
/// Component that provides segment demolition/recycling services.
/// Demolition removes segments; Recyclers enable material recovery.
/// </summary>
public class DemolitionComponent : ActorComponentBase {
    readonly string _optionCode;

    public DemolitionComponent(string optionCode = "X") {
        _optionCode = optionCode;
    }

    public override int Priority => 100;

    /// <summary>
    /// Get the best recycler efficiency available (0 if no recycler).
    /// </summary>
    float GetRecyclerEfficiency(Crawler crawler) {
        return crawler.IndustrySegments
            .Where(i => i.IndustryType == IndustryType.Recycler && i.IsActive)
            .Select(r => r.Efficiency)
            .DefaultIfEmpty(0)
            .Max();
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (Owner is not Crawler owner) yield break;

        // Self-demolition interactions
        if (Owner == subject) {
            foreach (var segment in owner.Segments.Where(CanDemolish).ToList()) {
                float recyclerEff = GetRecyclerEfficiency(owner);
                yield return new DemolishInteraction(owner, segment, recyclerEff, _optionCode);
            }

            // Also offer to demolish packaged segments in cargo
            foreach (var segment in owner.Cargo.Segments.Where(s => s.IsPackaged).ToList()) {
                float recyclerEff = GetRecyclerEfficiency(owner);
                yield return new DemolishPackagedInteraction(owner, segment, recyclerEff, _optionCode);
            }
            yield break;
        }

        // Service provision (settlements can demolish for visitors)
        if (!owner.Flags.HasFlag(ActorFlags.Settlement)) yield break;
        if (owner.To(subject).Hostile || subject.To(owner).Hostile) yield break;
        if (subject is not Crawler customer) yield break;

        float serviceEfficiency = GetRecyclerEfficiency(owner);

        // Offer to demolish customer's packaged segments
        foreach (var segment in customer.Cargo.Segments.Where(s => s.IsPackaged).ToList()) {
            yield return new DemolishServiceInteraction(
                owner, customer, segment, serviceEfficiency, _optionCode);
        }
    }

    /// <summary>
    /// Check if a segment can be demolished.
    /// Cannot demolish: last power source, active segments (must package first).
    /// </summary>
    static bool CanDemolish(Segment segment) {
        // Can only demolish packaged or destroyed segments
        return segment.IsPackaged || segment.IsDestroyed;
    }

    /// <summary>
    /// Calculate materials recovered from demolishing a segment.
    /// </summary>
    public static Dictionary<Commodity, float> CalculateRecovery(Segment segment, float recyclerEfficiency) {
        var recovery = new Dictionary<Commodity, float>();
        float baseCost = segment.Cost;

        if (recyclerEfficiency <= 0) {
            // No recycler = only scrap (10% of cost)
            recovery[Commodity.Scrap] = baseCost * 0.1f;
            return recovery;
        }

        // With recycler, recover materials based on segment kind and efficiency
        float eff = recyclerEfficiency;

        // Damaged segments yield less (50% reduction at max damage)
        float damageRatio = segment.MaxHealth > 0
            ? segment.Hits / (float)segment.MaxHealth
            : 0;
        float damageMultiplier = 1.0f - damageRatio * 0.5f;
        eff *= damageMultiplier;

        // Recovery based on segment kind
        var materials = GetRecoverableMaterials(segment.SegmentKind, baseCost);
        foreach (var (commodity, amount) in materials) {
            recovery[commodity] = amount * eff;
        }

        // Always get some scrap
        recovery[Commodity.Scrap] = baseCost * 0.05f * (1 + eff);

        return recovery;
    }

    static Dictionary<Commodity, float> GetRecoverableMaterials(SegmentKind kind, float cost) {
        return kind switch {
            SegmentKind.Offense => new() {
                [Commodity.Alloys] = cost * 0.002f,
                [Commodity.Electronics] = cost * 0.001f,
            },
            SegmentKind.Defense => new() {
                [Commodity.Alloys] = cost * 0.003f,
                [Commodity.Ceramics] = cost * 0.001f,
            },
            SegmentKind.Power => new() {
                [Commodity.Alloys] = cost * 0.001f,
                [Commodity.Electronics] = cost * 0.002f,
            },
            SegmentKind.Traction => new() {
                [Commodity.Alloys] = cost * 0.003f,
                [Commodity.Polymers] = cost * 0.001f,
            },
            SegmentKind.Industry => new() {
                [Commodity.Alloys] = cost * 0.002f,
                [Commodity.Electronics] = cost * 0.001f,
            },
            SegmentKind.Storage => new() {
                [Commodity.Alloys] = cost * 0.002f,
                [Commodity.Polymers] = cost * 0.001f,
            },
            SegmentKind.Harvest => new() {
                [Commodity.Alloys] = cost * 0.002f,
                [Commodity.Electronics] = cost * 0.001f,
            },
            SegmentKind.Habitat => new() {
                [Commodity.Alloys] = cost * 0.003f,
                [Commodity.Ceramics] = cost * 0.002f,
                [Commodity.Glass] = cost * 0.002f,
                [Commodity.Polymers] = cost * 0.001f,
            },
            _ => new() {
                [Commodity.Alloys] = cost * 0.002f,
            }
        };
    }

    static string FormatRecovery(Dictionary<Commodity, float> recovery) {
        return string.Join(", ", recovery
            .Where(kv => kv.Value >= 0.1f)
            .Select(kv => $"{kv.Value:F1} {kv.Key}"));
    }

    /// <summary>
    /// Interaction for demolishing an installed segment (must be packaged or destroyed).
    /// </summary>
    public record DemolishInteraction(
        Crawler Owner,
        Segment Target,
        float RecyclerEfficiency,
        string MenuOption
    ) : Interaction(Owner, Owner, MenuOption) {

        public override string Description {
            get {
                var recovery = CalculateRecovery(Target, RecyclerEfficiency);
                var recoveryDesc = FormatRecovery(recovery);
                return $"Demolish {Target.NameSize} -> {recoveryDesc}";
            }
        }

        public override bool Perform(string args = "") {
            var recovery = CalculateRecovery(Target, RecyclerEfficiency);

            // Remove segment from crawler
            Owner.RemoveSegment(Target);

            // Add recovered materials to cargo
            foreach (var (commodity, amount) in recovery) {
                Owner.Cargo.Add(commodity, amount);
            }

            Owner.Message($"Demolished {Target.NameSize}");
            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            // Must be packaged or destroyed
            if (!Target.IsPackaged && !Target.IsDestroyed) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => TimeDuration.FromHours(
            1 + Target.SegmentDef.Size.Size * 0.5f
        );
    }

    /// <summary>
    /// Interaction for demolishing a packaged segment in cargo.
    /// </summary>
    public record DemolishPackagedInteraction(
        Crawler Owner,
        Segment Target,
        float RecyclerEfficiency,
        string MenuOption
    ) : Interaction(Owner, Owner, MenuOption) {

        public override string Description {
            get {
                var recovery = CalculateRecovery(Target, RecyclerEfficiency);
                var recoveryDesc = FormatRecovery(recovery);
                return $"Demolish packaged {Target.NameSize} -> {recoveryDesc}";
            }
        }

        public override bool Perform(string args = "") {
            var recovery = CalculateRecovery(Target, RecyclerEfficiency);

            // Remove segment from cargo
            Owner.Cargo.Segments.Remove(Target);

            // Add recovered materials to cargo
            foreach (var (commodity, amount) in recovery) {
                Owner.Cargo.Add(commodity, amount);
            }

            Owner.Message($"Demolished packaged {Target.NameSize}");
            return true;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;

        public override TimeDuration ExpectedDuration => TimeDuration.FromHours(
            0.5f + Target.SegmentDef.Size.Size * 0.25f
        );
    }

    /// <summary>
    /// Interaction for settlement to demolish visitor's segment (service).
    /// </summary>
    public record DemolishServiceInteraction(
        Crawler ServiceProvider,
        Crawler Customer,
        Segment Target,
        float RecyclerEfficiency,
        string MenuOption
    ) : Interaction(ServiceProvider, Customer, MenuOption) {

        float ServiceFee => Target.Cost * 0.1f;

        public override string Description {
            get {
                var recovery = CalculateRecovery(Target, RecyclerEfficiency);
                var recoveryDesc = FormatRecovery(recovery);
                return $"Demolition service: {Target.NameSize} -> {recoveryDesc} (fee: {ServiceFee:F0} scrap)";
            }
        }

        public override bool Perform(string args = "") {
            // Charge fee
            Customer.Supplies.Remove(Commodity.Scrap, ServiceFee);
            ServiceProvider.Supplies.Add(Commodity.Scrap, ServiceFee);

            var recovery = CalculateRecovery(Target, RecyclerEfficiency);

            // Remove segment from customer's cargo
            Customer.Cargo.Segments.Remove(Target);

            // Add recovered materials to customer's cargo
            foreach (var (commodity, amount) in recovery) {
                Customer.Cargo.Add(commodity, amount);
            }

            ServiceProvider.Message($"Demolished {Target.NameSize} for {Customer.Name}");
            Customer.Message($"{ServiceProvider.Name} demolished your {Target.NameSize}");
            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Customer.Supplies[Commodity.Scrap] < ServiceFee) return Immediacy.Failed;
            if (!Target.IsPackaged) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => TimeDuration.FromHours(
            1 + Target.SegmentDef.Size.Size * 0.5f
        );
    }
}
