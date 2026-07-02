using System.ComponentModel;
using System.Windows;
using AwayTrace.App.ViewModels;
using MessageBox = System.Windows.MessageBox;

namespace AwayTrace.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public bool AllowClose { get; set; }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        if (DataContext is MainViewModel { IsProtectionActive: true })
        {
            e.Cancel = true;
            Hide();
            MessageBox.Show(
                "보호 중에는 PIN 인증 후 종료해야 합니다.\n창이 숨겨지면 AwayTrace를 다시 실행하거나 Ctrl+Alt+A를 눌러 다시 열 수 있습니다.",
                "AwayTrace",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
