namespace Torrid.App.Drivers;

using System.Diagnostics;
using Microsoft.Win32;
using Torrid.Core.Drivers;

/// <summary>
/// Looks for the PawnIO uninstall entry (written by its official installer) in both the 64-bit and the 32-bit view of
/// HKLM and reads its DisplayVersion. Only reads the registry; it never touches the driver itself.
/// </summary>
public sealed class RegistryPawnIoDetector : IPawnIoDetector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Wow6432UninstallPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string ProductName = "PawnIO";

    public PawnIoStatus Detect()
    {
        try
        {
            // Explicit 64-bit view so the WOW6432Node path below is a plain key even if this process were 32-bit.
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var path in new[] { UninstallPath, Wow6432UninstallPath })
            {
                if (!TryFindEntry(hklm, path, out var version)) continue;

                // An entry with no readable version is an installed driver of unknown age, not a missing one.
                return string.IsNullOrWhiteSpace(version)
                    ? PawnIoStatusEvaluator.Evaluate(null, detectionFailed: true)
                    : PawnIoStatusEvaluator.Evaluate(version, detectionFailed: false);
            }

            return PawnIoStatusEvaluator.Evaluate(null, detectionFailed: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PawnIO detection failed: {ex}");
            return PawnIoStatusEvaluator.Evaluate(null, detectionFailed: true);
        }
    }

    /// <summary>The PawnIO entry under <paramref name="uninstallPath"/> (matched by DisplayName or key name), or false if there is none.</summary>
    private static bool TryFindEntry(RegistryKey hklm, string uninstallPath, out string? version)
    {
        version = null;
        using var uninstall = hklm.OpenSubKey(uninstallPath);
        if (uninstall is null) return false;

        foreach (var name in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(name);
            if (entry is null) continue;

            var isPawnIo = string.Equals(entry.GetValue("DisplayName") as string, ProductName, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(name, ProductName, StringComparison.OrdinalIgnoreCase);
            if (!isPawnIo) continue;

            version = (entry.GetValue("DisplayVersion") as string)?.Trim();
            return true;
        }

        return false;
    }
}
