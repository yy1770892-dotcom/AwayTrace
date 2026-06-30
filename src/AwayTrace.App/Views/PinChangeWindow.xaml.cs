using System.Windows;
using AwayTrace.Core.Models;
using AwayTrace.Core.Services;

namespace AwayTrace.App.Views;

public partial class PinChangeWindow : Window
{
    private readonly PinService _pinService;

    public PinChangeWindow(PinService pinService)
    {
        InitializeComponent();
        _pinService = pinService;
        Loaded += (_, _) => CurrentPinBox.Focus();
    }

    private async void OnChangeClick(object sender, RoutedEventArgs e)
    {
        MessageText.Text = string.Empty;
        var verifyResult = await _pinService.VerifyAsync(CurrentPinBox.Password);
        if (verifyResult.Status != PinVerifyStatus.Success)
        {
            MessageText.Text = verifyResult.Status == PinVerifyStatus.Locked
                ? $"PIN 입력 실패가 누적되어 약 {Math.Ceiling(verifyResult.RetryAfter?.TotalSeconds ?? 30)}초 후 다시 시도하세요."
                : $"현재 PIN이 올바르지 않습니다. 남은 시도: {verifyResult.RemainingAttempts}회";
            return;
        }

        if (NewPinBox.Password != ConfirmPinBox.Password)
        {
            MessageText.Text = "새 PIN과 새 PIN 확인이 일치하지 않습니다.";
            return;
        }

        try
        {
            await _pinService.SetPinAsync(NewPinBox.Password);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            MessageText.Text = ex.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
