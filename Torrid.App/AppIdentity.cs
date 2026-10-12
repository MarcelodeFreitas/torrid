namespace Torrid.App;

/// <summary>
/// The Windows identity Torrid's toasts are delivered under. Debug builds use their own AppUserModelID and Start Menu
/// shortcut so a dev run never re-points the installed app's shortcut at a bin\Debug exe.
/// </summary>
public static class AppIdentity
{
#if DEBUG
    public const string Aumid = "Torrid.App.Dev";
    public const string ShortcutName = "Torrid (Dev).lnk";
#else
    public const string Aumid = "Torrid.App";
    public const string ShortcutName = "Torrid.lnk";
#endif
}
