using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using X3DCcdOptimizer.Models;

namespace X3DCcdOptimizer.ViewModels;

public class CcdPanelViewModel : ViewModelBase
{
    private string _roleLabel = "Idle";
    private SolidColorBrush _borderBrush;
    private int _borderThickness = 1;

    public string CcdName { get; }
    public string BadgeText { get; }
    public bool IsVCache { get; }
    public string L3SizeText { get; }
    public string CoreRangeText { get; }
    public int CcdIndex { get; }

    public SolidColorBrush BadgeBackground { get; }
    public SolidColorBrush BadgeForeground { get; }

    public ObservableCollection<CoreTileViewModel> Cores { get; } = [];

    public string RoleLabel
    {
        get => _roleLabel;
        set => SetProperty(ref _roleLabel, value);
    }

    public SolidColorBrush BorderBrush
    {
        get => _borderBrush;
        set => SetProperty(ref _borderBrush, value);
    }

    public int BorderThickness
    {
        get => _borderThickness;
        set => SetProperty(ref _borderThickness, value);
    }

    public CcdPanelViewModel(CpuTopology topology, int ccdIndex)
    {
        CcdIndex = ccdIndex;
        var cores = ccdIndex == 0 ? topology.VCacheCores : topology.FrequencyCores;
        var l3Size = ccdIndex == 0 ? topology.VCacheL3SizeMB : topology.StandardL3SizeMB;

        IsVCache = ccdIndex == 0 && topology.HasVCache;
        CcdName = $"CCD{ccdIndex}";
        BadgeText = topology.Tier switch
        {
            ProcessorTier.DualCcdX3D => ccdIndex == 0 ? "V-Cache" : "Frequency",
            _ => $"CCD {ccdIndex}"
        };
        L3SizeText = $"{l3Size} MB L3";

        if (cores.Length > 0)
        {
            var sorted = cores.OrderBy(c => c).ToArray();
            CoreRangeText = $"Cores {sorted[0]}-{sorted[^1]}";
        }
        else
        {
            CoreRangeText = "No cores";
        }

        BadgeBackground = FindBrush(IsVCache ? "AccentGreenBrush" : "AccentBlueBrush");
        BadgeForeground = new SolidColorBrush(Colors.White);
        _borderBrush = FindBrush("BorderDefaultBrush");

        foreach (var coreIndex in cores.OrderBy(c => c))
            Cores.Add(new CoreTileViewModel(coreIndex));
    }

    public void UpdateSnapshots(CoreSnapshot[] allSnapshots)
    {
        foreach (var snapshot in allSnapshots)
        {
            if ((CcdIndex == 0 && snapshot.CcdIndex == 0) || (CcdIndex == 1 && snapshot.CcdIndex == 1))
            {
                var tile = Cores.FirstOrDefault(c => c.CoreIndex == snapshot.CoreIndex);
                tile?.Update(snapshot);
            }
        }
    }

    public void UpdateBorderState(OperationMode mode, bool gameActive, int? gameCcdIndex)
    {
        if (gameActive && gameCcdIndex == CcdIndex)
        {
            if (mode == OperationMode.Optimize)
            {
                BorderBrush = FindBrush("AccentGreenBrush");
                BorderThickness = 2;
            }
            else
            {
                BorderBrush = FindBrush("AccentBlueBrush");
                BorderThickness = 2;
            }
        }
        else
        {
            BorderBrush = FindBrush("BorderDefaultBrush");
            BorderThickness = 1;
        }
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Gray);
    }
}
