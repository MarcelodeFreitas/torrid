namespace Torrid.App;

/// <summary>
/// The Windows identity Torrid's toasts are delivered under, and its Windows startup task. Debug builds use their own AppUserModelID,
/// Start Menu shortcut and startup task so a dev run never re-points the installed app's shortcut or task at a bin\Debug exe.
/// </summary>
public static class AppIdentity
{
#if DEBUG
    public const string Aumid = "Torrid.App.Dev";
    public const string ShortcutName = "Torrid (Dev).lnk";
    public const string StartupTaskName = "Torrid (Dev)";
#else
    public const string Aumid = "Torrid.App";
    public const string ShortcutName = "Torrid.lnk";
    public const string StartupTaskName = "Torrid";
#endif
}
