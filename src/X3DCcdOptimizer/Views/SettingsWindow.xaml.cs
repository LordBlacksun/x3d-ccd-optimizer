using System.Windows;
using System.Windows.Controls;
using X3DCcdOptimizer.ViewModels;

namespace X3DCcdOptimizer.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Apply();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Apply();
    }

    private void OnGameSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string exe && DataContext is SettingsViewModel vm)
        {
            vm.NewGameText = exe;
            vm.AddGameCommand.Execute(null);
        }
    }

}
