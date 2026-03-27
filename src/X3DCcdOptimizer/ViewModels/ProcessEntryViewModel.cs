using System.Windows;
using System.Windows.Media;

namespace X3DCcdOptimizer.ViewModels;

public class ProcessEntryViewModel : ViewModelBase
{
    public string ProcessName { get; }
    public int Pid { get; }
    public string CcdBadge { get; }
    public SolidColorBrush BadgeColor { get; }
    public string Detail { get; }
    public bool IsSimulated { get; }

    public ProcessEntryViewModel(string processName, int pid, string ccdBadge, bool isVCache, string detail, bool isSimulated)
    {
        ProcessName = processName;
        Pid = pid;
        CcdBadge = ccdBadge;
        Detail = detail;
        IsSimulated = isSimulated;

        BadgeColor = Application.Current?.TryFindResource(isVCache ? "AccentGreenBrush" : "AccentBlueBrush")
            as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
    }
}
