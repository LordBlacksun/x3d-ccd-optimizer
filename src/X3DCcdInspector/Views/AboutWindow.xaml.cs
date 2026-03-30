using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;

namespace X3DCcdInspector.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";
    }

    private void OnLinkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink link && link.Tag is string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
