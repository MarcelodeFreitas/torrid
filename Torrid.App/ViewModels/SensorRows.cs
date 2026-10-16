namespace Torrid.App.ViewModels;

using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>One sensor of the Sensors / Thresholds pages and the device group it sits in.</summary>
internal sealed record SensorRow(SensorIdentity Identity, DeviceGroup Group, string? HardwareName)
{
    /// <summary>Identifies the row and its placement: a change means a rebuild rather than a value refresh.</summary>
    public string Key => Identity.Id + "|" + Group.Key;
}

/// <summary>
/// The row set shared by the Sensors and Thresholds pages, so both list exactly the same sensors in the same order
/// under the same device titles.
/// </summary>
internal static class SensorRows
{
    /// <summary>The kinds of the built-in cards; a catalog entry they already read is "Already shown".</summary>
    private static readonly SensorKind[] BuiltInKinds =
    {
        SensorKind.CpuPackage, SensorKind.GpuCore, SensorKind.GpuHotSpot, SensorKind.GpuVram,
    };

    /// <summary>
    /// Detected ids, configured ids and added ids that <see cref="SensorDisplay.Identity"/> accepts (a <c>hw:</c> id only
    /// while it is in <see cref="AppConfig.AddedSensors"/>, so garbage keys and just-removed sensors get no row);
    /// ordered CPU, GPUs by index, Motherboard, Memory, Storage, Other, then by kind and name.
    /// </summary>
    public static List<SensorRow> Build(AppConfig config, IReadOnlyList<SensorReading> readings)
    {
        var identities = new Dictionary<string, SensorIdentity>();
        var hardwareNames = new Dictionary<string, string?>();

        foreach (var reading in readings)
        {
            if (reading is null || identities.ContainsKey(reading.Id)) continue;

            var identity = SensorIdentity.IsGenericId(reading.Id)
                ? SensorDisplay.Identity(config, reading.Id)
                : SensorIdentity.FromReading(reading);
            if (identity is null) continue;

            identities[reading.Id] = identity;
            hardwareNames[reading.Id] = reading.HardwareName;
        }

        foreach (var key in config.Sensors.Keys)
        {
            if (!identities.ContainsKey(key) && SensorDisplay.Identity(config, key) is { } parsed) identities[key] = parsed;
        }

        foreach (var key in (config.AddedSensors ?? new Dictionary<string, AddedSensor>()).Keys)
        {
            if (!identities.ContainsKey(key) && SensorIdentity.IsGenericId(key) && SensorDisplay.Identity(config, key) is { } added)
            {
                identities[key] = added;
            }
        }

        var gpuNames = DeviceGroups.GpuNames(readings.Where(r => r is not null));
        return identities.Values
            .Select(identity =>
            {
                var hardware = hardwareNames.GetValueOrDefault(identity.Id) ?? StoredHardwareName(config, identity.Id);
                return new SensorRow(identity, DeviceGroups.For(identity.Kind, identity.GpuIndex, hardware, gpuNames), hardware);
            })
            .OrderBy(r => r.Group.Rank)
            .ThenBy(r => r.Group.Index)
            .ThenBy(r => r.Identity.Kind)
            .ThenBy(r => r.HardwareName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Identity.DefaultLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Identity.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The hardware name remembered for an added sensor (shown while it is not detected).</summary>
    public static string? StoredHardwareName(AppConfig config, string id) =>
        SensorIdentity.IsGenericId(id) ? config.AddedSensors?.GetValueOrDefault(id)?.HardwareName : null;

    /// <summary>
    /// An added sensor the hardware tree doesn't contain comes back from <see cref="SensorReadingBuilder"/> as a placeholder
    /// (no value and no source sensor name): that is "not detected", so it maps to null. Other readings pass through.
    /// </summary>
    public static SensorReading? Detected(SensorIdentity identity, SensorReading? reading) =>
        reading is { ValueCelsius: null, SourceSensorName: null } && SensorIdentity.IsGenericId(identity.Id) ? null : reading;

    /// <summary>The group subtitle: the live hardware name of each item, else the one remembered for an added sensor.</summary>
    public static string? Subtitle(AppConfig config, IEnumerable<string> ids, IReadOnlyDictionary<string, SensorReading> byId) =>
        DeviceGroups.Subtitle(ids.Select(id => byId.GetValueOrDefault(id)?.HardwareName ?? StoredHardwareName(config, id)));

    /// <summary>Readings by id (first one wins), skipping nulls.</summary>
    public static Dictionary<string, SensorReading> ById(IReadOnlyList<SensorReading> readings) =>
        readings.Where(r => r is not null).DistinctBy(r => r.Id).ToDictionary(r => r.Id);

    /// <summary>(hardware name, source sensor name) of every built-in card reading in <paramref name="readings"/>.</summary>
    public static HashSet<(string Hardware, string Sensor)> BuiltInSources(IEnumerable<SensorReading> readings)
    {
        var sources = new HashSet<(string, string)>();
        foreach (var reading in readings)
        {
            if (reading is null || SensorIdentity.IsGenericId(reading.Id) || !BuiltInKinds.Contains(reading.Kind)) continue;
            if (reading.HardwareName is { } hardware && reading.SourceSensorName is { } source) sources.Add((hardware, source));
        }

        return sources;
    }

    /// <summary>A built-in id, or the very sensor a built-in card reads (same hardware and sensor name).</summary>
    public static bool IsBuiltIn(CatalogSensor sensor, HashSet<(string Hardware, string Sensor)> builtInSources) =>
        !SensorIdentity.IsGenericId(sensor.Id) || builtInSources.Contains((sensor.HardwareName ?? "", sensor.Name ?? ""));
}
