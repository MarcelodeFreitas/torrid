namespace Torrid.App.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

/// <summary>
/// Text boxes bound with the default LostFocus trigger commit on Enter and whenever they lose keyboard focus
/// (including page switches and the window being hidden); Escape reverts to the stored value.
/// </summary>
public static class InputBehaviors
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(InputBehaviors), new PropertyMetadata(false, OnCommitOnEnterChanged));

    public static bool GetCommitOnEnter(DependencyObject element) => (bool)element.GetValue(CommitOnEnterProperty);

    public static void SetCommitOnEnter(DependencyObject element, bool value) => element.SetValue(CommitOnEnterProperty, value);

    private static void OnCommitOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;

        box.KeyDown -= OnKeyDown;
        box.LostKeyboardFocus -= OnLostKeyboardFocus;
        box.GotKeyboardFocus -= OnGotKeyboardFocus;
        if (e.NewValue is true)
        {
            box.KeyDown += OnKeyDown;
            box.LostKeyboardFocus += OnLostKeyboardFocus;
            box.GotKeyboardFocus += OnGotKeyboardFocus;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding is null) return;

        if (e.Key == Key.Enter)
        {
            binding.UpdateSource();
            box.SelectAll();
        }
        else if (e.Key == Key.Escape)
        {
            binding.UpdateTarget();
            box.SelectAll();
            e.Handled = true;
        }
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    // Tabbing into a box selects its text so a new value can simply be typed over it.
    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed) ((TextBox)sender).SelectAll();
    }
}
