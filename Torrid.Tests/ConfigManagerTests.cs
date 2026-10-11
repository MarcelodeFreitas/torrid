using Torrid.Core.Config;

namespace Torrid.Tests;

public class ConfigManagerTests
{
    private sealed class CountingStore : IConfigStore
    {
        public bool Throw { get; set; }
        public Exception? ThrowException { get; set; }
        public bool EnumerateSensors { get; set; }
        public AppConfig Load() => new();
        public void Save(AppConfig config)
        {
            if (ThrowException is not null) throw ThrowException;
            if (Throw) throw new IOException("disk full");
            if (EnumerateSensors)
            {
                foreach (var _ in config.Sensors)
                {
                    Thread.Sleep(1);
                }
            }

            Interlocked.Increment(ref _saves);
        }

        private int _saves;
        public int Saves => Volatile.Read(ref _saves);
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

    [Fact]
    public void SaveFailure_OfAnyExceptionType_DoesNotEscapeUpdateOrFlush_AndIsReported()
    {
        var store = new CountingStore { ThrowException = new InvalidOperationException("collection modified") };
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.Zero);

        Assert.Null(Record.Exception(() => manager.Update(c => c.PollIntervalMs = 1500)));
        Assert.Contains("collection modified", manager.LastSaveError);
        Assert.Null(Record.Exception(() => manager.Flush()));
        Assert.Contains("collection modified", manager.LastSaveError);
    }

    [Fact]
    public void FailedSave_IsRetriedByLaterFlushOnceStoreWorks()
    {
        var store = new CountingStore { Throw = true };
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMinutes(5));
        manager.Update(c => c.PollIntervalMs = 1500);

        manager.Flush();
        Assert.Equal(0, store.Saves);
        Assert.NotNull(manager.LastSaveError);

        store.Throw = false;
        manager.Flush();

        Assert.Equal(1, store.Saves);
        Assert.Null(manager.LastSaveError);
    }

    [Fact]
    public void Update_AfterDispose_StillMutatesAndRaisesChanged_ButDoesNotSchedule()
    {
        var store = new CountingStore();
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMilliseconds(1));
        manager.Dispose();
        var raised = 0;
        manager.Changed += (_, _) => raised++;

        manager.Update(c => c.PollIntervalMs = 7000);
        Thread.Sleep(50);

        Assert.Equal(7000, manager.Config.PollIntervalMs);
        Assert.Equal(1, raised);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void ConcurrentUpdatesAndTimerSaves_DoNotThrowOrCorruptSerialization()
    {
        var store = new CountingStore { EnumerateSensors = true };
        var manager = new ConfigManager(store, new AppConfig(), TimeSpan.FromMilliseconds(1));

        var exception = Record.Exception(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                var id = $"gpu{i}-core";
                manager.Update(c => c.Sensors[id] = new SensorPreference { Notify = false });
            }
        });
        manager.Flush();

        Assert.Null(exception);
        Assert.Null(manager.LastSaveError);
        Assert.Equal(200, manager.Config.Sensors.Count);
    }
}
