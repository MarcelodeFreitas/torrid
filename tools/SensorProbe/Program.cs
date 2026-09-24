using LibreHardwareMonitor.Hardware;

var logPath = Path.Combine(Path.GetTempPath(), "torrid-probe-elevated.txt");
var fileWriter = new StreamWriter(logPath, append: false) { AutoFlush = true };
Console.SetOut(new MultiTextWriter(Console.Out, fileWriter));

var isElevated = new System.Security.Principal.WindowsPrincipal(
        System.Security.Principal.WindowsIdentity.GetCurrent())
    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
Console.WriteLine($"Running elevated: {isElevated}");
Console.WriteLine();

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
};

computer.Open();

var visitor = new UpdateVisitor();

for (var tick = 0; tick < 5; tick++)
{
    computer.Accept(visitor);

    Console.WriteLine($"=== Tick {tick} @ {DateTime.Now:HH:mm:ss} ===");
    foreach (var hardware in computer.Hardware)
    {
        DumpHardware(hardware, 0);
    }
    Console.WriteLine();

    Thread.Sleep(2000);
}

computer.Close();
fileWriter.Flush();
Console.WriteLine($"Log written to {logPath}");

static void DumpHardware(IHardware hardware, int depth)
{
    var indent = new string(' ', depth * 2);
    Console.WriteLine($"{indent}[{hardware.HardwareType}] {hardware.Name} (Id={hardware.Identifier})");

    foreach (var sensor in hardware.Sensors)
    {
        Console.WriteLine(
            $"{indent}  - {sensor.SensorType,-12} \"{sensor.Name}\" = {sensor.Value?.ToString("F1") ?? "null"} (Id={sensor.Identifier})");
    }

    foreach (var sub in hardware.SubHardware)
    {
        DumpHardware(sub, depth + 1);
    }
}

class MultiTextWriter(TextWriter a, TextWriter b) : TextWriter
{
    public override System.Text.Encoding Encoding => a.Encoding;
    public override void Write(char value) { a.Write(value); b.Write(value); }
    public override void Write(string? value) { a.Write(value); b.Write(value); }
    public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
}

class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
        {
            sub.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
