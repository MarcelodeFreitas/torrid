namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;

/// <summary>One device on the Sensors page ("CPU", "GPU 0", ...).</summary>
public sealed class SensorGroupViewModel : ObservableObject
{
    private string? _subtitle;

    public SensorGroupViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    /// <summary>Stable identity used to reuse the instance across rebuilds ("cpu", "gpu0", ...).</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>The hardware name when the device is detected, otherwise null.</summary>
    public string? Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    public ObservableCollection<SensorListItemViewModel> Items { get; } = new();
}
