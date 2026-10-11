namespace Torrid.Core.Config;

public sealed class ConfigManager : IDisposable
{
    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IConfigStore _store;
    private readonly TimeSpan _saveDelay;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _dirty;

    public ConfigManager(IConfigStore store, AppConfig? config = null, TimeSpan? saveDelay = null)
    {
        _store = store;
        Config = config ?? store.Load();
        _saveDelay = saveDelay ?? DefaultSaveDelay;
    }

    public AppConfig Config { get; }

    public string? LastSaveError { get; private set; }

    public event EventHandler? Changed;

    public void Update(Action<AppConfig> change)
    {
        change(Config);
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    public void Flush()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (!_dirty) return;
            SaveLocked();
        }
    }

    public void Dispose() => Flush();

    private void ScheduleSave()
    {
        lock (_gate)
        {
            _dirty = true;
            if (_saveDelay <= TimeSpan.Zero)
            {
                SaveLocked();
                return;
            }

            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Flush(), null, _saveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveLocked()
    {
        try
        {
            _store.Save(Config);
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex.Message;
        }
        finally
        {
            _dirty = false;
        }
    }
}
