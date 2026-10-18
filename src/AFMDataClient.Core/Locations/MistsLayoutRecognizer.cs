using System.Text.Json;

namespace AlbionDataAvalonia.Locations;

/// <summary>A Mists layout and how far the instance turns it. Each quarter turn maps a layout point (x, y)
/// to (y, -x), clockwise on a north-up map.</summary>
public readonly record struct MistsLayoutPlacement(string Layout, int QuarterTurns);

/// <summary>
/// Recognizes a Mists instance's layout from its wisp spawns and border exits. Entering through a wisp sends no
/// layout name, but those landmarks stand at fixed points of each layout, which an instance may turn.
/// </summary>
public static class MistsLayoutRecognizer
{
    // Landmarks arrive at their exact layout points; the tolerance only absorbs float rounding.
    private const double Tolerance = 1.5;
    private const int MinimumMatches = 2;
    private static readonly IReadOnlyList<(string Layout, (double X, double Y)[] Points)> Layouts = Load();

    /// <summary>The placement that fits the most landmarks, when at least two fit and no other placement fits as many;
    /// unless it fits them all, no other placement may fit more than half as many. Otherwise null, since a few more
    /// landmarks will tell. The two wisps often sent on entry place about five instances in six.</summary>
    public static MistsLayoutPlacement? Recognize(IReadOnlyCollection<ActivityWorldPosition> landmarks)
    {
        if (landmarks.Count < MinimumMatches) return null;
        MistsLayoutPlacement? best = null;
        int bestScore = 0, runnerUp = 0;
        foreach (var (layout, points) in Layouts)
        {
            for (var turns = 0; turns < 4; turns++)
            {
                var score = landmarks.Count(landmark => points.Any(point => Fits(Turn(point, turns), landmark)));
                if (score > bestScore)
                {
                    runnerUp = bestScore;
                    bestScore = score;
                    best = new(layout, turns);
                }
                else if (score > runnerUp) runnerUp = score;
            }
        }

        return bestScore >= MinimumMatches && runnerUp < bestScore && (bestScore == landmarks.Count || runnerUp * 2 <= bestScore)
            ? best : null;
    }

    private static (double X, double Y) Turn((double X, double Y) point, int turns)
    {
        for (var turn = 0; turn < turns; turn++) point = (point.Y, -point.X);
        return point;
    }

    private static bool Fits((double X, double Y) point, ActivityWorldPosition landmark) =>
        Math.Abs(point.X - landmark.X) <= Tolerance && Math.Abs(point.Y - landmark.Y) <= Tolerance;

    private static IReadOnlyList<(string, (double, double)[])> Load()
    {
        using var stream = typeof(MistsLayoutRecognizer).Assembly.GetManifestResourceStream("AFMDataClient.Core.Locations.activity-world.json")
            ?? throw new InvalidOperationException("Activity world classification resource is missing.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("mistsLandmarks", out var layouts)) return [];
        return layouts.EnumerateObject().Select(layout =>
        {
            var values = layout.Value.EnumerateArray().Select(value => value.GetDouble()).ToArray();
            return (layout.Name, Enumerable.Range(0, values.Length / 2).Select(index => (values[index * 2], values[index * 2 + 1])).ToArray());
        }).ToArray();
    }
}
