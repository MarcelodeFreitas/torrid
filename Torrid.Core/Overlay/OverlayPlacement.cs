namespace Torrid.Core.Overlay;

public static class OverlayPlacement
{
    /// Top-right inside the work area: Left = workLeft + workWidth - windowWidth - margin (never below workLeft), Top = workTop + margin.
    public static (double Left, double Top) TopRight(double workLeft, double workTop, double workWidth, double windowWidth, double margin)
    {
        var left = Math.Max(workLeft, workLeft + workWidth - windowWidth - margin);
        return (left, workTop + margin);
    }
}
