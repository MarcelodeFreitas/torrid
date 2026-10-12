namespace Torrid.App.Drivers;

using Torrid.Core.Drivers;

/// <summary>Demo mode (--demo): pretends the current PawnIO is installed so screenshots never depend on this PC's registry.</summary>
public sealed class DemoPawnIoDetector : IPawnIoDetector
{
    public PawnIoStatus Detect() => new(PawnIoState.Current, PawnIoStatusEvaluator.LatestKnownVersion);
}
