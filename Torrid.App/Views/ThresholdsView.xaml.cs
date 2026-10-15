namespace Torrid.App.Views;

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Torrid.App.ViewModels;

public partial class ThresholdsView : UserControl
{
    private ThresholdsViewModel? _vm;

    public ThresholdsView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a number box, which commits the edit.
        MouseDown += (_, _) => Focus();

        // This page is created each time it is opened, so a focus request made just before (from the Sensors page's
        // Limits link) is taken on load; while the page is open, later requests arrive through PropertyChanged.
        Loaded += (_, _) => Attach(DataContext as ThresholdsViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(ThresholdsViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = vm;
        if (_vm is null) return;

        _vm.PropertyChanged += OnViewModelChanged;
        if (_vm.TakeFocusRequest() is { } id) FocusSensor(id);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThresholdsViewModel.FocusedSensorId) || _vm is null) return;
        if (_vm.TakeFocusRequest() is { } id) FocusSensor(id);
    }

    /// <summary>
    /// Brings the sensor's card or row into view and focuses its first box. Runs after the layout pass, so a section that
    /// was just expanded has its items in place.
    /// </summary>
    private void FocusSensor(string sensorId)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            UpdateLayout();
            if (FindVisibleItem(this, sensorId) is not { } item) return;

            // A little space around the item, so it is not left flush against the edge of the view.
            item.BringIntoView(new Rect(new Point(0, -24), new Size(item.ActualWidth, item.ActualHeight + 48)));
            FindFirstBox(item)?.Focus();
        });
    }

    /// <summary>The outermost visible element bound to this sensor: the card or row's item container (the list and the cards are separate trees, only one shown).</summary>
    private static FrameworkElement? FindVisibleItem(DependencyObject root, string sensorId)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { IsVisible: true } element
                && element.DataContext is SensorThresholdViewModel sensor && sensor.Id == sensorId)
            {
                return element;
            }

            if (FindVisibleItem(child, sensorId) is { } found) return found;
        }

        return null;
    }

    private static TextBox? FindFirstBox(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { IsVisible: true, IsEnabled: true } box) return box;
            if (FindFirstBox(child) is { } found) return found;
        }

        return null;
    }
}
