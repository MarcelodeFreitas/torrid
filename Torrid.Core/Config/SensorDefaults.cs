namespace Torrid.Core.Config;

using Torrid.Core.Models;

public static class SensorDefaults
{
    public static bool ShownInWindow(SensorKind kind) => kind is SensorKind.CpuPackage or SensorKind.GpuHotSpot;

    public static bool ShownInTray(SensorKind kind) => ShownInWindow(kind);

    public static bool ShownInOverlay(SensorKind kind) => ShownInTray(kind);

    public static bool Notify(SensorKind kind) => true;
}
