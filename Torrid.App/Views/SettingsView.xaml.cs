namespace Torrid.App.Views;

using System.Windows.Controls;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a text box, which commits the edit.
        MouseDown += (_, _) => Focus();
    }
}
