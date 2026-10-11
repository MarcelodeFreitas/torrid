namespace Torrid.App.ViewModels;

using System.Globalization;
using Torrid.Core.Config;

public sealed class SensorCardViewModel : ObservableObject
{
    /// <summary>The bar is full a little past the red limit, so a reading at the limit sits at ~87 %.</summary>
    public const double FillHeadroom = 1.15;

    private string _label = "";
    private string? _caption;
    private string _valueText = "--";
    private bool _isAvailable;
    private ColorZone _zone = ColorZone.Blue;
    private double _fill;

    public SensorCardViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public string Label { get => _label; private set => Set(ref _label, value); }

    public string? Caption { get => _caption; private set => Set(ref _caption, value); }

    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public bool IsAvailable { get => _isAvailable; private set => Set(ref _isAvailable, value); }

    public ColorZone Zone { get => _zone; private set => Set(ref _zone, value); }

    public double Fill { get => _fill; private set => Set(ref _fill, value); }

    public void Update(string label, string? caption, double? valueCelsius, ThresholdBand? band)
    {
        Label = label;
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption;

        if (valueCelsius is double value)
        {
            ValueText = value.ToString("F0", CultureInfo.InvariantCulture);
            IsAvailable = true;
            Zone = band is null ? ColorZone.Blue : ColorThresholdEvaluator.GetColor(value, band);
            Fill = band is null || band.YellowMax <= 0 ? 0 : Math.Clamp(value / (band.YellowMax * FillHeadroom), 0, 1);
        }
        else
        {
            ValueText = "--";
            IsAvailable = false;
            Zone = ColorZone.Blue;
            Fill = 0;
        }
    }
}
