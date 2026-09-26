namespace Torrid.App;

using System.ComponentModel;
using System.Windows;
using Torrid.App.Notifications;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Notifications;
using Torrid.Core.Sensors;
using Torrid.Core.Time;

public partial class MainWindow : Window
{
    private readonly IConfigStore _configStore = new JsonConfigStore();
    private readonly AppConfig _config;
    private readonly SensorService _sensorSource;
    private readonly NotificationService _notificationService;
    private readonly TrayIconGroupManager _trayIconGroupManager;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();

        _config = _configStore.Load();
        _sensorSource = new SensorService(_config.PollIntervalMs);
        _notificationService = new NotificationService(new WindowsToastNotifier(), new SystemClock(), _config.NotificationRefireMinutes);

        DataContext = new MainWindowViewModel(_sensorSource, _config);

        _trayIconGroupManager = new TrayIconGroupManager(_sensorSource, _config);
        _trayIconGroupManager.TrayIconClicked += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
        _trayIconGroupManager.SettingsRequested += (_, _) => OpenSettings();
        _trayIconGroupManager.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
        };

        Closing += OnClosing;

        _sensorSource.ReadingsUpdated += OnReadingsUpdatedForNotifications;

        _sensorSource.Start();

        Closed += (_, _) =>
        {
            _sensorSource.Stop();
            _sensorSource.Dispose();
            _trayIconGroupManager.Dispose();
        };
    }

    public bool StartMinimizedToTray => _config.StartMinimizedToTray;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting) return;
        e.Cancel = true;
        Hide();
    }

    private void OnReadingsUpdatedForNotifications(object? sender, IReadOnlyList<Core.Models.SensorReading> readings)
    {
        var notifiable = readings
            .Where(r => Core.Config.SensorDisplay.NotificationsEnabled(_config, r.Id))
            .Select(r => r with { Label = Core.Config.SensorDisplay.ResolveLabel(_config, r) })
            .ToList();
        _notificationService.Evaluate(notifiable, _config.Thresholds);
    }

    private void OpenSettings()
    {
        var settings = new SettingsWindow(_config, _sensorSource.CurrentReadings, _configStore, newPollIntervalMs =>
        {
            _sensorSource.PollIntervalMs = newPollIntervalMs;
            _notificationService.RefireMinutes = _config.NotificationRefireMinutes;
        })
        {
            Owner = this,
        };
        settings.ShowDialog();
    }
}
