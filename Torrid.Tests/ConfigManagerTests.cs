using Torrid.Core.Config;

namespace Torrid.Tests;

public class ConfigManagerTests
{
    private sealed class CountingStore : IConfigStore
    {
        public int Saves { get; private set; }
        public bool Throw { get; set; }
        public AppConfig Load() => new();
        public void Save(AppConfig config)
        {
            if (Throw) throw new IOException("disk full");
            Saves++;
        }
    }

    [Fact]
    public void Update_MutatesConfigRaisesChangedAndSavesImmediatelyWhenDelayIsZero()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.Zero);
        var raised = 0;
        manager.Changed += (_, _) => raised++;

        manager.Update(c => c.PollIntervalMs = 4000);

        Assert.Equal(4000, manager.Config.PollIntervalMs);
        Assert.Equal(1, raised);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Update_WithDelay_DefersSaveUntilFlush_AndCoalescesManyUpdates()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));

        manager.Update(c => c.PollIntervalMs = 1000);
        manager.Update(c => c.PollIntervalMs = 2000);
        manager.Update(c => c.PollIntervalMs = 3000);
        Assert.Equal(0, store.Saves);

        manager.Flush();

        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public void Flush_WithNothingPending_DoesNotSave()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));

        manager.Flush();

        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void SaveFailure_DoesNotThrow_AndIsReported()
    {
        var store = new CountingStore { Throw = true };
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.Zero);

        var exception = Record.Exception(() => manager.Update(c => c.PollIntervalMs = 1500));

        Assert.Null(exception);
        Assert.Contains("disk full", manager.LastSaveError);
    }

    [Fact]
    public void Dispose_FlushesPendingSave()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));
        manager.Update(c => c.StartMinimizedToTray = false);

        manager.Dispose();

        Assert.Equal(1, store.Saves);
    }
}
