namespace Torrid.App.ViewModels;

public enum PageId { Home, Sensors, Thresholds, Settings }

public sealed class NavItem : ObservableObject
{
    private bool _isSelected;

    public NavItem(PageId id, string title, string glyph, object page)
    {
        Id = id;
        Title = title;
        Glyph = glyph;
        Page = page;
    }

    public PageId Id { get; }

    public string Title { get; }

    public string Glyph { get; }

    public object Page { get; }

    /// <summary>Bound two-way to the sidebar button; the shell keeps exactly one item selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

public sealed class ShellViewModel : ObservableObject
{
    private NavItem _selected;

    public ShellViewModel(IEnumerable<NavItem> items)
    {
        Items = items.ToList();
        if (Items.Count == 0) throw new ArgumentException("At least one page is required.", nameof(items));

        _selected = Items[0];
        _selected.IsSelected = true;
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavItem.IsSelected) && item.IsSelected) Selected = item;
            };
        }
    }

    public IReadOnlyList<NavItem> Items { get; }

    public NavItem Selected
    {
        get => _selected;
        set
        {
            if (value is null || !Items.Contains(value)) return;
            if (!Set(ref _selected, value)) return;

            foreach (var item in Items) item.IsSelected = ReferenceEquals(item, value);
            Raise(nameof(CurrentPage));
        }
    }

    public object CurrentPage => Selected.Page;

    public void Navigate(PageId id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is not null) Selected = item;
    }
}
