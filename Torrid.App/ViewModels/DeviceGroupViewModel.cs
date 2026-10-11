namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;

public sealed class DeviceGroupViewModel : ObservableObject
{
    private string? _subtitle;

    public DeviceGroupViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    /// <summary>Stable identity used to reuse the instance across updates ("cpu", "gpu0", ...).</summary>
    public string Key { get; }

    public string Title { get; }

    public string? Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    public ObservableCollection<SensorCardViewModel> Cards { get; } = new();
}
