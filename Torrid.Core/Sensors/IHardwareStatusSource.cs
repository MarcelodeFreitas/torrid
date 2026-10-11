namespace Torrid.Core.Sensors;

public interface IHardwareStatusSource
{
    HardwareStatus Status { get; }

    event EventHandler? StatusChanged;
}
