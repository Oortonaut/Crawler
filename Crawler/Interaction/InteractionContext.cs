namespace Crawler;

/// <summary>
/// Central registry of available interactions for an agent (typically the player).
/// Organizes interactions by subject actor and provides lookup mechanisms for both
/// text-based console UI and future panel-based TUI.
/// </summary>
public class InteractionContext {
    /// <summary>The actor performing interactions (usually the player)</summary>
    public required IActor Agent { get; init; }

    /// <summary>Grouped interactions organized by subject actor</summary>
    public List<InteractionGroup> Groups { get; } = [];

    /// <summary>Flat index for console-style "CA1", "CB2" lookup</summary>
    private Dictionary<string, Interaction> _keyMap = new();

    /// <summary>
    /// Lookup interaction by text key (e.g., "CA1" for first interaction with first subject).
    /// Used by console renderer for text input parsing.
    /// </summary>
    public Interaction? Lookup(string key) {
        return _keyMap.GetValueOrDefault(key.ToUpperInvariant());
    }

    /// <summary>
    /// Lookup interaction by group and item index.
    /// Used by TUI for direct selection via mouse/keyboard navigation.
    /// </summary>
    public Interaction? LookupByIndex(int groupIndex, int itemIndex) {
        if (groupIndex < 0 || groupIndex >= Groups.Count) return null;
        var group = Groups[groupIndex];
        if (itemIndex < 0 || itemIndex >= group.Interactions.Count) return null;
        return group.Interactions[itemIndex];
    }

    /// <summary>
    /// Register interaction with both text key and index-based lookup.
    /// Called during context building to populate lookup tables.
    /// </summary>
    public void RegisterInteraction(string key, Interaction interaction) {
        _keyMap[key.ToUpperInvariant()] = interaction;
    }

    /// <summary>Total number of interactions across all groups</summary>
    public int TotalInteractionCount => Groups.Sum(g => g.Interactions.Count);
}

/// <summary>
/// Group of interactions between the agent and a specific subject actor.
/// Includes both regular interactions (attack, trade, etc.) and trade offers
/// for market display in TUI.
/// </summary>
public record InteractionGroup {
    /// <summary>The actor being interacted with</summary>
    public required IActor Subject { get; init; }

    /// <summary>Display label for this subject (e.g., "Trader Bob", "Bandit Crawler")</summary>
    public required string Label { get; init; }

    /// <summary>Prefix for text-based option codes (e.g., "CA", "CB")</summary>
    public required string Prefix { get; init; }

    /// <summary>Available interactions with this subject</summary>
    public List<Interaction> Interactions { get; init; } = [];

    /// <summary>
    /// Trade offers from this subject (if any).
    /// Used by TUI to populate market panel/table.
    /// Null if subject is not a trader.
    /// </summary>
    public List<TradeOffer>? TradeOffers { get; init; }

    /// <summary>True if this group has any enabled (non-failed) interactions</summary>
    public bool HasEnabledInteractions => Interactions.Any(i => i.GetImmediacy() != Immediacy.Failed);

    /// <summary>True if this is a trading encounter with market data</summary>
    public bool IsMarket => TradeOffers != null && TradeOffers.Count > 0;
}
