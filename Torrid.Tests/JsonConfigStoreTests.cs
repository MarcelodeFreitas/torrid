using Torrid.Core.Config;
using Torrid.Core.Models;

namespace Torrid.Tests;

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"torrid-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(2000, config.PollIntervalMs);
        Assert.Equal(40, config.Thresholds[SensorKind.CpuPackage].BlueMax);
    }

    [Fact]
    public void Load_WhenFileCorrupted_ReturnsDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(_path, "{ not valid json");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(2000, config.PollIntervalMs);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCustomValues()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig { PollIntervalMs = 5000, NotificationRefireMinutes = 10 };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal(5000, loaded.PollIntervalMs);
        Assert.Equal(10, loaded.NotificationRefireMinutes);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
