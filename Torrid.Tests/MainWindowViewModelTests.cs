using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public sealed class FakeSensorSourceForTests : ISensorSource
{
    public IReadOnlyList<SensorReading> CurrentReadings { get; private set; } = Array.Empty<SensorReading>();
    public event EventHandler<IReadOnlyList<SensorReading>>? ReadingsUpdated;

    public void Start() { }
    public void Stop() { }

    public void Publish(IReadOnlyList<SensorReading> readings)
    {
        CurrentReadings = readings;
        ReadingsUpdated?.Invoke(this, readings);
    }
}

public class MainWindowViewModelTests
{
    private static SensorReading Cpu(double value = 50.0) => new("cpu", "CPU", SensorKind.CpuPackage, null, value);
    private static SensorReading Gpu0Core(double value = 50.0) => new("gpu0-core", "GPU0 Core", SensorKind.GpuCore, 0, value);
    private static SensorReading Gpu0Hot(double value = 50.0) => new("gpu0-hotspot", "GPU0 Hot", SensorKind.GpuHotSpot, 0, value);

    [Fact]
    public void Apply_UsesCustomLabelWhenSet()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        config.Sensors["cpu"] = new SensorPreference { Label = "5950X" };
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu() });

        Assert.Equal("5950X", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_WhenSensorHiddenByDefault_DoesNotAddRow()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Core() });

        Assert.Single(viewModel.Rows);
        Assert.Equal("CPU", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_WhenSensorBecomesInvisible_RemovesExistingRow()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        SensorPreferences.Update(config, "gpu0-core", p => p.ShowInWindow = true);
        source.Publish(new[] { Cpu(), Gpu0Core() });
        Assert.Equal(2, viewModel.Rows.Count);

        SensorPreferences.Update(config, "cpu", p => p.ShowInWindow = false);
        source.Publish(new[] { Cpu(), Gpu0Core() });

        Assert.Single(viewModel.Rows);
        Assert.Equal("GPU0 Core", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_SetsDeviceNameFromGpuIndex()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Hot() });

        var cpuRow = viewModel.Rows.Single(r => r.Label == "CPU");
        var gpuRow = viewModel.Rows.Single(r => r.Label == "GPU0 Hot");
        Assert.Equal("CPU", cpuRow.DeviceName);
        Assert.Equal("GPU 0", gpuRow.DeviceName);
    }
}
