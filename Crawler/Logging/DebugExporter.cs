using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Crawler.Logging;

file sealed class DebugExporter: BaseExporter<Activity> {
    static DateTime _startTime = DateTime.UtcNow;
    public override ExportResult Export(in Batch<Activity> batch) {
        // Debug.WriteLine($"=== {batch.Count} ===");
        var activities = new List<Activity>();
        activities.Capacity = ( int ) batch.Count;
        foreach (var activity in batch) {
            activities.Add(activity);
        }
        // var seen = new HashSet<Activity>();
        // var newActivities = new List<Activity>();
        // foreach (var activity in activities) {
        //     var curr = activity;
        //     seen.Add(curr);
        //     while (curr.Parent is { } nextParent) {
        //         curr = nextParent;
        //         if (seen.Add(curr)) {
        //             newActivities.Add(curr);           +
        //         }
        //     }
        // }
        // activities.AddRange(newActivities);
        activities.Sort((a, b) => a.StartTimeUtc.CompareTo(b.StartTimeUtc));

        foreach (var activity in activities) {
            var message = "";
            var parent = activity;
            while (parent.Parent is { } nextParent) {
                message += $"| ";
                parent = nextParent;
            }
            message += $"{activity.DisplayName} {activity.Duration.TotalMicroseconds,7:F0}µs ";
            var t = activity.StartTimeUtc - _startTime;
            message += $"t {t.TotalMicroseconds}µs";
            message += $" {activity.Source.Name} [{activity.Kind}]";

            if (activity.TagObjects.Any()) {
                var tags = string.Join(", ", activity.TagObjects.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                message += $": {tags}";
            }

            Debug.WriteLine(message);
        }

        return ExportResult.Success;
    }
}

public static class DebugExporterExtensions {
    public static TracerProviderBuilder AddDebugExporter(this TracerProviderBuilder builder) {
        return builder.AddProcessor(new SimpleActivityExportProcessor(new DebugExporter()));
    }
}
public class DebugLoggerProvider: ILoggerProvider {
    public ILogger CreateLogger(string categoryName) => new DebugLogger();
    public void Dispose() { }
}

public class DebugLogger: ILogger {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) {
        Debug.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    }
}
