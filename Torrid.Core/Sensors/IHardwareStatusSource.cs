namespace Torrid.Core.Sensors;

public interface IHardwareStatusSource
{
    // Test AntiCheat != null (not Level) to know an anti-cheat was detected: Continue mode reports Level == Full with a non-null AntiCheat.
    HardwareStatus Status { get; }

    event EventHandler? StatusChanged;
}
