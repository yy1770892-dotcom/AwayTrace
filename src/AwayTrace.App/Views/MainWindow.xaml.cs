using System.ComponentModel;
using System.Windows;
using AwayTrace.App.ViewModels;

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
            System.Windows.MessageBox.Show(
                "보호 중에는 PIN 인증 후 종료해야 합니다.\n트레이 메뉴에서 보호 종료를 선택하세요.",
                "AwayTrace",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
