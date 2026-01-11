using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// Represents a contact event where two actors' paths cross on a road.
/// </summary>
public record ContactEvent(
    IActor Actor1,
    IActor Actor2,
    Road Road,
    float Progress,      // Where on road (0-1) the contact occurred
    TimePoint Time       // When contact occurred
);

/// <summary>
/// Detects when actors' paths cross on roads using sign-change detection.
/// When the sign of (progress_A - progress_B) changes, they crossed paths.
/// </summary>
public static class ContactDetection {
    /// <summary>
    /// Check for path crossings on a road during a time step.
    /// Returns all pairs of actors that crossed paths during this step.
    /// </summary>
    /// <param name="road">The road to check</param>
    /// <param name="stepStart">Start of the time step</param>
    /// <param name="stepEnd">End of the time step</param>
    public static IEnumerable<ContactEvent> DetectContacts(Road road, TimePoint stepStart, TimePoint stepEnd) {
        var actors = TransitRegistry.ActorsOnRoad(road).ToList();
        if (actors.Count < 2) yield break;

        for (int i = 0; i < actors.Count; i++) {
            for (int j = i + 1; j < actors.Count; j++) {
                var a = actors[i];
                var b = actors[j];

                // Calculate where each was at step start
                float aPrevProgress = CalculatePreviousProgress(a, stepStart, stepEnd);
                float bPrevProgress = CalculatePreviousProgress(b, stepStart, stepEnd);

                // Current positions
                float aCurrProgress = a.Progress;
                float bCurrProgress = b.Progress;

                // Check for crossing: sign of (a - b) changed
                float deltaPrev = aPrevProgress - bPrevProgress;
                float deltaCurr = aCurrProgress - bCurrProgress;

                // Sign change means they crossed
                if (deltaPrev * deltaCurr < 0) {
                    // Calculate intersection point and time using linear interpolation
                    float t = deltaPrev / (deltaPrev - deltaCurr);
                    float contactProgress = aPrevProgress + t * (aCurrProgress - aPrevProgress);

                    // Calculate contact time
                    long stepSeconds = (stepEnd - stepStart).TotalSeconds;
                    var contactTime = stepStart + TimeDuration.FromSeconds((long)(t * stepSeconds));

                    yield return new ContactEvent(a.Actor, b.Actor, road, contactProgress, contactTime);
                }
            }
        }
    }

    /// <summary>
    /// Calculate where an actor was at the start of the step based on current progress and speed.
    /// </summary>
    static float CalculatePreviousProgress(TransitState state, TimePoint stepStart, TimePoint stepEnd) {
        // How much time elapsed in this step
        float elapsedHours = (float)(stepEnd - stepStart).TotalHours;

        // Distance traveled during this step
        float distanceTraveled = state.Speed * elapsedHours * state.Direction;

        // Progress delta during this step
        float progressDelta = distanceTraveled / state.Road.Distance;

        // Previous progress = current - delta
        return state.Progress - progressDelta;
    }

    /// <summary>
    /// Check all active roads for contacts during a time step.
    /// </summary>
    public static IEnumerable<ContactEvent> DetectAllContacts(TimePoint stepStart, TimePoint stepEnd) {
        foreach (var road in TransitRegistry.ActiveRoads) {
            foreach (var contact in DetectContacts(road, stepStart, stepEnd)) {
                yield return contact;
            }
        }
    }
}
