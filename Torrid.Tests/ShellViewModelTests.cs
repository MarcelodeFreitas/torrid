using Torrid.App.ViewModels;

namespace Torrid.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make(out object home, out object sensors)
    {
        home = new object();
        sensors = new object();
        return new ShellViewModel(new[]
        {
            new NavItem(PageId.Home, "Home", "H", home),
            new NavItem(PageId.Sensors, "Sensors", "S", sensors),
        });
    }

    [Fact]
    public void StartsOnTheFirstPage()
    {
        var shell = Make(out var home, out _);

        Assert.Equal(PageId.Home, shell.Selected.Id);
        Assert.Same(home, shell.CurrentPage);
    }

    [Fact]
    public void Navigate_SwitchesPageAndRaisesPropertyChanged()
    {
        var shell = Make(out _, out var sensors);
        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shell.Navigate(PageId.Sensors);

        Assert.Same(sensors, shell.CurrentPage);
        Assert.Contains(nameof(ShellViewModel.CurrentPage), raised);
    }

    [Fact]
    public void Navigate_ToAnUnregisteredPage_IsIgnored()
    {
        var shell = Make(out var home, out _);

        shell.Navigate(PageId.Settings);

        Assert.Same(home, shell.CurrentPage);
    }

    [Fact]
    public void SelectingAnItem_MarksOnlyThatItemSelected()
    {
        var shell = Make(out _, out _);

        shell.Items[1].IsSelected = true;

        Assert.Equal(PageId.Sensors, shell.Selected.Id);
        Assert.False(shell.Items[0].IsSelected);
        Assert.True(shell.Items[1].IsSelected);
    }
}
