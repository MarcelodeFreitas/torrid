namespace Torrid.Core.Config;

using System.Text.Json.Serialization;

public sealed class SensorPreference
{
    public string? Label { get; set; }
    public bool? ShowInWindow { get; set; }
    public bool? ShowInTray { get; set; }
    public bool? Notify { get; set; }

    /// <summary>This sensor's own colour/alert limits; null means it follows the default of its kind.</summary>
    public ThresholdBand? Thresholds { get; set; }

    /// <summary>Computed; not persisted to config.json.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Label) && ShowInWindow is null && ShowInTray is null && Notify is null && Thresholds is null;
}
