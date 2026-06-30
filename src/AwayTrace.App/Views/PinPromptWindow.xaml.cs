using System.Windows;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;

namespace AwayTrace.App.Views;

public partial class PinPromptWindow : Window
{
    private readonly PinService _pinService;

    public PinPromptWindow(PinService pinService)
    {
        InitializeComponent();
        _pinService = pinService;
        Loaded += (_, _) => PinBox.Focus();
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        var result = await _pinService.VerifyAsync(PinBox.Password);
        switch (result.Status)
        {
            case PinVerifyStatus.Success:
                DialogResult = true;
                break;
            case PinVerifyStatus.Locked:
                MessageText.Text = $"PIN 입력 실패가 5회 누적되어 잠시 대기해야 합니다. 약 {Math.Ceiling(result.RetryAfter?.TotalSeconds ?? 30)}초 후 다시 시도하세요.";
                break;
            case PinVerifyStatus.NotConfigured:
                MessageText.Text = "PIN이 아직 설정되어 있지 않습니다.";
                break;
            default:
                MessageText.Text = $"PIN이 올바르지 않습니다. 남은 시도: {result.RemainingAttempts}회";
                PinBox.Clear();
                PinBox.Focus();
                break;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
