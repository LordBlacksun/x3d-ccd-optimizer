using System.Collections.ObjectModel;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class ProcessRouterViewModel : ViewModelBase
{
    public ObservableCollection<ProcessEntryViewModel> Processes { get; } = [];

    public void OnAffinityChanged(AffinityEvent evt)
    {
        switch (evt.Action)
        {
            case AffinityAction.Engaged:
            case AffinityAction.WouldEngage:
                AddOrUpdate(evt.ProcessName, evt.Pid, "V-Cache", true, evt.Detail,
                    evt.Action == AffinityAction.WouldEngage);
                break;

            case AffinityAction.Migrated:
            case AffinityAction.WouldMigrate:
                if (evt.Pid != 0) // Skip summary entries
                    AddOrUpdate(evt.ProcessName, evt.Pid, "Frequency", false, evt.Detail,
                        evt.Action == AffinityAction.WouldMigrate);
                break;

            case AffinityAction.DriverSet:
            case AffinityAction.WouldSetDriver:
                AddOrUpdate(evt.ProcessName, evt.Pid, "V-Cache (Driver)", true, evt.Detail,
                    evt.Action == AffinityAction.WouldSetDriver);
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

    private void AddOrUpdate(string name, int pid, string badge, bool isVCache, string detail, bool simulated)
    {
        var existing = Processes.FirstOrDefault(p => p.Pid == pid);
        if (existing != null)
            Processes.Remove(existing);

        Processes.Insert(0, new ProcessEntryViewModel(name, pid, badge, isVCache, detail, simulated));
    }
}
