using System.Windows;
using System.Windows.Media;
using X3DCcdInspector.Models;

namespace X3DCcdInspector.ViewModels;

public class CoreTileViewModel : ViewModelBase
{
    private double _loadPercent;
    private double _frequencyMHz;
    private SolidColorBrush _loadColor;
    private double _tileOpacity = 1.0;

    public int CoreIndex { get; }
    public string CoreLabel { get; }

    public double LoadPercent
    {
        get => _loadPercent;
        private set
        {
            if (SetProperty(ref _loadPercent, value))
            {
                OnPropertyChanged(nameof(LoadText));
                UpdateVisuals();
            }
        }
    }

    public string LoadText => $"{_loadPercent:F0}%";

    public double FrequencyMHz
    {
        get => _frequencyMHz;
        private set
        {
            if (SetProperty(ref _frequencyMHz, value))
                OnPropertyChanged(nameof(FrequencyText));
        }
    }

    public string FrequencyText => _frequencyMHz > 0 ? $"{_frequencyMHz / 1000.0:F2}G" : "—";

    public SolidColorBrush LoadColor
    {
        get => _loadColor;
        private set => SetProperty(ref _loadColor, value);
    }

    public double TileOpacity
    {
        get => _tileOpacity;
        private set => SetProperty(ref _tileOpacity, value);
    }

    public CoreTileViewModel(int coreIndex)
    {
        CoreIndex = coreIndex;
        CoreLabel = $"C{coreIndex}";
        _loadColor = FindBrush("CoreIdleBrush");
    }

    public void Update(CoreSnapshot snapshot)
    {
        LoadPercent = snapshot.LoadPercent;
        FrequencyMHz = snapshot.FrequencyMHz;
    }

    private void UpdateVisuals()
    {
        bool isParked = _loadPercent < 1.0 && _frequencyMHz < 100;
        TileOpacity = isParked ? 0.35 : 1.0;

        if (_loadPercent <= 15)
            LoadColor = FindBrush("CoreIdleBrush");
        else if (_loadPercent <= 40)
            LoadColor = FindBrush("CoreModerateBrush");
        else
            LoadColor = FindBrush("CoreHotBrush");
    }

    private static SolidColorBrush FindBrush(string key)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
            ?? new SolidColorBrush(Colors.Transparent);
    }
}
