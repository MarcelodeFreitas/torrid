namespace Torrid.Core.Config;

using Torrid.Core.Models;

public sealed class AppConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public Dictionary<SensorKind, ThresholdBand> Thresholds { get; set; } = DefaultThresholds();
    public HashSet<string> VisibleSensorIds { get; set; } = new();
    public int NotificationRefireMinutes { get; set; } = 0;
    public bool StartMinimizedToTray { get; set; } = true;
    public Dictionary<string, string> CustomLabels { get; set; } = new();
    public HashSet<string> NotificationDisabledSensorIds { get; set; } = new();

    public static Dictionary<SensorKind, ThresholdBand> DefaultThresholds() => new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 },
        [SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.GpuVram] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
    };
}
