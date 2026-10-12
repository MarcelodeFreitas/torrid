using Torrid.App.Drivers;
using Torrid.App.ViewModels;
using Torrid.Core.Config;
using Torrid.Core.Drivers;
using Torrid.Core.Sensors;

namespace Torrid.Tests;

public class SettingsPageViewModelTests
{
    private static (SettingsPageViewModel Vm, ConfigManager Config, FakeSensorSourceForTests Hardware) Make(
        AppConfig? config = null, Action? sendTestNotification = null)
    {
        var hardware = new FakeSensorSourceForTests();
        var manager = new ConfigManager(new InMemoryConfigStore(), config ?? new AppConfig(), TimeSpan.Zero);
        var pawnIo = new PawnIoStatusTracker(new FakePawnIoDetector());
        return (new SettingsPageViewModel(manager, hardware, sendTestNotification ?? (() => { }), pawnIo, _ => { }), manager, hardware);
    }

    private static (SettingsPageViewModel Vm, FakePawnIoDetector Detector, List<string> Opened) MakePawnIo(PawnIoStatus status)
    {
        var hardware = new FakeSensorSourceForTests();
        var manager = new ConfigManager(new InMemoryConfigStore(), new AppConfig(), TimeSpan.Zero);
        var detector = new FakePawnIoDetector(status);
        var opened = new List<string>();
        var vm = new SettingsPageViewModel(manager, hardware, () => { }, new PawnIoStatusTracker(detector), opened.Add);
        return (vm, detector, opened);
    }

    [Fact]
    public void SendTestNotificationCommand_CallsTheActionOncePerExecute()
    {
        var calls = 0;
        var (vm, config, _) = Make(sendTestNotification: () => calls++);

        Assert.Equal(0, calls);
        Assert.True(vm.SendTestNotificationCommand.CanExecute(null));

        vm.SendTestNotificationCommand.Execute(null);

        Assert.Equal(1, calls);
        Assert.Empty(config.Config.Sensors);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(120000, 60000)]
    [InlineData(4000, 4000)]
    public void PollInterval_IsClampedAndWritten(int requested, int expected)
    {
        var (vm, config, _) = Make();

        vm.PollIntervalMs = requested;

        Assert.Equal(expected, vm.PollIntervalMs);
        Assert.Equal(expected, config.Config.PollIntervalMs);
    }

    [Fact]
    public void PollIntervalText_ShowsSeconds()
    {
        var (vm, _, _) = Make();

        vm.PollIntervalMs = 2500;

        Assert.Equal("2.5 s", vm.PollIntervalText);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(5000, 1440)]
    [InlineData(15, 15)]
    public void Refire_IsClampedAndWritten(int requested, int expected)
    {
        var (vm, config, _) = Make();

        vm.RefireMinutes = requested;

        Assert.Equal(expected, vm.RefireMinutes);
        Assert.Equal(expected, config.Config.NotificationRefireMinutes);
    }

    [Fact]
    public void StartMinimizedAndAntiCheatBehavior_WriteThrough()
    {
        var (vm, config, _) = Make();

        vm.StartMinimizedToTray = false;
        vm.WhileAntiCheatActive = AntiCheatBehavior.GpuOnly;

        Assert.False(config.Config.StartMinimizedToTray);
        Assert.Equal(AntiCheatBehavior.GpuOnly, config.Config.WhileAntiCheatActive);
    }

    [Fact]
    public void AntiCheatChoices_CoverEveryBehavior()
    {
        var (vm, _, _) = Make();

        Assert.Equal(new[] { AntiCheatBehavior.Pause, AntiCheatBehavior.GpuOnly, AntiCheatBehavior.Continue },
            vm.AntiCheatChoices.Select(c => c.Value));
        Assert.Equal("Pause monitoring (recommended)", vm.AntiCheatChoices[0].Title);
        Assert.All(vm.AntiCheatChoices, c => Assert.False(string.IsNullOrWhiteSpace(c.Description)));
    }

    [Fact]
    public void AddProcess_TrimsStripsExeDeduplicatesAndReplacesTheList()
    {
        var (vm, config, _) = Make();
        var before = config.Config.ExtraProtectedProcesses;

        vm.NewProcessName = "  MyGame.exe ";
        vm.AddProcessCommand.Execute(null);

        var after = config.Config.ExtraProtectedProcesses;
        Assert.False(ReferenceEquals(before, after));
        Assert.Equal(new[] { "MyGame" }, after);
        Assert.Equal(new[] { "MyGame" }, vm.ExtraProcesses);
        Assert.Equal("", vm.NewProcessName);

        vm.NewProcessName = "mygame";
        vm.AddProcessCommand.Execute(null);
        vm.NewProcessName = "MYGAME.EXE";
        vm.AddProcessCommand.Execute(null);
        vm.NewProcessName = "   ";
        vm.AddProcessCommand.Execute(null);
        vm.NewProcessName = ".exe";
        vm.AddProcessCommand.Execute(null);

        Assert.Equal(new[] { "MyGame" }, config.Config.ExtraProtectedProcesses);
        Assert.Equal(new[] { "MyGame" }, vm.ExtraProcesses);
    }

    [Fact]
    public void AddProcess_CannotExecuteWhenBlank()
    {
        var (vm, _, _) = Make();

        vm.NewProcessName = " ";
        Assert.False(vm.AddProcessCommand.CanExecute(null));

        vm.NewProcessName = "game";
        Assert.True(vm.AddProcessCommand.CanExecute(null));
    }

    [Fact]
    public void RemoveProcess_ReplacesTheList()
    {
        var config = new AppConfig { ExtraProtectedProcesses = new List<string> { "alpha", "beta" } };
        var (vm, manager, _) = Make(config);
        var before = manager.Config.ExtraProtectedProcesses;

        Assert.Equal(new[] { "alpha", "beta" }, vm.ExtraProcesses);
        vm.RemoveProcessCommand.Execute("alpha");

        Assert.False(ReferenceEquals(before, manager.Config.ExtraProtectedProcesses));
        Assert.Equal(new[] { "beta" }, manager.Config.ExtraProtectedProcesses);
        Assert.Equal(new[] { "beta" }, vm.ExtraProcesses);
        Assert.Equal(new[] { "alpha", "beta" }, before);
    }

    [Fact]
    public void HardwareStatusText_ReflectsStatus_AndUpdatesOnStatusChanged()
    {
        var (vm, _, hardware) = Make();
        var vanguard = new AntiCheatMatch("Riot Vanguard", "vgc");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("Monitoring normally.", vm.HardwareStatusText);

        hardware.SetStatus(new HardwareStatus(HardwareAccessLevel.Paused, vanguard));
        Assert.Equal("Paused — Riot Vanguard is running (vgc).", vm.HardwareStatusText);
        Assert.Contains(nameof(SettingsPageViewModel.HardwareStatusText), raised);

        hardware.SetStatus(new HardwareStatus(HardwareAccessLevel.GpuOnly, vanguard));
        Assert.Equal("GPU only — Riot Vanguard is running (vgc).", vm.HardwareStatusText);

        hardware.SetStatus(HardwareStatus.FullAccess);
        Assert.Equal("Monitoring normally.", vm.HardwareStatusText);
    }

    [Fact]
    public void HardwareStatusText_ContinueWithAntiCheat_SaysSo()
    {
        var (vm, _, hardware) = Make();

        hardware.SetStatus(new HardwareStatus(HardwareAccessLevel.Full, new AntiCheatMatch("BattlEye", "BEService")));

        Assert.Contains("BattlEye", vm.HardwareStatusText);
        Assert.True(vm.IsAntiCheatDetected);
    }

    [Fact]
    public void KnownProductsText_ListsKnownProducts()
    {
        var (vm, _, _) = Make();

        Assert.StartsWith("Detects: ", vm.KnownProductsText);
        Assert.Contains("Riot Vanguard", vm.KnownProductsText);
    }

    [Fact]
    public void ValuesAreSeededFromConfig()
    {
        var config = new AppConfig { PollIntervalMs = 4000, NotificationRefireMinutes = 10, StartMinimizedToTray = false };

        var (vm, _, _) = Make(config);

        Assert.Equal(4000, vm.PollIntervalMs);
        Assert.Equal("4.0 s", vm.PollIntervalText);
        Assert.Equal(10, vm.RefireMinutes);
        Assert.False(vm.StartMinimizedToTray);
    }

    // --- PawnIO driver card ---

    [Theory]
    [InlineData(PawnIoState.Current, "2.2.0", "PawnIO 2.2.0 is installed.", false)]
    [InlineData(PawnIoState.Outdated, "2.0.1.0",
        "PawnIO 2.0.1.0 is installed, but 2.1.0 or newer is recommended (older versions are blocked by some anti-cheats). Latest: 2.2.0.", true)]
    [InlineData(PawnIoState.NotInstalled, null,
        "PawnIO isn't installed, so CPU temperature is unavailable. GPU temperatures still work.", true)]
    [InlineData(PawnIoState.Unknown, null, "Couldn't check the PawnIO version.", true)]
    public void PawnIoCard_ShowsTextAndDownloadButton_PerState(PawnIoState state, string? version, string text, bool showDownload)
    {
        var (vm, _, _) = MakePawnIo(new PawnIoStatus(state, version));

        Assert.Equal(state, vm.PawnIoState);
        Assert.Equal(text, vm.PawnIoStatusText);
        Assert.Equal(showDownload, vm.ShowPawnIoDownloadButton);
    }

    [Fact]
    public void Recheck_AsksTheDetectorAgain_AndUpdatesTheCard()
    {
        var (vm, detector, _) = MakePawnIo(new PawnIoStatus(PawnIoState.NotInstalled, null));
        Assert.Equal(1, detector.Calls); // once at startup
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        detector.Status = new PawnIoStatus(PawnIoState.Current, "2.2.0");
        Assert.True(vm.RecheckPawnIoCommand.CanExecute(null));
        vm.RecheckPawnIoCommand.Execute(null);

        Assert.Equal(2, detector.Calls);
        Assert.Equal(PawnIoState.Current, vm.PawnIoState);
        Assert.Equal("PawnIO 2.2.0 is installed.", vm.PawnIoStatusText);
        Assert.False(vm.ShowPawnIoDownloadButton);
        Assert.Contains(nameof(SettingsPageViewModel.PawnIoState), raised);
        Assert.Contains(nameof(SettingsPageViewModel.PawnIoStatusText), raised);
        Assert.Contains(nameof(SettingsPageViewModel.ShowPawnIoDownloadButton), raised);
    }

    [Fact]
    public void Recheck_CanMoveFromCurrentToNotInstalled()
    {
        var (vm, detector, _) = MakePawnIo(new PawnIoStatus(PawnIoState.Current, "2.2.0"));

        detector.Status = new PawnIoStatus(PawnIoState.NotInstalled, null);
        vm.RecheckPawnIoCommand.Execute(null);

        Assert.Equal(PawnIoState.NotInstalled, vm.PawnIoState);
        Assert.True(vm.ShowPawnIoDownloadButton);
    }

    [Fact]
    public void OpenDownloadCommand_CallsOpenUrlWithTheExactReleasePage()
    {
        var (vm, _, opened) = MakePawnIo(new PawnIoStatus(PawnIoState.NotInstalled, null));

        Assert.True(vm.OpenPawnIoDownloadCommand.CanExecute(null));
        vm.OpenPawnIoDownloadCommand.Execute(null);

        Assert.Equal(new[] { "https://github.com/namazso/PawnIO.Setup/releases/latest" }, opened);
        Assert.Equal("https://github.com/namazso/PawnIO.Setup/releases/latest", SettingsPageViewModel.PawnIoDownloadUrl);
    }

    [Fact]
    public void Detector_ThatThrows_BecomesUnknown_NotACrash()
    {
        var hardware = new FakeSensorSourceForTests();
        var manager = new ConfigManager(new InMemoryConfigStore(), new AppConfig(), TimeSpan.Zero);
        var tracker = new PawnIoStatusTracker(new ThrowingPawnIoDetector());

        var vm = new SettingsPageViewModel(manager, hardware, () => { }, tracker, _ => { });

        Assert.Equal(PawnIoState.Unknown, vm.PawnIoState);
        Assert.Equal("Couldn't check the PawnIO version.", vm.PawnIoStatusText);
    }

    private sealed class ThrowingPawnIoDetector : IPawnIoDetector
    {
        public PawnIoStatus Detect() => throw new InvalidOperationException("boom");
    }
}
