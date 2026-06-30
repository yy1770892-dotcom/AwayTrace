using System.Windows;
using AwayTrace.App.ViewModels;

namespace AwayTrace.App.Views;

public partial class ReportWindow : Window
{
    public ReportWindow(ReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
