namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;

/// <summary>
/// One section of the Thresholds page: a device ("CPU", "GPU 0", ...) or the "Not shown" section. Its expanded state lives
/// in memory only; the instance is reused across rebuilds, so the state survives them.
/// </summary>
public sealed class ThresholdGroupViewModel : ObservableObject
{
    private bool _isExpanded;
    private string? _subtitle;
    private bool _isVisible = true;

    public ThresholdGroupViewModel(string key, string title, bool isNotShown = false)
    {
        Key = key;
        Title = title;
        IsNotShown = isNotShown;

        // Devices start open; the "Not shown" section starts collapsed.
        _isExpanded = !isNotShown;
    }

    /// <summary>Stable identity used to reuse the instance across rebuilds ("cpu", "gpu0", "not-shown", ...).</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>True for the section of sensors that are shown nowhere (Window, Tray and Alerts all off).</summary>
    public bool IsNotShown { get; }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    /// <summary>The hardware name(s) of the device when detected, otherwise null.</summary>
    public string? Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    /// <summary>False when the search text leaves none of this section's sensors visible.</summary>
    public bool IsVisible { get => _isVisible; internal set => Set(ref _isVisible, value); }

    public ObservableCollection<SensorThresholdViewModel> Items { get; } = new();
}
