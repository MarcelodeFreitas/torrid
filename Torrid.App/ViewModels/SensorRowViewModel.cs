namespace Torrid.App.ViewModels;

using System.ComponentModel;
using Torrid.Core.Config;

public sealed class SensorRowViewModel : INotifyPropertyChanged
{
    private string _displayValue = "--";
    private ColorZone _zone = ColorZone.Blue;

    public SensorRowViewModel(string label) => Label = label;

    public string Label { get; }

    public string DisplayValue
    {
        get => _displayValue;
        private set { _displayValue = value; OnPropertyChanged(nameof(DisplayValue)); }
    }

    public ColorZone Zone
    {
        get => _zone;
        private set { _zone = value; OnPropertyChanged(nameof(Zone)); }
    }

    public void Update(double? valueCelsius, ThresholdBand thresholds)
    {
        DisplayValue = valueCelsius is double v ? $"{v:F0}°C" : "--";
        Zone = valueCelsius is double v2 ? ColorThresholdEvaluator.GetColor(v2, thresholds) : ColorZone.Blue;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
