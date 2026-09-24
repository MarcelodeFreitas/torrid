namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

public sealed class MainWindowViewModel
{
    private readonly ISensorSource _sensorSource;
    private readonly AppConfig _config;
    private readonly Dictionary<string, SensorRowViewModel> _rowsById = new();

    public MainWindowViewModel(ISensorSource sensorSource, AppConfig config)
    {
        _sensorSource = sensorSource;
        _config = config;
        _sensorSource.ReadingsUpdated += OnReadingsUpdated;
        Apply(_sensorSource.CurrentReadings);
    }

    public ObservableCollection<SensorRowViewModel> Rows { get; } = new();

    private void OnReadingsUpdated(object? sender, IReadOnlyList<SensorReading> readings) =>
        Application.Current?.Dispatcher.Invoke(() => Apply(readings));

    private void Apply(IReadOnlyList<SensorReading> readings)
    {
        foreach (var reading in readings)
        {
            if (!_rowsById.TryGetValue(reading.Id, out var row))
            {
                row = new SensorRowViewModel(reading.Label);
                _rowsById[reading.Id] = row;
                Rows.Add(row);
            }

            if (_config.Thresholds.TryGetValue(reading.Kind, out var band))
            {
                row.Update(reading.ValueCelsius, band);
            }
        }
    }
}
