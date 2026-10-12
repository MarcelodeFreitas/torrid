namespace Torrid.App.Drivers;

using System.Diagnostics;
using Torrid.Core.Drivers;

/// <summary>
/// Holds the last known PawnIO status so Home and Settings agree. Detects once on construction (at startup) and again
/// only when <see cref="Refresh"/> is called (the Settings "Re-check" button). Used on the UI thread.
/// </summary>
public sealed class PawnIoStatusTracker
{
    private readonly IPawnIoDetector _detector;

    public PawnIoStatusTracker(IPawnIoDetector detector)
    {
        _detector = detector;
        Status = DetectSafely();
    }

    public PawnIoStatus Status { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Asks the detector again and notifies subscribers.</summary>
    public void Refresh()
    {
        Status = DetectSafely();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PawnIoStatus DetectSafely()
    {
        try
        {
            return _detector.Detect();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PawnIO detector threw: {ex}");
            return new PawnIoStatus(PawnIoState.Unknown, null);
        }
    }
}
