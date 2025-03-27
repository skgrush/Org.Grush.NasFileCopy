using System.Collections.ObjectModel;

namespace Org.Grush.NasFileCopy.Remote.Ui.Extensions;

public static class ObservableCollectionExtensions
{
  public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
  {
    foreach (var item in items)
    {
      collection.Add(item);
    }
  }

  public static void ReplaceIfDifferent<T>(this ObservableCollection<T> collection, IReadOnlyList<T> newItems)
  {
    if (collection.SequenceEqual(newItems))
      return;

    var oldSequence = collection.ToList();

    // remove items not in new set
    int idx = 0;
    foreach (var item in oldSequence)
    {
      if (!newItems.Contains(item))
        collection.RemoveAt(idx);
      else
        ++idx;
    }

    foreach (var (i, newItem) in newItems.Select((item, i) => (idx: i, item)))
    {
      if (!oldSequence.Contains(newItem))
        collection.Insert(i, newItem);
    }
  }
}