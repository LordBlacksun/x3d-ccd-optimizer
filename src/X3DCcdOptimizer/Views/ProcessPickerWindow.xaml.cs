using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using X3DCcdOptimizer.Core;

namespace X3DCcdOptimizer.Views;

public class ProcessPickerItem : INotifyPropertyChanged
{
    private bool _isSelected;
    public string DisplayName { get; init; } = "";
    public string ExeName { get; init; } = "";
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ProcessPickerWindow : Window
{
    private readonly HashSet<string> _alreadyAssigned;
    private readonly string? _currentGameExe;
    private List<ProcessPickerItem> _allItems = [];
    private List<ProcessPickerItem> _filteredItems = [];

    public List<string> SelectedExes { get; } = [];
    public Dictionary<string, string> SelectedDisplayNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ProcessPickerWindow(IEnumerable<string> alreadyAssigned, string? currentGameExe = null)
    {
        _alreadyAssigned = new HashSet<string>(alreadyAssigned, StringComparer.OrdinalIgnoreCase);
        _currentGameExe = currentGameExe;
        InitializeComponent();
        Loaded += async (_, _) => await LoadProcessesAsync(showAll: false);
    }

    private async Task LoadProcessesAsync(bool showAll)
    {
        CountText.Text = "Loading...";

        var items = await Task.Run(() => EnumerateProcesses(showAll));

        _allItems = items;
        _filteredItems = _allItems;
        ProcessList.ItemsSource = new ObservableCollection<ProcessPickerItem>(_filteredItems);
        CountText.Text = $"{_filteredItems.Count} processes";
    }

    private List<ProcessPickerItem> EnumerateProcesses(bool showAll)
    {
        var seen = new Dictionary<string, ProcessPickerItem>(StringComparer.OrdinalIgnoreCase);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName;
                var exe = name + ".exe";

                if (proc.Id <= 4) continue;
                if (AffinityManager.IsCriticalSystemProcess(name)) continue;
                if (_alreadyAssigned.Contains(exe)) continue;
                if (_currentGameExe != null &&
                    string.Equals(exe, _currentGameExe, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.ContainsKey(name)) continue;

                string? path = null;
                string displayName = name;

                try
                {
                    path = proc.MainModule?.FileName;
                }
                catch { }

                if (!showAll && path != null &&
                    path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (path != null)
                {
                    try
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                            displayName = versionInfo.FileDescription;
                    }
                    catch { }
                }

                seen[name] = new ProcessPickerItem
                {
                    DisplayName = displayName,
                    ExeName = exe
                };
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        return seen.Values.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async void OnShowAllChanged(object sender, RoutedEventArgs e)
    {
        await LoadProcessesAsync(ShowAllCheckBox.IsChecked == true);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allItems)
        {
            if (item.IsSelected)
            {
                SelectedExes.Add(item.ExeName);
                SelectedDisplayNames[item.ExeName] = item.DisplayName;
            }
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
