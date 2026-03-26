#nullable enable
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// ObservableCollection that can replace its entire contents in one binding notification
    /// instead of firing N Add events. Cuts UI thrash from O(n) to O(1) on full reloads.
    /// </summary>
    internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>
        /// Clears and re-populates the collection, firing a single Reset notification.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
