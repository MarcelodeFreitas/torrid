namespace Torrid.App.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Torrid.App.ViewModels;

public partial class SensorsView : UserControl
{
    private AddSensorViewModel? _addSensor;

    public SensorsView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a name box, which commits the edit.
        MouseDown += (_, _) => Focus();

        DataContextChanged += (_, _) => WatchAddSensorPanel((DataContext as SensorsViewModel)?.AddSensor);
    }

    /// <summary>
    /// The Add sensor button sits in the fixed header but the panel is at the top of the scrolling list: when the panel
    /// opens, bring it into view and put keyboard focus on Scan (or Close while a scan is still running).
    /// </summary>
    private void WatchAddSensorPanel(AddSensorViewModel? addSensor)
    {
        if (_addSensor is not null) _addSensor.OpenRequested -= OnOpenRequested;
        _addSensor = addSensor;
        if (_addSensor is not null) _addSensor.OpenRequested += OnOpenRequested;
    }

    // Raised on every press of Add sensor, so it also works when the panel is already open (no IsOpen change then).
    private void OnOpenRequested(object? sender, EventArgs e)
    {
        // After the panel's visibility change has been laid out, so the buttons can take focus.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ListScroller.ScrollToTop();
            if (!ScanButton.Focus()) CloseAddPanelButton.Focus();
        });
    }
}
