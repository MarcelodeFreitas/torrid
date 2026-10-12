using Torrid.App.Notifications;

namespace Torrid.Tests;

/// <summary>Uses a throwaway folder, never the real Start Menu.</summary>
public class StartMenuShortcutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"torrid-lnk-{Guid.NewGuid():N}");
    private readonly string _target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private readonly string _otherTarget = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private string LinkPath => Path.Combine(_dir, "Torrid (Test).lnk");

    [Fact]
    public void Ensure_CreatesShortcutCarryingTargetAndAppUserModelId()
    {
        var result = StartMenuShortcut.Ensure(LinkPath, _target, "Torrid.Tests.Aumid");

        Assert.Equal(StartMenuShortcut.Outcome.Created, result);
        Assert.True(File.Exists(LinkPath));
        Assert.True(StartMenuShortcut.TryRead(LinkPath, out var target, out var aumid));
        Assert.Equal(_target, target, ignoreCase: true);
        Assert.Equal("Torrid.Tests.Aumid", aumid);
    }

    [Fact]
    public void Ensure_LeavesAMatchingShortcutAlone()
    {
        StartMenuShortcut.Ensure(LinkPath, _target, "Torrid.Tests.Aumid");
        var written = File.GetLastWriteTimeUtc(LinkPath);

        var result = StartMenuShortcut.Ensure(LinkPath, _target, "Torrid.Tests.Aumid");

        Assert.Equal(StartMenuShortcut.Outcome.Unchanged, result);
        Assert.Equal(written, File.GetLastWriteTimeUtc(LinkPath));
    }

    [Fact]
    public void Ensure_RecreatesWhenTargetOrIdDiffer()
    {
        StartMenuShortcut.Ensure(LinkPath, _target, "Torrid.Tests.Aumid");

        Assert.Equal(StartMenuShortcut.Outcome.Recreated, StartMenuShortcut.Ensure(LinkPath, _otherTarget, "Torrid.Tests.Aumid"));
        Assert.True(StartMenuShortcut.TryRead(LinkPath, out var target, out _));
        Assert.Equal(_otherTarget, target, ignoreCase: true);

        Assert.Equal(StartMenuShortcut.Outcome.Recreated, StartMenuShortcut.Ensure(LinkPath, _otherTarget, "Torrid.Tests.Other"));
        Assert.True(StartMenuShortcut.TryRead(LinkPath, out _, out var aumid));
        Assert.Equal("Torrid.Tests.Other", aumid);
    }

    [Fact]
    public void Ensure_NeverThrows_ReportsFailure()
    {
        // A path inside a file cannot be created as a directory.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "file.txt");
        File.WriteAllText(blocker, "x");

        var result = StartMenuShortcut.Ensure(Path.Combine(blocker, "x.lnk"), _target, "Torrid.Tests.Aumid");

        Assert.Equal(StartMenuShortcut.Outcome.Failed, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
