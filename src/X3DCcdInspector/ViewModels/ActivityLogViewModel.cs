using System.Collections.ObjectModel;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class ActivityLogViewModel : ViewModelBase
{
    private const int MaxEntries = 200;

    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public void AddEntry(AffinityEvent evt)
    {
        Entries.Add(new LogEntryViewModel(evt));

        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);
    }
}
