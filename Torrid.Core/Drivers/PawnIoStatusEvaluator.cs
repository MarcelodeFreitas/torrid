namespace Torrid.Core.Drivers;

public static class PawnIoStatusEvaluator
{
    public static readonly Version MinimumRecommendedVersion = new(2, 1, 0);

    public const string LatestKnownVersion = "2.2.0";

    public static PawnIoStatus Evaluate(string? installedVersion, bool detectionFailed)
    {
        // A failed lookup says nothing about whether the driver is there, so it is never reported as "not installed".
        if (detectionFailed) return new PawnIoStatus(PawnIoState.Unknown, null);

        var text = installedVersion?.Trim();
        if (string.IsNullOrEmpty(text)) return new PawnIoStatus(PawnIoState.NotInstalled, null);

        // System.Version accepts 2 to 4 numeric parts, so "2.1.0" and "2.0.1.0" both parse; "2" or "v2.2.0" do not.
        if (!Version.TryParse(text, out var parsed)) return new PawnIoStatus(PawnIoState.Unknown, text);

        var state = parsed < MinimumRecommendedVersion ? PawnIoState.Outdated : PawnIoState.Current;
        return new PawnIoStatus(state, text);
    }
}
