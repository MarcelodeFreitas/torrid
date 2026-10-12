namespace Torrid.Core.Config;

/// <summary>
/// A store that never touches disk: <see cref="Load"/> returns fresh defaults and <see cref="Save"/> discards the config.
/// Used by <c>--demo</c> runs without <c>--config</c>, so demos can never overwrite the user's real settings.
/// </summary>
public sealed class TransientConfigStore : IConfigStore
{
    public AppConfig Load() => JsonConfigStore.Normalize(new AppConfig());

    public void Save(AppConfig config)
    {
    }
}
