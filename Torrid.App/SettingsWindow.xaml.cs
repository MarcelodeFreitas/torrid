namespace Torrid.App;

using System.Windows;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly SettingsViewModel _viewModel;
    private readonly IConfigStore _configStore;
    private readonly Action<int> _onPollIntervalApplied;

    public SettingsWindow(AppConfig config, IReadOnlyList<SensorReading> currentReadings, IConfigStore configStore, Action<int> onPollIntervalApplied)
    {
        InitializeComponent();
        _config = config;
        _viewModel = new SettingsViewModel(config, currentReadings);
        _configStore = configStore;
        _onPollIntervalApplied = onPollIntervalApplied;
        DataContext = _viewModel;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var appliedPollIntervalMs = _viewModel.Apply();
        _configStore.Save(_config);
        _onPollIntervalApplied(appliedPollIntervalMs);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
