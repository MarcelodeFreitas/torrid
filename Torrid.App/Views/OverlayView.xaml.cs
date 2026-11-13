namespace Torrid.App.Views;

using System.Windows.Controls;
using System.Windows.Input;
using Torrid.App.ViewModels;
using Torrid.Core.Overlay;

public partial class OverlayView : UserControl
{
    public OverlayView()
    {
        InitializeComponent();

        // Clicking empty space takes focus off a text box, which commits the edit.
        MouseDown += (_, _) => Focus();
    }

    /// <summary>Records the pressed combination as the overlay shortcut. Unsupported keys are ignored.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Tab stays a navigation key, otherwise keyboard users could not leave the box.
        if (e.Key == Key.Tab) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;

        (DataContext as OverlaySettingsViewModel)?.TrySetHotkey(new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key)));
    }
}
