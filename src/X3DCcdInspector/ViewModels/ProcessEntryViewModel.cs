using System.Windows;
using System.Windows.Media;

namespace X3DCcdInspector.ViewModels;

public class ProcessEntryViewModel : ViewModelBase
{
    private int _instanceCount;

    public string ProcessName { get; }
    public string ExeName { get; }
    public string CcdBadge { get; }
    public string CcdGroup { get; }
    public bool IsGame { get; }
    public int SortOrder { get; }
    public SolidColorBrush BadgeColor { get; }
    public SolidColorBrush TypeBadgeColor { get; }
    public string Detail { get; }
    public bool IsSimulated { get; }

    public int InstanceCount
    {
        get => _instanceCount;
        set
        {
            if (SetProperty(ref _instanceCount, value))
            {
                OnPropertyChanged(nameof(CountText));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public string CountText => _instanceCount > 1 ? $"({_instanceCount} processes)" : "";
    public string DisplayText => _instanceCount > 1
        ? $"{ProcessName} ({_instanceCount} processes)"
        : ProcessName;

    // Track all PIDs for this deduplicated entry
    public HashSet<int> Pids { get; } = [];

    public ProcessEntryViewModel(string processName, string exeName, int pid, string ccdBadge, bool isVCache,
        string detail, bool isSimulated, string ccdGroup = "", bool isGame = false)
    {
        ProcessName = processName;
        ExeName = exeName;
        CcdBadge = ccdBadge;
        CcdGroup = ccdGroup;
        IsGame = isGame;
        SortOrder = isGame ? 0 : 1;
        Detail = detail;
        IsSimulated = isSimulated;
        _instanceCount = 1;
        Pids.Add(pid);

        BadgeColor = Application.Current?.TryFindResource(isVCache ? "AccentGreenBrush" : "AccentBlueBrush")
            as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
        TypeBadgeColor = Application.Current?.TryFindResource("AccentGreenBrush")
            as SolidColorBrush ?? new SolidColorBrush(Colors.Green);
    }
}
