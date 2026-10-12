namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Torrid.App.Drivers;
using Torrid.Core.Config;
using Torrid.Core.Drivers;
using Torrid.Core.Sensors;

public sealed record AntiCheatChoice(AntiCheatBehavior Value, string Title, string Description);

/// <summary>
/// The Settings page. Every setter writes through <see cref="ConfigManager.Update"/> (no Save button); values are read
/// back from the config so they always show what is actually stored. App-lifetime singleton held by <see cref="AppHost"/>,
/// so its event subscriptions are never removed.
/// </summary>
public sealed class SettingsPageViewModel : ObservableObject
{
    public const int MinPollIntervalMs = 500;
    public const int MaxPollIntervalMs = 60000;
    public const int MaxRefireMinutes = 1440;

    private readonly ConfigManager _config;
    private readonly IHardwareStatusSource _hardware;
    private readonly PawnIoStatusTracker _pawnIo;
    private readonly RelayCommand _addProcessCommand;
    private string _newProcessName = "";
    private string _hardwareStatusText = "";
    private bool _isAntiCheatDetected;

    public SettingsPageViewModel(ConfigManager config, IHardwareStatusSource hardware, Action sendTestNotification,
        PawnIoStatusTracker pawnIo, Action<string> openUrl)
    {
        _config = config;
        _hardware = hardware;
        _pawnIo = pawnIo;
        SendTestNotificationCommand = new RelayCommand(_ => sendTestNotification());
        OpenPawnIoDownloadCommand = new RelayCommand(_ => openUrl(PawnIoDownloadUrl));
        RecheckPawnIoCommand = new RelayCommand(_ => _pawnIo.Refresh());
        _pawnIo.Changed += (_, _) => UiThread.Post(OnPawnIoChanged);

        AntiCheatChoices = new[]
        {
            new AntiCheatChoice(AntiCheatBehavior.Pause, "Pause monitoring (recommended)",
                "Stop reading all sensors until the game closes. Nothing touches the hardware."),
            new AntiCheatChoice(AntiCheatBehavior.GpuOnly, "GPU only — no PawnIO driver",
                "Keep GPU and storage temperatures; CPU, motherboard and memory temperatures (the parts that need the PawnIO driver) pause."),
            new AntiCheatChoice(AntiCheatBehavior.Continue, "Keep monitoring",
                "Ignore the anti-cheat. Some games may refuse to start or kick you."),
        };

        KnownProductsText = "Detects: " + string.Join(", ", AntiCheatDetector.KnownProducts) + ".";

        _addProcessCommand = new RelayCommand(_ => AddProcess(), _ => !string.IsNullOrWhiteSpace(Normalize(NewProcessName)));
        RemoveProcessCommand = new RelayCommand(p => RemoveProcess(p as string));

        foreach (var name in _config.Config.ExtraProtectedProcesses) ExtraProcesses.Add(name);
        UpdateHardwareStatus();

        _hardware.StatusChanged += (_, _) => UiThread.Post(UpdateHardwareStatus);
        _config.Changed += (_, _) => UiThread.Post(OnConfigChanged);
    }

    public int PollIntervalMs
    {
        get => _config.Config.PollIntervalMs;
        set
        {
            var clamped = Math.Clamp(value, MinPollIntervalMs, MaxPollIntervalMs);
            if (clamped != _config.Config.PollIntervalMs) _config.Update(c => c.PollIntervalMs = clamped);
            Raise(nameof(PollIntervalMs));
            Raise(nameof(PollIntervalText));
        }
    }

    /// <summary>The poll interval in seconds, e.g. "2.0 s".</summary>
    public string PollIntervalText =>
        (PollIntervalMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";

    public int RefireMinutes
    {
        get => _config.Config.NotificationRefireMinutes;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxRefireMinutes);
            if (clamped != _config.Config.NotificationRefireMinutes) _config.Update(c => c.NotificationRefireMinutes = clamped);
            Raise(nameof(RefireMinutes));
        }
    }

    public bool StartMinimizedToTray
    {
        get => _config.Config.StartMinimizedToTray;
        set
        {
            if (value != _config.Config.StartMinimizedToTray) _config.Update(c => c.StartMinimizedToTray = value);
            Raise(nameof(StartMinimizedToTray));
        }
    }

    public AntiCheatBehavior WhileAntiCheatActive
    {
        get => _config.Config.WhileAntiCheatActive;
        set
        {
            if (value != _config.Config.WhileAntiCheatActive) _config.Update(c => c.WhileAntiCheatActive = value);
            Raise(nameof(WhileAntiCheatActive));
        }
    }

    public IReadOnlyList<AntiCheatChoice> AntiCheatChoices { get; }

    public ObservableCollection<string> ExtraProcesses { get; } = new();

    public string NewProcessName
    {
        get => _newProcessName;
        set
        {
            if (Set(ref _newProcessName, value ?? "")) _addProcessCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand AddProcessCommand => _addProcessCommand;

    /// <summary>Parameter: the process name to remove.</summary>
    public ICommand RemoveProcessCommand { get; }

    public string HardwareStatusText { get => _hardwareStatusText; private set => Set(ref _hardwareStatusText, value); }

    /// <summary>True while an anti-cheat is detected (in any mode), so the status line can be highlighted.</summary>
    public bool IsAntiCheatDetected { get => _isAntiCheatDetected; private set => Set(ref _isAntiCheatDetected, value); }

    public string KnownProductsText { get; }

    public const string PawnIoDownloadUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest";

    public PawnIoState PawnIoState => _pawnIo.Status.State;

    /// <summary>One sentence describing the PawnIO driver, shown next to the status dot.</summary>
    public string PawnIoStatusText => DescribePawnIo(_pawnIo.Status);

    /// <summary>The download page is offered whenever the driver is missing, old, or could not be checked.</summary>
    public bool ShowPawnIoDownloadButton => _pawnIo.Status.State != PawnIoState.Current;

    /// <summary>Opens the official PawnIO release page in the browser. Torrid never downloads or runs the installer.</summary>
    public ICommand OpenPawnIoDownloadCommand { get; }

    /// <summary>Looks for the driver again, e.g. after the user installed it.</summary>
    public ICommand RecheckPawnIoCommand { get; }

    public static string DescribePawnIo(PawnIoStatus status) => status.State switch
    {
        PawnIoState.Current => $"PawnIO {status.Version} is installed.",
        PawnIoState.Outdated =>
            $"PawnIO {status.Version} is installed, but {PawnIoStatusEvaluator.MinimumRecommendedVersion} or newer is recommended " +
            $"(older versions are blocked by some anti-cheats). Latest: {PawnIoStatusEvaluator.LatestKnownVersion}.",
        PawnIoState.NotInstalled => "PawnIO isn't installed, so CPU temperature is unavailable. GPU temperatures still work.",
        _ => "Couldn't check the PawnIO version.",
    };

    /// <summary>Shows a test toast so the user can check that alerts reach them.</summary>
    public ICommand SendTestNotificationCommand { get; }

    public static string DescribeStatus(HardwareStatus status)
    {
        if (status.AntiCheat is not { } match) return "Monitoring normally.";

        var running = match.Product == "Custom"
            ? $"{match.DisplayName} is running"
            : $"{match.DisplayName} is running ({match.ProcessName})";

        return status.Level switch
        {
            HardwareAccessLevel.Paused => $"Paused — {running}.",
            HardwareAccessLevel.GpuOnly => $"GPU only — {running}.",
            _ => $"Monitoring normally — {running}.",
        };
    }

    private void UpdateHardwareStatus()
    {
        var status = _hardware.Status;
        HardwareStatusText = DescribeStatus(status);
        IsAntiCheatDetected = status.AntiCheat is not null;
    }

    private void OnPawnIoChanged()
    {
        Raise(nameof(PawnIoState));
        Raise(nameof(PawnIoStatusText));
        Raise(nameof(ShowPawnIoDownloadButton));
    }

    private void OnConfigChanged()
    {
        Raise(nameof(PollIntervalMs));
        Raise(nameof(PollIntervalText));
        Raise(nameof(RefireMinutes));
        Raise(nameof(StartMinimizedToTray));
        Raise(nameof(WhileAntiCheatActive));
        CollectionSync.Sync(ExtraProcesses, _config.Config.ExtraProtectedProcesses.Distinct().ToList());
    }

    private void AddProcess()
    {
        var name = Normalize(NewProcessName);
        if (name.Length == 0) return;

        NewProcessName = "";
        var current = _config.Config.ExtraProtectedProcesses;
        if (current.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        ReplaceProcesses(current.Append(name).ToList());
    }

    private void RemoveProcess(string? name)
    {
        if (name is null) return;
        var current = _config.Config.ExtraProtectedProcesses;
        if (!current.Contains(name, StringComparer.OrdinalIgnoreCase)) return;

        ReplaceProcesses(current.Where(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    /// <summary>The sensor thread reads this list, so it is always replaced wholesale, never mutated in place.</summary>
    private void ReplaceProcesses(List<string> replacement)
    {
        _config.Update(c => c.ExtraProtectedProcesses = replacement);
        CollectionSync.Sync(ExtraProcesses, replacement.Distinct().ToList());
    }

    private static string Normalize(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4].TrimEnd();
        return trimmed;
    }
}
