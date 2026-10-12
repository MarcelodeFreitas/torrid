namespace Torrid.Core.Sensors;

using LibreHardwareMonitor.Hardware;

/// <summary>An opened LibreHardwareMonitor <c>Computer</c> (a seam so the group bookkeeping is testable without hardware).</summary>
internal interface IHardwareComputer
{
    /// <summary>Updates every opened hardware item and returns a snapshot of the tree.</summary>
    IReadOnlyList<HardwareSnapshot> Read();

    void Close();
}

internal sealed class LhmHardwareComputer : IHardwareComputer
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();

    private LhmHardwareComputer(Computer computer) => _computer = computer;

    /// <summary>Opens a <c>Computer</c> with exactly <paramref name="groups"/> enabled.</summary>
    public static IHardwareComputer Open(HardwareGroups groups)
    {
        var computer = new Computer
        {
            IsCpuEnabled = groups.HasFlag(HardwareGroups.Cpu),
            IsGpuEnabled = groups.HasFlag(HardwareGroups.Gpu),
            IsMotherboardEnabled = groups.HasFlag(HardwareGroups.Motherboard),
            IsMemoryEnabled = groups.HasFlag(HardwareGroups.Memory),
            IsStorageEnabled = groups.HasFlag(HardwareGroups.Storage),
        };

        try
        {
            computer.Open();
        }
        catch
        {
            try { computer.Close(); } catch { /* best effort: don't leak a half-opened Computer */ }
            throw;
        }

        return new LhmHardwareComputer(computer);
    }

    public IReadOnlyList<HardwareSnapshot> Read()
    {
        _computer.Accept(_visitor);
        return _computer.Hardware.Select(ToSnapshot).ToList();
    }

    public void Close() => _computer.Close();

    private static HardwareSnapshot ToSnapshot(IHardware hardware) => new(
        hardware.HardwareType,
        hardware.Name,
        hardware.Sensors.Select(s => new SensorSnapshot(s.Name, s.SensorType, s.Value, s.Identifier.ToString())).ToList(),
        hardware.SubHardware.Select(ToSnapshot).ToList(),
        hardware.Identifier.ToString());

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }

        public void VisitParameter(IParameter parameter) { }
    }
}
