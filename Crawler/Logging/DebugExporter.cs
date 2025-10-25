using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Crawler.Logging;

file sealed class DebugExporter: BaseExporter<Activity> {
    public override ExportResult Export(in Batch<Activity> batch) {
        foreach (var activity in batch) {
            var message = $"[{activity.Kind}] {activity.DisplayName}";

            if (activity.TagObjects.Any()) {
                var tags = string.Join(", ", activity.TagObjects.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                message += $"{{{tags}}}";
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
