namespace Torrid.App.ViewModels;

using System.Windows;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;

/// <summary>
/// One threshold card. The three limits are edited independently; the band is written to the config (as a new
/// <see cref="ThresholdBand"/>) as soon as the three values form a valid ordering, and never while they don't.
/// </summary>
public sealed class ThresholdRowViewModel : ObservableObject
{
    public const double MaxLimit = 150;
    public const double RedHeadroomFactor = 0.25;
    public const string OrderingError = "Each limit must be higher than the one before.";
    public const string RangeError = "Limits must be between 0 and 150 °C.";

    private readonly ConfigManager _config;
    private double _blueMax;
    private double _greenMax;
    private double _yellowMax;
    private string? _error;

    public ThresholdRowViewModel(SensorKind kind, string title, ConfigManager config)
    {
        Kind = kind;
        Title = title;
        _config = config;
        ResetCommand = new RelayCommand(_ => Reset());
        LoadFromConfig();
    }

    public SensorKind Kind { get; }

    public string Title { get; }

    public double BlueMax { get => _blueMax; set => Edit(ref _blueMax, value, nameof(BlueMax)); }

    public double GreenMax { get => _greenMax; set => Edit(ref _greenMax, value, nameof(GreenMax)); }

    public double YellowMax { get => _yellowMax; set => Edit(ref _yellowMax, value, nameof(YellowMax)); }

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value)) Raise(nameof(HasError));
        }
    }

    public bool HasError => _error is not null;

    public GridLength BlueWidth => Star(_blueMax);

    public GridLength GreenWidth => Star(_greenMax - _blueMax);

    public GridLength YellowWidth => Star(_yellowMax - _greenMax);

    public GridLength RedWidth => Star(_yellowMax * RedHeadroomFactor);

    public ICommand ResetCommand { get; }

    public static string? Validate(double blue, double green, double yellow)
    {
        if (double.IsNaN(blue) || double.IsNaN(green) || double.IsNaN(yellow) || blue < 0 || yellow > MaxLimit) return RangeError;
        return blue < green && green < yellow ? null : OrderingError;
    }

    /// <summary>Reloads the values from the config unless the user is part-way through an invalid edit.</summary>
    public void Refresh()
    {
        if (Error is null) LoadFromConfig();
    }

    private void Edit(ref double field, double value, string name)
    {
        if (!Set(ref field, value, name)) return;
        RaiseWidths();
        ApplyIfValid();
    }

    private void ApplyIfValid()
    {
        Error = Validate(_blueMax, _greenMax, _yellowMax);
        if (Error is not null) return;

        var current = _config.Config.Thresholds.GetValueOrDefault(Kind);
        if (current is not null && current.BlueMax == _blueMax && current.GreenMax == _greenMax && current.YellowMax == _yellowMax) return;

        var band = new ThresholdBand { BlueMax = _blueMax, GreenMax = _greenMax, YellowMax = _yellowMax };
        _config.Update(c => c.Thresholds[Kind] = band);
    }

    private void Reset()
    {
        var defaults = AppConfig.DefaultThresholds()[Kind];
        SetAll(defaults.BlueMax, defaults.GreenMax, defaults.YellowMax);
        ApplyIfValid();
    }

    private void LoadFromConfig()
    {
        var band = _config.Config.Thresholds.GetValueOrDefault(Kind) ?? AppConfig.DefaultThresholds()[Kind];
        SetAll(band.BlueMax, band.GreenMax, band.YellowMax);
        Error = null;
    }

    private void SetAll(double blue, double green, double yellow)
    {
        var changed = Set(ref _blueMax, blue, nameof(BlueMax));
        changed |= Set(ref _greenMax, green, nameof(GreenMax));
        changed |= Set(ref _yellowMax, yellow, nameof(YellowMax));
        if (changed) RaiseWidths();
    }

    private void RaiseWidths()
    {
        Raise(nameof(BlueWidth));
        Raise(nameof(GreenWidth));
        Raise(nameof(YellowWidth));
        Raise(nameof(RedWidth));
    }

    private static GridLength Star(double value) =>
        new(double.IsFinite(value) ? Math.Max(0, value) : 0, GridUnitType.Star);
}
