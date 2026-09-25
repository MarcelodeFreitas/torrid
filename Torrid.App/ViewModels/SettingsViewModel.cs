namespace Torrid.App.ViewModels;

using Torrid.Core.Config;
using Torrid.Core.Models;

public sealed class SettingsViewModel
{
    private const int MinPollIntervalMs = 500;
    private const int MaxPollIntervalMs = 60000;

    private readonly AppConfig _config;

    public SettingsViewModel(AppConfig config, IReadOnlyList<SensorReading> currentReadings)
    {
        _config = config;
        PollIntervalMs = config.PollIntervalMs;
        NotificationRefireMinutes = config.NotificationRefireMinutes;
        StartMinimizedToTray = config.StartMinimizedToTray;

        SensorRows = currentReadings
            .Select(reading => new SensorSettingRowViewModel(
                reading.Id,
                reading.Label,
                SensorDisplay.IsVisible(config, reading.Id),
                config.CustomLabels.TryGetValue(reading.Id, out var custom) ? custom : string.Empty,
                SensorDisplay.NotificationsEnabled(config, reading.Id)))
            .ToList();

        ThresholdRows = Enum.GetValues<SensorKind>()
            .Select(kind =>
            {
                var band = config.Thresholds.TryGetValue(kind, out var existing) ? existing : new ThresholdBand();
                return new ThresholdSettingRowViewModel(kind, band.BlueMax, band.GreenMax, band.YellowMax);
            })
            .ToList();
    }

    public int PollIntervalMs { get; set; }
    public int NotificationRefireMinutes { get; set; }
    public bool StartMinimizedToTray { get; set; }
    public List<SensorSettingRowViewModel> SensorRows { get; }
    public List<ThresholdSettingRowViewModel> ThresholdRows { get; }

    public int Apply()
    {
        var clampedPollIntervalMs = Math.Clamp(PollIntervalMs, MinPollIntervalMs, MaxPollIntervalMs);

        _config.PollIntervalMs = clampedPollIntervalMs;
        _config.NotificationRefireMinutes = NotificationRefireMinutes;
        _config.StartMinimizedToTray = StartMinimizedToTray;

        _config.VisibleSensorIds.Clear();
        if (!SensorRows.All(r => r.IsVisible))
        {
            foreach (var row in SensorRows.Where(r => r.IsVisible))
            {
                _config.VisibleSensorIds.Add(row.SensorId);
            }
        }

        _config.CustomLabels.Clear();
        foreach (var row in SensorRows.Where(r => !string.IsNullOrWhiteSpace(r.CustomLabel)))
        {
            _config.CustomLabels[row.SensorId] = row.CustomLabel;
        }

        _config.NotificationDisabledSensorIds.Clear();
        foreach (var row in SensorRows.Where(r => !r.NotificationsEnabled))
        {
            _config.NotificationDisabledSensorIds.Add(row.SensorId);
        }

        foreach (var row in ThresholdRows)
        {
            _config.Thresholds[row.Kind] = new ThresholdBand { BlueMax = row.BlueMax, GreenMax = row.GreenMax, YellowMax = row.YellowMax };
        }

        return clampedPollIntervalMs;
    }
}
