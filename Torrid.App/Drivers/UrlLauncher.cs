namespace Torrid.App.Drivers;

using System.Diagnostics;

public static class UrlLauncher
{
    /// <summary>Only absolute https URLs are ever handed to the shell.</summary>
    public static bool IsOpenable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Opens <paramref name="url"/> in the default browser. Only absolute https URLs are opened; failures are logged, not thrown.</summary>
    public static void Open(string url)
    {
        if (!IsOpenable(url))
        {
            Debug.WriteLine($"Refusing to open non-https URL: {url}");
            return;
        }

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
