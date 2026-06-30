using System.Windows;
using System.Windows.Input;
using AwayTrace.App.Services;

namespace AwayTrace.App.Views;

public partial class RunningAppPickerWindow : Window
{
    public RunningAppPickerWindow(IReadOnlyList<ProtectedAppCandidate> candidates)
    {
        InitializeComponent();
        AppList.ItemsSource = candidates;
    }

    public ProtectedAppCandidate? SelectedCandidate { get; private set; }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        SelectCurrent();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectCurrent();
    }

    private void SelectCurrent()
    {
        if (AppList.SelectedItem is not ProtectedAppCandidate candidate)
        {
            return;
        }

        SelectedCandidate = candidate;
        DialogResult = true;
    }
}
