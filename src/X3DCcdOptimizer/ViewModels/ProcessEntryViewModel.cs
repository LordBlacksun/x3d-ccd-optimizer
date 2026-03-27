using System.Windows;
using System.Windows.Media;

namespace X3DCcdOptimizer.ViewModels;

public class ProcessEntryViewModel : ViewModelBase
{
    public string ProcessName { get; }
    public int Pid { get; }
    public string PidText { get; }
    public string CcdBadge { get; }
    public string CcdGroup { get; }
    public bool IsGame { get; }
    public int SortOrder { get; }
    public SolidColorBrush BadgeColor { get; }
    public SolidColorBrush TypeBadgeColor { get; }
    public string Detail { get; }
    public bool IsSimulated { get; }

    public ProcessEntryViewModel(string processName, int pid, string ccdBadge, bool isVCache,
        string detail, bool isSimulated, string ccdGroup = "", bool isGame = false)
    {
        ProcessName = processName;
        Pid = pid;
        PidText = $"PID {pid}";
        CcdBadge = ccdBadge;
        CcdGroup = ccdGroup;
        IsGame = isGame;
        SortOrder = isGame ? 0 : 1;
        Detail = detail;
        IsSimulated = isSimulated;

        BadgeColor = Application.Current?.TryFindResource(isVCache ? "AccentGreenBrush" : "AccentBlueBrush")
            as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
        TypeBadgeColor = Application.Current?.TryFindResource("AccentGreenBrush")
            as SolidColorBrush ?? new SolidColorBrush(Colors.Green);
    }
}
