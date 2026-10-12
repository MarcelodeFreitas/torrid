namespace Torrid.Core.Config;

using System.Text.Json.Serialization;

public sealed class SensorPreference
{
    public string? Label { get; set; }
    public bool? ShowInWindow { get; set; }
    public bool? ShowInTray { get; set; }
    public bool? Notify { get; set; }

    /// <summary>Computed; not persisted to config.json.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Label) && ShowInWindow is null && ShowInTray is null && Notify is null;
}
