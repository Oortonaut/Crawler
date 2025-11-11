using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crawler.Logging;

/// <summary>
/// OpenTelemetry ActivitySource definitions for the Crawler game.
/// Provides structured logging and tracing across different subsystems.
/// </summary>
public static class LogCat {
    public static readonly string ServiceName = Assembly.GetExecutingAssembly().GetName().Name ?? "Crawler";
    public static readonly string ServiceVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    public static readonly ActivitySource Interaction = new($"{ServiceName}.Interaction", ServiceVersion);
    public static readonly ActivitySource Encounter = new($"{ServiceName}.Encounter", ServiceVersion);
    public static readonly ActivitySource Game = new($"{ServiceName}.Game", ServiceVersion);
    public static readonly ActivitySource Console = new($"{ServiceName}.Console", ServiceVersion);

    public static readonly Meter GameMetrics = new($"{ServiceName}.Game", ServiceVersion);
    public static readonly Meter EncounterMetrics = new($"{ServiceName}.Encounter", ServiceVersion);

    public static ILogger Log { get; set; } = NullLogger.Instance;
}
