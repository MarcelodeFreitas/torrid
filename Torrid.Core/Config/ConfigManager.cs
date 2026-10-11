namespace Torrid.Core.Config;

public sealed class ConfigManager : IDisposable
{
    private static readonly TimeSpan DefaultSaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly IConfigStore _store;
    private readonly TimeSpan _saveDelay;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _dirty;
    private bool _disposed;
    private volatile string? _lastSaveError;

    public ConfigManager(IConfigStore store, AppConfig? config = null, TimeSpan? saveDelay = null)
    {
        _store = store;
        Config = config ?? store.Load();
        _saveDelay = saveDelay ?? DefaultSaveDelay;
    }

    public AppConfig Config { get; }

    public string? LastSaveError => _lastSaveError;

    public event EventHandler? Changed;

    public void Update(Action<AppConfig> change)
    {
        lock (_gate)
        {
            change(Config);
            ScheduleSaveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
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

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Flush();
    }

    private void ScheduleSaveLocked()
    {
        _dirty = true;
        if (_disposed) return;

        if (_saveDelay <= TimeSpan.Zero)
        {
            SaveLocked();
            return;
        }

        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => Flush(), null, _saveDelay, Timeout.InfiniteTimeSpan);
    }

    private void SaveLocked()
    {
        try
        {
            _store.Save(Config);
            _lastSaveError = null;
            _dirty = false;
        }
        catch (Exception ex)
        {
            // Never let a save failure escape (this can run on a timer pool thread).
            // _dirty stays true so the next Flush/Update retries.
            _lastSaveError = ex.Message;
        }
    }
}
