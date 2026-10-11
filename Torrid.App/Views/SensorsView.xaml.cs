namespace Torrid.App.Views;

using System.Windows.Controls;

public partial class SensorsView : UserControl
{
    public SensorsView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a name box, which commits the edit.
        MouseDown += (_, _) => Focus();
    }
}
