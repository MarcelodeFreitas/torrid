namespace Torrid.App;

using System.Windows;
using Torrid.App.Notifications;
using Torrid.App.Sensors;
using Torrid.App.Tray;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Notifications;
using Torrid.Core.Sensors;
using Torrid.Core.Time;

/// <summary>
/// Composition root: owns config, the sensor source, notifications, the tray and the page view models.
/// Created on the UI thread by <see cref="App"/>; every sensor event is marshalled back with <see cref="UiThread.Post"/>.
/// </summary>
public sealed class AppHost : IDisposable
{
    private MainWindow? _window;
    private bool _disposed;

    public AppHost(StartupOptions options)
    {
        ConfigManager = new ConfigManager(new JsonConfigStore(options.ConfigPath));
        var config = ConfigManager.Config;

        if (options.Demo)
        {
            var fake = new FakeSensorSource();
            if (options.DemoPaused)
            {
                fake.Status = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc"));
            }

            Sensors = fake;
            Hardware = fake;
        }
        else
        {
            // The poll thread reads these delegates; config must only change through ConfigManager.Update,
            // and lists are replaced wholesale rather than mutated in place.
            var service = new SensorService(
                config.PollIntervalMs,
                () => ConfigManager.Config.WhileAntiCheatActive,
                () => ConfigManager.Config.ExtraProtectedProcesses);
            Sensors = service;
            Hardware = service;
        }

        Notifications = new NotificationService(new WindowsToastNotifier(), new SystemClock(), config.NotificationRefireMinutes);
        Sensors.ReadingsUpdated += (_, readings) => UiThread.Post(() => EvaluateNotifications(readings));

        Home = new HomeViewModel(Sensors, Hardware, ConfigManager);
        Shell = new ShellViewModel(new[]
        {
            new NavItem(PageId.Home, "Home", "", Home),
        });

        Tray = new TrayIconGroupManager(Sensors, config);
        Tray.TrayIconClicked += (_, _) => ShowMainWindow();
        Tray.SettingsRequested += (_, _) => ShowMainWindow();
        Tray.ExitRequested += (_, _) => Exit();
    }

    public ConfigManager ConfigManager { get; }

    public ISensorSource Sensors { get; }

    public IHardwareStatusSource Hardware { get; }

    public NotificationService Notifications { get; }

    public HomeViewModel Home { get; }

    public ShellViewModel Shell { get; }

    public TrayIconGroupManager Tray { get; }

    public void AttachWindow(MainWindow window) => _window = window;

    public void Start() => Sensors.Start();

    public void ShowMainWindow(PageId? page = null)
    {
        if (page is PageId id) Shell.Navigate(id);
        if (_window is null) return;

        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Exit()
    {
        if (_window is not null)
        {
            _window.ExitApplication();
        }
        else
        {
            System.Windows.Application.Current?.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Sensors.Stop();
        (Sensors as IDisposable)?.Dispose();
        Tray.Dispose();
        ConfigManager.Dispose(); // flushes a pending debounced save
    }

    private void EvaluateNotifications(IReadOnlyList<SensorReading> readings)
    {
        if (_disposed) return;

        var config = ConfigManager.Config;
        var notifiable = readings
            .Where(r => SensorDisplay.NotificationsEnabled(config, r))
            .Select(r => r with { Label = SensorDisplay.ResolveLabel(config, r) })
            .ToList();
        Notifications.Evaluate(notifiable, config.Thresholds);
    }
}
