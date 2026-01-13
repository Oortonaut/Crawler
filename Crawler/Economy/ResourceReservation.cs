namespace Crawler.Economy;

using Production;

/// <summary>
/// Tracks resource commitments for in-progress production.
/// Each commitment represents the remaining input requirements for a recipe in progress.
/// As production progresses and inputs are consumed, the commitment decreases.
/// This prevents multiple segments from double-booking the same resources.
/// </summary>
public class ResourceReservation {
    readonly Dictionary<IndustrySegment, Commitment> _commitments = new();

    /// <summary>Tracks remaining input commitment for a segment.</summary>
    record Commitment(ProductionRecipe Recipe, float BatchSize, EArray<Commodity, float> Remaining);

    /// <summary>
    /// Try to commit resources for a recipe on a segment.
    /// Returns true if all required inputs are available after existing commitments.
    /// </summary>
    public bool TryCommit(IndustrySegment segment, ProductionRecipe recipe, Inventory inventory, float batchSize = 1.0f) {
        // Check if all inputs are available after existing commitments
        foreach (var (commodity, amount) in recipe.Inputs) {
            float available = AvailableStock(inventory, commodity);
            if (available < amount * batchSize) return false;
        }

        // Create commitment for full input amounts (scaled by batch size)
        var remaining = new EArray<Commodity, float>();
        foreach (var (commodity, amount) in recipe.Inputs) {
            remaining[commodity] = amount * batchSize;
        }
        _commitments[segment] = new Commitment(recipe, batchSize, remaining);
        return true;
    }

    /// <summary>
    /// Update commitment after production progress.
    /// Call this after inputs are consumed to reduce the committed amount.
    /// </summary>
    public void UpdateCommitment(IndustrySegment segment, float progressDelta) {
        if (!_commitments.TryGetValue(segment, out var commitment)) return;

        // Reduce remaining commitment by the consumed fraction (already scaled by batch size in commitment)
        foreach (var (commodity, amount) in commitment.Recipe.Inputs) {
            float consumed = amount * commitment.BatchSize * progressDelta;
            commitment.Remaining[commodity] = Math.Max(0, commitment.Remaining[commodity] - consumed);
        }
    }

    /// <summary>
    /// Release a segment's commitment (on completion, cancel, or segment destruction).
    /// </summary>
    public void Release(IndustrySegment segment) {
        _commitments.Remove(segment);
    }

    /// <summary>
    /// Get total committed amount of a commodity across all segments.
    /// </summary>
    public float TotalCommitted(Commodity commodity) {
        return _commitments.Values.Sum(c => c.Remaining[commodity]);
    }

    /// <summary>
    /// Get available (uncommitted) stock of a commodity.
    /// </summary>
    public float AvailableStock(Inventory inventory, Commodity commodity) {
        return inventory[commodity] - TotalCommitted(commodity);
    }

    /// <summary>
    /// Check if a recipe can be started (all inputs available after commitments).
    /// </summary>
    public bool CanStart(ProductionRecipe recipe, Inventory inventory, float batchSize = 1.0f) {
        foreach (var (commodity, amount) in recipe.Inputs) {
            if (AvailableStock(inventory, commodity) < amount * batchSize) return false;
        }
        return true;
    }

    /// <summary>Check if a segment has an active commitment.</summary>
    public bool HasCommitment(IndustrySegment segment) => _commitments.ContainsKey(segment);

    /// <summary>Get the remaining commitment for a segment, or null if none.</summary>
    public IReadOnlyDictionary<Commodity, float>? GetRemaining(IndustrySegment segment) {
        if (!_commitments.TryGetValue(segment, out var commitment)) return null;
        var dict = new Dictionary<Commodity, float>();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commitment.Remaining[commodity] > 0) {
                dict[commodity] = commitment.Remaining[commodity];
            }
        }
        return dict;
    }

    /// <summary>Clear all commitments (for testing or reset).</summary>
    public void Clear() => _commitments.Clear();
}
