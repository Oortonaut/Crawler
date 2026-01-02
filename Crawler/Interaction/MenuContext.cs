namespace Crawler;

/// <summary>
/// Unified menu context containing all available actions for the agent.
/// Extends InteractionContext with system, player, and navigation actions.
/// Organizes menu into categories suitable for panel-based TUI display.
/// </summary>
public class MenuContext {
    /// <summary>The actor (typically player) for whom this menu is generated</summary>
    public required IActor Agent { get; init; }

    /// <summary>System actions (Save, Quit, Look, etc.)</summary>
    public List<MenuAction> SystemActions { get; init; } = [];

    /// <summary>Player-specific actions (Segment power, packaging, inventory)</summary>
    public List<MenuAction> PlayerActions { get; init; } = [];

    /// <summary>Navigation actions organized by destination</summary>
    public List<NavigationGroup> NavigationGroups { get; init; } = [];

    /// <summary>Interaction groups organized by subject actor</summary>
    public List<InteractionGroupEx> InteractionGroups { get; init; } = [];

    /// <summary>Flat lookup map for text-based console input (e.g., "M", "CA1", "PP2")</summary>
    private Dictionary<string, IMenuAction> _keyMap = new();

    /// <summary>
    /// Register an action with text key lookup.
    /// </summary>
    public void RegisterAction(string key, IMenuAction action) {
        _keyMap[key.ToUpperInvariant()] = action;
    }

    /// <summary>
    /// Lookup action by text key (e.g., "M" for map, "CA1" for first interaction).
    /// </summary>
    public IMenuAction? Lookup(string key) {
        return _keyMap.GetValueOrDefault(key.ToUpperInvariant());
    }
}

/// <summary>
/// Common interface for all menu actions (interactions, system actions, navigation, etc.).
/// Provides unified execution and display interface.
/// </summary>
public interface IMenuAction {
    /// <summary>Option code for text input (e.g., "M", "CA1", "PP2")</summary>
    string OptionCode { get; }

    /// <summary>Display description</summary>
    string Description { get; }

    /// <summary>Whether this action can be performed right now</summary>
    bool IsEnabled { get; }

    /// <summary>Whether to show in menu (some items hidden until submenu)</summary>
    bool IsVisible { get; }

    /// <summary>Execute the action, returning true if successful</summary>
    bool Perform(string args = "");
}

/// <summary>
/// Menu action wrapping a function (for system and player actions).
/// </summary>
public record MenuAction(
    string OptionCode,
    string Description,
    Func<string, bool> Action,
    bool IsEnabled = true,
    bool IsVisible = true
) : IMenuAction {
    public bool Perform(string args = "") => Action(args);
}

/// <summary>
/// Wrapper for Interaction to implement IMenuAction interface.
/// </summary>
public class InteractionAction : IMenuAction {
    private readonly Interaction _interaction;

    public InteractionAction(Interaction interaction, string optionCode) {
        _interaction = interaction;
        OptionCode = optionCode;
    }

    public string OptionCode { get; }
    public string Description => _interaction.Description;
    public bool IsEnabled => _interaction.GetImmediacy() != Immediacy.Failed;
    public bool IsVisible { get; init; } = true;

    public bool Perform(string args = "") => _interaction.Perform(args);

    public Interaction Interaction => _interaction;
}

/// <summary>
/// Navigation group for a destination type (sector locations or globe sectors).
/// </summary>
public record NavigationGroup {
    public required string GroupName { get; init; } // "Sector Map", "Global Map"
    public required string Prefix { get; init; } // "M", "G"
    public List<MenuAction> Destinations { get; init; } = [];
}

/// <summary>
/// Extended interaction group for MenuContext that includes IMenuAction wrappers.
/// Based on InteractionGroup from InteractionContext with additional fields.
/// </summary>
public class InteractionGroupEx {
    /// <summary>The actor being interacted with</summary>
    public required IActor Subject { get; init; }

    /// <summary>Display label for this subject</summary>
    public required string Label { get; init; }

    /// <summary>Prefix for text-based option codes (e.g., "CA", "CB")</summary>
    public required string Prefix { get; init; }

    /// <summary>Available interactions with this subject as IMenuAction wrappers</summary>
    public List<InteractionAction> Actions { get; init; } = [];

    /// <summary>Raw interactions (for backward compatibility and immediate processing)</summary>
    public List<Interaction> RawInteractions { get; init; } = [];

    /// <summary>Trade offers from this subject (if any)</summary>
    public List<TradeOffer>? TradeOffers { get; init; }

    /// <summary>True if this group has any enabled interactions</summary>
    public bool HasEnabledActions => Actions.Any(a => a.IsEnabled);

    /// <summary>True if this is a trading encounter</summary>
    public bool IsMarket => TradeOffers != null && TradeOffers.Count > 0;
}
