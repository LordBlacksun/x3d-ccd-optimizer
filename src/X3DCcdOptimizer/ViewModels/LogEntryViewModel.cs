using System.Windows;
using System.Windows.Media;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class LogEntryViewModel
{
    public string Timestamp { get; }
    public string ActionText { get; }
    public SolidColorBrush ActionColor { get; }
    public string DetailText { get; }
    public bool IsMonitorAction { get; }
    public System.Windows.FontStyle FontStyle { get; }
    public double Opacity { get; }

    public LogEntryViewModel(AffinityEvent evt)
    {
        Timestamp = evt.Timestamp.ToString("HH:mm:ss");
        var displayProcess = evt.DisplayName ?? evt.ProcessName;
        DetailText = string.IsNullOrEmpty(displayProcess)
            ? evt.Detail
            : $"{displayProcess} {evt.Detail}";

        IsMonitorAction = evt.Action is AffinityAction.WouldEngage
            or AffinityAction.WouldMigrate
            or AffinityAction.WouldRestore
            or AffinityAction.WouldSetDriver
            or AffinityAction.WouldRestoreDriver
            or AffinityAction.DetectionSkipped;

        FontStyle = IsMonitorAction ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
        Opacity = IsMonitorAction ? 0.7 : 1.0;

        (ActionText, ActionColor) = evt.Action switch
        {
            AffinityAction.Engaged => ("ENGAGE", FindBrush("AccentGreenBrush")),
            AffinityAction.Migrated => ("MIGRATE", FindBrush("AccentGreenBrush")),
            AffinityAction.Restored => ("RESTORE", FindBrush("AccentBlueBrush")),
            AffinityAction.Skipped => ("SKIP", FindBrush("AccentAmberBrush")),
            AffinityAction.Error => ("ERROR", FindBrush("AccentRedBrush")),
            AffinityAction.WouldEngage => ("[MONITOR] WOULD ENGAGE", FindBrush("AccentBlueBrush")),
            AffinityAction.WouldMigrate => ("[MONITOR] WOULD MIGRATE", FindBrush("AccentBlueBrush")),
            AffinityAction.WouldRestore => ("[MONITOR] WOULD RESTORE", FindBrush("AccentBlueBrush")),
            AffinityAction.DriverSet => ("DRIVER SET", FindBrush("AccentGreenBrush")),
            AffinityAction.DriverRestored => ("DRIVER RESTORE", FindBrush("AccentBlueBrush")),
            AffinityAction.WouldSetDriver => ("[MONITOR] WOULD SET DRIVER", FindBrush("AccentBlueBrush")),
            AffinityAction.WouldRestoreDriver => ("[MONITOR] WOULD RESTORE DRIVER", FindBrush("AccentBlueBrush")),
            AffinityAction.DetectionSkipped => ("[AUTO] BELOW THRESHOLD", FindBrush("TextTertiaryBrush")),
            _ => ("UNKNOWN", FindBrush("TextSecondaryBrush"))
        };
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
