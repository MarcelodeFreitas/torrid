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

    /// <summary>The Thresholds page shows the advanced list instead of the cards.</summary>
    public bool ThresholdsListView { get; set; }
    public Dictionary<string, SensorPreference> Sensors { get; set; } = new();

    /// <summary>Generic hardware sensors the user added, keyed by their <c>hw:</c> id. Replaced wholesale, never mutated in place.</summary>
    public Dictionary<string, AddedSensor> AddedSensors { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AntiCheatBehavior WhileAntiCheatActive { get; set; } = AntiCheatBehavior.Pause;

    public List<string> ExtraProtectedProcesses { get; set; } = new();

    public static Dictionary<SensorKind, ThresholdBand> DefaultThresholds() => new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuCore] = new ThresholdBand { BlueMax = 40, GreenMax = 75, YellowMax = 85 },
        [SensorKind.GpuHotSpot] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.GpuVram] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.CpuOther] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
        [SensorKind.GpuOther] = new ThresholdBand { BlueMax = 50, GreenMax = 85, YellowMax = 95 },
        [SensorKind.Motherboard] = new ThresholdBand { BlueMax = 45, GreenMax = 70, YellowMax = 90 },
        [SensorKind.Storage] = new ThresholdBand { BlueMax = 40, GreenMax = 55, YellowMax = 70 },
        [SensorKind.Memory] = new ThresholdBand { BlueMax = 40, GreenMax = 60, YellowMax = 80 },
        [SensorKind.OtherTemp] = new ThresholdBand { BlueMax = 45, GreenMax = 70, YellowMax = 85 },
    };
}
