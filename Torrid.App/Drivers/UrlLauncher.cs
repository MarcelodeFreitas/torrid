namespace Torrid.App.Drivers;

using System.Diagnostics;

public static class UrlLauncher
{
    /// <summary>Opens <paramref name="url"/> in the default browser. Only ever opens a page; failures are logged, not thrown.</summary>
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Opening {url} failed: {ex}");
        }
    }
}
