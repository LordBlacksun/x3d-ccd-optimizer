using System.Windows;
using System.Windows.Media;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

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
        if (displayProcess.Length > 50)
            displayProcess = displayProcess[..47] + "...";
        DetailText = string.IsNullOrEmpty(displayProcess)
            ? evt.Detail
            : $"{displayProcess} {evt.Detail}";

        IsMonitorAction = evt.Action is AffinityAction.DetectionSkipped or AffinityAction.CcdObservation;
        FontStyle = IsMonitorAction ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
        Opacity = IsMonitorAction ? 0.7 : 1.0;

        (ActionText, ActionColor) = evt.Action switch
        {
            AffinityAction.GameDetected => ("DETECTED", FindBrush("AccentGreenBrush")),
            AffinityAction.GameExited => ("EXITED", FindBrush("AccentBlueBrush")),
            AffinityAction.Error => ("ERROR", FindBrush("AccentRedBrush")),
            AffinityAction.DetectionSkipped => ("[AUTO] BELOW THRESHOLD", FindBrush("TextTertiaryBrush")),
            AffinityAction.DriverStateChanged => ("DRIVER STATE", FindBrush("AccentAmberBrush")),
            AffinityAction.GameBarStatus => ("GAME BAR", FindBrush("AccentPurpleBrush")),
            AffinityAction.CcdObservation => ("CCD", FindBrush("AccentBlueBrush")),
            AffinityAction.CcdPreferenceSet => ("CCD PREF", FindBrush("AccentGreenBrush")),
            AffinityAction.CcdPreferenceRemoved => ("CCD PREF", FindBrush("AccentAmberBrush")),
            AffinityAction.AffinityPinApplied => ("AFFINITY PIN", FindBrush("AccentGreenBrush")),
            AffinityAction.AffinityPinRestored => ("AFFINITY PIN", FindBrush("AccentBlueBrush")),
            _ => ("UNKNOWN", FindBrush("TextSecondaryBrush"))
        };
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
