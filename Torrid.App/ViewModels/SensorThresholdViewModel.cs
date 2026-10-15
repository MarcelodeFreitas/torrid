namespace Torrid.App.ViewModels;

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;

/// <summary>
/// One sensor on the Thresholds page (a card or a list row). Its limits are its own band when it has a valid one (Custom),
/// otherwise the default of its kind (Default). A valid trio is written to the config at once; an invalid trio is only shown
/// with an error and never written. One instance per sensor id, reused across ticks: a tick only changes the live values.
/// </summary>
public sealed class SensorThresholdViewModel : ObservableObject
{
    private readonly ConfigManager _config;
    private SensorReading? _reading;
    private string _label;
    private string _caption = SensorListItemViewModel.NotDetectedText;
    private string _valueText = "--";
    private bool _hasValue;
    private ColorZone _zone = ColorZone.Blue;
    private bool _isDetected;
    private double _blueMax;
    private double _greenMax;
    private double _yellowMax;
    private string? _error;
    private bool _isCustom;
    private bool _hasPendingEdit;
    private bool _isFocused;
    private bool _isVisible = true;
    private string _deviceTitle = "";

    public SensorThresholdViewModel(SensorIdentity identity, ConfigManager config)
    {
        Identity = identity;
        _config = config;
        _label = identity.DefaultLabel;
        ResetCommand = new RelayCommand(_ => Reset());
        RefreshFromConfig();
    }

    public SensorIdentity Identity { get; }

    public string Id => Identity.Id;

    /// <summary>The resolved name (the one set on the Sensors page).</summary>
    public string Label { get => _label; private set => Set(ref _label, value); }

    /// <summary>The real sensor / hardware names, or "Not detected right now".</summary>
    public string Caption { get => _caption; private set => Set(ref _caption, value); }

    /// <summary>The live value, or "--" when there is none.</summary>
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public bool HasValue { get => _hasValue; private set => Set(ref _hasValue, value); }

    public ColorZone Zone { get => _zone; private set => Set(ref _zone, value); }

    public bool IsDetected { get => _isDetected; private set => Set(ref _isDetected, value); }

    /// <summary>The device this sensor is listed under on the Sensors page ("CPU", "GPU 0", ...), whatever its placement here.</summary>
    public string DeviceTitle { get => _deviceTitle; internal set => Set(ref _deviceTitle, value); }

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

    /// <summary>True when the sensor has its own band (the badge reads "Custom"), false when it follows its kind default.</summary>
    public bool IsCustom
    {
        get => _isCustom;
        private set
        {
            if (Set(ref _isCustom, value)) Raise(nameof(BadgeText));
        }
    }

    public string BadgeText => IsCustom ? "Custom" : "Default";

    public GridLength BlueWidth => ThresholdRowViewModel.Star(_blueMax);

    public GridLength GreenWidth => ThresholdRowViewModel.Star(_greenMax - _blueMax);

    public GridLength YellowWidth => ThresholdRowViewModel.Star(_yellowMax - _greenMax);

    public GridLength RedWidth => ThresholdRowViewModel.Star(_yellowMax * ThresholdRowViewModel.RedHeadroomFactor);

    /// <summary>Removes the sensor's own band, so it follows its kind default again.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Set by the page when this sensor is the target of <see cref="ThresholdsViewModel.Focus"/>.</summary>
    public bool IsFocused { get => _isFocused; internal set => Set(ref _isFocused, value); }

    /// <summary>False when the search text hides this sensor (the row is kept, only hidden).</summary>
    public bool IsVisible { get => _isVisible; internal set => Set(ref _isVisible, value); }

    /// <summary>Refreshes the live part (caption, value, zone). <paramref name="reading"/> is null when the sensor is not read.</summary>
    public void UpdateReading(SensorReading? reading)
    {
        _reading = SensorRows.Detected(Identity, reading);
        IsDetected = _reading is not null;

        if (_reading is null)
        {
            Caption = SensorListItemViewModel.NotDetectedText;
            ValueText = "--";
            HasValue = false;
        }
        else
        {
            var parts = new[] { _reading.SourceSensorName, _reading.HardwareName }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            Caption = parts.Count > 0 ? string.Join(" · ", parts) : SensorListItemViewModel.KindName(Identity.Kind);

            var value = _reading.ValueCelsius;
            ValueText = value is double v ? v.ToString("F0", CultureInfo.InvariantCulture) + " °C" : "--";
            HasValue = value is not null;
        }

        UpdateZone();
    }

    /// <summary>
    /// Reloads the name, the Custom flag and the limits from the config. The limits are not reloaded while the user's last
    /// edit is invalid and still in the boxes, so a refresh never overwrites what they are typing.
    /// </summary>
    internal void RefreshFromConfig()
    {
        var config = _config.Config;
        Label = SensorDisplay.ResolveLabel(config, Identity);
        IsCustom = ThresholdResolver.HasCustom(config, Id);
        if (!_hasPendingEdit) LoadFromConfig();
        UpdateZone();
    }

    private void Edit(ref double field, double value, string name)
    {
        if (!Set(ref field, value, name)) return;
        RaiseWidths();
        ApplyIfValid();
    }

    private void ApplyIfValid()
    {
        var error = ThresholdRowViewModel.Validate(_blueMax, _greenMax, _yellowMax);
        _hasPendingEdit = error is not null;
        Error = error;
        if (error is not null) return;

        // Typing a value that equals what the sensor already follows writes nothing (and does not make it Custom).
        var effective = ThresholdResolver.For(_config.Config, Identity);
        if (effective.BlueMax == _blueMax && effective.GreenMax == _greenMax && effective.YellowMax == _yellowMax) return;

        var band = new ThresholdBand { BlueMax = _blueMax, GreenMax = _greenMax, YellowMax = _yellowMax };
        _config.Update(c => ThresholdResolver.SetCustom(c, Id, band));
    }

    private void Reset()
    {
        _config.Update(c => ThresholdResolver.ClearCustom(c, Id));
        _hasPendingEdit = false;
        RefreshFromConfig();
    }

    private void LoadFromConfig()
    {
        var band = ThresholdResolver.For(_config.Config, Identity);
        var changed = Set(ref _blueMax, band.BlueMax, nameof(BlueMax));
        changed |= Set(ref _greenMax, band.GreenMax, nameof(GreenMax));
        changed |= Set(ref _yellowMax, band.YellowMax, nameof(YellowMax));
        if (changed) RaiseWidths();

        _hasPendingEdit = false;

        // A hand-edited config can hold a bad kind default: show it as an error rather than pretending it is fine.
        Error = ThresholdRowViewModel.Validate(band.BlueMax, band.GreenMax, band.YellowMax);
        IsCustom = ThresholdResolver.HasCustom(_config.Config, Id);
    }

    private void UpdateZone()
    {
        var band = ThresholdResolver.For(_config.Config, Identity);
        Zone = _reading?.ValueCelsius is double value ? ColorThresholdEvaluator.GetColor(value, band) : ColorZone.Blue;
    }

    private void RaiseWidths()
    {
        Raise(nameof(BlueWidth));
        Raise(nameof(GreenWidth));
        Raise(nameof(YellowWidth));
        Raise(nameof(RedWidth));
    }
}
