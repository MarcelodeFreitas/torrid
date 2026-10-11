namespace Torrid.App.ViewModels;

using System.Collections.ObjectModel;

internal static class CollectionSync
{
    /// <summary>Makes <paramref name="target"/> equal to <paramref name="desired"/> with minimal moves, keeping instances.</summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i])) target.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var existing = target.IndexOf(desired[i]);
            if (existing < 0)
            {
                target.Insert(i, desired[i]);
            }
            else if (existing != i)
            {
                target.Move(existing, i);
            }
        }
    }
}
