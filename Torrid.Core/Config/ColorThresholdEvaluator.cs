namespace Torrid.Core.Config;

public static class ColorThresholdEvaluator
{
    public static ColorZone GetColor(double value, ThresholdBand thresholds)
    {
        if (value < thresholds.BlueMax) return ColorZone.Blue;
        if (value < thresholds.GreenMax) return ColorZone.Green;
        if (value <= thresholds.YellowMax) return ColorZone.Yellow;
        return ColorZone.Red;
    }
}
