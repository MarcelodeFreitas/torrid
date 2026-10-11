namespace Torrid.Core.Config;

using System.Text.Json.Serialization;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class AppConfig
{
    public int PollIntervalMs { get; set; } = 2000;
    public Dictionary<SensorKind, ThresholdBand> Thresholds { get; set; } = DefaultThresholds();
    public int NotificationRefireMinutes { get; set; } = 0;
    public bool StartMinimizedToTray { get; set; } = true;
    public Dictionary<string, SensorPreference> Sensors { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AntiCheatBehavior WhileAntiCheatActive { get; set; } = AntiCheatBehavior.Pause;

    public List<string> ExtraProtectedProcesses { get; set; } = new();

    public static Dictionary<SensorKind, ThresholdBand> DefaultThresholds() => new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 },
        [SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.GpuVram] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
    };
}
