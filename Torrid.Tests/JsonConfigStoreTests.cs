using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

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
    public void Load_DropsNullAndBlankExtraProtectedProcesses_AndTrims()
    {
        File.WriteAllText(_path, "{ \"ExtraProtectedProcesses\": [\"x\", null, \"  \", \" y \"] }");
        var store = new JsonConfigStore(_path);

        var config = store.Load();

        Assert.Equal(new[] { "x", "y" }, config.ExtraProtectedProcesses);
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
    public void Load_WhenSensorsIsNullOrHasNullEntries_NormalizesToUsableDictionary()
    {
        File.WriteAllText(_path, "{\"Sensors\": null}");
        Assert.NotNull(new JsonConfigStore(_path).Load().Sensors);

        File.WriteAllText(_path, "{\"Sensors\": {\"cpu\": null, \"gpu0-vram\": {\"ShowInTray\": true}}}");
        var config = new JsonConfigStore(_path).Load();

        Assert.False(config.Sensors.ContainsKey("cpu"));
        Assert.True(config.Sensors["gpu0-vram"].ShowInTray);
    }

    [Fact]
    public void Load_OldSchemaFileStillLoads_IgnoringRemovedFields()
    {
        File.WriteAllText(_path, "{\"PollIntervalMs\": 3000, \"VisibleSensorIds\": [\"cpu\"], \"CustomLabels\": {\"cpu\": \"x\"}}");

        var config = new JsonConfigStore(_path).Load();

        Assert.Equal(3000, config.PollIntervalMs);
        Assert.Empty(config.Sensors);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSensorPreferences()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig();
        config.Sensors["gpu0-core"] = new SensorPreference { Label = "MSI Core", ShowInWindow = true, Notify = false };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal("MSI Core", loaded.Sensors["gpu0-core"].Label);
        Assert.True(loaded.Sensors["gpu0-core"].ShowInWindow);
        Assert.False(loaded.Sensors["gpu0-core"].Notify);
    }

    [Fact]
    public void Save_DoesNotWriteTheComputedIsEmptyProperty()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X", ShowInTray = false };

        store.Save(config);

        var json = File.ReadAllText(_path);
        Assert.Contains("5950X", json);
        Assert.DoesNotContain("IsEmpty", json);
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

    [Fact]
    public void AntiCheatSettings_RoundTripAndDefaultToPause()
    {
        var defaults = new JsonConfigStore(_path).Load();
        Assert.Equal(AntiCheatBehavior.Pause, defaults.WhileAntiCheatActive);
        Assert.Empty(defaults.ExtraProtectedProcesses);

        var store = new JsonConfigStore(_path);
        store.Save(new AppConfig { WhileAntiCheatActive = AntiCheatBehavior.GpuOnly, ExtraProtectedProcesses = new List<string> { "mygame" } });
        var loaded = store.Load();

        Assert.Equal(AntiCheatBehavior.GpuOnly, loaded.WhileAntiCheatActive);
        Assert.Equal(new[] { "mygame" }, loaded.ExtraProtectedProcesses);
        Assert.Contains("GpuOnly", File.ReadAllText(_path));
    }

    [Fact]
    public void Load_NullExtraProcessesOrBogusBehavior_FallBackToSafeValues()
    {
        File.WriteAllText(_path, "{\"ExtraProtectedProcesses\": null, \"WhileAntiCheatActive\": 99}");

        var config = new JsonConfigStore(_path).Load();

        Assert.NotNull(config.ExtraProtectedProcesses);
        Assert.Equal(AntiCheatBehavior.Pause, config.WhileAntiCheatActive);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
