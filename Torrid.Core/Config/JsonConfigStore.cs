namespace Torrid.Core.Config;

using System.Text.Json;

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

        config.VisibleSensorIds ??= new HashSet<string>();
        config.CustomLabels ??= new Dictionary<string, string>();
        config.NotificationDisabledSensorIds ??= new HashSet<string>();

        return config;
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, SerializerOptions));
    }
}
