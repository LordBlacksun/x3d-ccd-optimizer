using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using Serilog;
using X3DCcdInspector.Config;
using X3DCcdInspector.Core;

namespace X3DCcdInspector.ViewModels;

public class RunningProcessViewModel : ViewModelBase
{
    private bool _isExcluded;

    public string ProcessName { get; }
    public int InstanceCount { get; set; }
    public string CountText => InstanceCount > 1 ? $"({InstanceCount})" : "";

    public bool IsExcluded
    {
        get => _isExcluded;
        set => SetProperty(ref _isExcluded, value);
    }

    public RunningProcessViewModel(string processName, int instanceCount, bool isExcluded)
    {
        ProcessName = processName;
        InstanceCount = instanceCount;
        _isExcluded = isExcluded;
    }
}

public class ProcessExclusionsViewModel : ViewModelBase, IDisposable
{
    private readonly AppConfig _config;
    private readonly GameDetector _gameDetector;
    private readonly System.Timers.Timer _refreshTimer;
    private string _filterText = "";
    private string _totalText = "";

    // Processes that are always hidden — system services and OS infrastructure
    private static readonly HashSet<string> HiddenProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kernel / session
        "system", "idle", "registry", "secure system", "memory compression",
        "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "lsass.exe",
        "services.exe", "fontdrvhost.exe", "dwm.exe",
        // Service hosts
        "svchost.exe", "sihost.exe", "taskhostw.exe", "runtimebroker.exe",
        "searchhost.exe", "searchindexer.exe", "searchprotocolhost.exe",
        "searchfilterhost.exe", "startmenuexperiencehost.exe",
        "textinputhost.exe", "ctfmon.exe", "dllhost.exe", "conhost.exe",
        "wudfhost.exe", "dashost.exe", "wmiprvse.exe", "msdtc.exe",
        "spoolsv.exe", "lsaiso.exe", "sgrmbroker.exe",
        // Windows shell / UWP
        "explorer.exe", "shellexperiencehost.exe", "applicationframehost.exe",
        "systemsettings.exe", "lockapp.exe", "windowsinternal.composableshell.experiences.textinput.inputapp.exe",
        // WMI / management
        "unsecapp.exe", "wbem\\wmiprvse.exe", "msiexec.exe",
        // Security
        "securityhealthservice.exe", "securityhealthsystray.exe",
        "msmpeng.exe", "nissrv.exe",
        // Networking
        "networkservice", "localservice",
        // This app
        "x3dccdInspector.exe"
    };

    public ObservableCollection<RunningProcessViewModel> Processes { get; } = [];
    public ICollectionView ProcessView { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ProcessView.Refresh();
        }
    }

    public string TotalText
    {
        get => _totalText;
        set => SetProperty(ref _totalText, value);
    }

    public RelayCommand RefreshCommand { get; }

    public ProcessExclusionsViewModel(AppConfig config, GameDetector gameDetector)
    {
        _config = config;
        _gameDetector = gameDetector;

        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(
            nameof(RunningProcessViewModel.IsExcluded), ListSortDirection.Descending));
        ProcessView.SortDescriptions.Add(new SortDescription(
            nameof(RunningProcessViewModel.ProcessName), ListSortDirection.Ascending));

        RefreshCommand = new RelayCommand(RefreshProcessList);

        // Initial load
        RefreshProcessList();

        // Auto-refresh every 5 seconds
        _refreshTimer = new System.Timers.Timer(5000);
        _refreshTimer.AutoReset = true;
        _refreshTimer.Elapsed += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(RefreshProcessList);
        _refreshTimer.Start();
    }

    public void ToggleExclusion(RunningProcessViewModel item)
    {
        if (item.IsExcluded)
        {
            // Remove from exclusion list
            item.IsExcluded = false;
            _config.ExcludedProcesses.RemoveAll(e =>
                string.Equals(e, item.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e, item.ProcessName.Replace(".exe", ""), StringComparison.OrdinalIgnoreCase));
            _gameDetector.RemoveExclusion(item.ProcessName);
            Log.Information("Removed exclusion: {Process}", item.ProcessName);
        }
        else
        {
            // Add to exclusion list
            item.IsExcluded = true;
            if (!_config.ExcludedProcesses.Contains(item.ProcessName, StringComparer.OrdinalIgnoreCase))
                _config.ExcludedProcesses.Add(item.ProcessName);
            _gameDetector.AddExclusion(item.ProcessName);
            Log.Information("Added exclusion: {Process}", item.ProcessName);
        }

        _config.Save();
        ProcessView.Refresh();
    }

    private void RefreshProcessList()
    {
        Process[] procs;
        try
        {
            procs = Process.GetProcesses();
        }
        catch { return; }

        // Build a deduplicated map of running processes: exe name -> instance count
        var running = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in procs)
        {
            var name = p.ProcessName;
            p.Dispose();

            // Append .exe for consistency
            var nameWithExe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name : name + ".exe";

            if (IsHidden(nameWithExe))
                continue;

            running[nameWithExe] = running.TryGetValue(nameWithExe, out var count) ? count + 1 : 1;
        }

        // Merge with existing collection — update counts, add new, remove gone
        var existingMap = new Dictionary<string, RunningProcessViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in Processes)
            existingMap[vm.ProcessName] = vm;

        // Remove processes that are no longer running
        var toRemove = existingMap.Keys.Except(running.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in toRemove)
        {
            Processes.Remove(existingMap[name]);
            existingMap.Remove(name);
        }

        // Add or update
        foreach (var (name, count) in running)
        {
            if (existingMap.TryGetValue(name, out var existing))
            {
                if (existing.InstanceCount != count)
                {
                    existing.InstanceCount = count;
                    existing.OnPropertyChanged(nameof(RunningProcessViewModel.CountText));
                }
            }
            else
            {
                var isExcluded = _gameDetector.IsExcluded(name);
                Processes.Add(new RunningProcessViewModel(name, count, isExcluded));
            }
        }

        var excludedCount = Processes.Count(p => p.IsExcluded);
        TotalText = $"{Processes.Count} processes — {excludedCount} excluded";
    }

    private bool FilterProcess(object obj)
    {
        if (obj is not RunningProcessViewModel vm) return false;
        if (string.IsNullOrEmpty(_filterText)) return true;
        return vm.ProcessName.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHidden(string processName)
    {
        if (HiddenProcesses.Contains(processName))
            return true;

        // Also hide without .exe suffix
        var withoutExe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4] : processName;
        return HiddenProcesses.Contains(withoutExe);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
