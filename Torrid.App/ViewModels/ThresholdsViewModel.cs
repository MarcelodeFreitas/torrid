namespace Torrid.App.ViewModels;

using Torrid.Core.Config;
using Torrid.Core.Models;

/// <summary>The Thresholds page: one card per sensor kind. App-lifetime singleton, so the subscription is never removed.</summary>
public sealed class ThresholdsViewModel : ObservableObject
{
    public ThresholdsViewModel(ConfigManager config)
    {
        Rows = Enum.GetValues<SensorKind>()
            .Select(kind => new ThresholdRowViewModel(kind, Title(kind), config))
            .ToList();

        config.Changed += (_, _) => UiThread.Post(() =>
        {
            foreach (var row in Rows) row.Refresh();
        });
    }

    public IReadOnlyList<ThresholdRowViewModel> Rows { get; }

    public static string Title(SensorKind kind) => kind switch
    {
        SensorKind.CpuPackage => "CPU temperature",
        SensorKind.GpuCore => "GPU core",
        SensorKind.GpuHotSpot => "GPU hot spot",
        SensorKind.GpuVram => "GPU memory junction (VRAM)",
        SensorKind.CpuOther => "Other CPU sensors",
        SensorKind.GpuOther => "Other GPU sensors",
        SensorKind.Motherboard => "Motherboard / VRM",
        SensorKind.Storage => "Storage",
        SensorKind.Memory => "Memory",
        SensorKind.OtherTemp => "Other sensors",
        _ => kind.ToString(),
    };
}
