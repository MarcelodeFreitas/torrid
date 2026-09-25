namespace Torrid.App.ViewModels;

using Torrid.Core.Models;

public sealed class ThresholdSettingRowViewModel
{
    public ThresholdSettingRowViewModel(SensorKind kind, double blueMax, double greenMax, double yellowMax)
    {
        Kind = kind;
        BlueMax = blueMax;
        GreenMax = greenMax;
        YellowMax = yellowMax;
    }

    public SensorKind Kind { get; }
    public double BlueMax { get; set; }
    public double GreenMax { get; set; }
    public double YellowMax { get; set; }
}
