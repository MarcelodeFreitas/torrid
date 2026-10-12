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

    [Fact]
    public void AddedSensors_DefaultsToEmpty_AndOldConfigsWithoutTheFieldLoad()
    {
        Assert.Empty(new AppConfig().AddedSensors);

        File.WriteAllText(_path, "{\"PollIntervalMs\": 3000}");
        var config = new JsonConfigStore(_path).Load();

        Assert.NotNull(config.AddedSensors);
        Assert.Empty(config.AddedSensors);
    }

    [Fact]
    public void AddedSensors_RoundTripWithKindSerialisedAsAString()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig();
        config.AddedSensors["hw:/lpc/nct6798d/0/temperature/4"] =
            new AddedSensor { Name = "VRM", HardwareName = "ASUS ROG", Kind = SensorKind.Motherboard };

        store.Save(config);
        var loaded = store.Load();

        Assert.Contains("\"Motherboard\"", File.ReadAllText(_path));
        var added = Assert.Single(loaded.AddedSensors);
        Assert.Equal("hw:/lpc/nct6798d/0/temperature/4", added.Key);
        Assert.Equal("VRM", added.Value.Name);
        Assert.Equal("ASUS ROG", added.Value.HardwareName);
        Assert.Equal(SensorKind.Motherboard, added.Value.Kind);
    }

    [Fact]
    public void Load_AddedSensors_DropsNullEntriesInvalidKeysAndBlankNames_WithoutThrowing()
    {
        File.WriteAllText(_path, """
            { "AddedSensors": {
                "hw:/ok/0": { "Name": "Good", "Kind": "Storage" },
                "hw:/nullentry": null,
                "cpu": { "Name": "Not generic", "Kind": "CpuOther" },
                "hw:": { "Name": "Empty tail", "Kind": "CpuOther" },
                "hw:/has space": { "Name": "Space", "Kind": "CpuOther" },
                "hw:/blank": { "Name": "   ", "Kind": "CpuOther" },
                "hw:/noname": { "Kind": "CpuOther" }
            } }
            """);

        var config = new JsonConfigStore(_path).Load();

        var added = Assert.Single(config.AddedSensors);
        Assert.Equal("hw:/ok/0", added.Key);
        Assert.Equal(SensorKind.Storage, added.Value.Kind);
    }

    [Fact]
    public void Load_AddedSensorsNull_NormalizesToEmpty()
    {
        File.WriteAllText(_path, "{ \"AddedSensors\": null }");

        var config = new JsonConfigStore(_path).Load();

        Assert.NotNull(config.AddedSensors);
        Assert.Empty(config.AddedSensors);
    }

    [Fact]
    public void DefaultThresholds_IncludeTheNewKindsWithThePlannedBands()
    {
        var t = AppConfig.DefaultThresholds();

        Assert.Equal(new[] { 40.0, 70, 85 }, Band(t[SensorKind.CpuOther]));
        Assert.Equal(new[] { 50.0, 85, 95 }, Band(t[SensorKind.GpuOther]));
        Assert.Equal(new[] { 45.0, 70, 90 }, Band(t[SensorKind.Motherboard]));
        Assert.Equal(new[] { 40.0, 55, 70 }, Band(t[SensorKind.Storage]));
        Assert.Equal(new[] { 40.0, 60, 80 }, Band(t[SensorKind.Memory]));
        Assert.Equal(new[] { 45.0, 70, 85 }, Band(t[SensorKind.OtherTemp]));
        foreach (SensorKind kind in Enum.GetValues(typeof(SensorKind)))
        {
            Assert.True(t.ContainsKey(kind));
        }
    }

    [Fact]
    public void Load_ConfigSavedByAnOlderBuild_GetsTheNewKindsBands_AndKeepsCustomisedOnes()
    {
        File.WriteAllText(_path, """
            { "Thresholds": {
                "CpuPackage": { "BlueMax": 1, "GreenMax": 2, "YellowMax": 3 },
                "GpuCore": { "BlueMax": 40, "GreenMax": 75, "YellowMax": 85 },
                "GpuHotSpot": { "BlueMax": 50, "GreenMax": 85, "YellowMax": 95 },
                "GpuVram": { "BlueMax": 50, "GreenMax": 85, "YellowMax": 95 }
            } }
            """);

        var config = new JsonConfigStore(_path).Load();

        Assert.Equal(3, config.Thresholds[SensorKind.CpuPackage].YellowMax);
        Assert.Equal(90, config.Thresholds[SensorKind.Motherboard].YellowMax);
        Assert.Equal(70, config.Thresholds[SensorKind.Storage].YellowMax);
        foreach (SensorKind kind in Enum.GetValues(typeof(SensorKind)))
        {
            Assert.NotNull(config.Thresholds[kind]);
        }
    }

    private static double[] Band(ThresholdBand b) => new[] { b.BlueMax, b.GreenMax, b.YellowMax };

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void RoundTrip_KeepsAPerSensorBandAndTheListViewFlag()
    {
        var store = new JsonConfigStore(_path);
        var config = new AppConfig { ThresholdsListView = true };
        config.Sensors["cpu"] = new SensorPreference
        {
            Label = "5950X",
            Thresholds = new ThresholdBand { BlueMax = 30, GreenMax = 60, YellowMax = 90 },
        };
        store.Save(config);

        var loaded = new JsonConfigStore(_path).Load();

        Assert.True(loaded.ThresholdsListView);
        Assert.Equal("5950X", loaded.Sensors["cpu"].Label);
        Assert.Equal(new[] { 30.0, 60.0, 90.0 }, Band(loaded.Sensors["cpu"].Thresholds!));
    }

    [Fact]
    public void Load_ConfigWithoutPerSensorBands_LoadsUnchanged()
    {
        File.WriteAllText(_path, "{ \"Sensors\": { \"cpu\": { \"Label\": \"5950X\", \"Notify\": false } } }");

        var config = new JsonConfigStore(_path).Load();

        Assert.Null(config.Sensors["cpu"].Thresholds);
        Assert.Equal("5950X", config.Sensors["cpu"].Label);
        Assert.False(config.ThresholdsListView);
    }

    [Fact]
    public void Load_DropsAnInvalidPerSensorBand_ButKeepsTheOtherFields()
    {
        File.WriteAllText(_path, """
            { "Sensors": {
                "cpu": { "Label": "5950X", "Thresholds": { "BlueMax": 90, "GreenMax": 60, "YellowMax": 30 } },
                "gpu0-core": { "Thresholds": { "BlueMax": 90, "GreenMax": 60, "YellowMax": 30 } },
                "gpu0-vram": { "Thresholds": { "BlueMax": 30, "GreenMax": 60, "YellowMax": 90 } }
            } }
            """);

        var config = new JsonConfigStore(_path).Load();

        Assert.Equal("5950X", config.Sensors["cpu"].Label);
        Assert.Null(config.Sensors["cpu"].Thresholds);
        Assert.False(config.Sensors.ContainsKey("gpu0-core"));
        Assert.Equal(90, config.Sensors["gpu0-vram"].Thresholds!.YellowMax);
    }
}
