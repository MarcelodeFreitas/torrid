namespace Torrid.App.Startup;

using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

/// <summary>
/// Start with Windows as a Task Scheduler logon task. Highest run level, so no UAC prompt at sign-in (an HKCU Run entry would
/// prompt, because the exe requires administrator). Shells out to schtasks.exe.
/// </summary>
public sealed class TaskSchedulerStartup : IStartupRegistration
{
    private const int TimeoutMs = 10000;

    /// <summary>Task Scheduler 1.2 XML. XML rather than schtasks flags: the flags stop the task after 72 hours and skip battery power.</summary>
    public static string BuildTaskXml(string exePath, string userId)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "Triggers",
                new XElement(ns + "LogonTrigger",
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "UserId", userId))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", userId),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", exePath),
                    new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(exePath) ?? ""))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + task;
    }

    public bool IsEnabled()
    {
        try
        {
            return Run($"/Query /TN \"{AppIdentity.StartupTaskName}\"").ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TaskSchedulerStartup: query failed: {ex}");
            return false;
        }
    }

    public string? SetEnabled(bool enable)
    {
        try
        {
            var name = AppIdentity.StartupTaskName;
            if (!enable)
            {
                if (!IsEnabled()) return null;
                return Outcome(Run($"/Delete /TN \"{name}\" /F"));
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return "Couldn't find Torrid's own path.";

            // shortcut: path is captured when the switch is turned on; moving the exe needs the switch toggled off and on.
            var xmlPath = Path.Combine(Path.GetTempPath(), $"torrid-startup-{Guid.NewGuid():N}.xml");
            try
            {
                var user = WindowsIdentity.GetCurrent().Name;
                File.WriteAllText(xmlPath, BuildTaskXml(exePath, user), Encoding.Unicode);
                return Outcome(Run($"/Create /TN \"{name}\" /XML \"{xmlPath}\" /F"));
            }
            finally
            {
                File.Delete(xmlPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TaskSchedulerStartup: change failed: {ex}");
            return $"Couldn't change the startup task: {ex.Message}";
        }
    }

    private static string? Outcome((int ExitCode, string Text) result)
    {
        if (result.ExitCode == 0) return null;

        Debug.WriteLine($"TaskSchedulerStartup: schtasks exit {result.ExitCode}: {result.Text}");
        var firstLine = result.Text.Trim().Split('\n')[0].Trim();
        return $"Couldn't change the startup task: {firstLine}";
    }

    private static (int ExitCode, string Text) Run(string arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("schtasks.exe did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeoutMs))
        {
            process.Kill();
            return (-1, "schtasks.exe did not finish in time.");
        }

        return (process.ExitCode, stdout.Result + stderr);
    }
}
