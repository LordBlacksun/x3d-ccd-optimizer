using System.Windows;
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
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.Apply();
    }
}
