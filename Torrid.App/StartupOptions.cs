namespace Torrid.App;

/// <summary>Command-line switches: <c>--show</c>, <c>--demo</c>, <c>--demo-paused</c> (implies <c>--demo</c>), <c>--config &lt;path&gt;</c>.</summary>
public sealed record StartupOptions(bool ShowWindow, bool Demo, bool DemoPaused, string? ConfigPath)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        var show = false;
        var demo = false;
        var demoPaused = false;
        string? configPath = null;

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
            }
        }

        return new StartupOptions(show, demo, demoPaused, configPath);
    }
}
