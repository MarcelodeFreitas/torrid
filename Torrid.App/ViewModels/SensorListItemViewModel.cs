namespace Torrid.App.ViewModels;

using System.Globalization;
using System.Windows.Input;
using Torrid.Core.Config;
using Torrid.Core.Models;

/// <summary>
/// One row of the Sensors page. Every setter writes through <see cref="ConfigManager.Update"/> immediately, storing an
/// explicit preference only when it differs from the per-kind default (so defaults can still evolve in code).
/// </summary>
public sealed class SensorListItemViewModel : ObservableObject
{
    public const string NotDetectedText = "Not detected right now";

    private readonly ConfigManager _config;
    private string _label;
    private string _caption = "";
    private string _valueText = "--";
    private bool _hasValue;
    private ColorZone _zone = ColorZone.Blue;
    private bool _isDetected;
    private bool _showInWindow;
    private bool _showInTray;
    private bool _notify;
    private bool _hasCustomLabel;

    public SensorListItemViewModel(SensorIdentity identity, ConfigManager config)
    {
        Identity = identity;
        _config = config;
        _label = identity.DefaultLabel;
        ResetLabelCommand = new RelayCommand(_ => Label = DefaultLabel);
    }

    public SensorIdentity Identity { get; }

    public string Id => Identity.Id;

    public string DefaultLabel => Identity.DefaultLabel;

    /// <summary>The resolved name. A blank value or the default name clears the custom label.</summary>
    public string Label
    {
        get => _label;
        set
        {
            var trimmed = value?.Trim() ?? "";
            var custom = trimmed.Length == 0 || trimmed == DefaultLabel ? null : trimmed;

            // Name boxes commit on every focus loss; only write (and save) when the stored label really changes.
            var stored = _config.Config.Sensors.TryGetValue(Id, out var preference) && !string.IsNullOrWhiteSpace(preference.Label)
                ? preference.Label
                : null;
            if (stored != custom) _config.Update(c => SensorPreferences.Update(c, Id, p => p.Label = custom));

            // Always raise: the edit box may hold "   " while the resolved value is unchanged.
            _label = custom ?? DefaultLabel;
            Raise(nameof(Label));
            HasCustomLabel = custom is not null;
        }
    }

    public bool HasCustomLabel { get => _hasCustomLabel; private set => Set(ref _hasCustomLabel, value); }

    /// <summary>The real sensor/hardware names, or a "not detected" note.</summary>
    public string Caption { get => _caption; private set => Set(ref _caption, value); }

    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }

    public bool HasValue { get => _hasValue; private set => Set(ref _hasValue, value); }

    public ColorZone Zone { get => _zone; private set => Set(ref _zone, value); }

    public bool IsDetected { get => _isDetected; private set => Set(ref _isDetected, value); }

    public bool ShowInWindow
    {
        get => _showInWindow;
        set
        {
            var stored = value == SensorDefaults.ShownInWindow(Identity.Kind) ? (bool?)null : value;
            _config.Update(c => SensorPreferences.Update(c, Id, p => p.ShowInWindow = stored));
            Set(ref _showInWindow, value);
        }
    }

    public bool ShowInTray
    {
        get => _showInTray;
        set
        {
            var stored = value == SensorDefaults.ShownInTray(Identity.Kind) ? (bool?)null : value;
            _config.Update(c => SensorPreferences.Update(c, Id, p => p.ShowInTray = stored));
            Set(ref _showInTray, value);
        }
    }

    public bool Notify
    {
        get => _notify;
        set
        {
            var stored = value == SensorDefaults.Notify(Identity.Kind) ? (bool?)null : value;
            _config.Update(c => SensorPreferences.Update(c, Id, p => p.Notify = stored));
            Set(ref _notify, value);
        }
    }

    public ICommand ResetLabelCommand { get; }

    /// <summary>Refreshes the preference-derived state from the config (no writes).</summary>
    public void RefreshPreferences(AppConfig config)
    {
        var resolved = SensorDisplay.ResolveLabel(config, Identity);
        Set(ref _label, resolved, nameof(Label));
        HasCustomLabel = resolved != DefaultLabel;
        Set(ref _showInWindow, SensorDisplay.IsShownInWindow(config, Identity), nameof(ShowInWindow));
        Set(ref _showInTray, SensorDisplay.IsShownInTray(config, Identity), nameof(ShowInTray));
        Set(ref _notify, SensorDisplay.NotificationsEnabled(config, Identity), nameof(Notify));
    }

    /// <summary>Refreshes the live part (caption, value, zone). <paramref name="reading"/> is null when not detected.</summary>
    public void UpdateReading(SensorReading? reading, ThresholdBand? band)
    {
        IsDetected = reading is not null;

        if (reading is null)
        {
            Caption = $"{KindName(Identity.Kind)} · {NotDetectedText}";
            ValueText = "--";
            HasValue = false;
            Zone = ColorZone.Blue;
            return;
        }

        var parts = new[] { reading.SourceSensorName, reading.HardwareName }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        Caption = parts.Count > 0 ? string.Join(" · ", parts) : KindName(Identity.Kind);

        if (reading.ValueCelsius is double value)
        {
            ValueText = value.ToString("F0", CultureInfo.InvariantCulture) + " °C";
            HasValue = true;
            Zone = band is null ? ColorZone.Blue : ColorThresholdEvaluator.GetColor(value, band);
        }
        else
        {
            ValueText = "--";
            HasValue = false;
            Zone = ColorZone.Blue;
        }
    }

    public static string KindName(SensorKind kind) => kind switch
    {
        SensorKind.CpuPackage => "CPU package",
        SensorKind.GpuCore => "GPU core",
        SensorKind.GpuHotSpot => "GPU hot spot",
        SensorKind.GpuVram => "GPU memory junction",
        SensorKind.CpuOther => "Other CPU sensor",
        SensorKind.GpuOther => "Other GPU sensor",
        SensorKind.Motherboard => "Motherboard sensor",
        SensorKind.Storage => "Storage sensor",
        SensorKind.Memory => "Memory sensor",
        SensorKind.OtherTemp => "Other sensor",
        _ => kind.ToString(),
    };
}
