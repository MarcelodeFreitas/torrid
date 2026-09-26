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

    [Fact]
    public void Apply_UsesCustomLabelWhenSet()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig { CustomLabels = { ["cpu"] = "5950X" } };
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu() });

        Assert.Equal("5950X", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_WhenSensorNotVisible_DoesNotAddRow()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig { VisibleSensorIds = { "cpu" } };
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

        source.Publish(new[] { Cpu(), Gpu0Core() });
        Assert.Equal(2, viewModel.Rows.Count);

        config.VisibleSensorIds.Add("cpu");
        source.Publish(new[] { Cpu(), Gpu0Core() });

        Assert.Single(viewModel.Rows);
        Assert.Equal("CPU", viewModel.Rows.Single().Label);
    }

    [Fact]
    public void Apply_SetsDeviceNameFromGpuIndex()
    {
        var source = new FakeSensorSourceForTests();
        var config = new AppConfig();
        var viewModel = new MainWindowViewModel(source, config);

        source.Publish(new[] { Cpu(), Gpu0Core() });

        var cpuRow = viewModel.Rows.Single(r => r.Label == "CPU");
        var gpuRow = viewModel.Rows.Single(r => r.Label == "GPU0 Core");
        Assert.Equal("CPU", cpuRow.DeviceName);
        Assert.Equal("GPU 0", gpuRow.DeviceName);
    }
}
