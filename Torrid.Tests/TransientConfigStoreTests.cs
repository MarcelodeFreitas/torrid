using Torrid.Core.Config;

namespace Torrid.Tests;

public class TransientConfigStoreTests
{
    [Fact]
    public void Load_ReturnsFreshDefaults()
    {
        var store = new TransientConfigStore();

        var config = store.Load();

        Assert.Equal(2000, config.PollIntervalMs);
        Assert.NotEmpty(config.Thresholds);
        Assert.Empty(config.Sensors);
    }

    [Fact]
    public void Save_PersistsNothing()
    {
        var store = new TransientConfigStore();
        var config = new AppConfig { PollIntervalMs = 9000 };
        config.Sensors["cpu"] = new SensorPreference { Label = "demo" };

        store.Save(config);

        var reloaded = store.Load();
        Assert.Equal(2000, reloaded.PollIntervalMs);
        Assert.Empty(reloaded.Sensors);
        Assert.NotSame(config, reloaded);
    }

    [Fact]
    public void ConfigManager_OverTransientStore_UpdatesInMemoryAndFlushesWithoutError()
    {
        using var manager = new ConfigManager(new TransientConfigStore(), saveDelay: TimeSpan.Zero);

        manager.Update(c => c.NotificationRefireMinutes = 7);
        manager.Flush();

        Assert.Equal(7, manager.Config.NotificationRefireMinutes);
        Assert.Null(manager.LastSaveError);
    }
}
