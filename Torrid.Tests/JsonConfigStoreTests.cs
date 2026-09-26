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
    public void Load_WhenThresholdsIsNull_ReplacesWithDefaults()
    {
        File.WriteAllText(_path, "{ \"Thresholds\": null }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.NotNull(config.Thresholds);
        foreach (SensorKind kind in Enum.GetValues(typeof(SensorKind)))
        {
            Assert.True(config.Thresholds.ContainsKey(kind));
            Assert.NotNull(config.Thresholds[kind]);
        }
    }

    [Fact]
    public void Load_WhenThresholdEntryIsNull_ReplacesThatEntryWithDefault()
    {
        File.WriteAllText(_path, "{ \"Thresholds\": { \"CpuPackage\": null } }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.NotNull(config.Thresholds[SensorKind.CpuPackage]);
        Assert.Equal(40, config.Thresholds[SensorKind.CpuPackage].BlueMax);
    }

    [Fact]
    public void Load_WhenThresholdsMissingSomeKinds_MergesInDefaultsForMissingKinds()
    {
        File.WriteAllText(_path, "{ \"Thresholds\": { \"CpuPackage\": { \"BlueMax\": 1, \"GreenMax\": 2, \"YellowMax\": 3 } } }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(3, config.Thresholds[SensorKind.CpuPackage].YellowMax);
        Assert.True(config.Thresholds.ContainsKey(SensorKind.GpuCore));
        Assert.True(config.Thresholds.ContainsKey(SensorKind.GpuHotSpot));
        Assert.True(config.Thresholds.ContainsKey(SensorKind.GpuVram));
        Assert.NotNull(config.Thresholds[SensorKind.GpuCore]);
    }

    [Fact]
    public void Load_WhenPollIntervalMsIsZeroOrNegative_ClampsToMinimum()
    {
        File.WriteAllText(_path, "{ \"PollIntervalMs\": 0 }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.True(config.PollIntervalMs >= 500);
    }

    [Fact]
    public void Load_WhenVisibleSensorIdsIsNull_ReplacesWithEmptySet()
    {
        File.WriteAllText(_path, "{ \"VisibleSensorIds\": null }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.NotNull(config.VisibleSensorIds);
    }

    [Fact]
    public void Load_WhenCustomLabelsAndNotificationDisabledSensorIdsAreNull_ReplacesWithEmptyCollections()
    {
        File.WriteAllText(_path, "{\"CustomLabels\": null, \"NotificationDisabledSensorIds\": null}");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.NotNull(config.CustomLabels);
        Assert.NotNull(config.NotificationDisabledSensorIds);
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
