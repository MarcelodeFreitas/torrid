namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using Torrid.Core.Models;
using Torrid.Core.Sensors;

/// <summary>
/// One sensor found by a scan in the Add sensor panel: its name, value then, and an Add button, or a status instead
/// ("Added", or "Already shown" for a sensor a built-in card already reads, which can never be added).
/// </summary>
public sealed class AddSensorItemViewModel : ObservableObject
{
    public const string BuiltInText = "Already shown";

    private bool _isAdded;

    public AddSensorItemViewModel(CatalogSensor sensor, bool isAdded, Action<AddSensorItemViewModel> add, bool isBuiltIn = false)
    {
        Sensor = sensor;
        _isAdded = isAdded;
        IsBuiltIn = isBuiltIn;
        ValueText = sensor.ValueCelsius is double value ? value.ToString("F0", CultureInfo.InvariantCulture) + " °C" : "";
        AddCommand = new RelayCommand(_ => add(this), _ => !IsAdded && !IsBuiltIn);
    }

    public CatalogSensor Sensor { get; }

    public string Id => Sensor.Id;

    public string Name => Sensor.Name;

    public string HardwareName => Sensor.HardwareName;

    public SensorKind Kind => Sensor.Kind;

    /// <summary>The value at scan time ("52 °C"), or empty when the sensor had none.</summary>
    public string ValueText { get; }

    public bool HasValue => Sensor.ValueCelsius is not null;

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (!Set(ref _isAdded, value)) return;
            Raise(nameof(AddButtonText));
            Raise(nameof(AutomationName));
            AddCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>A built-in card (CPU temperature, GPU core / hot spot / memory junction) already reads this sensor.</summary>
    public bool IsBuiltIn { get; }

    public string AddButtonText => IsBuiltIn ? BuiltInText : IsAdded ? "Added" : "Add";

    /// <summary>The button's accessible name, following its state.</summary>
    public string AutomationName => IsBuiltIn ? $"{Name} is already shown" : IsAdded ? $"Added {Name}" : $"Add {Name}";

    public RelayCommand AddCommand { get; }
}

/// <summary>The scan results of one piece of hardware (the group header is its name).</summary>
public sealed class AddSensorGroupViewModel
{
    public AddSensorGroupViewModel(string hardwareName, IEnumerable<AddSensorItemViewModel> items)
    {
        HardwareName = hardwareName;
        Items = new ObservableCollection<AddSensorItemViewModel>(items);
    }

    public string HardwareName { get; }

    public ObservableCollection<AddSensorItemViewModel> Items { get; }
}
