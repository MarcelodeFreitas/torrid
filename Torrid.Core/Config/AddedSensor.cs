namespace Torrid.Core.Config;

using System.Text.Json.Serialization;
using Torrid.Core.Models;

/// <summary>
/// What Torrid remembers about a hardware sensor the user added (keyed by its <c>hw:</c> id in
/// <see cref="AppConfig.AddedSensors"/>), so it still has a name, kind and hardware group while unreadable.
/// </summary>
public sealed class AddedSensor
{
    public string Name { get; set; } = string.Empty;
    public string? HardwareName { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SensorKind Kind { get; set; } = SensorKind.OtherTemp;
}
