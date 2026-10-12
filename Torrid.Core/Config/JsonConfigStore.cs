namespace Torrid.Core.Config;

using System.Text.Json;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class JsonConfigStore : IConfigStore
{
    private const int MinimumPollIntervalMs = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;

    public JsonConfigStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Torrid", "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppConfig();
            var json = File.ReadAllText(_path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
            return Normalize(config);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    internal static AppConfig Normalize(AppConfig config)
    {
        var defaults = AppConfig.DefaultThresholds();

        if (config.Thresholds is null)
        {
            config.Thresholds = defaults;
        }
        else
        {
            foreach (var (kind, defaultBand) in defaults)
            {
                if (!config.Thresholds.TryGetValue(kind, out var band) || band is null)
                {
                    config.Thresholds[kind] = defaultBand;
                }
            }
        }

        if (config.PollIntervalMs <= 0)
        {
            config.PollIntervalMs = MinimumPollIntervalMs;
        }

        config.Sensors ??= new Dictionary<string, SensorPreference>();
        foreach (var key in config.Sensors.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            config.Sensors.Remove(key);
        }

        // A hand-edited per-sensor band that breaks the ordering/range rule is dropped: the sensor follows its kind default.
        foreach (var (key, preference) in config.Sensors.ToList())
        {
            if (preference.Thresholds is null || ThresholdResolver.IsValid(preference.Thresholds)) continue;

            preference.Thresholds = null;
            if (preference.IsEmpty) config.Sensors.Remove(key);
        }

        config.AddedSensors = config.AddedSensors is null
            ? new Dictionary<string, AddedSensor>()
            : config.AddedSensors
                .Where(p => p.Value is not null
                            && SensorIdentity.IsGenericId(p.Key)
                            && !string.IsNullOrWhiteSpace(p.Value.Name))
                .ToDictionary(p => p.Key, p => p.Value);
        foreach (var added in config.AddedSensors.Values)
        {
            if (!Enum.IsDefined(added.Kind)) added.Kind = SensorKind.OtherTemp;
        }

        config.ExtraProtectedProcesses = (config.ExtraProtectedProcesses ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (!Enum.IsDefined(config.WhileAntiCheatActive))
        {
            config.WhileAntiCheatActive = AntiCheatBehavior.Pause;
        }

        return config;
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, SerializerOptions));
    }
}
