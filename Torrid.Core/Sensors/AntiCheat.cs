namespace Torrid.Core.Sensors;

using System.Diagnostics;

public enum AntiCheatBehavior { Pause, GpuOnly, Continue }

public enum HardwareAccessLevel { Full, GpuOnly, Paused }

public sealed record AntiCheatMatch(string Product, string ProcessName);

public sealed record HardwareStatus(HardwareAccessLevel Level, AntiCheatMatch? AntiCheat)
{
    public static HardwareStatus FullAccess { get; } = new(HardwareAccessLevel.Full, null);
}

public static class AntiCheatDetector
{
    // Process names of the anti-cheat *services/clients* that only run while protection is active.
    // vgtray (Vanguard's always-on tray icon) is deliberately absent; "vgc" is the Vanguard service.
    private static readonly (string Product, string[] Processes)[] Known =
    {
        ("Riot Vanguard", new[] { "vgc", "VALORANT-Win64-Shipping" }),
        ("Easy Anti-Cheat", new[] { "EasyAntiCheat", "EasyAntiCheat_EOS", "start_protected_game" }),
        ("BattlEye", new[] { "BEService", "BEService_x64" }),
        ("FACEIT Anti-Cheat", new[] { "FACEIT" }),
        ("Tencent ACE", new[] { "SGuard64", "SGuardSvc64" }),
        ("PunkBuster", new[] { "PnkBstrA", "PnkBstrB" }),
        ("nProtect GameGuard", new[] { "GameMon", "GameMon64", "npggsvc" }),
    };

    public static IReadOnlyList<string> KnownProducts { get; } = Known.Select(k => k.Product).ToList();

    public static AntiCheatMatch? Detect(IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames = null)
    {
        // Normalised once (the input may be a lazy enumerable); the dictionary keeps the first-seen original-cased name.
        var running = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in runningProcessNames)
        {
            var normalised = Normalize(name);
            running.TryAdd(normalised, normalised);
        }

        foreach (var (product, processes) in Known)
        {
            foreach (var process in processes)
            {
                if (running.ContainsKey(process)) return new AntiCheatMatch(product, process);
            }
        }

        if (extraProcessNames is not null)
        {
            foreach (var extra in extraProcessNames.Select(Normalize).Where(n => n.Length > 0))
            {
                if (running.TryGetValue(extra, out var actual)) return new AntiCheatMatch("Custom", actual);
            }
        }

        return null;
    }

    private static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }
}

public static class HardwareAccessPolicy
{
    public static HardwareAccessLevel Decide(AntiCheatBehavior behavior, AntiCheatMatch? match) =>
        match is null
            ? HardwareAccessLevel.Full
            : behavior switch
            {
                AntiCheatBehavior.Pause => HardwareAccessLevel.Paused,
                AntiCheatBehavior.GpuOnly => HardwareAccessLevel.GpuOnly,
                _ => HardwareAccessLevel.Full,
            };

    public static HardwareStatus Evaluate(AntiCheatBehavior behavior, IEnumerable<string> runningProcessNames, IEnumerable<string>? extraProcessNames)
    {
        var match = AntiCheatDetector.Detect(runningProcessNames, extraProcessNames);
        return match is null ? HardwareStatus.FullAccess : new HardwareStatus(Decide(behavior, match), match);
    }
}

public interface IProcessLister
{
    IReadOnlyCollection<string> GetRunningProcessNames();
}

public sealed class SystemProcessLister : IProcessLister
{
    public IReadOnlyCollection<string> GetRunningProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return names;
    }
}
