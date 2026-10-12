namespace Torrid.Core.Sensors;

/// <summary>
/// The outcome of a sensor scan. <see cref="UnavailableReason"/> is non-null (and <see cref="Sensors"/> empty) when
/// the scan was refused (monitoring paused for an anti-cheat) or failed.
/// </summary>
public sealed record SensorCatalogResult(IReadOnlyList<CatalogSensor> Sensors, string? UnavailableReason)
{
    public static SensorCatalogResult Unavailable(string reason) => new(Array.Empty<CatalogSensor>(), reason);

    public static SensorCatalogResult PausedFor(AntiCheatMatch? antiCheat) =>
        Unavailable($"Monitoring is paused while {antiCheat?.DisplayName ?? "an anti-cheat"} is running.");

    public static SensorCatalogResult Failed(string message) => Unavailable($"Couldn't scan hardware: {message}");
}

/// <summary>Lists every temperature sensor that can be added. Implementations never block the caller's thread.</summary>
public interface ISensorCatalogSource
{
    /// <summary>Scans for temperature sensors. Refusals and failures are reported in the result, not thrown.</summary>
    /// <remarks>
    /// May throw <see cref="OperationCanceledException"/> (the returned task is cancelled) if <paramref name="ct"/> is
    /// cancelled before hardware is opened; once the scan has opened hardware it runs to completion.
    /// </remarks>
    Task<SensorCatalogResult> ScanAsync(CancellationToken ct = default);
}
