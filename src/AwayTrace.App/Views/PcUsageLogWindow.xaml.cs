using System.Windows;
using AwayTrace.App.ViewModels;

namespace AwayTrace.App.Views;

public partial class PcUsageLogWindow : Window
{
    public PcUsageLogWindow(PcUsageLogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
