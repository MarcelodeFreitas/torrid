namespace Torrid.App.Drivers;

using Torrid.Core.Drivers;

/// <summary>Reports whether the PawnIO driver (needed for CPU temperature) is installed. Read-only: never installs anything.</summary>
public interface IPawnIoDetector
{
    PawnIoStatus Detect();
}
