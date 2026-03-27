using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class ProcessRouterViewModel : ViewModelBase
{
    private readonly string _ccd0Name;
    private readonly string _ccd1Name;
    private Visibility _emptyVisibility = Visibility.Collapsed;

    public ObservableCollection<ProcessEntryViewModel> Processes { get; } = [];
    public ICollectionView ProcessView { get; }

    public Visibility EmptyVisibility
    {
        get => _emptyVisibility;
        private set => SetProperty(ref _emptyVisibility, value);
    }

    public ProcessRouterViewModel(string ccd0Name = "CCD 0", string ccd1Name = "CCD 1")
    {
        _ccd0Name = ccd0Name;
        _ccd1Name = ccd1Name;

        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProcessEntryViewModel.CcdGroup)));
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntryViewModel.SortOrder), ListSortDirection.Ascending));
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntryViewModel.ProcessName), ListSortDirection.Ascending));

        Processes.CollectionChanged += (_, _) => UpdateEmptyVisibility();
    }

    public void OnAffinityChanged(AffinityEvent evt)
    {
        switch (evt.Action)
        {
            case AffinityAction.Engaged:
            case AffinityAction.WouldEngage:
                AddOrUpdate(evt.ProcessName, evt.Pid, "V-Cache", true, evt.Detail,
                    evt.Action == AffinityAction.WouldEngage, _ccd0Name, isGame: true);
                break;

            case AffinityAction.Migrated:
            case AffinityAction.WouldMigrate:
                if (evt.Pid != 0) // Skip summary entries
                    AddOrUpdate(evt.ProcessName, evt.Pid, "Frequency", false, evt.Detail,
                        evt.Action == AffinityAction.WouldMigrate, _ccd1Name, isGame: false);
                break;

            case AffinityAction.DriverSet:
            case AffinityAction.WouldSetDriver:
                AddOrUpdate(evt.ProcessName, evt.Pid, "V-Cache (Driver)", true, evt.Detail,
                    evt.Action == AffinityAction.WouldSetDriver, _ccd0Name, isGame: true);
                break;

            case AffinityAction.Restored:
            case AffinityAction.WouldRestore:
            case AffinityAction.DriverRestored:
            case AffinityAction.WouldRestoreDriver:
                Clear();
                break;
        }
    }

    public void Clear()
    {
        Processes.Clear();
    }

    private void AddOrUpdate(string name, int pid, string badge, bool isVCache, string detail,
        bool simulated, string ccdGroup, bool isGame)
    {
        var existing = Processes.FirstOrDefault(p => p.Pid == pid);
        if (existing != null)
            Processes.Remove(existing);

        Processes.Add(new ProcessEntryViewModel(name, pid, badge, isVCache, detail, simulated, ccdGroup, isGame));
    }

    private void UpdateEmptyVisibility()
    {
        EmptyVisibility = Processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
