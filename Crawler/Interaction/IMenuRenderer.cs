namespace Crawler;

/// <summary>
/// Strategy interface for rendering interaction menus and capturing user selection.
/// Decouples interaction logic from presentation, enabling multiple UI implementations
/// (console text, TUI panels, etc.).
/// </summary>
public interface IMenuRenderer {
    /// <summary>
    /// Display the interaction context and return user's selection.
    /// Implementations handle all rendering, input processing, and validation.
    /// </summary>
    /// <param name="context">Interaction context with available options</param>
    /// <param name="title">Menu title/prompt</param>
    /// <param name="defaultOption">Default selection hint (optional)</param>
    /// <returns>User's menu selection</returns>
    MenuSelection Render(InteractionContext context, string title = "", string defaultOption = "");
}

/// <summary>
/// Result of menu interaction, containing selected action and any arguments.
/// Renderer-agnostic representation of user choice.
/// </summary>
public record MenuSelection {
    /// <summary>The interaction the user chose to perform (null if cancelled)</summary>
    public Interaction? SelectedInteraction { get; init; }

    /// <summary>Arguments for the interaction (e.g., quantity for trade)</summary>
    public string Arguments { get; init; } = "";

    /// <summary>True if user cancelled/exited menu</summary>
    public bool Cancelled { get; init; }

    /// <summary>Convenience factory for cancelled selection</summary>
    public static MenuSelection Cancel => new() { Cancelled = true };

    /// <summary>Convenience factory for interaction selection</summary>
    public static MenuSelection Select(Interaction interaction, string args = "") =>
        new() { SelectedInteraction = interaction, Arguments = args };
}
