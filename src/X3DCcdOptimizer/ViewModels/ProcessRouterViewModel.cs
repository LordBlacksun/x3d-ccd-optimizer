using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class ProcessRouterViewModel : ViewModelBase
{
    private readonly string _ccd0Name;
    private readonly string _ccd1Name;
    private Visibility _emptyVisibility = Visibility.Collapsed;
    private readonly System.Timers.Timer _pruneTimer;

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

        // Prune exited processes every 5 seconds
        _pruneTimer = new System.Timers.Timer(5000);
        _pruneTimer.AutoReset = true;
        _pruneTimer.Elapsed += (_, _) => PruneExitedProcesses();
    }

    public void OnAffinityChanged(AffinityEvent evt)
    {
        var displayName = evt.DisplayName ?? evt.ProcessName;
        switch (evt.Action)
        {
            case AffinityAction.Engaged:
            case AffinityAction.WouldEngage:
                AddOrUpdate(displayName, evt.ProcessName, evt.Pid, "V-Cache", true, evt.Detail,
                    evt.Action == AffinityAction.WouldEngage, _ccd0Name, isGame: true);
                StartPruneTimer();
                break;

            case AffinityAction.Migrated:
            case AffinityAction.WouldMigrate:
                if (evt.Pid != 0)
                    AddOrUpdate(displayName, evt.ProcessName, evt.Pid, "Frequency", false, evt.Detail,
                        evt.Action == AffinityAction.WouldMigrate, _ccd1Name, isGame: false);
                break;

            case AffinityAction.DriverSet:
            case AffinityAction.WouldSetDriver:
                AddOrUpdate(displayName, evt.ProcessName, evt.Pid, "V-Cache (Driver)", true, evt.Detail,
                    evt.Action == AffinityAction.WouldSetDriver, _ccd0Name, isGame: true);
                StartPruneTimer();
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
        _pruneTimer.Stop();
        Processes.Clear();
    }

    private void StartPruneTimer()
    {
        if (!_pruneTimer.Enabled)
            _pruneTimer.Start();
    }

    private void AddOrUpdate(string displayName, string processName, int pid, string badge, bool isVCache,
        string detail, bool simulated, string ccdGroup, bool isGame)
    {
        // Deduplicate by exe name + CCD group
        var exeName = processName;
        var existing = Processes.FirstOrDefault(p =>
            string.Equals(p.ExeName, exeName, StringComparison.OrdinalIgnoreCase) &&
            p.CcdGroup == ccdGroup);

        if (existing != null)
        {
            if (existing.Pids.Add(pid))
                existing.InstanceCount = existing.Pids.Count;
            return;
        }

        Processes.Add(new ProcessEntryViewModel(displayName, exeName, pid, badge, isVCache, detail, simulated, ccdGroup, isGame));
    }

    private void PruneExitedProcesses()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var toRemove = new List<ProcessEntryViewModel>();

            foreach (var entry in Processes)
            {
                var deadPids = new List<int>();
                foreach (var pid in entry.Pids)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        if (proc.HasExited)
                            deadPids.Add(pid);
                    }
                    catch (ArgumentException)
                    {
                        deadPids.Add(pid);
                    }
                    catch { }
                }

                foreach (var pid in deadPids)
                    entry.Pids.Remove(pid);

                if (entry.Pids.Count == 0)
                    toRemove.Add(entry);
                else
                    entry.InstanceCount = entry.Pids.Count;
            }

            foreach (var entry in toRemove)
                Processes.Remove(entry);

            if (Processes.Count == 0)
                _pruneTimer.Stop();
        });
    }

    private void UpdateEmptyVisibility()
    {
        EmptyVisibility = Processes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
