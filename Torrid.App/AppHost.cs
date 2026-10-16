namespace Torrid.App;

using System.Diagnostics;
using System.Windows;
using Torrid.App.Drivers;
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
    private readonly WindowsToastNotifier _toasts;
    private MainWindow? _window;
    private bool _disposed;

    public AppHost(StartupOptions options)
    {
        // A demo without an explicit --config must never read or overwrite the user's real settings.
        IConfigStore store = options.Demo && options.ConfigPath is null
            ? new TransientConfigStore()
            : new JsonConfigStore(options.ConfigPath);
        ConfigManager = new ConfigManager(store);
        var config = ConfigManager.Config;

        if (options.Demo)
        {
            var fake = new FakeSensorSource(() => ConfigManager.Config.AddedSensors);
            if (options.DemoPaused)
            {
                fake.Status = new HardwareStatus(HardwareAccessLevel.Paused, new AntiCheatMatch("Riot Vanguard", "vgc"));
            }

            Sensors = fake;
            Hardware = fake;
            Catalog = fake;
        }
        else
        {
            // The poll thread reads these delegates; config must only change through ConfigManager.Update,
            // and lists/dictionaries are replaced wholesale rather than mutated in place.
            var service = new SensorService(
                config.PollIntervalMs,
                () => ConfigManager.Config.WhileAntiCheatActive,
                () => ConfigManager.Config.ExtraProtectedProcesses,
                addedSensors: () => ConfigManager.Config.AddedSensors);
            Sensors = service;
            Hardware = service;
            Catalog = service;
        }

        _toasts = new WindowsToastNotifier();
        Notifications = new NotificationService(_toasts, new SystemClock(), config.NotificationRefireMinutes);
        // ReadingsUpdated fires on the sensor timer thread; alerts are only ever evaluated on the UI thread.
        Sensors.ReadingsUpdated += (_, readings) => UiThread.Post(() => EvaluateNotifications(readings));

        // Detected once here; the Settings "Re-check" button asks again. Read-only: Torrid never installs a driver.
        PawnIo = new PawnIoStatusTracker(options.Demo ? new DemoPawnIoDetector() : new RegistryPawnIoDetector());

        Home = new HomeViewModel(Sensors, Hardware, ConfigManager, PawnIo);
        SensorsPage = new SensorsViewModel(Sensors, ConfigManager, Catalog, ShowThresholdsFor);
        if (options.Demo && options.OpenAddSensor)
        {
            // Screenshot aid, demo only (also enforced by StartupOptions): the panel itself never scans on its own.
            SensorsPage.AddSensor.OpenCommand.Execute(null);
            SensorsPage.AddSensor.ScanCommand.Execute(null);
        }

        ThresholdsPage = new ThresholdsViewModel(Sensors, ConfigManager, Catalog);
        SettingsPage = new SettingsPageViewModel(ConfigManager, Hardware, SendTestNotification, PawnIo, UrlLauncher.Open);
        Shell = new ShellViewModel(new[]
        {
            new NavItem(PageId.Home, "Home", "\uE80F", Home),
            new NavItem(PageId.Sensors, "Sensors", "\uE9CA", SensorsPage),
            new NavItem(PageId.Thresholds, "Thresholds", "\uE790", ThresholdsPage),
            new NavItem(PageId.Settings, "Settings", "\uE713", SettingsPage),
        });
        if (options.InitialPage is PageId initialPage) Shell.Navigate(initialPage);

        // Edits apply live: the page view models write through ConfigManager.Update, which raises Changed.
        ConfigManager.Changed += (_, _) => UiThread.Post(ApplyLiveSettings);

        Tray = new TrayIconGroupManager(Sensors, ConfigManager, Hardware);
        Tray.TrayIconClicked += (_, _) => ShowMainWindow(PageId.Home);
        // Tray "Settings" opens the Sensors page: live values plus the window / tray / alert switches.
        Tray.SettingsRequested += (_, _) => ShowMainWindow(PageId.Sensors);
        Tray.ExitRequested += (_, _) => Exit();
    }

    public ConfigManager ConfigManager { get; }

    public ISensorSource Sensors { get; }

    public IHardwareStatusSource Hardware { get; }

    /// <summary>Lists the sensors that can be added (the Sensors page's Add sensor panel). Scans run off the UI thread.</summary>
    public ISensorCatalogSource Catalog { get; }

    public NotificationService Notifications { get; }

    public PawnIoStatusTracker PawnIo { get; }

    public HomeViewModel Home { get; }

    public SensorsViewModel SensorsPage { get; }

    public ThresholdsViewModel ThresholdsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

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

    /// <summary>
    /// The Sensors page's Limits link: opens the Thresholds page with that sensor expanded, scrolled to and focused.
    /// The page takes the request when it is shown, so this works whether or not it was already on screen.
    /// </summary>
    public void ShowThresholdsFor(string sensorId)
    {
        Shell.Navigate(PageId.Thresholds);
        ThresholdsPage.Focus(sensorId);
    }

    /// <summary>Shows a "Torrid test notification" toast through the same notifier that delivers alerts.</summary>
    public void SendTestNotification() => _toasts.ShowTestNotification();

    /// <summary>Really quits (tray "Exit"): saves pending settings first, then shuts down; OnExit disposes this host.</summary>
    public void Exit()
    {
        ConfigManager.Flush();

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

        // Each teardown step is isolated so a failing sensor source or tray can never skip the config flush.
        try
        {
            try
            {
                Sensors.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Stopping the sensor source failed: {ex}");
            }

            try
            {
                (Sensors as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disposing the sensor source failed: {ex}");
            }

            try
            {
                Tray.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disposing the tray failed: {ex}");
            }
        }
        finally
        {
            ConfigManager.Dispose(); // flushes a pending debounced save
        }
    }

    private void ApplyLiveSettings()
    {
        if (_disposed) return;

        var config = ConfigManager.Config;
        if (Sensors is SensorService service && service.PollIntervalMs != config.PollIntervalMs)
        {
            service.PollIntervalMs = config.PollIntervalMs;
        }

        Notifications.RefireMinutes = config.NotificationRefireMinutes;
    }

    private void EvaluateNotifications(IReadOnlyList<SensorReading> readings)
    {
        if (_disposed) return;

        // Labels are resolved before Evaluate so alerts use the user's names; sensors with alerts off are skipped.
        // Null readings never alert (NotificationService skips them).
        var config = ConfigManager.Config;
        var notifiable = readings
            .Where(r => SensorDisplay.NotificationsEnabled(config, r))
            .Select(r => r with { Label = SensorDisplay.ResolveLabel(config, r) })
            .ToList();
        Notifications.Evaluate(notifiable, r => ThresholdResolver.For(config, r));
    }
}
