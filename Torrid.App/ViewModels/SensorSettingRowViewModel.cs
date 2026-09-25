namespace Torrid.App.ViewModels;

public sealed class SensorSettingRowViewModel
{
    public SensorSettingRowViewModel(string sensorId, string defaultLabel, bool isVisible, string customLabel, bool notificationsEnabled)
    {
        SensorId = sensorId;
        DefaultLabel = defaultLabel;
        IsVisible = isVisible;
        CustomLabel = customLabel;
        NotificationsEnabled = notificationsEnabled;
    }

    public string SensorId { get; }
    public string DefaultLabel { get; }
    public bool IsVisible { get; set; }
    public string CustomLabel { get; set; }
    public bool NotificationsEnabled { get; set; }
}
