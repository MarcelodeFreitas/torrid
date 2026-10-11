namespace Torrid.App.Views;

using System.Windows.Controls;

public partial class ThresholdsView : UserControl
{
    public ThresholdsView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a number box, which commits the edit.
        MouseDown += (_, _) => Focus();
    }
}
