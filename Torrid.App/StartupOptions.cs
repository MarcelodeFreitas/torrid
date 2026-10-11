namespace Torrid.App;

using Torrid.App.ViewModels;

/// <summary>
/// Command-line switches: <c>--show</c>, <c>--demo</c>, <c>--demo-paused</c> (implies <c>--demo</c>), <c>--config &lt;path&gt;</c>,
/// and the undocumented <c>--page &lt;Home|Sensors|Thresholds|Settings&gt;</c> (initial page, used for screenshots).
/// </summary>
public sealed record StartupOptions(bool ShowWindow, bool Demo, bool DemoPaused, string? ConfigPath)
{
    /// <summary>The page to open first; null keeps the default (Home).</summary>
    public PageId? InitialPage { get; init; }

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var show = false;
        var demo = false;
        var demoPaused = false;
        string? configPath = null;
        PageId? page = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--show":
                    show = true;
                    break;
                case "--demo":
                    demo = true;
                    break;
                case "--demo-paused":
                    demo = true;
                    demoPaused = true;
                    break;
                case "--config" when i + 1 < args.Count:
                    configPath = args[++i];
                    break;
                case "--page" when i + 1 < args.Count:
                    page = ParsePage(args[++i]) ?? page;
                    break;
            }
        }

        return new StartupOptions(show, demo, demoPaused, configPath) { InitialPage = page };
    }

    // Names only: Enum.TryParse alone would also accept numbers such as "2".
    private static PageId? ParsePage(string value) =>
        Enum.GetNames<PageId>().FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<PageId>(name)
            : null;
}
