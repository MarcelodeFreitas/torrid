namespace Torrid.App;

using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Torrid.App.Interop;

/// <summary>The shell window. Composition lives in <see cref="AppHost"/>; this only handles window chrome and close-to-tray.</summary>
public partial class MainWindow : Window
{
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // Create the HWND now (the window usually starts hidden in the tray) so the dark title bar is
        // applied before the first Show and there is no white caption flash.
        new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Really quits: closes the window instead of hiding it and shuts the application down.</summary>
    public void ExitApplication()
    {
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // X hides to the tray; only the tray's Exit quits.
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
