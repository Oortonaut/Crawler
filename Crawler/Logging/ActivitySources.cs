using System.Diagnostics;

namespace Crawler.Logging;

/// <summary>
/// OpenTelemetry ActivitySource definitions for the Crawler game.
/// Provides structured logging and tracing across different subsystems.
/// </summary>
public static class LogCat {
    /// <summary>Service name for the entire application</summary>
    public const string ServiceName = "Crawler";

    /// <summary>Version for telemetry</summary>
    public const string ServiceVersion = "0.1";

    /// <summary>ActivitySource for proposal and interaction system logging</summary>
    public static readonly ActivitySource Interaction = new(
        $"{ServiceName}.Interaction",
        ServiceVersion);

    /// <summary>ActivitySource for encounters</summary>
    public static readonly ActivitySource Encounter = new(
        $"{ServiceName}.Encounter",
        ServiceVersion);

    /// <summary>ActivitySource for game loop and tick events</summary>
    public static readonly ActivitySource Game = new(
        $"{ServiceName}.Game",
        ServiceVersion);

    /// <summary>ActivitySource for console and UI events</summary>
    public static readonly ActivitySource Console = new(
        $"{ServiceName}.Console",
        ServiceVersion);
}
